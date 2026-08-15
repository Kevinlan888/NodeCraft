# NodeCraft Session 节点初始化与跨节点 session 输出设计

## 背景

NodeCraft 的图执行已经采用 `GraphExecutionSession`：session 启动时创建并复用每个 workflow 节点的 executor，持续运行期间反复执行 iteration，session 结束时统一清理节点资源。现有节点生命周期契约包含 `IFlowNodeSessionLifecycle` 的 `StartSessionAsync` / `StopSessionAsync`，以及用于逐轮数据源的 `IFlowIterationSource.PrepareIterationAsync`。

这套生命周期能够满足“节点启动一次、持续执行多轮、结束时释放”的需求，但 `StartSessionAsync` 当前没有前置节点的运行时数据。它在第一次 `ExecuteIterationAsync` 之前执行，而每轮输出只存在于该轮的 `FlowExecutionContext` 中。因此，算法节点无法在 session 启动阶段直接使用相机节点的标定输出。

典型需求是：

```text
相机节点 -- calibration --> 算法节点
相机节点 -- image -------> 算法节点
```

相机的标定信息可以通过设备接口直接读取，不需要拉流；算法需要用这份标定信息创建内部实例，随后在每一轮只处理相机图像。

## 目标

- 增加一个按 workflow 拓扑顺序执行的 session 初始化数据阶段。
- 允许节点在 session 初始化阶段读取前置节点的 session 输出。
- 让 session 初始化输出沿用现有节点端口和连线，不引入隐藏的全局依赖。
- 让算法节点的初始化只执行一次，后续 iteration 复用初始化后的实例。
- 保持现有 `StartSessionAsync` / `StopSessionAsync` 生命周期行为和旧插件兼容。
- 在停止、取消、初始化失败和正常结束时都可靠清理已创建的资源。
- 保持普通每轮数据与 session 级稳定数据的语义边界。

## 非目标

- 不在本次设计中把所有节点执行改成并行 DAG 调度。
- 不让 session 初始化执行普通 `ExecuteAsync`，避免初始化阶段触发拉流、处理帧或其他逐轮副作用。
- 不引入全局服务定位器，让算法节点通过类型强转直接查找相机 executor。
- 不改变现有 `.flow.xml` 的节点和连线持久化格式；端口阶段能力属于注册定义。
- 不在本次设计中解决条件分支内部的动态 session 初始化。第一版 session 初始化只依赖普通数据端口，不依赖 `control` 端口。

## 核心概念

### Session 生命周期

现有 `IFlowNodeSessionLifecycle` 保持不变：

```csharp
Task StartSessionAsync(
    FlowNodeSessionContext context,
    CancellationToken cancellationToken);

Task StopSessionAsync(
    FlowNodeSessionContext context,
    CancellationToken cancellationToken);
```

职责是准备和释放节点自身的 session 资源。`StartSessionAsync` 不负责读取前置节点输出；需要前置数据的工作放到新的初始化接口中。

### Session 初始化器

新增可选接口：

```csharp
public interface IFlowNodeSessionInitializer
{
    Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
        FlowNodeSessionContext context,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken);
}
```

约定如下：

- `inputs` 的 key 是当前节点输入端口 ID。
- link 输入由引擎从前置节点的 session 输出中解析。
- 常量输入仍然直接沿用 `WorkflowNode.Inputs` 中的配置值。
- 返回值的 key 是当前节点输出端口 ID。
- 返回值必须通过端口定义进行存在性和类型校验。
- 不产生 session 输出的初始化器返回空字典。
- 创建了持久运行资源的初始化器必须同时实现 `IFlowNodeSessionLifecycle`，由 `StopSessionAsync` 释放该资源。

现有节点不实现这个接口时行为不变。实现初始化器但只做纯数据计算的节点可以不实现生命周期；实现初始化状态、句柄或算法实例的节点应同时实现生命周期。

### 端口可用阶段

现有 `FlowPortDefinition` 目前只描述端口的类型、方向和连接规则。增加阶段能力：

```csharp
[Flags]
public enum FlowPortAvailability
{
    Iteration = 1,
    Session = 2,
}
```

`FlowPortDefinition` 增加：

```csharp
public FlowPortAvailability Availability { get; set; }
    = FlowPortAvailability.Iteration;
```

含义如下：

| 能力 | 含义 |
| --- | --- |
| `Iteration` | 普通每轮执行阶段产生或消费的值 |
| `Session` | session 初始化阶段产生或消费的值；值在整个 session 内保持 |
| `Session | Iteration` | 初始化阶段可用，并允许每轮重新产生或覆盖 |

这不是新的物理连接线。编辑器仍然使用现有 socket 和 `LinkRef`，只是类型校验和运行时解析额外检查端口的阶段能力。

相机标定端口可以定义为 `Session`，图像端口定义为 `Iteration`；算法的标定输入定义为 `Session`，图像输入定义为 `Iteration`。

## Session 值存储

新增 session 级端口值存储，概念上类似现有 `FlowExecutionContext` 的值表，但生命周期属于 `GraphExecutionSession`：

```text
SessionValueStore
    (nodeId, outputSlot) -> value
```

要求：

- 每个 `GraphExecutionSession` 有独立存储，不在不同运行之间共享。
- 初始化器成功返回的输出按输出端口 ID 转换为定义 slot 后写入。
- 只有标记为 `Session` 的输出端口可以写入 session 存储。
- session 值在 session 结束前保持不变。
- 不把 session 值写回 workflow 模型，不序列化硬件句柄、算法实例或其他运行时对象。

每轮创建 `FlowExecutionContext` 时，session 值作为稳定基础值提供给输入解析。当前轮产生的值优先级更高：

```text
当前 iteration 值 > session 值 > 常量默认值
```

这样算法节点即使在 `ExecuteAsync` 中声明了必需的 `calibration` 输入，也不会因为相机每轮只输出 `image` 而被判定为缺少输入。算法内部真正使用的标定对象仍然来自一次性的 `InitializeSessionAsync`。

## 初始化执行时序

`GraphExecutionSession.StartCoreAsync` 按现有拓扑顺序处理节点。每个节点的顺序为：

```text
1. 如果实现 IFlowNodeSessionLifecycle，调用 StartSessionAsync
2. 根据已经完成的上游节点解析 session 输入
3. 如果实现 IFlowNodeSessionInitializer，调用 InitializeSessionAsync
4. 校验并保存该节点的 session 输出
5. 继续处理下一个节点
```

完整时序：

```text
创建 GraphExecutionSession
    ↓
创建每个 workflow 节点的 executor
    ↓
按拓扑顺序启动和初始化节点
    ├─ 相机 StartSessionAsync
    ├─ 相机 InitializeSessionAsync
    │    └─ 读取 calibration，写入 SessionValueStore
    ├─ 算法 StartSessionAsync
    └─ 算法 InitializeSessionAsync
         └─ 从 session 输入取得 calibration，创建算法实例
    ↓
进入 iteration 循环
    ├─ 相机 PrepareIterationAsync
    ├─ 相机 ExecuteAsync 输出 image
    ├─ 算法 ExecuteAsync 处理 image
    └─ 重复下一轮
    ↓
停止或异常
    ├─ 逆序 StopSessionAsync
    └─ 清理 SessionValueStore
```

由于相机标定端口连接到了算法输入，现有拓扑排序会保证相机节点先于算法节点。初始化阶段只调用显式实现了 `IFlowNodeSessionInitializer` 的节点，不会调用普通 `ExecuteAsync`。

第一版明确规定：依赖前置数据的资源创建放入 `InitializeSessionAsync`。`StartSessionAsync` 只做不依赖前置 session 数据的本地启动。如果未来存在“启动本身就必须接收上游数据”的节点，再单独扩展带输入的生命周期接口，不在本次范围内修改现有接口签名。

## 相机节点示例

相机节点注册两个阶段不同的输出：

```csharp
new FlowPortDefinition
{
    Id = "calibration",
    DisplayName = "Calibration",
    IOType = EIOType.Output,
    DataType = FlowDataType.CameraCalibration,
    Availability = FlowPortAvailability.Session,
}

new FlowPortDefinition
{
    Id = "image",
    DisplayName = "Image",
    IOType = EIOType.Output,
    DataType = FlowDataType.Image,
    Availability = FlowPortAvailability.Iteration,
}
```

执行器可以拆成：

```csharp
public async Task StartSessionAsync(
    FlowNodeSessionContext context,
    CancellationToken cancellationToken)
{
    _camera = await _deviceFactory.OpenAsync(
        context.Node,
        cancellationToken);
}

public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
    FlowNodeSessionContext context,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken)
{
    var calibration = _camera.ReadCalibration();

    return Task.FromResult<IReadOnlyDictionary<string, object>>(
        new Dictionary<string, object>
        {
            ["calibration"] = calibration,
        });
}

public async Task PrepareIterationAsync(
    FlowNodeSessionContext context,
    CancellationToken cancellationToken)
{
    _currentImage = await _camera.WaitForNextImageAsync(
        cancellationToken);
}
```

标定接口不需要拉流，因此 `InitializeSessionAsync` 读取标定时不会产生额外帧。后续 `PrepareIterationAsync` 才负责等待新图像。

对于现有立体相机节点，可以把 `colorCalibration` 和 `depthCalibration` 标记为 session 输出。若这些对象也需要在每轮结果中显示，则可以把端口能力设为 `Session | Iteration`；否则只保留 session 值，由执行上下文在每轮解析时提供。

## 算法节点示例

算法节点的端口：

```csharp
new FlowPortDefinition
{
    Id = "calibration",
    DisplayName = "Calibration",
    IOType = EIOType.Input,
    DataType = FlowDataType.CameraCalibration,
    IsRequired = true,
    Availability = FlowPortAvailability.Session,
}

new FlowPortDefinition
{
    Id = "image",
    DisplayName = "Image",
    IOType = EIOType.Input,
    DataType = FlowDataType.Image,
    IsRequired = true,
    Availability = FlowPortAvailability.Iteration,
}
```

初始化器：

```csharp
public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
    FlowNodeSessionContext context,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken)
{
    var calibration = (CameraCalibration)inputs["calibration"];
    _algorithm = new Algorithm(calibration);

    return Task.FromResult<IReadOnlyDictionary<string, object>>(
        new Dictionary<string, object>());
}
```

之后的 `ExecuteAsync` 只读取当前轮的 `image`，复用 `_algorithm`。如果初始化失败，session 不进入 `Running` 状态。

## 输入解析和验证

### 初始化输入

初始化阶段只解析以下数据：

- 直接配置在 `WorkflowNode.Inputs` 中的常量。
- 指向上游 `Session` 输出端口的 `LinkRef`。
- 可选的默认值。

如果 link 指向只有 `Iteration` 能力的输出端口，而当前输入需要 `Session` 能力，验证阶段报错，错误码建议为 `SessionInputUnavailable`。

### 输出验证

初始化器返回输出时，引擎验证：

- output ID 是否存在于 `FlowNodeDefinition.OutputPorts`。
- 端口是否声明了 `Session` 能力。
- 返回对象是否符合 `FlowDataType.AcceptsValue`。
- 必需的 session 输出是否确实返回。

返回未知端口或错误类型应当让 session 启动失败，而不是静默丢弃。

### 普通 iteration 输入

普通 iteration 仍然由现有 `FlowGraphIterationRunner` 处理。解析顺序调整为：

1. 当前 iteration 的上游输出；
2. 当前节点对应的 session 值；
3. 常量或默认值。

因此 session 标定信息可以作为稳定输入参与每轮执行，而图像等动态数据仍然按照现有 DAG 顺序流动。

## 失败和清理

初始化过程需要和现有 session 生命周期保持一致：

- 节点 `StartSessionAsync` 成功后加入已启动集合。
- 节点 `InitializeSessionAsync` 成功后才写入 session 输出。
- 当前节点初始化失败时，先清理当前节点已经启动的资源，再逆序清理之前成功的节点。
- 初始化取消、后续 iteration 异常或用户停止都会进入统一的 `StopSessionAsync` 清理路径。
- 清理使用不可取消 token，避免用户取消导致设备句柄或算法资源泄漏。
- 初始化器本身若在内部申请资源并失败，必须像现有视觉节点一样在内部 catch 中释放部分资源，随后再抛出原始异常。

session 启动失败时不允许进入 `Running` 状态，也不允许开始第一轮 `ExecuteAsync`。

## 兼容性

- `IFlowNodeExecutor` 不变。
- `IFlowNodeSessionLifecycle` 的方法签名不变。
- 不实现新接口的旧节点完全按现有流程运行。
- 默认端口能力为 `Iteration`，现有节点不需要修改定义即可继续工作。
- 端口阶段能力属于注册元数据，不写入 `.flow.xml`。
- session 运行时对象、设备句柄、算法实例和 `CameraCalibration` 的具体内存对象不序列化。
- 现有 `GraphExecutor.ExecuteAsync` 和 `FlowExecutionController` 自动获得相同的初始化行为，因为二者都通过 `GraphExecutionSession` 启停。

## 测试验收标准

新增 session 生命周期测试：

- 相机初始化器先于算法初始化器执行。
- 算法初始化器收到正确的 `CameraCalibration` 实例。
- 连续执行多轮时，算法初始化器调用次数为 1，`ExecuteAsync` 调用次数等于 iteration 数。
- 相机标定不触发 `PrepareIterationAsync` 或拉流调用。
- session 停止时算法和相机按逆拓扑顺序清理。
- 初始化失败时已经启动的节点全部清理，失败节点不进入正常执行。
- session 输入连接到非 session 输出时验证失败。
- 初始化器返回未知端口或错误类型时 session 启动失败。
- 一次性执行和连续执行都使用同一套 session 初始化语义。
- 旧的、不实现初始化器的节点回归测试继续通过。

现有测试跑棒仍应输出 `ALL PASS`。

## 实现范围

实现阶段预计涉及：

- `NodeCraft.Flow/Flow/FlowSessionContracts.cs`：新增初始化器契约和端口阶段枚举。
- `NodeCraft.Flow/Flow/FlowSchema.cs`：增加端口阶段能力。
- `NodeCraft.Flow/Flow/GraphExecutionSession.cs`：加入 session 初始化顺序、值存储和失败回滚。
- `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`：支持 session 值作为每轮输入后备。
- `NodeCraft.Flow/Flow/GraphExecutor.cs`：增加 session 阶段能力验证。
- `NodeCraft.Vision`：将相机标定读取接入初始化器，标记标定输出端口。
- `NodeCraft.Tests`：新增生命周期、依赖传递、类型校验和失败清理测试。

本设计不包含 UI 视觉改造；第一版可以继续使用现有 socket。若后续需要让用户区分 session 端口和 iteration 端口，再增加 tooltip 或端口图标。
