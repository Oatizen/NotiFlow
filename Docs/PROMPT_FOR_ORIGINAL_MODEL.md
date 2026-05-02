# 给原始开发模型的提示词

> 本文件由 Claude Code 编写，旨在交付给最初开发 NotiFlow 的 AI 模型，对该项目的代码审查报告进行二次评估和修复。

---

## 背景

你之前为项目 NotiFlow 编写了全部代码。该项目是一个 .NET 8 + WPF 桌面应用，功能是监听 Windows 通知中心的 Toast 通知，以弹幕形式在全屏透明置顶窗口中横向滚动展示。

项目处于 1.0.1 正式发布阶段，目前可以正常运行，没有严重 Bug。

## 发生了什么

我请了另一个 AI（Claude Code）对 NotiFlow 的**全部手写源代码**做了一次独立的第三方代码审查，目的是：
1. 发现可能有隐患的代码（隐形 Bug、潜在的未来技术债）
2. 发现可以重构/合并/复用的地方
3. 给出每个问题的理由和建议修复方案

这份审查报告已经保存在项目根目录下的 **`CODE_REVIEW.md`** 中。

## 你的任务

请你**仔细阅读 `CODE_REVIEW.md` 中的每一项发现**，然后分别完成以下工作：

### 第一步：对每一项建议给出你的独立判断

请逐条回复，格式如下：

| 编号 | 审查建议 | 你的判断 | 理由 |
|------|---------|---------|------|
| 1 | P/Invoke int 截断 | 同意 / 不同意 / 部分同意 | 写清楚你是认同还是反对，为什么 |
| 2 | 轮询循环静默吞异常 | ... | ... |
| ... | ... | ... | ... |

如果你**不同意**某项建议，请务必给出充分的技术理由。比如说：
- 审查者可能误解了某段代码的设计意图
- 建议的修复方案在 WPF/WinRT 环境下可能引入新问题
- 代码之所以那样写是刻意为之（如为了规避某个已知的系统级 Bug），并说明那个 Bug 是什么

### 第二步：对你认同的建议实施修复

对你**同意**和**部分同意**的建议，写出你的修改方案。

修复时请注意：
- 确保项目仍然能通过编译（`dotnet build`）
- 不要引入新的警告或错误
- 如果某条建议的修复方案和审查者提出的不同（你认为有更好的改法），按你的方式来做，并在代码注释中简要说明理由
- 不要修改 `CODE_REVIEW.md` 和本文件

### 第三步：将你的判断和修改总结写回

在项目根目录创建一个新文件 `CODE_REVIEW_RESPONSE.md`，包含：
1. 第一步中的逐条判断表格
2. 实际做了哪些修改的清单（文件 + 改动摘要）
3. 如果有你不同意的建议，详细解释为什么

---

## 项目关键信息速查

| 项目 | 详情 |
|------|------|
| 平台 | .NET 8 + WPF |
| 目标框架 | `net8.0-windows10.0.19041.0` |
| UI 框架 | WPF-UI 4.2.0 |
| MVVM 工具 | CommunityToolkit.Mvvm 8.4.2 |
| 关键 NuGet | `System.Drawing.Common` 10.0.5 |
| 源码目录 | 项目根目录下的所有 `.cs` / `.xaml` 文件（排除 `IconTool/`、`Backup/`、`bin/`、`obj/`） |

### 项目架构速查

```
NotiFlow/
├── App.xaml.cs                    # 启动入口、单实例Mutex、托盘初始化
├── MainWindow.xaml.cs             # 弹幕叠加层（全屏透明Topmost）+ 物理引擎
├── SettingsWindow.xaml.cs         # 设置主窗口（FluentWindow）
├── BarrageSettings.cs            # 全局静态配置 + JSON持久化
├── NativeMethods.cs              # Win32/DWM API P/Invoke
├── Models/
│   ├── NotificationMessage.cs    # 通知数据模型
│   ├── BarrageItem.cs            # 弹幕渲染单元（DrawingVisual）
│   ├── BarragePreviewMessage.cs  # MVVM 消息
│   └── SettingsViewModel.cs      # 设置页 ViewModel
├── Rendering/
│   └── BarrageEngineHost.cs      # VisualCollection 底层宿主
├── Services/
│   ├── NotificationService.cs    # 异步轮询式通知监听
│   ├── TrayIconService.cs        # 系统托盘 + 全局热键
│   └── UpdateService.cs          # GitHub API 版本检查
├── Converters/
│   └── BooleanToAppearanceConverter.cs
└── Views/Pages/
    ├── CustomPage.xaml.cs         # 弹幕外观自定义 + 预览
    ├── SettingsPage.xaml.cs       # 通用设置 + 主题
    └── AboutPage.xaml.cs          # 关于页面
```

### 关键设计决策（原始模型做出的）

- **`DrawingVisual` 替代 WPF 控件树**：控件树在多弹幕场景下 GPU 占用过高
- **异步轮询替代事件订阅**：`NotificationChanged` 事件在未打包 Win32 应用中会触发 COMException (0x80070490)
- **`WS_EX_TRANSPARENT` 鼠标穿透**：弹幕窗口不能拦截用户鼠标操作
- **双阶段渲染流水线 (Prepare/Commit)**：分解大量通知涌入时的高 CPU 峰值
- **Mutex 全局互斥锁**：防止多开导致底层单文件共享资源冲突
- **DWM 合成透明替代 `AllowsTransparency`**：避免 `WS_EX_LAYERED` 导致 `SetWindowDisplayAffinity` 失效
- **`CoerceBackground` 强制回调**：锁定 MainWindow 背景为 Transparent，免疫 WPF-UI 主题切换的背景污染

---

## 补充上下文

Claude Code 的审查是完全独立的第三方视角。它不了解你在开发时的具体约束和考量。所以请用你的第一手开发经验来审视这些建议——可能有遗漏、可能有过度担忧的地方、也可能发现了你确实没注意到的真问题。

请以建设性的态度来处理这份审查报告，目标是一致的：让 NotiFlow 的代码更健壮、更可维护。
