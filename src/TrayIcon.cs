using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NxDrawer;

// ---- 系统托盘图标 ----
// 零 NuGet 依赖：直接 P/Invoke Shell_NotifyIcon 实现。
// （GeekDesk 用的是 HandyControl 的 NotifyIcon/WinUIEx 的 TrayIcon，
//   两者都要为这一个图标拖进一整个控件库——不值得。）
//
// 关键点：
//   1. 必须有一个**消息窗口**接收托盘回调（WM_APP+1 右键菜单、左键切换）；
//      这里用 HwndSource 挂到 HWND_MESSAGE（消息专用窗口，不显示在任务栏）。
//   2. Explorer 重启后托盘图标会消失，要监听 TaskbarCreated 重新注册。
//   3. 右键菜单用 Win32 popup menu（CreatePopupMenu + TrackPopupMenu），
//      菜单项用 TPM_RETURNCMD 直接拿命令号，不用自己处理 WM_COMMAND。

public sealed class TrayIcon : IDisposable
{
    private const uint WM_TASKBARCREATED = 0x8000 + 1; // 占位，实际用 RegisterWindowMessage 取
    private const uint WM_APP = 0x8000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_CONTEXTMENU = 0x007B;   // 右键（部分系统用这个而非 WM_RBUTTONUP）
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private const int CMD_SHOW = 1001;
    private const int CMD_EXIT = 1002;
    private const int CMD_SETTINGS = 1003;

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private HwndSource? _source;
    private NOTIFYICONDATA _nid;
    private IntPtr _icon;
    private IntPtr _ownerHwnd;      // 主窗口句柄——菜单置前要用（消息窗口不可见，置前不可靠）
    private bool _added;
    private uint _taskbarCreatedMsg;

    /// <summary>托盘图标是否注册成功（Shell_NotifyIcon 返回值）</summary>
    public bool Added => _added;

    /// <summary>
    /// 自测用：程序化向消息窗口投递一次「托盘右键」消息。
    /// 用来验证右键菜单真的能被触发——肉眼点托盘图标没法自动化。
    /// </summary>
    public void SimulateRightClick()
    {
        if (_source is null) return;
        // NOTIFYICON_VERSION_4：lParam 的 LOWORD=鼠标消息，HIWORD=图标 id
        var lParam = (IntPtr)((1 << 16) | (int)WM_RBUTTONUP);
        PostMessage(_source.Handle, (int)(WM_APP + 1), (IntPtr)1, lParam);
    }

    /// <summary>自测用：菜单项是否真的被触发过（记录最近一次点中的命令）</summary>
    public int LastMenuCommand { get; private set; }

    /// <summary>自测用：右键消息是否真的到达了 ShowContextMenu</summary>
    public bool ContextMenuShown { get; private set; }

    /// <summary>左键图标 / 菜单「显示抽屉」</summary>
    public event Action? ToggleRequested;

    /// <summary>菜单「退出」</summary>
    public event Action? ExitRequested;

    /// <summary>菜单「设置…」</summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// 注册托盘图标。
    /// </summary>
    /// <param name="owner">主窗口（用作图标来源与 owner hwnd 上下文）</param>
    /// <param name="tip">鼠标悬停提示</param>
    public void Register(Window owner, string tip = "nx-drawer — 贴边呼出工作台")
    {
        var ownerHwnd = new WindowInteropHelper(owner).EnsureHandle();
        _ownerHwnd = ownerHwnd;

        // 消息窗口：HWND_MESSAGE 父级，纯粹收消息，不出现在任务栏/alt-tab
        var params0 = new HwndSourceParameters("nx-drawer-tray")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            // 不设 WS_EX_NOACTIVATE：菜单弹出需要这个窗口能接收键盘输入，
            // 否则 TrackPopupMenu 拿到焦点后立刻丢失，菜单一点就消失。
        };
        _source = new HwndSource(params0);
        _source.AddHook(WndProc);

        // Explorer 重启广播的这条消息，需要重新注册图标
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

        _icon = LoadIconFromFile();
        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = (int)(NIF_MESSAGE | NIF_ICON | NIF_TIP),
            uCallbackMessage = (int)(WM_APP + 1),
            hIcon = _icon,
            szTip = tip,
        };

        Shell_NotifyIcon(NIM_ADD, ref _nid);
        _added = true;

        // v4 让图标按 DPI 缩放（否则高分屏上是糊的）
        _nid.uTimeoutOrVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIcon(NIM_SETVERSION, ref _nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_taskbarCreatedMsg)
        {
            // Explorer 重启：图标被清掉了，重新挂回去
            if (_added) Shell_NotifyIcon(NIM_ADD, ref _nid);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == (int)(WM_APP + 1))
        {
            // ⚠️ NOTIFYICON_VERSION_4 下 lParam 的语义是：
            //      LOWORD = 鼠标消息（WM_LBUTTONUP / WM_RBUTTONUP / …）
            //      HIWORD = 图标 id
            // 早期版本拿 lParam 整体去比 WM_RBUTTONUP，图标 id=1 拼进去后
            // lParam = 0x00010205 ≠ 0x0205 → **右键完全没反应**（图标在、菜单不出）。
            // 必须只取 LOWORD。
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
            {
                ToggleRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                ShowContextMenu();
                handled = true;
            }
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_source is null) return;
        ContextMenuShown = true;   // 自测探针：确认右键消息确实走到了这里

        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, CMD_SHOW, "显示 / 隐藏抽屉");
        AppendMenu(hMenu, MF_STRING, CMD_SETTINGS, "设置…");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);   // 分隔线：MF_SEPARATOR，不是 MF_STRING
        AppendMenu(hMenu, MF_STRING, CMD_EXIT, "退出");

        var pt = new POINT();
        GetCursorPos(out pt);

        // 弹出前必须让**某个前台窗口**接管，否则菜单点一下就消失。
        // 用主窗口（真实可见窗口）的 HWND，而不是 HWND_MESSAGE 消息窗口——
        // 后者不可见，SetForegroundWindow 对它不可靠。
        var fg = _ownerHwnd != IntPtr.Zero ? _ownerHwnd : _source.Handle;
        SetForegroundWindow(fg);

        // TPM_NONOTIFY：菜单项点击不向 owner 窗口发 WM_COMMAND（我们自己用 TPM_RETURNCMD 拿命令号）
        // TPM_RETURNCMD：TrackPopupMenu 直接返回被点中的菜单 id，不用自己处理 WM_COMMAND
        var cmd = TrackPopupMenu(hMenu,
            TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
            pt.X, pt.Y, 0, fg, IntPtr.Zero);
        // 收尾：把焦点还给系统（标准做法，否则菜单会粘住不消失）
        PostMessage(fg, 0x0112 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (cmd == CMD_SHOW) ToggleRequested?.Invoke();
        else if (cmd == CMD_SETTINGS) SettingsRequested?.Invoke();
        else if (cmd == CMD_EXIT) ExitRequested?.Invoke();
        LastMenuCommand = (int)cmd;
    }

    private IntPtr LoadIconFromFile()
    {
        // 从输出目录的 app.ico 加载（HICON 需自绘管理，Dispose 时 DestroyIcon）
        var ico = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        if (File.Exists(ico))
        {
            var h = LoadImage(IntPtr.Zero, ico, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (h != IntPtr.Zero) return h;
        }
        // 兜底：系统默认应用图标
        return LoadImage(IntPtr.Zero, new IntPtr(32512), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE);
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }

    // ---- Win32 ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr hinst, IntPtr lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hmenu, uint fuFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
