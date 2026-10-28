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
                                  $"Visible={st.Visible} HitTest={st.HitTest} Revealed={st.Revealed}");
                // 程序化驱动显隐：1s 呼出、2s 收回，验证位置真的在动
                if (n == 4) win.ForceReveal();
                if (n == 8) win.ForceHide();
                if (n >= 12) { t.Stop(); win.Close(); Shutdown(0); }
            };
            t.Start();
        }

        win.Show();
    }
}
