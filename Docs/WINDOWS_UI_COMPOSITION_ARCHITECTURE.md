# Windows.UI.Composition (WinRT) 弹幕渲染架构深度解析

## 架构演进背景

在项目的早期阶段，我们将弹幕渲染从基于 WPF 控件（`TextBlock` + `DoubleAnimation`）的重度架构迁移到了基于 `DrawingVisual` 的轻量级架构。这一步极大地减少了由于 WPF 布局和测量系统带来的 CPU 负担。

然而，在面对几十上百条并发弹幕，甚至系统高负载的情况下，基于 WPF (DirectX 9 时代的 GDI/WPF 混合渲染通道) 的渲染依然存在瓶颈。最主要的问题是：WPF 的分层窗口 (`AllowsTransparency="True"`) 会在底层创建一个 `WS_EX_LAYERED` 窗口，当窗口内容频繁更新时，系统会产生大量的重绘开销和内存拷贝（CPU 到 GPU 的回传）。

为了追求极致的性能，我们进行了第二次底层级别的架构跃迁：**彻底绕过 WPF 渲染管线，直接使用 Windows 10/11 的原生 `Windows.UI.Composition` API，将渲染全权交由 DWM (桌面窗口管理器) 接管。**

## 核心技术方案

### 1. 拦截与剥离 WPF 窗口渲染
我们将弹幕覆盖层窗口从原有的 WPF `Window` 降级剥离，通过 P/Invoke 直接调用 User32 API (`CreateWindowEx`) 创建原生的 Win32 窗口。
最关键的一步是给该窗口打上了 `WS_EX_NOREDIRECTIONBITMAP` 标志。这个标志会告诉 Windows 不要为这个窗口分配重定向位图（即关闭系统的自动缓冲池），这意味着普通的 GDI/User32 将无法再向该窗口绘制任何内容。它完全变成了一个为了承载现代 GPU 渲染图层的“空壳”。

### 2. 构建 WinRT Composition 视觉树
通过底层的 `ICompositorDesktopInterop` 接口，我们将 `Windows.UI.Composition.Compositor` 与我们创建的 Win32 HWND 进行绑定，创建了 `DesktopWindowTarget`。
在此基础上，我们构建了一个纯 GPU 的视觉树，每个弹幕不再是 WPF 中的对象，而是 `SpriteVisual`。这种视觉对象完全由操作系统的 DWM 进程进行管理和硬件级合成，绕过了我们应用自身繁重的渲染线程。

### 3. 使用 Win2D 绘制纹理图集 (Texture Atlas)
我们使用 `Win2D` (Microsoft.Graphics.Canvas) 的底端互操作 API，基于 `CompositionGraphicsDevice` 创建了一块巨大的 GPU 纹理画布 (`CompositionDrawingSurface`)。
当一条新弹幕产生时，我们并不创建新的文本对象。而是使用 `CanvasDrawingSession`，将文字、图标、背景、甚至文字阴影一次性“画”进这块纹理图集的某个特定矩形区域中。随后，我们使用 `CompositionSurfaceBrush` 将这块区域映射到对于的 `SpriteVisual` 表面。
这种做法极大地复用了显存（VRAM），所有的弹幕都共用同一张后台位图（Atlas），实现了真正意义上的 **Draw Call 极致压缩**。

### 4. GPU 级动画驱动
与以前依赖 CPU 每秒 60 次计算 X 坐标并提交渲染不同，我们使用了 `Windows.UI.Composition` 自带的 `Vector3KeyFrameAnimation`。
我们只需向显卡声明：“这个 `SpriteVisual` 需要在 10 秒内，从 X=1920 移动到 X=-500”，接下来整个平滑滚动计算、以及 144Hz 甚至更高刷新率的插值画面，都完全由独立于应用主线程的系统 DWM 进程（显卡层）去执行。
哪怕我们的 NotiFlow 主程序线程发生假死或垃圾回收停顿，屏幕上的弹幕也会丝般顺滑地继续飘过。

## 妥协与规避策略 (截图工具冲突)

由于使用了 `WS_EX_NOREDIRECTIONBITMAP` 并将渲染完全托付给 DWM，User32 子系统彻底失去了这块窗口的“像素级 Alpha 通道数据”。
这导致了一个附带的技术挑战：传统使用 `EnumWindows` 和透明度判定来进行窗口抓取的截图工具（如微信截图、QQ截图），会将我们这块全屏透明弹幕视作一个“实体的全屏遮罩”，从而在用户想要使用“按窗口截图”功能时，错误地高亮并阻挡下层的应用窗口。

对此，我们采取了如下规避与妥协：
1. **隐匿标题特征**：将承载该 `Composition` 的 Win32 窗口的 `lpWindowName` 设为 `string.Empty`。部分截图工具在遍历时如果发现无边框且无标题的窗口，会把它当做系统内部隐蔽组件而跳过。
2. **逻辑权衡**：这是为了获得当前架构 0.01% CPU 占用率所必须接受的设计牺牲。如果必须精细截图下层窗口，需暂时在托盘将其关闭。
