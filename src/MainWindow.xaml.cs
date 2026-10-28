using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NxDrawer;

public partial class MainWindow : Window
{
    private readonly DrawerConfig _cfg;
    private readonly ProcessManager _procs = new();
    private readonly EdgeWatcher _watcher;
    private readonly GlobalHotkey _hotkey = new();
    private readonly TrayIcon _tray = new();
    private readonly List<CardRow> _rows = new();   // 卡片行（app + 徽标 + 停止按钮）

    public MainWindow()
    {
        InitializeComponent();
        _cfg = ConfigStore.Load(ConfigStore.Locate());

        _watcher = new EdgeWatcher(this, _cfg);
        _watcher.PositionInitial();
        // 抽屉整体透明度由配置驱动（半透明观感的主旋钮）
        DrawerPanel.Opacity = _cfg.Opacity;
        _watcher.RevealChanged += on => Dispatcher.Invoke(() =>
        {
            if (on) _watcher.RaiseTopmost();
            UpdateStatus();
        });

        BuildCards();
        // Changed 会在**线程池线程**被触发（进程退出回调），必须切回 UI 线程
        // 才能碰 WPF 控件——否则跨线程异常直接崩进程。
        _procs.Changed += () => Dispatcher.Invoke(UpdateStatus);
        EdgeBadge.Text = _cfg.Edge;

        // 全局热键：Ctrl+Alt+D 切换显隐（失败静默降级）
        _hotkey.Pressed += () => Dispatcher.Invoke(() => _watcher.Toggle());
        _hotkey.Register(ModifierKeys.Control | ModifierKeys.Alt, Key.D, this);

        // 系统托盘：左键切换抽屉，右键菜单「显示/隐藏」+「设置」+「退出」
        _tray.ToggleRequested += () => Dispatcher.Invoke(() => _watcher.Toggle());
        _tray.SettingsRequested += () => Dispatcher.Invoke(OpenSettings);
        _tray.ExitRequested += () => Dispatcher.Invoke(QuitApp);
        _tray.Register(this);

        Closed += (_, _) => { _procs.StopAll(); _hotkey.Dispose(); _watcher.Dispose(); _tray.Dispose(); };

        // 边缘轮询必须在这里启动，不能靠 SourceInitialized：
        // 上面的 _hotkey.Register → EnsureHandle() 会**当场创建 HWND 并同步触发
        // SourceInitialized**，之后再订阅该事件就永远收不到了（悬浮唤不醒的根因）。
        _watcher.Start();
    }

    /// <summary>打开设置页</summary>
    private void OpenSettings()
    {
        var win = new SettingsWindow(_cfg) { Owner = this };
        if (win.ShowDialog() == true)
        {
            // 保存后热重载：重建抽屉并重新落位
            _watcher.ApplyConfig(_cfg);
            DrawerPanel.Opacity = _cfg.Opacity;
            EdgeBadge.Text = _cfg.Edge;
            UpdateStatus();
        }
    }

    /// <summary>退出：清掉全部子进程再关窗（托盘菜单 / 窗口 ✕ 走同一条路）</summary>
    private void QuitApp()
    {
        _procs.StopAll();
        Application.Current.Shutdown();
    }

    // ---- 卡片构建：按 group 分区 ----

    private sealed record CardRow(AppItemConfig App, string Group, Border Root, TextBlock Badge, Button Stop);

    private void BuildCards()
    {
        AppList.Children.Clear();
        _rows.Clear();

        var groups = _cfg.Apps
            .GroupBy(a => string.IsNullOrWhiteSpace(a.Group) ? a.Name : a.Group)
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            // 分区标题（只有一组时不显示，省空间）
            if (groups.Count() > 1)
            {
                AppList.Children.Add(new TextBlock
                {
                    Text = g.Key.ToUpperInvariant(),
                    Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, 8, 0, 2),
                });
            }

            foreach (var app in g)
            {
                var row = BuildCard(app, g.Key);
                _rows.Add(row);
                AppList.Children.Add(row.Root);
            }
        }
        UpdateStatus();
    }

    private CardRow BuildCard(AppItemConfig app, string group)
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(10, 8, 8, 8),
            Cursor = Cursors.Hand,
            Tag = "card",
        };
        // hover 反馈：进入提亮（Fluent 的交互反馈）
        root.MouseEnter += (_, _) => root.Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        root.MouseLeave += (_, _) => root.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = BuildIcon(app);
        Grid.SetColumn(icon, 0);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock
        {
            Text = app.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7)),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
        });
        var hint = string.IsNullOrWhiteSpace(app.Url) ? app.Cmd : "↗ " + app.Url;
        mid.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 190,
        });
        Grid.SetColumn(mid, 1);

        var badge = new TextBlock
        {
            Text = "○",
            Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Tag = group,
        };
        var stop = new Button
        {
            Content = "■",
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0x88, 0x88)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = false,
            Visibility = Visibility.Collapsed,
        };
        stop.Click += (_, e) => { e.Handled = true; _procs.StopGroup(group); };

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(badge);
        right.Children.Add(stop);
        Grid.SetColumn(right, 2);

        grid.Children.Add(icon);
        grid.Children.Add(mid);
        grid.Children.Add(right);
        root.Child = grid;

        // 左键启动；右键停掉这一组
        root.MouseLeftButtonUp += (_, e) => { e.Handled = true; _procs.Start(app); };
        root.MouseRightButtonUp += (_, e) => { e.Handled = true; _procs.StopGroup(group); };

        return new CardRow(app, group, root, badge, stop);
    }

    private FrameworkElement BuildIcon(AppItemConfig app)
    {
        if (!string.IsNullOrWhiteSpace(app.Logo))
        {
            var exeDir = AppContext.BaseDirectory;
            var p = Path.IsPathRooted(app.Logo) ? app.Logo : Path.Combine(exeDir, app.Logo.Replace('/', '\\'));
            if (File.Exists(p))
            {
                try
                {
                    return new Image
                    {
                        Width = 30, Height = 30,
                        Source = new BitmapImage(new Uri(p)),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                catch { /* 落到占位 */ }
            }
        }
        // 占位：首字母 + 撞色底（家族感来自同一几何）
        var letter = app.Name.StartsWith("nx-") && app.Name.Length > 3 ? app.Name[3].ToString() : app.Name[..1];
        return new Border
        {
            Width = 30, Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(0x49, 0x2D, 0x22)),
            Child = new TextBlock
            {
                Text = letter,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xC7, 0xB5)),
                FontSize = 15, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    // ---- 搜索过滤（Flow Launcher 式输入即过滤） ----

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        foreach (var row in _rows)
        {
            var hit = q.Length == 0
                || row.App.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (row.App.Cmd ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (row.App.Url ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.Group.Contains(q, StringComparison.OrdinalIgnoreCase);
            row.Root.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
        }
        // 分区标题跟着隐藏的卡片走：只留还有可见卡片的分区
        foreach (var child in AppList.Children.OfType<TextBlock>())
        {
            var visible = _rows.Any(r => r.Root.Visibility == Visibility.Visible && r.Group == child.Text);
            child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>回车启动第一张可见卡片；↑↓ 切换焦点</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var visible = _rows.Where(r => r.Root.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _procs.Start(visible[0].App);
            SearchBox.Clear();
        }
        else if (e.Key == Key.Down || e.Key == Key.Up)
        {
            e.Handled = true;
            var idx = visible.FindIndex(r => r.Root.Background is SolidColorBrush b && b.Color.A > 0x30);
            idx = e.Key == Key.Down
                ? (idx + 1) % visible.Count
                : (idx - 1 + visible.Count) % visible.Count;
            foreach (var r in visible)
                r.Root.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            visible[idx].Root.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            visible[idx].Root.BringIntoView();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _watcher.Hide();
        }
    }

    // ---- 状态与计数 ----

    private void UpdateStatus()
    {
        var counts = _procs.Counts;
        foreach (var row in _rows)
        {
            var n = counts.GetValueOrDefault(row.Group, 0);
            row.Badge.Text = n > 0 ? $"●{n}" : "○";
            row.Badge.Foreground = n > 0
                ? new SolidColorBrush(Color.FromRgb(0x7A, 0xE8, 0x7A))
                : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            row.Stop.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        var total = counts.Values.Sum();
        StatusText.Text = total > 0
            ? $"运行中 {total} 个进程 · 左键启动 / 右键停止 / Ctrl+Alt+D 收起"
            : "就绪 · 左键启动 / 右键停止 / Ctrl+Alt+D 收起";
    }

    /// <summary>供 --probe 模式读取的抽屉状态（程序化验证用，别肉眼猜）</summary>
    public (double Left, double Top, bool Visible, bool HitTest, bool Revealed) DrawerState()
        => (Left, Top, Visibility == Visibility.Visible, IsHitTestVisible, _watcher.IsRevealed);

    /// <summary>托盘是否注册成功</summary>
    public bool TrayAdded => _tray.Added;

    /// <summary>当前存活子进程总数（自测断言用）</summary>
    public int RunningCount => _procs.Counts.Values.Sum();

    /// <summary>
    /// 启动链路自测：启动一个会立刻退出的子进程，走完
    /// 「启动 → 退出回调 → 计数刷新」全链路。
    /// </summary>
    public void LaunchTest()
    {
        var app = new AppItemConfig
        {
            Name = "__launch_test__",
            Cmd = "cmd /c exit 0",
        };
        _procs.Start(app);
    }

    /// <summary>供 --probe 模式直接驱动显隐</summary>
    public void ForceReveal() => _watcher.Reveal();
    public void ForceHide() => _watcher.Hide();

    private void OnQuit(object sender, RoutedEventArgs e) => QuitApp();
}
