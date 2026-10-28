using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace NxDrawer;

// ---- 全局热键 ----
// 参考 GeekDesk 的 Util/GlobalHotKey.cs：同样是 RegisterHotKey + WM_HOTKEY 消息。
// 但它用 System.Windows.Forms.NativeWindow 收消息（因此整个项目引用了 WinForms）。
// 这里改用 WPF 自带的 HwndSource 挂 AddHook，**不引入 WinForms 依赖**——
// WinForms 会拖进一堆与本项目无关的初始化副作用。
//
// 语义：Ctrl+Alt+D 切换抽屉显隐。注册失败（热键被占）静默降级，不影响边缘呼出。

public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4E58; // 'NX'

    private HwndSource? _source;
    private bool _registered;

    /// <summary>热键按下时触发</summary>
    public event Action? Pressed;

    /// <summary>
    /// 注册全局热键。
    /// </summary>
    /// <param name="modifiers">组合键，如 Ctrl+Alt</param>
    /// <param name="virtualKey">主键的 Win32 虚拟键码（如 'D' = 0x44）</param>
    /// <param name="owner">用于接收 WM_HOTKEY 的窗口</param>
    public void Register(ModifierKeys modifiers, Key key, System.Windows.Window owner)    {
        var hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        var mods = ToWin32(modifiers);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(hwnd, HOTKEY_ID, mods, vk);
        if (!_registered)
        {
            // 热键已被别的程序占用：静默降级，边缘呼出照常工作
            System.Diagnostics.Debug.WriteLine("[nx-drawer] RegisterHotKey 失败（可能被占用），热键呼出不可用");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    private static uint ToWin32(ModifierKeys mods)
    {
        uint m = 0;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= 0x0001;
        if (mods.HasFlag(ModifierKeys.Control)) m |= 0x0002;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= 0x0004;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= 0x0008;
        return m;
    }

    public void Dispose()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _registered = false;
        }
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
