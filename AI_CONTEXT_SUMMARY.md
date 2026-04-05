# NotiFlow 开发上下文摘要

> 本文档用于在新会话中快速恢复项目上下文，减少重复研究。

## 项目概述

NotiFlow 是一款 .NET 8 + WPF 桌面应用。核心功能是监听 Windows 系统通知，以弹幕形式在全屏透明置顶窗口中横向滚动展示。

当前阶段：渲染引擎和通知监听已完成，设置界面 UI 布局已完成，下一步是实现数据绑定。

## 架构概览

```
启动入口 (App.xaml → SettingsWindow.xaml)
│
├── SettingsWindow          设置界面主窗口（FluentWindow + NavigationView）
│   ├── CustomPage          弹幕外观自定义
│   ├── SettingsPage        通用设置
│   └── AboutPage           关于信息
│
├── MainWindow              弹幕叠加层（全屏透明 Topmost 窗口）
│   └── BarrageEngineHost   DrawingVisual 宿主
│       └── BarrageItem[]   渲染单元（对象池管理）
│
├── NotificationService     异步轮询式通知监听（1.5s 周期）
└── BarrageSettings         静态全局配置 + JSON 持久化
```

## 关键技术决策

| 决策 | 原因 |
|------|------|
| `DrawingVisual` 替代控件树 | WPF 控件在多弹幕场景下 GPU 占用过高 |
| 异步轮询替代事件订阅 | `NotificationChanged` 事件在未打包 Win32 应用中会触发 COMException |
| `WS_EX_TRANSPARENT` 鼠标穿透 | 弹幕窗口不能拦截用户的鼠标操作 |
| 窗口级 `OnPreviewMouseWheel` 重写 | NavigationView 内部 ScrollViewer 会拦截页面的滚轮事件 |

## 已解决的关键问题

### 滚轮滚动失效（已修复）

**现象**：设置界面无法使用鼠标滚轮滚动，仅滚动条可用。

**根因**：WPF-UI 的 `NavigationView` 内部包含一个自带的 `ScrollViewer`。页面内部又嵌套了一个 `ScrollViewer`（MainScrollViewer）。由于外层 ScrollViewer 在布局测量时向内层传递了无限高度，导致内层 `ScrollableHeight = 0`，认为自身无需滚动。`VisualTreeHelper.HitTest` 命中页面内部元素后向上查找到的第一个 ScrollViewer 就是这个无效的内层 ScrollViewer。

**修复方案**：在 `SettingsWindow.xaml.cs` 中重写 `OnPreviewMouseWheel`，使用 `FindScrollableParent()` 跳过 `ScrollableHeight == 0` 的 ScrollViewer，定位外层真正可滚动的 ScrollViewer 并驱动滚动。

## 关键文件索引

| 文件 | 职责 |
|------|------|
| `MainWindow.xaml.cs` | 弹幕动画循环、轨道管理、对象池 |
| `SettingsWindow.xaml.cs` | 设置窗口、全局滚轮事件处理 |
| `Models/BarrageItem.cs` | 弹幕绘制逻辑（FormattedText 预烘焙） |
| `Services/NotificationService.cs` | WinRT 通知监听、图标提取 |
| `BarrageSettings.cs` | 全局配置（字体/颜色/速度/背景等） |
| `Views/Pages/*.xaml` | 设置子页面布局 |

## 下一步工作

1. **数据绑定**：将 CustomPage / SettingsPage 中的 UI 控件（ToggleSwitch、Slider、ComboBox 等）与 `BarrageSettings` 的属性双向绑定。
2. **弹幕预览**：在 CustomPage 的预览区实现实时动态效果。
3. **MVVM 重构**：引入 CommunityToolkit.Mvvm，建立 ViewModel 层。

## 配置属性速查

### BarrageSettings 属性列表

**字体**：FontFamily, FontSize, FontWeight, FontStyle, IsUnderlined, TextOpacity

**显示**：ShowAppIcon, ShowAppName

**背景**：ShowBackground, BackgroundColor, BackgroundOpacity, BackgroundCornerRadius

**行为**：MaxTextLength, ScrollSpeedCharsPerSec, HighlightEllipsis, EllipsisColor
