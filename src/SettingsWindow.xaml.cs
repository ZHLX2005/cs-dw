using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace NxDrawer;

public partial class SettingsWindow : Window
{
    private readonly DrawerConfig _cfg;
    private readonly DrawerConfig _previous;
    private readonly string _path;
    private bool _suppressEdgeEvent;

    public SettingsWindow(DrawerConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        _previous = ConfigStore.Load(ConfigStore.Locate());
        _path = ConfigStore.Locate();

        // 载入现值
        _suppressEdgeEvent = true;
        switch (cfg.Edge.Trim().ToLowerInvariant())
        {
            case "right": EdgeRight.IsChecked = true; break;
            case "top": EdgeTop.IsChecked = true; break;
            default: EdgeLeft.IsChecked = true; break;
        }
        _suppressEdgeEvent = false;

        WidthBox.Text = cfg.Width.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = cfg.Height.ToString(CultureInfo.InvariantCulture);
        OpacitySlider.Value = cfg.Opacity;
        OpacityText.Text = cfg.Opacity.ToString("0.00");
        RevealBox.Text = cfg.RevealDelayMs.ToString();
        HideBox.Text = cfg.HideDelayMs.ToString();
        SlideBox.Text = cfg.SlideMs.ToString();

        var sb = new StringBuilder();
        foreach (var a in cfg.Apps)
        {
            var target = string.IsNullOrWhiteSpace(a.Url) ? a.Cmd : a.Url;
            var group = string.IsNullOrWhiteSpace(a.Group) ? "" : a.Group;
            sb.AppendLine($"{a.Name} | {target} | {group}");
        }
        AppsBox.Text = sb.ToString().TrimEnd();
    }

    private void OnEdgeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEdgeEvent) return;
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => OpacityText.Text = e.NewValue.ToString("0.00");

    private void OnReset(object sender, RoutedEventArgs e)
    {
        EdgeLeft.IsChecked = true;
        WidthBox.Text = "340";
        HeightBox.Text = "0";
        OpacitySlider.Value = 0.92;
        RevealBox.Text = "120";
        HideBox.Text = "600";
        SlideBox.Text = "220";
        AppsBox.Text = "nx-kv | nx-kv serve | \nnx-nx | nx-nx | \nnx-pg | nx-pg | ";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _cfg.Edge = EdgeRight.IsChecked == true ? "right"
                  : EdgeTop.IsChecked == true ? "top" : "left";
        _cfg.Width = ParseNum(WidthBox.Text, 340);
        _cfg.Height = ParseNum(HeightBox.Text, 0);
        _cfg.Opacity = Math.Clamp(OpacitySlider.Value, 0.1, 1.0);
        _cfg.RevealDelayMs = (int)ParseNum(RevealBox.Text, 120);
        _cfg.HideDelayMs = (int)ParseNum(HideBox.Text, 600);
        _cfg.SlideMs = (int)ParseNum(SlideBox.Text, 220);

        _cfg.Apps = ParseApps(AppsBox.Text);

        try
        {
            ConfigStore.Save(_path, _cfg, _previous);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "nx-drawer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>解析「名称 | 命令/网址 | 分组」每行一条</summary>
    private static List<AppItemConfig> ParseApps(string text)
    {
        var list = new List<AppItemConfig>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var raw = line.Trim();
            if (raw.Length == 0) continue;

            var parts = raw.Split('|');
            var name = parts[0].Trim();
            if (name.Length == 0) continue;

            var target = parts.Length > 1 ? parts[1].Trim() : "";
            var group = parts.Length > 2 ? parts[2].Trim() : "";

            var app = new AppItemConfig { Name = name };
            // 目标看起来像网址就走 url 分支，否则当命令
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                app.Url = target;
            }
            else
            {
                app.Cmd = target;
            }
            if (group.Length > 0) app.Group = group;
            list.Add(app);
        }
        return list;
    }

    private static double ParseNum(string s, double fallback)
        => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
