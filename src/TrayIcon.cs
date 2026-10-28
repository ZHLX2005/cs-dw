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
    private bool _added;
    private uint _taskbarCreatedMsg;

    /// <summary>托盘图标是否注册成功（Shell_NotifyIcon 返回值）</summary>
    public bool Added => _added;

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

        // 消息窗口：HWND_MESSAGE 父级，纯粹收消息，不出现在任务栏/alt-tab
        var params0 = new HwndSourceParameters("nx-drawer-tray")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
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
            var mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg == WM_LBUTTONUP)
            {
                ToggleRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP)
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

        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, CMD_SHOW, "显示 / 隐藏抽屉");
        AppendMenu(hMenu, MF_STRING, CMD_SETTINGS, "设置…");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);   // 分隔线：MF_SEPARATOR，不是 MF_STRING
        AppendMenu(hMenu, MF_STRING, CMD_EXIT, "退出");

        var pt = new POINT();
        GetCursorPos(out pt);
        // 弹出前必须置前，否则菜单点一下就消失
        SetForegroundWindow(_source.Handle);
        var cmd = TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _source.Handle, IntPtr.Zero);
        PostMessage(_source.Handle, 0x0112 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero); // 收尾，标准做法
        DestroyMenu(hMenu);

        if (cmd == CMD_SHOW) ToggleRequested?.Invoke();
        else if (cmd == CMD_SETTINGS) SettingsRequested?.Invoke();
        else if (cmd == CMD_EXIT) ExitRequested?.Invoke();
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
