# NotiFlow

基于 .NET 8 + WPF 的 Windows 系统通知弹幕工具。将系统通知中心的消息以弹幕形式叠加在屏幕最顶层滚动显示，支持鼠标穿透，不影响正常操作。

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 8 (`net8.0-windows10.0.19041.0`) |
| UI 框架 | WPF + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent Design) |
| 通知 API | `Windows.UI.Notifications.Management` (WinRT) |
| 窗口控制 | `User32.dll` P/Invoke (`WS_EX_TRANSPARENT` 鼠标穿透) |
| 渲染方式 | `DrawingVisual` + `CompositionTarget.Rendering` 帧循环 |

## 项目结构

```
NotiFlow/
├── MainWindow.xaml(.cs)           # 弹幕叠加层（全屏透明置顶窗口）
├── SettingsWindow.xaml(.cs)       # 设置界面主窗口（NavigationView 导航）
├── BarrageSettings.cs             # 全局配置管理（静态属性 + JSON 持久化）
├── NativeMethods.cs               # Win32 P/Invoke 声明
├── Models/
│   ├── BarrageItem.cs             # 弹幕渲染单元（DrawingVisual）
│   └── NotificationMessage.cs    # 通知消息数据模型
├── Rendering/
│   └── BarrageEngineHost.cs      # 视觉对象宿主（FrameworkElement）
├── Services/
│   └── NotificationService.cs    # 系统通知监听服务
└── Views/Pages/
    ├── CustomPage.xaml(.cs)       # 弹幕外观自定义页
    ├── SettingsPage.xaml(.cs)     # 通用设置页
    └── AboutPage.xaml(.cs)       # 关于页
```

## 核心模块说明

### 弹幕引擎 (`MainWindow`)

- 使用 `CompositionTarget.Rendering` 驱动逐帧更新，实现匀速水平滚动。
- 多轨道管理：根据屏幕高度动态计算轨道数，采用"黄金三分区"优先分配策略（优先使用屏幕上方 1/3 区域）。
- 对象池复用：`BarrageItem` 飞出屏幕后回收至对象池，减少 GC 压力。
- 渲染优化：使用 `DrawingVisual` 替代 WPF 控件树，避免布局计算开销。

### 通知监听 (`NotificationService`)

- 通过 WinRT 的 `UserNotificationListener` 获取系统通知。
- 采用异步轮询模式（1.5s 周期），通过通知 ID 去重比对检测新消息。
- 规避了直接事件订阅在未打包桌面应用中可能触发 `COMException` 的问题。

### 设置界面 (`SettingsWindow`)

- 基于 WPF-UI 的 `NavigationView` 实现左侧导航 + 右侧内容的分页布局。
- 使用 Mica 材质背景，遵循 Windows 11 Fluent Design 规范。
- 在窗口层级重写 `OnPreviewMouseWheel`，通过 `VisualTreeHelper.HitTest` 定位真实可滚动的 `ScrollViewer`，解决 NavigationView 嵌套 ScrollViewer 导致的滚轮事件被拦截问题。

### 全局配置 (`BarrageSettings`)

提供弹幕外观和行为的配置接口，支持 JSON 文件导入/导出：

- **字体**：FontFamily / FontSize / FontWeight / FontStyle / IsUnderlined
- **显示**：TextOpacity / ShowAppIcon / ShowAppName
- **背景**：ShowBackground / BackgroundColor / BackgroundOpacity / BackgroundCornerRadius
- **行为**：MaxTextLength / ScrollSpeedCharsPerSec / HighlightEllipsis / EllipsisColor

## 构建与运行

```bash
dotnet build
dotnet run
```

需要 Windows 10 19041 及以上版本。首次运行时需在系统设置中授予通知访问权限。

## 开发计划

- [x] 全屏透明叠加层 + 鼠标穿透
- [x] 多轨道弹幕调度算法
- [x] WinRT 通知监听（异步轮询）
- [x] DrawingVisual 渲染引擎 + 对象池
- [x] 设置界面基础布局（NavigationView 三页面）
- [x] 滚轮事件穿透修复
- [ ] 设置界面数据绑定（UI ↔ BarrageSettings）
- [ ] 弹幕预览区实时刷新
- [ ] MVVM 架构重构（CommunityToolkit.Mvvm）
- [ ] Win32 应用图标提取（基于 AUMID 的 ExtractAssociatedIcon 回退方案）
