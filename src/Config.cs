using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NxDrawer;

// ---- 配置模型：drawer.json 的全部形态 ----
// 设计原则：**配置驱动一切**——抽屉贴哪条边、多宽、呼出延迟、每个应用的
// logo/命令/启动方式，全部来自 JSON，程序本体不知道 nx 系列的任何细节。

public sealed class DrawerConfig
{
    /// <summary>抽屉停靠方向：left / top / right（靠近哪条边触发呼出）</summary>
    [JsonPropertyName("edge")]
    public string Edge { get; set; } = "left";

    /// <summary>抽屉主体宽度（DIP，不含隐藏把手）</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; } = 320;

    /// <summary>抽屉高度（DIP）。0 = 跟随屏幕工作区高度的 80%</summary>
    [JsonPropertyName("height")]
    public double Height { get; set; } = 0;

    /// <summary>不透明度 0.1 ~ 1.0，半透明抽屉的观感主旋钮</summary>
    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.92;

    /// <summary>鼠标进入边缘热区多少 ms 后滑出抽屉（避免误触）</summary>
    [JsonPropertyName("revealDelayMs")]
    public int RevealDelayMs { get; set; } = 120;

    /// <summary>鼠标离开抽屉多少 ms 后自动收回（0 = 不自动收）</summary>
    [JsonPropertyName("hideDelayMs")]
    public int HideDelayMs { get; set; } = 600;

    /// <summary>滑入/滑出动画时长 ms</summary>
    [JsonPropertyName("slideMs")]
    public int SlideMs { get; set; } = 220;

    /// <summary>应用列表</summary>
    [JsonPropertyName("apps")]
    public List<AppItemConfig> Apps { get; set; } = new();
}

public sealed class AppItemConfig
{
    /// <summary>展示名（卡片标题）</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>logo 文件相对路径（相对 exe 目录）。留空 = 用首字母占位</summary>
    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    /// <summary>要启动的命令行（交由 cmd /c 执行，支持 nx-as 这类 shim）</summary>
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "";

    /// <summary>工作目录。留空 = 继承抽屉进程的目录</summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    /// <summary>url 类型：不启动子进程，直接交给系统默认浏览器</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>进程分组 id：同一组的实例共用一个计数器展示。留空 = 用 name</summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }
}

// ---- 读取与保存 ----

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>配置查找顺序：exe 同目录 drawer.json → 当前目录 drawer.json。都没有则产出一份默认模板。</summary>
    public static string Locate()
    {
        var exeDir = AppContext.BaseDirectory;
        var beside = Path.Combine(exeDir, "drawer.json");
        if (File.Exists(beside)) return beside;

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "drawer.json");
        if (File.Exists(cwd)) return cwd;

        // 首次运行：把默认配置写进 exe 目录，让用户从模板改起
        File.WriteAllText(beside, DefaultJson, new System.Text.UTF8Encoding(false));
        return beside;
    }

    public static DrawerConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<DrawerConfig>(json, JsonOpts) ?? new DrawerConfig();
        cfg.Opacity = Math.Clamp(cfg.Opacity, 0.1, 1.0);
        cfg.SlideMs = Math.Clamp(cfg.SlideMs, 60, 1000);
        return cfg;
    }

    /// <summary>保存配置到磁盘。</summary>
    /// <param name="path">目标文件</param>
    /// <param name="cfg">配置</param>
    /// <param name="previous">旧配置——用来继承设置页没暴露的字段（如 logo 路径）</param>
    public static void Save(string path, DrawerConfig cfg, DrawerConfig? previous = null)
    {
        // 设置页只编辑「名称 | 命令 | 分组」，logo 路径不在编辑面里——
        // 按名称从旧配置继承，否则保存一次就把所有图标弄丢了。
        if (previous is not null)
        {
            foreach (var app in cfg.Apps)
            {
                var old = previous.Apps.FirstOrDefault(a =>
                    string.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase));
                if (old is null) continue;
                app.Logo = old.Logo;
                app.Cwd = old.Cwd;
                if (string.IsNullOrWhiteSpace(app.Cmd) && string.IsNullOrWhiteSpace(app.Url))
                    app.Cmd = old.Cmd;
            }
        }

        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
    }

    private const string DefaultJson = """
{
  // 停靠边: left | top | right
  "edge": "left",
  "width": 320,
  "height": 0,
  "opacity": 0.92,
  "revealDelayMs": 120,
  "hideDelayMs": 600,
  "slideMs": 220,
  "apps": [
    { "name": "nx-kv",  "logo": "assets/logos/nx-kv.png",  "cmd": "nx-kv serve" },
    { "name": "nx-nx",  "logo": "assets/logos/nx-nx.png",  "cmd": "nx-nx" },
    { "name": "nx-pg",  "logo": "assets/logos/nx-pg.png",  "cmd": "nx-pg" },
    { "name": "nx-sk",  "logo": "assets/logos/nx-sk.png",  "cmd": "nx-sk" },
    { "name": "nx-rp",  "logo": "assets/logos/nx-rp.png",  "cmd": "nx-rp" },
    { "name": "nx-as",  "logo": "assets/logos/nx-as.png",  "cmd": "nx-as" },
    { "name": "nx-ft",  "logo": "assets/logos/nx-ft.png",  "cmd": "nx-ft" },
    { "name": "nx-kn",  "logo": "assets/logos/nx-kn.png",  "cmd": "nx-kn" },
    { "name": "nx-rh",  "logo": "assets/logos/nx-rh.png",  "cmd": "nx-rh" },
    { "name": "浏览器·GitHub", "url": "https://github.com", "logo": null }
  ]
}
""";
}
