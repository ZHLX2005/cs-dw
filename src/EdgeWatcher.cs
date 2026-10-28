using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NxDrawer;

// ---- 贴边抽屉：位置移动式显隐 ----
//
// **为什么用「移动窗口位置」而不是 RenderTransform**（这是本轮返工的关键教训）：
//   上一版把 TranslateTransform 挂在 Window.Tag 上——Tag 只是任意对象槽位，
//   **不参与渲染**，动画在动一个没人看的对象，窗口纹丝不动。
//   即便挂对了 RenderTransform，transform 只影响绘制不影响命中测试，
//   隐藏后那块区域仍会吃掉点击。
//   而**移动 Left/Top** 同时解决三件事：真的移走了、命中测试跟着走、
//   任何缩放/多屏都成立。
//   GeekDesk 的 Util/MarginHide.cs 正是这么做的（window.Left -= subLen）。
//
// 三条边各自的隐藏位移：
//   left  → Left = 工作区左 - 宽度 + HandleDip   （留一条把手）
//   right → Left = 工作区右 - HandleDip            （把手在右缘内侧）
//   top   → Top  = 工作区上 - 高度 + HandleDip
//
// 显隐用 WPF Storyboard 动画 Left/Top 属性（不是手写 while 循环逐帧改），
// 时长与缓动由配置控制。

public sealed class EdgeWatcher : IDisposable
{
    private readonly Window _drawer;
    private readonly DrawerConfig _cfg;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _revealDelay = new() { Interval = TimeSpan.FromMilliseconds(1) };
    private readonly DispatcherTimer _hideDelay = new() { Interval = TimeSpan.FromMilliseconds(1) };

    private bool _hot;        // 鼠标在热区或抽屉上
    private bool _revealed;   // 当前是否展开
    private bool _animating;  // 动画进行中（期间不重复触发）

    /// <summary>展开状态，外部（热键）也可读</summary>
    public bool IsRevealed => _revealed;

    public event Action<bool>? RevealChanged;

    public EdgeWatcher(Window drawer, DrawerConfig cfg)
    {
        _drawer = drawer;
        _cfg = cfg;
        _revealDelay.Tick += (_, _) => { _revealDelay.Stop(); Reveal(); };
        _hideDelay.Tick += (_, _) => { _hideDelay.Stop(); Hide(); };
        _poll.Tick += Poll;
    }

    public void Start() => _poll.Start();

    public void Dispose()
    {
        _poll.Stop();
        _revealDelay.Stop();
        _hideDelay.Stop();
    }

    private string Edge => _cfg.Edge.Trim().ToLowerInvariant();

    // ---- 位置计算：展开位 / 隐藏位 ----

    /// <summary>展开时的窗口位置</summary>
    private (double Left, double Top) ShownPos()
    {
        var wa = SystemParameters.WorkArea;
        double w = _drawer.Width, h = _drawer.Height;
        return Edge switch
        {
            "right" => (wa.Right - w, wa.Top + (wa.Height - h) / 2),
            "top"   => (wa.Left + (wa.Width - w) / 2, wa.Top),
            _       => (wa.Left, wa.Top + (wa.Height - h) / 2),
        };
    }

    /// <summary>隐藏时的窗口位置（缩进屏幕外，留 HandleDip 把手）</summary>
    private (double Left, double Top) HiddenPos()
    {
        var wa = SystemParameters.WorkArea;
        double w = _drawer.Width, h = _drawer.Height;
        return Edge switch
        {
            "right" => (wa.Right - HandleDip, wa.Top + (wa.Height - h) / 2),
            "top"   => (wa.Left + (wa.Width - w) / 2, wa.Top - h + HandleDip),
            _       => (wa.Left - w + HandleDip, wa.Top + (wa.Height - h) / 2),
        };
    }

    // ---- 边缘轮询 ----

    private void Poll(object? s, EventArgs e)
    {
        if (_animating) return;
        if (!GetCursorPos(out var p)) return;

        // GetCursorPos 是物理像素；WPF 坐标是 DIP。先折算再比，
        // 否则 150% 缩放下热区厚度会差一个数量级（上一版踩过）。
        var src = PresentationSource.FromVisual(_drawer);
        var m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cur = new Point(p.X * m.M11, p.Y * m.M22);

        var wa = SystemParameters.WorkArea;
        bool onEdge = Edge switch
        {
            "left"  => cur.X <= wa.Left + HotZoneDip,
            "right" => cur.X >= wa.Right - HotZoneDip,
            "top"   => cur.Y <= wa.Top + HotZoneDip,
            _ => false,
        };

        bool onDrawer = _revealed && IsInsideDrawer(cur);

        if (onEdge || onDrawer)
        {
            if (!_hot)
            {
                _hot = true;
                _hideDelay.Stop();
                if (!_revealed)
                {
                    _revealDelay.Interval = TimeSpan.FromMilliseconds(Math.Max(0, _cfg.RevealDelayMs));
                    _revealDelay.Start();
                }
            }
        }
        else if (_hot)
        {
            _hot = false;
            _revealDelay.Stop();
            if (_revealed)
            {
                _hideDelay.Interval = TimeSpan.FromMilliseconds(Math.Max(0, _cfg.HideDelayMs));
                _hideDelay.Start();
            }
        }
    }

    private bool IsInsideDrawer(Point dip)
    {
        var b = new Rect(_drawer.Left, _drawer.Top, _drawer.ActualWidth, _drawer.ActualHeight);
        return b.Contains(dip);
    }

    // ---- 显隐 ----

    /// <summary>切到展开态（幂等）</summary>
    public void Reveal()
    {
        if (_revealed) return;
        _revealed = true;
        AnimateTo(ShownPos(), show: true);
    }

    /// <summary>切到隐藏态（幂等）</summary>
    public void Hide()
    {
        if (!_revealed) return;
        _revealed = false;
        AnimateTo(HiddenPos(), show: false);
    }

    /// <summary>热键用：开→关切换</summary>
    public void Toggle()
    {
        if (_revealed) Hide();
        else Reveal();
    }

    private void AnimateTo((double Left, double Top) target, bool show)
    {
        _animating = true;
        var (left, top) = target;

        var db = new DoubleAnimation(left, TimeSpan.FromMilliseconds(_cfg.SlideMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var dt = new DoubleAnimation(top, TimeSpan.FromMilliseconds(_cfg.SlideMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        var sb = new Storyboard();
        sb.Children.Add(db);
        sb.Children.Add(dt);
        Storyboard.SetTarget(db, _drawer);
        Storyboard.SetTarget(dt, _drawer);
        Storyboard.SetTargetProperty(db, new PropertyPath(Window.LeftProperty));
        Storyboard.SetTargetProperty(dt, new PropertyPath(Window.TopProperty));

        sb.Completed += (_, _) =>
        {
            _animating = false;
            // 落定终值：动画结束后 WPF 会把属性停在目标值，显式再写一次更稳
            _drawer.Left = left;
            _drawer.Top = top;
            _drawer.IsHitTestVisible = show;
            if (!show) SetTopmost(false);
            RevealChanged?.Invoke(show);
        };
        sb.Begin(_drawer, isControllable: false);
    }

    /// <summary>贴边初始定位：直接落到隐藏位（只露把手），不播动画。</summary>
    public void PositionInitial()
    {
        var wa = SystemParameters.WorkArea;
        _drawer.Width = _cfg.Width;
        _drawer.Height = _cfg.Height > 0 ? _cfg.Height : wa.Height * 0.8;

        var (left, top) = HiddenPos();
        _drawer.WindowStartupLocation = WindowStartupLocation.Manual;
        _drawer.Left = left;
        _drawer.Top = top;
        _drawer.IsHitTestVisible = false;
    }

    private void SetTopmost(bool on)
    {
        // SetWindowPos 直调比 WPF Topmost 属性可靠——置顶位会被窗口管理器事件吃掉，
        // 需主动 re-assert（见 Handy PR#383 的同款处理）。
        var hwnd = new WindowInteropHelper(_drawer).EnsureHandle();
        _ = SetWindowPos(hwnd, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>展开时置顶</summary>
    public void RaiseTopmost() => SetTopmost(true);

    // ---- Win32 ----

    private const double HotZoneDip = 6;  // 边缘热区厚度（DIP）
    private const double HandleDip = 4;   // 隐藏态露出的把手宽度

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}
