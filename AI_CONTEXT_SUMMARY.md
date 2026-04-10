# NotiFlow 开发上下文摘要

> 本文档用于在新会话中快速恢复项目上下文，减少重复研究。

## 项目概述

NotiFlow 是一款 .NET 8 + WPF 桌面应用。核心功能是监听 Windows 系统通知，以弹幕形式在全屏透明置顶窗口中横向滚动展示。

当前阶段：渲染引擎和通知监听已完成，高级设置界面 UI 布局已全面对齐与确立，完成了所有控件的原生集成。下一步核心是完成配置前后端的数据持久化大一统与完整测试。

## 架构概览

```
启动入口 (App.xaml → SettingsWindow.xaml)
│
├── SettingsWindow          设置界面主窗口（FluentWindow + NavigationView）
│   ├── CustomPage          弹幕外观自定义（已包含各项高级自定义配置及布局弹性拉伸）
│   ├── SettingsPage        通用设置
│   └── AboutPage           关于信息
│
├── MainWindow              弹幕叠加层（全屏透明 Topmost 窗口）
│   └── BarrageEngineHost   DrawingVisual 宿主
│       └── BarrageItem[]   渲染单元（对象池管理）
│
├── NotificationService     异步轮询式通知监听（1.5s 周期）
└── BarrageSettings         静态全局配置 + JSON 持久化（待全面绑定）
```

## 关键技术决策

| 决策 | 原因 |
|------|------|
| `DrawingVisual` 替代控件树 | WPF 控件在多弹幕场景下 GPU 占用过高 |
| 异步轮询替代事件订阅 | `NotificationChanged` 事件在未打包 Win32 应用中会触发 COMException |
| `WS_EX_TRANSPARENT` 鼠标穿透 | 弹幕窗口不能拦截用户的鼠标操作 |
| 窗口级 `OnPreviewMouseWheel` 重写 | NavigationView 内部 ScrollViewer 会拦截页面的滚轮事件 |
| 弹性栅格列等齐布局 | 移除了 `VerticalAlignment="Top"`，利用 WPF Grid 天然的 Stretch 使得外观/内容双栏绝对底面对齐 |
| 暂缓使用假前端预览动画 | 考虑到代码复用度，放弃在前端实现复杂的跑马灯动画，改用后续直接通过 `BarrageEngineHost` 的底层通讯触发真实预览 |

## 已解决的关键问题

### 滚轮滚动失效（已修复）

**现象**：设置界面无法使用鼠标滚轮滚动，仅滚动条可用。
**根因**：WPF-UI 的 `NavigationView` 内部包含一个自带的 `ScrollViewer` 与页面的发生拦截冲撞。
**修复方案**：在 `SettingsWindow.xaml.cs` 中重写 `OnPreviewMouseWheel` 跳过高度校验失败的底层对象，直接驱动真实 ScrollViewer 滚动。

## 关键文件索引

| 文件 | 职责 |
|------|------|
| `MainWindow.xaml.cs` | 弹幕动画循环、轨道管理、对象池 |
| `SettingsWindow.xaml.cs` | 设置窗口、全局滚轮事件处理 |
| `Models/BarrageItem.cs` | 弹幕绘制逻辑（FormattedText 预烘焙） |
| `Services/NotificationService.cs` | WinRT 通知监听、图标提取 |
| `BarrageSettings.cs` | 全局配置（字体/颜色/速度/背景等） |
| `Views/Pages/CustomPage.xaml` | 高级弹幕效果自定义页面（含完全动态对其的网格布局） |
| `BARRAGE_ENGINE_ARCHITECTURE.md`| 弹幕渲染引擎重构与性能优化（DrawingVisual+对象池）的专项分析报告 |
| `overlay_tech_research.md` | 基于 Win2D/DirectComposition 等底层方案解决全屏透明 DWM 性能瓶颈的终极架构路线图 |

## 下一步工作

1. **前后端接口打通与真实预览**：基于已确立完毕的所有 UI 组件，实现统一的事件抛出，跨模块调用 `BarrageEngineHost` 实现无缝的弹幕渲染预览。
2. **数据双向持久化**：将 UI 控件事件与 `BarrageSettings` 对象映射逻辑进行收尾封装，支持退出/重启保存。
3. **MVVM 结构重组 (可选)**：根据后续业务发展评估是否引入 CommunityToolkit.Mvvm，或维持现在的轻量 Code-Behind 直调形态。

## 配置属性速查

### BarrageSettings 可用属性

**字体**：FontFamily, FontSize, FontWeight, FontStyle, IsUnderlined, TextOpacity
**显示**：ShowAppIcon, ShowAppName
**背景**：ShowBackground, BackgroundColor, BackgroundOpacity, BackgroundCornerRadius
**行为**：MaxTextLength (范围 5~50), ScrollSpeedCharsPerSec (滑块范围 5-30), HighlightEllipsis, EllipsisColor
