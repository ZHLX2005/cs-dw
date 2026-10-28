using System.Runtime.InteropServices;

namespace NxDrawer;

// ---- 程序化移动鼠标（仅供 --edge-test 自测用） ----
// 自测时需要真的把光标送到屏幕边缘，否则「边缘热区」无从验证。
// 注意：这是**测试辅助**，正常运行时不会调用。

internal static class NativeMouse
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    /// <summary>把光标移到指定物理像素位置</summary>
    public static void MoveTo(int x, int y) => SetCursorPos(x, y);
}
