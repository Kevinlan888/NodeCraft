# NodeCraft.Vision Virtual Camera 帧率配置设计

状态：已确认方向，待实现评审

日期：2026-08-15

## 1. 背景和目标

Virtual Camera 在连续执行时实现了 `IFlowIterationSource`，原先会在没有其他 iteration source 时被执行控制器高速反复调用。对于大图或多图文件夹，图片解码和预览渲染速度远低于迭代生产速度，最新预览结果会持续被后续结果淘汰，表现为 `imagePath` 快速变化而图片预览停滞，停止执行后最后一帧才显示出来。

本变更为 Virtual Camera 增加可持久化的目标帧率配置，默认值为 18 FPS，用于限制该节点连续输出的帧率上限，给图片解码和预览渲染留下稳定的处理窗口。

## 2. 非目标

- 不修改全局 `FlowExecutionController` 的连续迭代节奏；其他 iteration source 不受 Virtual Camera 配置影响。
- 不改变 Virtual Camera 的图片来源解析、排序、预加载、动态加载、输出端口或 `FrameId` 语义。
- 不把帧率实现为预览队列的丢帧策略；预览队列仍然只负责合并待渲染结果。
- 不承诺硬实时调度或精确的墙钟帧率；帧率是连续帧的最大启动频率，图片解码和图执行耗时可能使实际频率更低。
- 不为单次 `RunOnce` 增加人为首帧等待。

## 3. 配置契约

`VirtualCameraNodeModel` 新增公开可读写属性：

```csharp
public int FrameRate { get; set; } = 18;
```

配置约束：

- `FrameRate` 必须是正整数。
- 默认值为 `18`。
- `GraphModelXmlSerializer` 通过现有公开属性机制自动将其持久化到 `<Properties>`，不修改 graph format version。
- 缺少 `FrameRate` property 的旧 graph 加载后使用默认值 18。
- 编辑器增加帧率输入控件；非整数、空值或非正数不更新 NodeModel，也不触发 graph-changed 通知。

`WriteWorkflowInputs` 增加固定运行时 key：

| NodeModel property | WorkflowNode input key | 类型 |
| --- | --- | --- |
| `FrameRate` | `frameRate` | `int` |

Virtual Camera 的输出和其他五个既有配置 key 保持不变。

## 4. 运行时节拍

`VirtualCameraExecutor.StartSessionAsync` 从 `frameRate` 读取并验证正整数。缺失、类型错误或小于等于 0 的值都作为配置错误失败；不能静默回退到 18，也不能让 Dynamic/Preload 分支出现不同的校验规则。

节拍由 `VirtualCameraExecutor.PrepareIterationAsync` 在节点内部实现，而不是由全局执行控制器实现：

1. session 启动后第一次 `PrepareIterationAsync` 立即选择并准备第一张图片，不等待一个帧间隔。
2. 每次后续 Prepare 在选择下一张图片前等待，确保相邻帧的启动时间间隔至少为 `TimeSpan.FromSeconds(1.0 / FrameRate)`。
3. 等待使用传入的 `CancellationToken`，停止或取消执行时应立即结束等待并传播 `OperationCanceledException`。
4. 图像加载或 Flow graph 执行耗时超过目标间隔时，不额外压缩下一帧时间；实际频率可以低于配置值。
5. session 停止、启动失败或取消后清理节拍状态；重新启动从“首帧立即输出”开始。

实现可以使用基于单调时钟的下一帧 deadline，避免简单累加 `Task.Delay` 导致执行耗时造成不必要的漂移。等待前后必须保留 cancellation 检查，不能因为节拍等待吞掉取消。

## 5. 依赖边界和可测试性

默认延迟实现使用 `Task.Delay`，但执行器允许测试注入一个等价的 delay delegate。生产代码不暴露新的 Flow 端口或 UI 依赖；测试可以记录请求的 delay 时长和 cancellation token，而不需要真实等待 55.56ms。

节拍状态只属于 executor 的当前 session，不写入 graph、不跨 session 复用，也不影响 `_index`、entry ordinal、缓存图片或图片元数据。首帧仍必须使用初始 entry ordinal 0。

## 6. 备选方案和取舍

### 方案 A：Virtual Camera executor 内部节流（采用）

帧率配置和产生图片的节点位于同一边界，只有 Virtual Camera 的连续 Prepare 受影响；现有 Flow runtime 和其他节点无需理解视觉设备帧率。测试可以在执行器边界注入虚拟延迟，验证首帧、间隔、取消和重启语义。

### 方案 B：在 `FlowExecutionController` 对所有 iteration source 统一延迟

能够快速限制循环速度，但会把 Virtual Camera 的配置传播到通用执行层，也会改变真实相机或未来 iteration source 的运行行为；当前需求只针对 Virtual Camera，不采用。

### 方案 C：只调整 `LatestPreviewRenderQueue`

可以减少预览线程被高速结果饿死的概率，但不能限制图片解码、Flow graph 执行和 `imagePath` 输出的生产速度，也没有真正的 Virtual Camera 帧率语义；不采用。

## 7. 测试设计

新增或扩展现有 Virtual Camera 控制台测试，至少覆盖：

1. 默认 `FrameRate == 18`，自定义值通过 XML round-trip 保存和恢复。
2. `WriteWorkflowInputs` 写入 `frameRate` 且类型为 `int`；旧 XML 缺少该属性时仍恢复为 18。
3. `StartSessionAsync` 拒绝缺失、错误类型、0 和负数配置。
4. 首次 Prepare 不调用 delay；后续 Prepare 请求约 `1000 / 18` ms 的间隔，并按自定义 FPS 计算间隔。
5. 注入的 delay 收到原始 cancellation token；等待期间取消会传播，且不会推进图片 index/current 状态。
6. 停止后重新启动首帧仍立即输出 ordinal 0，节拍状态不跨 session 泄漏。
7. 使用真实 graph integration/执行器测试确认已有输出契约和 `imagePath` 行为未改变；帧率只降低连续迭代速度，不改变图片顺序。
8. 编辑器初始化不产生 graph change；有效帧率修改触发一次通知，无效输入不修改模型。

测试不通过真实睡眠验证墙钟时间；延迟注入 seam 只验证请求的 deadline/间隔计算和取消传递。全量测试仍以 `NodeCraft.Tests` 控制台跑棒的 `ALL PASS` 为完成标准。

## 8. 完成标准

- Virtual Camera 模型、XML/workflow mapping、编辑器和 executor 都支持 `FrameRate`。
- 新节点默认 18 FPS，首帧立即输出，连续帧不超过配置上限。
- 取消/停止可以中断帧率等待，且不会污染下一次 session。
- 既有 Virtual Camera 图片来源和输出契约保持兼容。
- 新增测试和现有全量测试通过。
