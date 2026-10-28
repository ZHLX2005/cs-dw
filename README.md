# nx-drawer — Windows 边缘抽屉启动器（nx 家族工作台）

鼠标靠近屏幕**左 / 上 / 右**边缘 → 半透明抽屉自动滑出 → 点卡片启动 / 监控 / 停止
nx 系列工具（nx-kv / nx-nx / nx-pg / …），URL 卡片直接开浏览器。

纯 C#/WPF（.NET 8），无 WebView、无 Electron、零 NuGet 依赖。配置驱动：`drawer.json`。

## 构建工具链

| 工具 | 用途 | 现状 |
|---|---|---|
| .NET 8 SDK (`dotnet`) | 编译 WPF | ✅ 8.0.200 |
| Node.js (`node`) | 仅重新生成 logo 时跑 `tools/gen-logos.mjs` | ✅ |
| Visual Studio | **不需要** | — |

## 运行

```bash
cd src
dotnet build -c Release
dotnet run -c Release              # 常驻
dotnet run -c Release -- --smoke   # 冒烟：出现 2.6s 自动退出
dotnet run -c Release -- --probe   # 探针：每 250ms 打印位置，程序化验证显隐
```

首次运行在 exe 目录产出 `drawer.json` 模板。

## 交互

| 操作 | 行为 |
|---|---|
| 鼠标贴边缘悬停 120ms | 抽屉滑出（只留 4DIP 把手在边缘） |
| 鼠标移开 600ms | 抽屉收回 |
| `Ctrl+Alt+D` | 全局热键，切换显隐（被占用时静默降级） |
| 左键卡片 | 启动（可多实例，徽标 `●N`） |
| 右键卡片 / `■` | `taskkill /T /F` 杀整棵进程树 |
| 搜索框输入 | 实时过滤（Flow Launcher 式） |
| `↑` `↓` `Enter` | 键盘选卡并启动，`Esc` 收起 |
| 退出抽屉 | 自动清理全部子进程 |

## drawer.json

```jsonc
{
  "edge": "left",        // left | top | right
  "width": 340,
  "opacity": 0.92,       // 抽屉整体透明度
  "revealDelayMs": 120,  // 悬停多久滑出
  "hideDelayMs": 600,    // 离开多久收回
  "slideMs": 220,        // 动画时长
  "apps": [
    { "name": "nx-kv", "logo": "assets/logos/nx-kv.png", "cmd": "nx-kv serve" },
    { "name": "GitHub", "url": "https://github.com" },
    { "name": "编辑器", "group": "工具", "cmd": "code ." }
  ]
}
```

- `cmd` 经 `cmd /c` 执行，支持 `~/.local/bin` 的 nx shim
- `url` 交给系统默认浏览器，不进子进程管理
- `group` 相同的应用共享计数并归入同一分区

## 实现要点（踩过的坑）

- **显隐用「移动窗口位置」而非 RenderTransform**：transform 只影响绘制不影响命中测试，
  隐藏后那块区域仍会吃掉点击；移动 `Left/Top` 三件事一起解决。
  做法参考 GeekDesk 的 `Util/MarginHide.cs`（`window.Left -= subLen`）。
- **动画目标必须是 `RenderTransform`**：`Window.Tag` 是任意对象槽位，**不参与渲染**，
  挂在上面动画等于白动（第一版就栽在这）。
- **热区统一用 DIP**：`GetCursorPos` 给物理像素，150% 缩放下直接比会差一个数量级。
- **热键用 `HwndSource.AddHook`**：GeekDesk 用 WinForms `NativeWindow` 收 `WM_HOTKEY`，
  这里换成 WPF 的 `HwndSource`，不引入 WinForms 依赖。

## 参考仓库（均在 nx 看板 b_mvnm9xdkrzmwm）

| 仓库 | 参考了什么 |
|---|---|
| `D:\a_other\nxkvrepo\geekdesk` | 贴边隐藏 `MarginHide.cs`、全局热键 `GlobalHotKey.cs`、窗口亚克力 `BlurGlassUtil.cs` |
| `D:\a_other\nxkvrepo\flow.launcher` | 输入即过滤的启动器交互 |
| `D:\a_other\nxkvrepo\agile-tiles` | 贴边滑出侧边栏 + 卡片组织 |
| `D:\a_other\nxkvrepo\deskbox` | 卡片化收纳、widget 观感 |
| `D:\a_other\nxkvrepo\wpfui` / `handycontrol` | Fluent 配色与控件风格 |
| `D:\a_other\nxkvrepo\unigetui` | 托盘常驻 + 任务管理面板 |

## 结构

```
src/
  NxDrawer.csproj   net8.0-windows / UseWPF，零 NuGet 依赖
  Config.cs         DrawerConfig/AppItemConfig + ConfigStore
  EdgeWatcher.cs    边缘热区轮询 + 位置移动式显隐动画
  GlobalHotkey.cs   RegisterHotKey + HwndSource 收 WM_HOTKEY
  ProcessManager.cs 子进程表 / 计数 / taskkill /T /F / 退出回调
  MainWindow.xaml   Fluent 抽屉 UI（搜索 + 分组卡片 + 状态栏）
tools/
  gen-logos.mjs     复用 ../nx-pg/tools/logo-gen 产出 10 个 nx logo + app.ico
```
