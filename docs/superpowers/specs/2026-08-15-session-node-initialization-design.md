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
- `SessionValueStore` 只由 session 初始化阶段写入；iteration 只能读取 session 绑定，不能向 store 写回、替换或删除值。引擎不复制或冻结存储对象；session 输出应由节点视为逻辑只读值。

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
public enum FlowPortAvailability
{
    Iteration,
    Session,
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
| `Session` | session 初始化阶段产生或消费的值；初始化后形成只读快照，并在整个 session 内保持 |

V1 的端口阶段约束如下：

| 端口方向 | V1 允许的 `Availability` |
| --- | --- |
| Input | `Iteration` 或 `Session`，必须恰好选择一个 |
| Output | `Iteration` 或 `Session`，必须恰好选择一个 |

每个端口在 V1 只属于一个阶段，不允许单个端口同时承担稳定值和每轮临时值。若同一业务同时需要稳定 baseline 和每轮临时结果，应定义两个独立 output 端口，或增加一个在 session 初始化阶段产生稳定值的计算节点；不让同一端口跨阶段复用。

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
- `GraphExecutionSession` 实例是一次性运行对象；停止后不能再次 `Start`，必须创建新的 session 才能重新执行。
- 只有 session 初始化阶段在校验初始化器输出后，才能按输出端口 ID 转换为定义 slot 写入。
- 初始化写入是每个 `(nodeId, outputSlot)` 在当前 session 内的一次性写入；session store 不提供 iteration 写入、覆盖或删除路径。
- 只有 `Availability == Session` 的输出端口可以由初始化器写入 session 存储；`Iteration` 输出不能写入 session store。
- session 启动完成并进入 `Running` 前，`SessionValueStore` 封存为只读视图；`PrepareIterationAsync`、`ExecuteAsync`、`FlowGraphIterationRunner` 和 `FlowExecutionContext.SetPortValue` 都不能修改它。
- session 值在 session 结束前保持不变；停止或释放时才清理整个 store。
- 不把 session 值写回 workflow 模型，不序列化硬件句柄、算法实例或其他运行时对象。

运行时应当把 store 的写入接口和读取接口分开。概念上，初始化阶段持有内部可写的 `SessionValueStore`，iteration 和节点只拿到等价的 `IReadOnlySessionValueStore` 读取视图；`FlowExecutionContext` 自己维护独立的当前轮值表。对一个已配置的 `LinkRef`，运行时按它指向的 source output port 的 `Availability` 选择存储位置：

```text
source Availability == Iteration -> 当前 FlowExecutionContext
source Availability == Session    -> SessionValueStore
未配置 LinkRef                    -> 常量或 DefaultValue
```

已配置 `LinkRef` 但对应 source output 在其声明阶段没有值时，输入保持缺失，不使用 `DefaultValue`。需要同时提供稳定 baseline 和每轮临时结果时，使用不同的 output ID；iteration output 只写入当前 `FlowExecutionContext`，不会覆盖 session output。

这样算法节点即使在 `FlowNodeDefinition.InputPorts` 中将 `calibration` 定义为必需输入，在 iteration 的输入解析中也可以从 session 值获得该稳定输入，不会因为相机每轮只输出 `image` 而被判定为缺少输入。算法内部真正使用的标定对象仍然来自一次性的 `InitializeSessionAsync`。

### Session 原始值的逻辑只读约定

`SessionValueStore` 中的值是初始化快照，不是供 iteration 原地编辑的工作缓冲区。`SessionValueStore` 只由 session 初始化阶段写入；iteration 只能通过只读视图读取 session 绑定，不能向 store 写回、替换或删除值。引擎不对存储的 `object` 做隐式深拷贝，也不冻结对象本身，因此无法阻止消费者通过同一引用原地修改可变对象；节点应将 session 输出视为逻辑只读值，需要修改时自行创建副本。

节点需要可变工作数据时，应在初始化时创建自己的副本，或使用明确的只读/不可变类型。仅仅给同一个可变对象增加一个局部变量引用不算复制。`CameraCalibration` 作为不可变快照可以直接作为 session 值传递；引擎不对任意 `object` 做无法定义语义的深拷贝。

## 初始化执行时序

`GraphExecutionSession.StartCoreAsync` 按现有拓扑顺序处理节点。每个节点的顺序为：

```text
1. 如果实现 IFlowNodeSessionLifecycle，调用 StartSessionAsync
2. 根据已经完成的上游节点解析 session 输入
3. 如果实现 IFlowNodeSessionInitializer，调用 InitializeSessionAsync
4. 校验并一次性保存该节点的 session 输出；只允许初始化阶段写入 SessionValueStore
5. 继续处理下一个节点
6. 所有节点初始化完成后封存 SessionValueStore，再进入 Running
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
封存 SessionValueStore（之后只读）
    ↓
进入 iteration 循环
    ├─ 相机 PrepareIterationAsync
    ├─ 相机 ExecuteAsync 输出 image
    ├─ 算法 ExecuteAsync 读取稳定 session 值并处理 image
    ├─ iteration 输出只写入当前 FlowExecutionContext
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

对于现有立体相机节点，可以把 `colorCalibration` 和 `depthCalibration` 标记为 `Session` 输出。若需要每轮产生派生结果，应增加独立的 `Iteration` output，例如 `currentColorCalibration` 和 `currentDepthCalibration`；不要把同一个 calibration 端口跨阶段复用。

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

如果 link 指向只有 `Iteration` 能力的输出端口，而当前 input 的 `Availability == Session`，验证阶段报错，错误码建议为 `SessionInputUnavailable`。V1 每个 input 和 output 都只允许单一阶段，不存在跨阶段复用的端口。

`DefaultValue` 仅在 workflow 未配置该输入端口时生效。若 input 已配置为 `LinkRef`，但对应的上游 session 值不存在，则该输入保持缺失，不回退到 `DefaultValue`；required session input 应报告 `SessionInputUnavailable`。

### 输出验证

初始化器返回输出时，引擎验证：

- output ID 是否存在于 `FlowNodeDefinition.OutputPorts`。
- 端口是否声明了 `Session` 能力。
- 返回对象是否符合 `FlowDataType.AcceptsValue`。
- 不根据输出端口的 `IsRequired` 推断 initializer 必须返回该 output；输出是否必须可用由下游的 required input 约束。

返回未知端口或错误类型应当让 session 启动失败，而不是静默丢弃。

普通 `ExecuteAsync` 返回输出时，引擎另行验证：

- output ID 是否存在于 `FlowNodeDefinition.OutputPorts`。
- 端口的 `Availability` 是否为 `Iteration`；`Session` output 不能在 iteration 中产生输出。
- 返回对象是否符合 `FlowDataType.AcceptsValue`。
- 返回值只写入当前 `FlowExecutionContext`；`Session` output 不能在 iteration 阶段返回值，也不能写入、覆盖或删除 `SessionValueStore`。

iteration 输出违反上述规则时，当前 iteration 失败；不得降级为静默丢弃或更新 session store。

### 普通 iteration 输入

普通 iteration 仍然由现有 `FlowGraphIterationRunner` 处理。对已配置的 `LinkRef`，先查看它指向的 source output port 的 `Availability`：

1. source output 为 `Iteration`：读取 `(linkRef.SourceNodeId, linkRef.SourceSlot)` 在当前 `FlowExecutionContext` 的值；
2. source output 为 `Session`：读取同一 `(sourceNodeId, sourceSlot)` 在 `SessionValueStore` 的稳定值；
3. 只有 workflow 未配置该 input key 时，才使用常量或 `DefaultValue`。

已配置 `LinkRef` 但对应 source output 在其声明阶段没有值时，保持输入缺失，不使用 `DefaultValue`。只有 workflow 未配置该 input 时才使用 `DefaultValue`；required `Session` input 由 session 初始化阶段的 `SessionInputUnavailable` 处理，required `Iteration` input 由现有 required-input 逻辑处理。

因此 session 标定信息可以作为只读稳定输入参与每轮执行，而图像等动态数据仍然按照现有 DAG 顺序流动。需要每轮变化的结果使用独立的 `Iteration` output，不覆盖 session baseline。

## 失败和清理

初始化过程需要和现有 session 生命周期保持一致：

- 节点 `StartSessionAsync` 成功后加入已启动集合。
- 节点 `InitializeSessionAsync` 成功后才写入 session 输出。
- 节点初始化完成后不得再次写入 session 输出；iteration 期间引擎没有 session store 的写入、覆盖或删除路径。由于 store 保存 `object` 引用，节点仍可能通过同一引用原地修改可变对象；这不是引擎可阻止的行为，节点应按逻辑只读约定使用 session 输出。
- 当前节点初始化失败时，先清理当前节点已经启动的资源，再逆序清理之前成功的节点。
- 初始化取消、后续 iteration 异常或用户停止都会进入统一的 `StopSessionAsync` 清理路径。
- 清理使用不可取消 token，避免用户取消导致设备句柄或算法资源泄漏。
- 初始化器本身若在内部申请资源并失败，必须像现有视觉节点一样在内部 catch 中释放部分资源，随后再抛出原始异常。
- 停止清理顺序为：进入 `Stopping` → 逆序 `StopSessionAsync` → `SessionValueStore.Clear()` → 将 state 设为 `Stopped` → release `_iterationGate`；`Stopped` 表示 lifecycle 和 session store 均已清理完成。

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
- `Session` output 在多轮 iteration 后仍保持初始化原值；`Iteration` output 只在当前轮传递给下游。
- 下一轮 iteration 重新读取原始 `Session` output，不会读取上一轮的临时 `Iteration` output。
- 节点显式基于 session 值创建副本并修改时，原始 session 值不发生变化；这依赖节点的复制行为，不是引擎隐式深拷贝的保证。
- iteration 尝试从只有 `Session` 能力的输出端口返回值时失败，且不改变 session store。
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
