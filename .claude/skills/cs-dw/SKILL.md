---
name: cs-dw
description: cs-dw (C# Drawer) 项目 skill——维护和扩展 Windows 边缘抽屉启动器。适用信号："cs-dw 怎么构建 / 怎么加个新应用卡片 / 边缘唤醒不灵 / 托盘图标 / drawer.json 配置 / 加个自测 / 发版"。A 序列：A01 开发人员使用方式、A02 模块职责边界、A03 扩展指南。
---

# cs-dw (C# Drawer) — Windows 边缘抽屉启动器

> 本 skill **仅适用于 cs-dw**（仓库 `github.com/ZHLX2005/cs-dw`，本地根目录 `D:\a_js\js_proj\nx-drawer`）。
> 远程仓库名是 `cs-dw`，本地目录名是 `nx-drawer`——**两者不一致是既定事实**，不要"顺手改名"。
## 这是什么

纯 C#/WPF（.NET 8）的 Windows 常驻工具：鼠标贴屏幕边缘 → 半透明抽屉滑出 → 点卡片启动
nx 系列 CLI / 打开网址。托盘常驻，右键菜单「显示/隐藏 · 设置… · 退出」。

**零 NuGet 依赖**——没有 HandyControl、没有 WinUIEx、没有 MaterialDesign。所有 UI 是原生 WPF，
所有系统能力是手写 P/Invoke（user32 / shell32）。

## ref 路由

| 代号 | 主题 | 何时读取 | ref 文件 |
| --- | --- | --- | --- |
| **A01** | 开发人员使用方式 | 构建、运行、自测模式、CI/发版流程、常用命令 | [[A01-开发人员使用方式]] |
| **A02** | 模块职责边界 | 改代码前先看：哪个文件管什么、依赖方向、哪些是雷区 | [[A02-模块职责边界]] |
| **A03** | 扩展指南 | 加新功能：加卡片类型、加配置项、加自测、改交互 | [[A03-扩展指南]] |

## 铁律（改代码前必读）

这五条是本项目反复踩过的坑，违反会直接导致功能静默失效或进程崩溃。

| # | 铁律 | 违反后果 |
| --- | --- | --- |
| 1 | **UI 只能在 UI 线程操作** | 跨线程异常 → 未处理 → **进程崩溃**（历史上「点击启动即崩」的根因） |
| 2 | **显隐用移动 `Window.Left/Top`，不用 RenderTransform** | transform 不影响命中测试，隐藏后那块区域仍吃掉点击 |
| 3 | **坐标一律 DIP**，Win32 物理像素先经 `ScreenHelper` 折算 | 150% 缩放下热区差一个数量级，悬浮永远唤不醒 |
| 4 | **热区按鼠标所在屏算**，不用 `SystemParameters.WorkArea` | 多屏时副屏边缘永远触发不到 |
| 5 | **`EnsureHandle()` 会同步触发 `SourceInitialized`** | 在它之后才订阅该事件 = 永远收不到（曾导致轮询从未启动） |

## 项目速查

| 项 | 值 |
| --- | --- |
| 仓库 | `github.com/ZHLX2005/cs-dw`（本地目录 `D:\a_js\js_proj\nx-drawer`） |
| 目标框架 | `net8.0-windows`，`UseWPF=true`，`Nullable=enable` |
| 入口 | `src/App.xaml.cs`（`OnStartup`） |
| 主窗 | `src/MainWindow.xaml(.cs)` |
| 配置文件 | `src/drawer.json` → 运行时读 exe 同目录 |
| 构建 | `cd src && dotnet build -c Release` |
| 产物名 | `nx-drawer.exe`（`AssemblyName`），对外仓库/发布名 `cs-dw` |
| CI | `.github/workflows/ci.yml`（构建 + 3 自测 + artifact） |
| 发版 | `.github/workflows/release.yml`（`v*` tag → zip + Release） |

## 与其他 skill 的关系

- `key_board` → skill 生命周期管理（本 skill 属其 Z02 产物，结构遵循 X04 项目级红线）
- `gh-action` → GitHub Actions 模板与排障（A01 有本项目流水线细节，A 系列不重复通用 CI 知识）

## 维护约定

改完代码 → 跑 `dotnet run -c Release -- --launch-test` 与 `--edge-test` →
提交前确认 `dotnet build -c Release` 零警告 → 推送后看 CI 是否全绿。
新增可验证的行为 → 同时在 `src/App.xaml.cs` 加一个自测模式并挂进 `ci.yml`。
