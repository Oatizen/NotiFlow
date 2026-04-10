# 弹幕渲染引擎架构与重构分析

## 背景与痛点
在传统的 WPF 开发中，开发人员通常会使用 `Canvas` 配合大量的 `TextBlock` 或 `UserControl` 来实现弹幕的横向滚动效果。通过为每一个控件绑定 `DoubleAnimation` (或是 `Storyboard`) 来驱动位移。

这种做法在少量弹幕（例如 < 10 条）时表现良好。但是如果同一屏幕内出现海量密集通知弹幕（如几十条甚至上百条），将会遭遇极大的性能瓶颈：
1. **Visual Tree 爆炸**：每个控件不仅自身是一个 Visual，它内部还有 Text、Border 等多层视觉结构。WPF 在进行 Layout (布局测量) 和 Render 时开销成倍增加。
2. **重绘风暴 (Render Thread Overload)**：大量的独立动画会导致 WPF 的独立渲染线程长期处于高负荷，造成电脑风扇狂转、掉帧、甚至 UI 线程假死。
3. **GC 压力**：不断地 `new TextBlock()` 创建，再在它飘出屏幕外时 `Remove`，会产生海量的生命周期极短的托管内存碎片，导致垃圾回收器（GC）频繁介入卡顿。

## 核心重构决策：DrawingVisual + 对象池化

为了彻底解决上述痛点，我们舍弃了 WPF 中的“控件架构”，转而走向偏向游戏引擎开发的“低级绘制 API 架构”。

### 1. 使用 `DrawingVisual` 和底层的 `DrawingContext`
我们将 MainWindow 作为唯一的一个顶层控件（宿主），将所有的弹幕逻辑剥离出逻辑树。
抛弃 `TextBlock`，直接使用底层的 `FormattedText` 与 `DrawingContext.DrawText()` 将文字绘制在轻量级的 `DrawingVisual` 表面上。
**优势**: 
由于所有这些绘制脱离了 WPF 繁重的 Measure 和 Arrange 布局计算周期，开销几乎缩减了近一个数量级。

### 2. 帧驱动渲染 (Frame-based Rendering)
彻底废弃通过绑定 `DoubleAnimation` 计算 X 坐标的形式。
我们引入了类似游戏循环中的机制，通过订阅 `CompositionTarget.Rendering` 回调事件，在显卡的每一次 VSync (垂直同步) 刷新帧时，批量遍历一次所有当前存活弹幕的位置，统一减去 `(Speed * deltaTime)` 的位移，然后统一执行重绘（Redraw）。
**优势**: 
确保每一帧的所有弹幕位置计算和绘制定格在同一时间发生，大幅减少 CPU-GPU 间的异步指令拥堵，实现丝般顺滑的 60FPS / 120FPS 甚至更高的连贯体验。

### 3. 对象池机制 (Object Pool) 
我们在 `BarrageEngineHost` / `MainWindow` 等实现域维护一个固定的集合或使用 `Stack<BarrageItem>`，而不是每次来一条弹幕就 `new` 一个新的实例。
当屏幕外的弹幕实例结束其生命周期后，会被立即重置为闲置状态（Inactive），下一条新弹幕到来时直接复用它的壳进行 `Text` 的替换。
**优势**: 
把高频碎片的内存分配过程转化为内部状态变更，彻底避免频繁 GC 引起的画面周期性卡顿。

## 未来（下阶段）计划
当高级自定义（CustomPage）页面的双向绑定打通后，此引擎将承担起一个纯正的渲染器（Renderer）职责。你可以通过暴露 `EngineHost.SendTestBarrage()` 之类的独立接口，在不破坏任何结构的前提下，在前端实时渲染动态预览的示例弹幕。
