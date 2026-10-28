# cs-dw (C# Drawer) — Windows 边缘抽屉启动器

鼠标靠近屏幕**左 / 上 / 右**边缘 → 半透明抽屉自动滑出 → 点卡片启动 / 监控 / 停止
nx 系列工具（nx-kv / nx-nx / nx-pg / …），URL 卡片直接开浏览器。

纯 C#/WPF（.NET 8），无 WebView、无 Electron、零 NuGet 依赖。配置驱动：`drawer.json`。

## 下载

从 [Releases](https://github.com/ZHLX2005/cs-dw/releases) 下载 `cs-dw-<version>-win-x64.zip`，
解压运行 `nx-drawer.exe`。运行时需 .NET 8 Desktop Runtime。

## 流水线

| 工作流 | 触发 | 做什么 |
|---|---|---|
| `ci.yml` | push / PR / 手动 | Windows runner 构建 → 三个自测（冒烟 / 启动链路 / 边缘唤醒）→ 上传 artifact |
| `release.yml` | 推 `v*` tag / 手动 | publish → 打 zip → 建 Release（自动生成 release notes） |

发版：改版本号 → `git tag -a v0.1.1 -m "..." && git push origin v0.1.1`。

## 构建工具链

| 工具 | 用途 | 现状 |
|---|---|---|
| .NET 8 SDK (`dotnet`) | 编译 WPF | ✅ 8.0.200 |
| Node.js (`node`) | 仅重新生成 logo 时跑 `tools/gen-logos.mjs` | ✅ |
| Visual Studio | **不需要** | — |

## 本地运行

```bash
cd src
dotnet build -c Release
dotnet run -c Release              # 常驻
dotnet run -c Release -- --smoke     # 冒烟：出现 2.6s 自动退出
dotnet run -c Release -- --probe     # 探针：每 250ms 打印位置，程序化验证显隐
dotnet run -c Release -- --launch-test  # 启动链路自测（断言计数起来又归零）
dotnet run -c Release -- --edge-test    # 边缘唤醒自测（程序化移光标到边缘）
```

## 交互

| 操作 | 行为 |
|---|---|
| 鼠标贴边缘悬停 120ms | 抽屉滑出（只留 4DIP 把手在边缘） |
| 鼠标移开 600ms | 抽屉收回 |
| `Ctrl+Alt+D` | 全局热键，切换显隐（被占用时静默降级） |
| **托盘左键** | 切换抽屉显隐 |
| **托盘右键** | 菜单：显示/隐藏抽屉 · 设置… · 退出 |
| **托盘「设置…」** | 打开设置页（边/尺寸/透明度/延迟/动画/应用列表），保存即时生效 |
| **托盘「退出」** | 清理全部子进程后退出程序 |
| 左键卡片 | 启动（可多实例，徽标 `●N`） |
| 右键卡片 / `■` | `taskkill /T /F` 杀整棵进程树 |
| 搜索框输入 | 实时过滤（Flow Launcher 式） |
| `↑` `↓` `Enter` | 键盘选卡并启动，`Esc` 收起 |

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
- **轮询要在构造末尾启动，不能靠 `SourceInitialized`**：`EnsureHandle()` 会**当场创建
  HWND 并同步触发**该事件，之后再订阅就永远收不到——这是「悬浮唤不醒」的根因。
- **热区要按鼠标所在屏算**：`SystemParameters.WorkArea` 只给主屏，多屏时用主屏边界
  判定副屏边缘永远不触发。改用 `ScreenHelper.WorkAreaAtCursorDip()`。
- **托盘零依赖**：`Shell_NotifyIcon` + `HwndSource(HWND_MESSAGE)` 自实现，
  不引 HandyControl/WinUIEx 那种为单个图标拖进整个控件库的方案；
  并监听 `TaskbarCreated` 以在 Explorer 重启后自动重新注册。
- **设置页保存要继承 logo 路径**：设置页只编辑「名称|命令|分组」，
  logo 不在编辑面内，保存时按名称从旧配置继承，否则存一次图标全丢。

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
  Config.cs         DrawerConfig/AppItemConfig + ConfigStore（Load/Save）
  EdgeWatcher.cs    边缘热区轮询 + 位置移动式显隐动画
  GlobalHotkey.cs   RegisterHotKey + HwndSource 收 WM_HOTKEY
  TrayIcon.cs       Shell_NotifyIcon 托盘图标 + 右键菜单 + Explorer 重启恢复
  ScreenHelper.cs   多屏工作区 / DPI 折算
  NativeMouse.cs    程序化移光标（仅 --edge-test 自测用）
  ProcessManager.cs 子进程表 / 计数 / taskkill /T /F / 退出回调
  MainWindow.xaml   Fluent 抽屉 UI（搜索 + 分组卡片 + 状态栏）
  SettingsWindow.xaml 设置页（边/尺寸/透明度/延迟/动画/应用列表）
tools/
  gen-logos.mjs     复用 ../nx-pg/tools/logo-gen 产出 10 个 nx logo + app.ico
```
