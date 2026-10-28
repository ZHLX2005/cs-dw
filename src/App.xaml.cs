using System.Windows;
using System.Windows.Threading;

namespace NxDrawer;

public partial class App : Application
{
    // --smoke：抽屉出现 2.6s 后自动退出，供 CI/无人值守验证
    // --probe：每 250ms 打印一次窗口位置与显隐状态，用于程序化验证
    //         「初始隐藏 → 呼出 → 收回」三态是否真的在动（肉眼不可靠）
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 全局兜底：未捕获异常不该让常驻托盘程序直接崩掉——
        // 记一笔到 stderr，继续活着比退出更符合「常驻工具」的预期。
        DispatcherUnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"[nx-drawer] 未捕获异常: {args.Exception}");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"[nx-drawer] 致命异常: {args.ExceptionObject}");
        };

        var win = new MainWindow();
        var probe = e.Args.Contains("--probe");
        var smoke = e.Args.Contains("--smoke");

        if (smoke)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            t.Tick += (_, _) => { t.Stop(); win.Close(); Shutdown(0); };
            t.Start();
        }

        if (probe)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            var n = 0;
            t.Tick += (_, _) =>
            {
                n++;
                var st = win.DrawerState();
                Console.WriteLine($"[{n * 250,5}ms] Left={st.Left,8:F1} Top={st.Top,8:F1} " +
                                  $"Visible={st.Visible} HitTest={st.HitTest} Revealed={st.Revealed} " +
                                  $"Tray={win.TrayAdded}");
                // 程序化驱动显隐：1s 呼出、2s 收回，验证位置真的在动
                if (n == 4) win.ForceReveal();
                if (n == 8) win.ForceHide();
                if (n >= 12) { t.Stop(); win.Close(); Shutdown(0); }
            };
            t.Start();
        }

        if (e.Args.Contains("--edge-test"))
        {
            // 边缘唤醒自测：程序化把鼠标移到屏幕左缘，验证抽屉真的能被「悬浮」唤出。
            // 不用肉眼——之前两次「看起来没反应」都是因为事件根本没启动。
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            var n = 0;
            t.Tick += (_, _) =>
            {
                n++;
                var st = win.DrawerState();
                if (n <= 4)
                {
                    // 把鼠标摆到左缘热区
                    NativeMouse.MoveTo(1, (int)(ScreenHelper.WorkAreaAtCursorDip().Top + 200));
                }
                Console.WriteLine($"[{n * 300,5}ms] Left={st.Left,8:F1} HitTest={st.HitTest} Revealed={st.Revealed}");
                if (n >= 10) { t.Stop(); win.Close(); Shutdown(0); }
            };
            t.Start();
        }

        if (e.Args.Contains("--launch-test"))
        {
            // 启动链路自测：真启动一个会立刻退出的子进程，
            // 验证「点击启动 → 进程退出 → Exited 回调 → UI 刷新」整条链路不崩。
            // 早期版本在这个回调里跨线程操作 WPF 控件，点击即崩。
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            var n = 0;
            var sawRunning = false;
            var ok = true;
            t.Tick += (_, _) =>
            {
                n++;
                if (n == 1) win.LaunchTest();
                var st = win.DrawerState();
                var running = win.RunningCount;
                if (running > 0) sawRunning = true;
                Console.WriteLine($"[{n * 400,5}ms] Left={st.Left,8:F1} Running={running}");
                if (n >= 8)
                {
                    t.Stop();
                    // 断言：子进程确实起来过（计数曾 >0），且退出后归零
                    ok = sawRunning && running == 0;
                    Console.WriteLine(sawRunning
                        ? $"[result] 启动链路正常：进程起来过(sawRunning=true)，退出后计数归零({running})"
                        : $"[result] 异常：从未观测到运行中的进程 (sawRunning={sawRunning}, running={running})");
                    win.Close();
                    Shutdown(ok ? 0 : 1);
                }
            };
            t.Start();
        }

        win.Show();
    }
}
