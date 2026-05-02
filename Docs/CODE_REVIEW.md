# NotiFlow 代码审查报告

> 审查范围：全部手写源代码（排除 bin/obj 生成文件、IconTool 独立工具）
> 审查日期：2026-05-01
> 审查者：Claude Code (作为第三方独立审查)

---

## 🔴 高危问题（建议立即修复）

### 1. P/Invoke 声明错误：`GetWindowLong`/`SetWindowLong` 使用 `int` 返回值

**文件**：[NativeMethods.cs:27-29](NotiFlow/NativeMethods.cs#L27-L29)

```csharp
[DllImport("user32.dll")]
public static extern int GetWindowLong(IntPtr hwnd, int index);    // ❌ 应返回 IntPtr

[DllImport("user32.dll")]
public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);  // ❌ 参数应为 IntPtr
```

**问题**：在 64 位 Windows 上，`LONG_PTR` 是 8 字节，而 `int` 只有 4 字节。虽然 `GWL_EXSTYLE` 的实际值目前能放进 32 位，但这是经典的 P/Invoke 错误 — 如果未来扩展样式位使用了高位，会导致**值静默截断**，产生难以排查的怪异行为。

**建议修复**：改用 `GetWindowLongPtr`/`SetWindowLongPtr`，返回值和参数统一用 `IntPtr`（或 `nint`）。

```csharp
[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
public static extern IntPtr GetWindowLong(IntPtr hwnd, int index);

[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
public static extern IntPtr SetWindowLong(IntPtr hwnd, int index, IntPtr newStyle);
```

调用处（[MainWindow.xaml.cs:208-212](NotiFlow/MainWindow.xaml.cs#L208-L212)）也需要对应调整。

---

### 2. 通知轮询循环静默吞异常 + 无恢复机制

**文件**：[NotificationService.cs:39-82](NotiFlow/Services/NotificationService.cs#L39-L82)

```csharp
private async Task StartPollingLoopAsync()
{
    // 初始化时 catch { } ← 静默吞掉
    try { ... } catch { }

    while (true)  // 永久循环
    {
        await Task.Delay(1500);
        try
        {
            // 核心轮询逻辑
            var currentNotifications = await _listener!.GetNotificationsAsync(...);
            // ...
        }
        catch { }  // ← 任何异常都静默吞掉！
    }
}
```

**问题**：
- 外层的 `catch { }` 没有任何日志，如果 `GetNotificationsAsync` 持续失败（如权限被撤销、COM 对象损坏），通知功能会**永久静默失效**，用户毫无感知。
- 内部解析单条通知的 `catch { }` 也是空的 — 一条坏通知不会影响其他，这个是合理的，但外层的死循环 `catch { }` 不行。
- `_listener!` 使用了 null-forgiving 操作符，虽然逻辑上不会为 null，但没有防守。

**建议修复**：
```csharp
catch (Exception ex)
{
    Debug.WriteLine($"NotificationService polling error: {ex}");
    // 可选：连续失败 N 次后通过托盘气泡通知用户，或尝试重新初始化
}
```

---

### 3. `BarrageSettings.IsWorking` 的线程安全问题

**文件**：[BarrageSettings.cs:107](NotiFlow/BarrageSettings.cs#L107)

```csharp
public static bool IsWorking { get; set; } = false;
```

**问题**：`IsWorking` 在**后台线程**被读取（`NotificationService` 的 `OnNotificationReceived` 回调，[MainWindow.xaml.cs:127](NotiFlow/MainWindow.xaml.cs#L127)），但在**UI 线程**被写入（多处）。这是一个经典的 **data race**：
- 虽然 `bool` 在 x64 上是原子的，但没有内存屏障，编译器/CPU 可能将读取优化到寄存器中，导致后台线程永远读到旧值。
- 未来如果有人在 `IsWorking` 附近增加逻辑（如先读后写），会引入更复杂的竞态。

**建议修复**：将字段改为 `volatile` 或使用 `Interlocked` 操作，或者更好的方式 — 用一个信号机制（如 `CancellationTokenSource`）替代轮询检查布尔值。

最低成本修复：
```csharp
private static volatile bool _isWorking;
public static bool IsWorking { get => _isWorking; set => _isWorking = value; }
```

---

### 4. `StartPollingLoopAsync()` Fire-and-Forget 无生命周期管理

**文件**：[NotificationService.cs:34](NotiFlow/Services/NotificationService.cs#L34)

```csharp
_ = StartPollingLoopAsync();
```

**问题**：启动了一个永久运行的异步任务，但没有 `CancellationToken`，没有异常通知，没有停止方法。如果应用需要优雅退出，这个任务只会因为进程终止而强制中止。虽然目前由 `App.OnExit` 控制整个进程生命周期，但如果未来需要暂停/恢复通知监听，会很难扩展。

**建议修复**：让 `NotificationService` 实现 `IDisposable`，通过 `CancellationTokenSource` 控制循环生命周期。

---

## 🟡 中危问题（建议近期修复）

### 5. `BarrageSettings` 的全静态可变状态（Ambient Context 反模式）

**文件**：[BarrageSettings.cs](NotiFlow/BarrageSettings.cs)

当前所有配置都是 `public static` 属性。这导致：
- **不可测试性**：无法为单元测试创建隔离的配置实例
- **隐式耦合**：任何代码都可以随时修改任何配置，调试时难以追踪谁在何时改了什么
- **线程安全模糊**：有些属性在后台线程读取（`IsWorking`、`FontSize` 在渲染线程），有些在 UI 线程写入，完全没有一致的保护策略

**建议**：短期至少将静态类改为单例实例类（`BarrageSettings.Instance.FontSize`），长期考虑依赖注入。如果保持静态，至少用 [CallerMemberName] 添加修改日志。

---

### 6. `Process.GetProcessesByName` 使用显示名称而非进程名进行查找

**文件**：[NotificationService.cs:188](NotiFlow/Services/NotificationService.cs#L188)

```csharp
var process = System.Diagnostics.Process.GetProcessesByName(msg.AppName)
```

**问题**：`msg.AppName` 是通知的**显示名称**（如 "Microsoft Edge"），而 `GetProcessesByName` 需要的是**进程名**（如 "msedge"）。这两者几乎从不匹配。这意味着这个 Win32 进程图标回落机制在绝大多数情况下**静默失败**，永远不会命中。

再加上这个操作本身就非常缓慢（`GetProcessesByName` 会遍历所有进程），建议要么删除此分支，要么改为正确的启发式查找（如通过 `AUMID` 反查已安装应用的 `AppUserModelId` 注册表项）。

---

### 7. 废弃 API `FormattedText` 的使用

**文件**：[BarrageItem.cs:78-95](NotiFlow/Models/BarrageItem.cs#L78-L95)

```csharp
#pragma warning disable CS0618
var formattedText = new FormattedText(...);  // 旧版 API，已被标记为废弃
```

**问题**：.NET 4.0+ 引入了新的 `FormattedText` 构造函数重载，接受 `TextFormattingMode` 和 `TextRenderingMode` 参数。旧版 API 在未来的 .NET 版本中可能被移除。更重要的是，新版 API 允许指定 `TextFormattingMode.Ideal` vs `TextFormattingMode.Display`，对文字渲染质量有直接影响。

**建议修复**：切换到新版构造函数：
```csharp
var formattedText = new FormattedText(
    fullText, cultureInfo, FlowDirection.LeftToRight,
    typeface, fontSize, finalTxtBrush,
    pixelsPerDip,
    TextFormattingMode.Display);  // 屏幕渲染优化
```

---

### 8. 自动隐藏逻辑遗漏了 `_pendingMessages` 队列

**文件**：[MainWindow.xaml.cs:382-391](NotiFlow/MainWindow.xaml.cs#L382-L391)

```csharp
if (_activeItems.Count == 0 && _spawnQueue.IsEmpty && _readyQueue.Count == 0)
{
    if (!BarrageSettings.IsWorking && this.IsVisible)
    {
        this.Hide();
    }
    return;
}
```

**问题**：自动隐藏的判断条件没有包含 `_pendingMessages` 队列。在极端场景下（轨道全满 → 所有弹幕同时出屏 → 轨道释放 → `TryFlushQueue` 将 pending 消息移入 `_readyQueue` 但尚未 commit），理论上不会有问题，因为 `TryFlushQueue` 会将它们移到 `_readyQueue`。但如果 `TryFlushQueue` → `PrepareBarrage` 在轨道释放时被调用，而 `_readyQueue` 恰好在同一帧已被 commit 消耗，这个时序上就存在一个理论窗口。建议加上 `_pendingMessages.Count == 0` 作为额外保护。

---

### 9. 滚动穿透查找不能跨越 Popup 视觉树边界

**文件**：[SettingsWindow.xaml.cs:74-85](NotiFlow/SettingsWindow.xaml.cs#L74-L85)

```csharp
private static ScrollViewer? FindScrollableParent(DependencyObject child)
{
    // 从命中元素向上遍历
    while (current != null)
    {
        if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
            return sv;
        current = VisualTreeHelper.GetParent(current);  // ← 不会跨越 Popup 根边界
    }
    return null;
}
```

**问题**：代码注释说使用 `OriginalSource` 可以穿透 Popup，但 `VisualTreeHelper.GetParent` **无法跨越 Popup 的视觉树根节点**回到主窗口的视觉树。如果 ComboBox 的下拉框内容比可见区域长，用户在弹出区域滚轮时，`FindScrollableParent` 会从 Popup 内部元素向上遍历，但遇到 PopupRoot 时 `GetParent` 返回 `null`，从而永远找不到 NavigationView 的 ScrollViewer。

**建议修复**：先尝试在 Popup 内部找 ScrollViewer，如果找不到，再通过 `Window.GetWindow(child)` 获取所属窗口，从窗口级别重新向下查找。

---

## 🟢 低危问题 / 优化建议

### 10. `HttpClient` 未复用

**文件**：[UpdateService.cs:25](NotiFlow/Services/UpdateService.cs#L25)

```csharp
using var client = new HttpClient();
```

虽然更新检查频率很低（静默启动一次 + 手动触发），不会造成 socket 耗尽，但最佳实践中 `HttpClient` 应为静态单例。短期无实际影响，但建议改为 `static readonly` 实例。

---

### 11. UWP 图标 2.5x 硬编码缩放系数

**文件**：[BarrageItem.cs:138](NotiFlow/Models/BarrageItem.cs#L138)

```csharp
dc.PushTransform(new ScaleTransform(2.5, 2.5, ...));
```

2.5x 是一个经验值，用于补偿 UWP 图标中的透明垫层。但这个值并非对所有应用都正确（不同应用图标在 256x256 画布中的实际内容占比差异很大）。建议将此系数设为可配置，至少提取为常量。

---

### 12. 对象池中的 `BarrageItem` 没有 `Reset()` 方法

**文件**：[MainWindow.xaml.cs:312-318](NotiFlow/MainWindow.xaml.cs#L312-L318) 和 [BarrageItem.cs](NotiFlow/Models/BarrageItem.cs)

```csharp
item = _pool.Dequeue();
item.IsAlive = true;
item.TrackReleased = false;
// 其他状态（CurrentX, CurrentY, Offset 等）由 PrepareBarrage 重设
```

虽然是手动重设了关键状态，但缺少显式的 `Reset()` 方法。如果未来 `BarrageItem` 增加了新字段而忘记在取出时重置，就会出现脏数据。建议加一个 `Reset()` 方法集中管理。

---

### 13. `ExportConfig` 保存圆角时丢失信息

**文件**：[BarrageSettings.cs:136](NotiFlow/BarrageSettings.cs#L136)

```csharp
BackgroundCornerRadius = BackgroundCornerRadius.TopLeft, // 简化为统一圆角数值
```

当前四个角的圆角值始终相等（都来自同一个 Slider），所以这只是理论上的数据丢失。但如果未来改为独立四角圆角，这里会导致数据丢失。

---

### 14. `NotificationService` 没有实现 `IDisposable`

轮询循环永远运行，没有办法从外部停止。如果未来需要暂停通知（例如设置中开关），只能依赖 `BarrageSettings.IsWorking` 在消费者端过滤，但轮询本身仍在空转。

---

### 15. `SettingsPage.xaml.cs` 中 `LostKeyboardFocus` 触发不可控

**文件**：[SettingsPage.xaml.cs:128-135](NotiFlow/SettingsPage.xaml.cs#L128-L135)

快捷键捕获模式下，如果系统弹窗（如输入法候选窗、Windows 通知弹窗）抢走焦点，捕获会意外中止。建议增加超时机制，或者改用 `PreviewKeyDown` 的 Tunnel 事件 + `Key.System` 的 IME 容错（已经做了），并移除对 `LostKeyboardFocus` 的依赖，改为 ESC 或超时自动退出。

---

### 16. "1像素欺骗法" 脆弱

**文件**：[MainWindow.xaml.cs:201-205](NotiFlow/MainWindow.xaml.cs#L201-L205)

```csharp
this.Height = SystemParameters.PrimaryScreenHeight - 1;
```

用减 1 像素来规避第三方任务栏软件的误判，这是一种经验性的 workaround。没有文档说明哪些任务栏软件会受影响，也没有办法在运行时动态检测是否需要这个 workaround。如果某款任务栏软件需要减 2 像素才能规避，这里就会失效。

---

### 17. 代码复用机会

**a) 弹幕运动物理逻辑重复**：
[MainWindow.xaml.cs:358-423](NotiFlow/MainWindow.xaml.cs#L358-L423) 的渲染循环和 [CustomPage.xaml.cs:92-117](NotiFlow/Views/Pages/CustomPage.xaml.cs#L92-L117) 的预览循环几乎是一样的模式（`dt` 计算 → X 位移 → Offset 更新 → 越界判断）。建议抽取一个 `BarragePhysicsEngine` 类。

**b) 配置保存逻辑分散**：
`SettingsViewModel` 中每个属性的 `partial void OnXxxChanged` 都调用 `TriggerSaveAndPreview()`。大量样板代码。可以利用 CommunityToolkit.Mvvm 的 `[ObservableProperty]` 的 `OnPropertyChanged` 回调，或者用源生成器的批量通知机制减少重复。更好的方案是在 `BarrageSettings` 自身属性变更时触发保存，而不是在 ViewModel 层重复反射。

**c) `ForceGreyBackground` 递归遍历很重**：
[UpdateService.cs:161-176](NotiFlow/Services/UpdateService.cs#L161-L176) 在每个 MessageBox 的 Loaded 事件中递归遍历整个视觉树。MessageBox 的视觉树很小，所以目前没有性能问题，但递归实现不够优雅，建议用 `VisualTreeHelper` 的静态遍历替代手写递归。

---

## 📋 建议的改进优先级

| 优先级 | 问题 | 影响 |
|--------|------|------|
| P0（立即） | P/Invoke int 截断 | 64位潜在数据损坏 |
| P0（立即） | 轮询循环静默吞异常 | 通知功能永久失效无感知 |
| P1（本周） | IsWorking 线程安全 | 竞态条件 |
| P1（本周） | Fire-and-forget 无生命周期 | 优雅退出困难 |
| P2（本月） | 静态可变状态重构 | 可测试性/可维护性 |
| P2（本月） | Process.GetProcessesByName 误用 | 图标回落逻辑空转 |
| P3（下个版本） | 废弃 API、代码复用等 | 长期技术债 |

---

## 验证方法

1. **P/Invoke 修复后**：在 x64 Release 构建下运行，确认窗口透明、鼠标穿透、防截屏功能正常
2. **轮询异常处理修复后**：故意在运行时撤销通知权限（Windows 设置 → 隐私 → 通知），观察托盘是否有气泡提示或日志输出
3. **IsWorking 修复后**：高速切换工作状态（快捷键 Ctrl+Shift+D 反复按），观察弹幕是否正确响应开关
4. **代码复用重构后**：确认预览弹幕和真实弹幕的行为完全一致（速度、越界循环、视觉样式）
