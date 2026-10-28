using System.Runtime.InteropServices;
using System.Windows;

namespace NxDrawer;

// ---- 显示器信息 ----
// 抽屉停靠与热区判定都需要「鼠标当前在哪块屏」——
// SystemParameters.WorkArea 只给**主屏**，多屏时鼠标在副屏边缘永远触发不了。
// 这里直接问 Win32 要虚拟屏幕的物理像素边界 + 每块屏的工作区。

public static class ScreenHelper
{
    // GetSystemMetrics 虚拟屏幕（物理像素）
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>虚拟屏幕（所有屏）的物理像素边界</summary>
    public static (int Left, int Top, int Width, int Height) VirtualScreenPixels()
        => (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>
    /// 取鼠标所在显示器的工作区，**返回 DIP**（WPF 坐标系）。
    /// 找不到时退回主屏工作区。
    /// </summary>
    public static Rect WorkAreaAtCursorDip()
    {
        if (!GetCursorPos(out var p)) return SystemParameters.WorkArea;

        var hMon = MonitorFromPoint(p, 2 /*MONITOR_DEFAULTTONEAREST*/);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return SystemParameters.WorkArea;

        // 物理像素 → DIP：用当前窗口的 DPI 折算
        var scale = DipScale();
        return new Rect(
            mi.rcWork.Left / scale, mi.rcWork.Top / scale,
            (mi.rcWork.Right - mi.rcWork.Left) / scale,
            (mi.rcWork.Bottom - mi.rcWork.Top) / scale);
    }

    /// <summary>当前系统 DPI 缩放（1.0 = 100%）</summary>
    public static double DipScale()
    {
        var dpi = GetDpiForSystem();
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
