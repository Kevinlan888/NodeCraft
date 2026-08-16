# NodeCraft.Vision Virtual Camera 帧率配置设计

状态：设计已确认，待书面复核

日期：2026-08-15

修订日期：2026-08-16

本设计是 `2026-08-15-vision-virtual-camera-design.md` 的后续补充。涉及采集节拍、
`FrameId`、`DeviceTimestamp` 和 `CapturedAtUtc` 的内容以本设计为准；原设计中的图片来源、
排序、Preload/Dynamic、坏图处理、输出端口和生命周期错误边界继续有效。

## 1. 背景和目标

Virtual Camera 当前实现 `IFlowIterationSource`，但 `PrepareIterationAsync` 不等待新帧，连续执行时会以 Flow graph 能达到的最高速度循环。对于大图或多图文件夹，结果产生速度远高于预览渲染速度，最新预览任务会被后续结果持续替换，表现为 `imagePath` 快速变化、图片预览不变，停止后才显示最后一帧。

本变更为 Virtual Camera 增加相机主导的帧率配置，默认 18 FPS。Virtual Camera 在每次 graph iteration 前决定下一帧何时可用；图片严格按来源顺序循环，不跳帧、不积压历史帧。下游处理较慢时形成背压，实际帧率可以低于配置值。

## 2. 范围和非目标

本变更只修改 Virtual Camera 自身：

- `NodeCraft.Vision` 内的 Virtual Camera 模型、执行器、编辑器和私有图片数据。
- `NodeCraft.Tests` 中现有 Virtual Camera 测试。
- Virtual Camera 设计文档。

明确不做以下改动：

- 不修改 `NodeCraft.Flow`、`FlowImage`、`FlowExecutionController` 或 `GraphExecutionSession`。
- 不修改真实 Vision Camera、Stereo Camera、`LatestFrameMailbox` 或它们的采集会话。
- 不增加通用 Camera 基类、公共帧率框架、后台采集线程或图片队列。
- 不使用自由运行相机的“最新帧覆盖”语义；本节点采用有背压的软件触发语义，以保证文件序列不跳图。
- 不承诺硬实时调度。操作系统定时器、图片解码和 graph 执行都可能使实际帧率低于配置值。

## 3. 配置契约

`VirtualCameraNodeModel` 新增公开可读写属性：

```csharp
public double FrameRate { get; set; } = 18.0;
```

配置规则：

- 默认值为 `18.0` FPS。
- 有效范围为 `0.1 <= FrameRate <= 1000.0`。
- `NaN`、正负无穷大和范围外数值均无效。
- `GraphModelXmlSerializer` 通过现有公开属性机制将其保存到 `<Properties>`，不修改 graph format version。
- 旧 graph 缺少 `FrameRate` property 时，由 NodeModel 构造默认值恢复为 18。

`WriteWorkflowInputs` 增加固定运行时 key：

| NodeModel property | WorkflowNode input key | 类型 |
| --- | --- | --- |
| `FrameRate` | `frameRate` | `double` |

`VirtualCameraExecutor.StartSessionAsync` 的兼容规则为：

- `frameRate` key 缺失时使用 18，兼容已有直接构造的 `WorkflowDocument`。
- key 存在但不是 `double`、不是有限数值或超出范围时，启动失败。
- 错误消息包含 `VirtualCamera`、source path/URI 和无效帧率。

编辑器增加 `Frame rate (FPS)` 文本框，使用 invariant culture 解析 `double`。初始化不触发 graph-changed；合法修改更新 NodeModel 并触发一次通知；空值、非数字、`NaN`、无穷大和越界值不修改模型，也不触发通知。

预加载内存上限的内部契约保持不变：`MaxPreloadedBytes` 仍是 `long` 字节数，`maxPreloadedBytes` runtime key 仍传递字节数，executor 仍按解码后像素 buffer 的实际字节数校验。编辑器仅将该字段以 MB 展示和输入：标签为 `Maximum preloaded memory (MB)`，使用二进制换算 `1 MB = 1,048,576 bytes`；默认的 `536,870,912` bytes 显示为 `512 MB`。编辑器输入正整数 MB 后以 checked 乘法转换回字节，非数字、非正数或转换溢出时不修改模型，也不触发 graph-changed。XML 属性名、runtime key 和已有 graph 中的数值保持向后兼容。

## 4. 相机主导的背压节拍

节拍是 `VirtualCameraExecutor` 的私有职责，不进入 Flow framework。执行器使用单调时钟和可取消 delay；测试可通过内部构造参数注入时钟、delay 和 UTC clock，生产路径使用现有 Vision 插件内的系统单调时钟、`Task.Delay` 和 `DateTimeOffset.UtcNow`。

session 成功完成来源解析和 Preload 后初始化：

```text
period = TimeSpan.FromSeconds(1.0 / FrameRate)
nextDue = monotonicNow + period
nextFrameId = 0
index = -1
current = null
```

第一次 Prepare 等待完整的一个帧周期。之后每次 `PrepareIterationAsync`：

1. 验证 session 已启动，并检查 cancellation token。
2. 读取单调时钟；若早于 `nextDue`，使用原始 cancellation token 等待剩余时间。
3. 等待结束后再次检查取消，并记录本帧实际开始准备的 `frameStart`。
4. 严格选择来源序列中的下一张图片；Preload 读取缓存模板，Dynamic 重新加载文件。
5. Dynamic 的坏图跳过继续沿用现有删除游标规则，在同一次 Prepare 内寻找下一张可读图片。
6. 图片成功后创建本帧 `FlowImage` 和 `imagePath`，再次检查取消。
7. 原子提交 current、entry、index、FrameId 和下一 deadline。

成功提交后：

```text
nextFrameId = checked(nextFrameId + 1)
nextDue = frameStart + period
```

若 graph 处理完成时 `nextDue` 已经过期，下一帧立即开始；该帧成功后以新的 `frameStart` 重新建立节拍。实现不得通过连续立即执行来追赶错过的历史时刻，也不得跳过来源图片或预先积压图片。

因此：

- 下游快于配置帧率时，Virtual Camera 控制 graph 不超过配置值。
- 下游慢于配置帧率时，每张图片仍严格执行一次，实际 FPS 降低。
- 同一 graph 中的其他 iteration source 仍按现有 `GraphExecutionSession` 顺序等待；本变更不声称它们的 graph cadence 完全独立。
- 单次 Run Once 同样等待首个帧周期。

## 5. 图片数据和逐帧元数据

不修改公共 `FlowImage` API。`NodeCraft.Vision` 内部增加仅供 Virtual Camera 使用的不可变图片模板，保存宽、高、stride、像素格式、图片类型和私有 `byte[]`。模板通过现有 `FlowImage.FromOwnedBuffer` 创建每帧轻量包装；像素数组始终由插件内部持有且从不修改。

模式语义：

- Preload 和 builtin 在 session 启动时创建模板，后续帧共享其不可变像素数组，不进行逐帧像素复制。
- Dynamic 每次 Prepare 重新解码当前文件，使用该次解码得到的像素数组创建当前帧。
- `MaxPreloadedBytes` 只统计唯一模板的像素数组，不统计每帧轻量 `FlowImage` 包装。

每个成功提交的帧使用：

| 字段 | 语义 |
| --- | --- |
| `FrameId` | 当前 session 内从 0 连续递增的成功帧序号 |
| `DeviceTimestamp` | `frameStart` 距 session 节拍起点的整数微秒数 |
| `CapturedAtUtc` | 图片成功加载并即将提交时的 UTC 时间 |
| `imagePath` | 本帧实际使用的本地绝对路径或 canonical builtin URI |

entry 的稳定 ordinal 只用于来源排序和 Dynamic 删除游标，不再作为公开 `FrameId`。取消、加载失败或其他异常发生在提交前时，不更新 current、index、FrameId 或下一 deadline；之后使用未取消 token 重试时仍从同一图片和帧号开始。

## 6. 生命周期和错误处理

- Start 在提交 session 状态前完成帧率校验、来源解析和 Preload；失败或取消后恢复未启动状态。
- Stop 不创建额外并发协议；继续遵守现有 runtime 串行调用生命周期的边界。
- Stop 清空节拍、模板、current、entry、index 和帧号；未启动、失败启动后、已启动或重复 Stop 都保持幂等。
- Prepare 的 delay 使用传入 token，因此 Flow 停止可以中断首帧或后续帧等待。
- `nextFrameId` 使用 checked 递增；耗尽 `ulong` 时抛出明确的 VirtualCamera 异常，不回绕。
- Dynamic/Preload 现有的窄图片异常包装、`SkipErrorImages`、数量/字节限制和取消优先级保持不变。

## 7. 方案取舍

### 方案 A：Executor 内部背压节拍（采用）

Virtual Camera 在 Prepare 边界决定下一帧何时可用，文件严格顺序输出；没有后台线程、队列或框架改动。下游较慢时实际 FPS 降低，符合已确认的软件触发/背压语义。

### 方案 B：后台自由运行 + 最新帧 mailbox

最接近现有真实相机的自由运行模式，但下游慢时会覆盖中间帧，违反“不跳帧”要求，不采用。

### 方案 C：后台自由运行 + 无损队列

可以同时保持相机时钟和不丢帧，但下游慢时队列持续增长，Dynamic 大图会造成不可控内存和延迟，不采用。

## 8. 测试设计

扩展现有 `VirtualCameraTests`，不增加测试框架：

1. 默认 `FrameRate == 18.0`；自定义小数值通过 XML round-trip 和 workflow mapping 保持 `double` 类型。
2. 旧 XML 缺少属性、runtime key 缺失时使用 18。
3. runtime 拒绝错误类型、`NaN`、正负无穷大、小于 0.1 和大于 1000 的值。
4. 编辑器初始化不通知；合法小数触发一次通知；空值、非法数值和越界值不修改模型。
5. 使用 fake monotonic clock 和 fake delay 验证首帧等待完整周期、后续快路径只等待剩余时间。
6. 模拟 graph 处理超过 deadline，验证下一帧立即开始但后续重新建立正常周期，不发生追赶突发。
7. 多次 Prepare 始终按来源顺序循环，不因慢处理跳图。
8. FrameId 为 `0, 1, 2...`；DeviceTimestamp 单调且使用微秒；CapturedAtUtc 来自注入 UTC clock；imagePath 对应本帧图片。
9. 等待和 Dynamic load 期间取消都不提交 index、current、FrameId 或 deadline，随后重试同一帧。
10. Stop/restart 后首帧重新等待一个周期，并从首图、FrameId 0 和新时间戳起点开始。
11. Preload 重复帧共享同一像素数组，证明 6400×3000 图片不会逐帧复制；预加载字节限制仍只计算唯一模板。
12. Dynamic 文件修改、坏图删除、全坏来源和异常传播的现有测试继续通过，并适配连续 FrameId 语义。
13. graph integration 使用可控节拍执行多轮，验证 Preview、imagePath 和 imageDirectory 契约不变。

最终验证：

```text
dotnet build NodeCraft.sln --no-restore
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-build --no-restore
```

全量测试必须输出 `ALL PASS`。另外使用 `D:\test\barcode-pics`、Preload 和默认 18 FPS 做实际预览回归，确认连续运行时图片和 `imagePath` 按相同顺序稳定变化，停止后没有额外补帧。

## 9. 完成标准

- Virtual Camera 模型、XML/workflow mapping、编辑器和 executor 支持 `double FrameRate`，默认 18 FPS。
- 首帧等待一个周期，后续帧由相机节拍门控；严格顺序、不跳帧、不积压、不追赶。
- 每个成功帧具有连续 FrameId、虚拟设备微秒时间戳和当前 UTC 采集时间。
- Preload 不逐帧复制像素；Dynamic、坏图、取消和重启保持既有正确性。
- 不修改 NodeCraft.Flow、执行控制器、真实相机或通用相机框架。
- 新增测试、现有全量测试和 `D:\test\barcode-pics` 实际预览回归通过。
