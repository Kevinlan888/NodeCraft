# NodeCraft Session 节点初始化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变旧节点生命周期契约和 `.flow.xml` 格式的前提下，为 `GraphExecutionSession` 增加按拓扑顺序执行的一次性 session 初始化阶段，使前置节点的稳定输出可以被后置节点用于创建 session 级资源。

**Architecture:** 新增可选的 `IFlowNodeSessionInitializer` 和 `FlowPortAvailability` 元数据；初始化阶段使用内部可写的 `SessionValueStore`，完成后只把 `IReadOnlySessionValueStore` 交给 iteration 输入解析。`GraphExecutionSession` 负责启动、按拓扑为所有节点解析并校验 session inputs、对可选 initializer 执行初始化、封存和清理；`FlowGraphIterationRunner` 按 `LinkRef` source output 的阶段选择当前轮 context 或 `SessionValueStore`，处理未配置 input 的 `DefaultValue`，并校验 iteration 输出；立体相机把 calibration 从每轮 `FrameBundle` 输出迁移到 session 初始化输出。

**Tech Stack:** C# 9、.NET 8 Windows、WPF、`NodeCraft.Flow` 插件 API、现有 `GraphExecutionSession`/`FlowGraphIterationRunner`、Windows 控制台测试跑棒。

## Global Constraints

- `IFlowNodeExecutor` 不变。
- `IFlowNodeSessionLifecycle` 的方法签名不变。
- 不实现新接口的旧节点完全按现有流程运行。
- `FlowPortDefinition.Availability` 默认值必须为 `FlowPortAvailability.Iteration`。
- `GraphExecutionSession` 实例为一次性运行对象；停止后重新执行必须创建新的 session。
- V1 的 input 和 output 的 `Availability` 都必须恰好为 `Iteration` 或 `Session`；不允许单个端口跨阶段复用，也不保留组合枚举。需要稳定 baseline 和每轮临时结果时，使用独立 output 端口或增加 session 初始化计算节点。
- `SessionValueStore` 只由 session 初始化阶段写入；iteration 只能读取 session 绑定，不能向 store 写回、替换或删除值。引擎不复制或冻结存储对象；session 输出应由节点视为逻辑只读值。
- iteration hot path 必须复用 session 级的 node/definition lookup；不得在每个 input 上执行 LINQ `Single`、`Registry.Resolve` 或重复构建 lookup。LinkRef metadata 的进一步预解析属于后续可选优化，不作为 V1 公共 API。
- 初始化阶段只调用显式实现 `IFlowNodeSessionInitializer` 的节点，不调用普通 `ExecuteAsync`，不触发普通逐轮副作用。
- `GraphExecutionSession` 在启动阶段对每个节点执行非 control 的 `Session` input 解析和 required 检查；该检查不依赖 executor 是否实现 `IFlowNodeSessionInitializer`。没有 initializer 的节点不产生 session output，但其缺失 required session input 仍必须使 `StartAsync` 失败。
- 初始化只依赖普通数据端口；第一版不依赖 `control` 端口，不实现条件分支内部的动态 session 初始化。
- `Session` output 只在初始化阶段写入 `SessionValueStore`；`Iteration` output 只写入当前 `FlowExecutionContext`。需要两类结果时必须使用不同 output ID。
- 对 `IsRequired = true` 且 `Availability == Session` 的输入，已配置的 `LinkRef` 找不到 session source value 时必须报 `SessionInputUnavailable`；绝不能使用 `DefaultValue`。当前计划对所有已配置 `LinkRef` 统一采用同样的不回退规则，避免 optional 和 required link 产生两套连接语义。
- `SessionValueStore` 在不同 `GraphExecutionSession` 之间隔离，session 停止、取消、初始化失败或释放时清空。
- 端口阶段能力属于注册定义，不写入 `.flow.xml`；不修改节点和连线持久化格式或 UI socket 结构。
- 现有测试跑棒 `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows` 最终必须输出 `ALL PASS`。
- 每个实现任务遵循红—绿—重构：先添加一个能证明行为缺失的测试，确认失败，再实现最小改动并运行聚焦测试，任务结束提交独立 commit。

## File Map

- `NodeCraft.Flow/Flow/FlowSessionContracts.cs`：新增初始化器契约和只读 session store 读取接口，保留既有 lifecycle/iteration 契约。
- `NodeCraft.Flow/Flow/FlowSchema.cs`：增加单值阶段枚举及 `FlowPortDefinition.Availability` 默认值。
- `NodeCraft.Flow/Flow/SessionValueStore.cs`：实现一次性写入、封存、读取和清理；不把写入 API 暴露给 executor 或 iteration runner。
- `NodeCraft.Flow/Flow/FlowRuntimeValueValidator.cs`：集中校验初始化输出和 iteration 输出，保证未知端口、阶段不匹配和类型错误都失败。
- `NodeCraft.Flow/Flow/GraphExecutor.cs`：在已有类型校验旁增加 session link 能力校验，产生 `SessionInputUnavailable`。
- `NodeCraft.Flow/Flow/GraphExecutionSession.cs`：编排生命周期启动、session 输入解析、初始化输出写入、封存和逆序清理；在 session 构造阶段缓存按 node ID 索引的 definitions，并把该 lookup 与只读 store 传给 iteration runner。
- `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`：使用 session 级 definitions lookup 按 source output 的 `Availability` 解析当前轮输出或 session 值，未配置 input 时使用默认值，校验 iteration 输出后只写当前 `FlowExecutionContext`。
- `NodeCraft.Vision/Camera/FrameBundle.cs`：只保留同步的每轮 color/depth image，不再把 calibration 当作逐轮数据携带。
- `NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs`：继续在设备 session 启动时读取并缓存 calibration，逐轮 bundle 不再重复保存 calibration。
- `NodeCraft.Vision/Nodes/StereoCameraExecutor.cs`：实现初始化器并返回 calibration session 输出；`ExecuteAsync` 只返回 image 输出。
- `NodeCraft.Vision/Plugin/StereoCameraRegistration.cs`：将 calibration 输出标记为 `Session`，image 输出保持 `Iteration`。
- `NodeCraft.Tests/SessionNodeInitializationTests.cs`：新增核心初始化、值优先级、类型校验、失败清理和旧节点兼容测试；作为 `Program` 的 partial 文件加入现有跑棒。
- `NodeCraft.Tests/VisionPluginTests.cs`：验证立体相机端口阶段能力和初始化输出契约。
- `NodeCraft.Tests/Program.cs`：调用新的核心 session 初始化测试组；不改变现有测试组顺序之外的行为。

---

### Task 1: 建立 session 初始化契约、端口阶段能力和只读值存储

**Files:**
- Modify: `NodeCraft.Flow/Flow/FlowSessionContracts.cs`
- Modify: `NodeCraft.Flow/Flow/FlowSchema.cs`
- Create: `NodeCraft.Flow/Flow/SessionValueStore.cs`
- Create: `NodeCraft.Tests/SessionNodeInitializationTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**

在 `FlowSessionContracts.cs` 中添加以下公开契约；不修改 `IFlowNodeExecutor` 和 `IFlowNodeSessionLifecycle`：

```csharp
public interface IFlowNodeSessionInitializer
{
    Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
        FlowNodeSessionContext context,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken);
}

public interface IReadOnlySessionValueStore
{
    bool TryGetPortValue(string nodeId, int outputSlot, out object value);
}
```

在 `FlowSchema.cs` 中添加：

```csharp
public enum FlowPortAvailability
{
    Iteration,
    Session,
}

public class FlowPortDefinition
{
    // 保留现有属性；新增属性默认保证旧注册定义仍是 iteration 端口。
    public FlowPortAvailability Availability { get; set; }
        = FlowPortAvailability.Iteration;
}
```

`SessionValueStore` 的写入端只给 `GraphExecutionSession` 使用，读端实现 `IReadOnlySessionValueStore`。同一个 `(nodeId, outputSlot)` 第二次写入和封存后的任何写入都抛出 `InvalidOperationException`；`Clear()` 清空引用并使读取视图返回 false。读取视图不能暴露 `SetPortValue`、`Seal` 或 `Clear`。

- [ ] **Step 1: 添加 store 的失败测试。** 在新的 partial 测试文件中加入测试入口，并先写出一次性写入/只读/清理契约：

```csharp
private static async Task RunSessionNodeInitializationTestsAsync()
{
    await RunAsync("session value store is write-once and read-only after sealing", async () =>
    {
        var store = new SessionValueStore();
        var view = store.CreateReadOnlyView();
        var value = new object();

        store.SetPortValue("camera", 0, value);
        var firstRead = view.TryGetPortValue("camera", 0, out var first)
            && ReferenceEquals(first, value);
        var duplicateRejected = Throws<InvalidOperationException>(
            () => store.SetPortValue("camera", 0, new object()));

        store.Seal();
        var sealedRejected = Throws<InvalidOperationException>(
            () => store.SetPortValue("camera", 1, new object()));
        store.Clear();

        await Task.CompletedTask;
        return firstRead
            && duplicateRejected
            && sealedRejected
            && !view.TryGetPortValue("camera", 0, out _);
    });
}
```

- [ ] **Step 2: 运行测试确认红灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 编译失败，原因是 `IFlowNodeSessionInitializer`、`IReadOnlySessionValueStore`、`FlowPortAvailability` 和 `SessionValueStore` 尚不存在。

- [ ] **Step 3: 实现契约和存储。** 在 `SessionValueStore.cs` 中实现以下结构；内部实现可以使用现有 `Tuple<string, int>` key 语义，但读取视图不能向调用方返回可写字典：

```csharp
internal sealed class SessionValueStore
{
    private readonly Dictionary<Tuple<string, int>, object> _values
        = new Dictionary<Tuple<string, int>, object>();
    private bool _sealed;

    internal IReadOnlySessionValueStore CreateReadOnlyView()
    {
        return new ReadOnlySessionValueStore(this);
    }

    internal void SetPortValue(string nodeId, int outputSlot, object value)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Session value store is sealed.");
        }

        var key = Tuple.Create(nodeId, outputSlot);
        if (_values.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Session output '{nodeId}' slot {outputSlot} was already initialized.");
        }

        _values.Add(key, value);
    }

    internal void Seal()
    {
        _sealed = true;
    }

    internal void Clear()
    {
        _values.Clear();
        _sealed = true;
    }

    private bool TryGetPortValue(string nodeId, int outputSlot, out object value)
    {
        return _values.TryGetValue(Tuple.Create(nodeId, outputSlot), out value);
    }

    private sealed class ReadOnlySessionValueStore : IReadOnlySessionValueStore
    {
        private readonly SessionValueStore _owner;

        internal ReadOnlySessionValueStore(SessionValueStore owner)
        {
            _owner = owner;
        }

        public bool TryGetPortValue(string nodeId, int outputSlot, out object value)
        {
            return _owner.TryGetPortValue(nodeId, outputSlot, out value);
        }
    }
}
```

实现 `FlowPortAvailability` 时使用普通 enum，不增加 `Control` 或其他未在设计中定义的阶段。V1 每个 input 和 output 都必须精确选择 `Iteration` 或 `Session`；需要稳定值和临时值时使用独立 output 端口或 session 初始化计算节点。把 `IFlowNodeSessionInitializer` 放在现有 session contract 区域，令未实现该接口的 executor 自动保持旧行为。

在 `NodeCraft.Tests/Program.cs` 的 `Main` 中，在现有 graph session lifecycle/iteration 测试调用之后加入一次 `await RunSessionNodeInitializationTestsAsync();`；新测试文件显式引入 `System.Collections.Generic`、`System.Linq`、`System.Threading`、`System.Threading.Tasks` 和 `NodeCraft.Flow`，沿用当前 partial `Program` 的 `RunAsync`/`Throws<TException>` helper。

- [ ] **Step 4: 运行聚焦测试确认绿灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 新的 store 测试和当前所有既有测试通过；此时 store 尚未接入 graph runtime，旧 graph 行为不应变化。

- [ ] **Step 5: 提交契约和存储。**

```powershell
git add NodeCraft.Flow/Flow/FlowSessionContracts.cs NodeCraft.Flow/Flow/FlowSchema.cs NodeCraft.Flow/Flow/SessionValueStore.cs NodeCraft.Tests/SessionNodeInitializationTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: add session initialization contracts"
```

### Task 2: 增加阶段校验、运行时输出校验和 source-stage 输入解析

**Files:**
- Create: `NodeCraft.Flow/Flow/FlowRuntimeValueValidator.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutor.cs`
- Modify: `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Modify: `NodeCraft.Tests/SessionNodeInitializationTests.cs`

**Interfaces:**

新增内部校验器，所有 session 初始化输出和 iteration 输出都通过它校验，不允许两个运行时路径各自静默忽略未知 output ID：

```csharp
internal static class FlowRuntimeValueValidator
{
    internal static void ValidateSessionOutputs(
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> outputs);

    internal static void ValidateIterationOutputs(
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> outputs);

    internal static int FindOutputSlot(
        FlowNodeDefinition definition,
        string portId);
}
```

`ValidateSessionOutputs` 必须验证：返回字典非 null；每个 key 在 `OutputPorts` 中存在；端口带 `Session` 能力；`DataType.AcceptsValue(value)` 为 true。它不读取 output 端口的 `IsRequired`，也不要求 initializer 返回所有声明的 output；是否必须可用由下游的 required input 约束。`ValidateIterationOutputs` 使用同一套未知端口和类型规则，但要求 `Iteration` 能力，不允许只有 `Session` 的端口在 iteration 产生输出。校验必须先完整通过，再由调用方写入 store 或 context，避免半写入。

- [ ] **Step 1: 添加失败的阶段和输出校验测试。** 在 `SessionNodeInitializationTests.cs` 中先注册最小定义并覆盖以下行为：

```csharp
await RunAsync("session link to iteration-only output is rejected", async () =>
{
    var source = CreateDefinition("test.stage.source");
    source.OutputPorts[0].Availability = FlowPortAvailability.Iteration;
    var target = CreateDefinition("test.stage.target");
    target.InputPorts.Add(new FlowPortDefinition
    {
        Id = "calibration",
        IOType = EIOType.Input,
        DataType = FlowDataType.String,
        IsRequired = true,
        Availability = FlowPortAvailability.Session,
    });

    var workflow = new WorkflowDocument();
    workflow.Nodes.Add(new WorkflowNode
    {
        Id = "source",
        TypeKey = source.TypeKey,
    });
    workflow.Nodes.Add(new WorkflowNode
    {
        Id = "target",
        TypeKey = target.TypeKey,
        Inputs =
        {
            ["calibration"] = new LinkRef { SourceNodeId = "source", SourceSlot = 0 },
        },
    });

    var registry = new FlowNodeRegistry();
    registry.Register(new FlowNodeRegistration(source, () => new ValidationTestExecutor()));
    registry.Register(new FlowNodeRegistration(target, () => new ValidationTestExecutor()));
    var validation = new GraphExecutor(workflow, registry).Validate();

    await Task.CompletedTask;
    return validation.Errors.Any(error =>
        error.Code == "SessionInputUnavailable"
        && error.NodeId == "target"
        && error.PortId == "calibration");
});

private sealed class ValidationTestExecutor : IFlowNodeExecutor
{
    public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
        FlowExecutionContext context,
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object>>(
            new Dictionary<string, object>());
    }
}

await RunAsync("runtime output validation rejects unknown and wrong-stage outputs", async () =>
{
    var definition = CreateDefinition("test.stage.outputs");
    definition.OutputPorts[0].Availability = FlowPortAvailability.Session;
    var node = new WorkflowNode { Id = "node", TypeKey = definition.TypeKey };

    var unknown = Throws<InvalidOperationException>(() =>
        FlowRuntimeValueValidator.ValidateSessionOutputs(
            node,
            definition,
            new Dictionary<string, object> { ["missing"] = "value" }));
    var wrongStage = Throws<InvalidOperationException>(() =>
        FlowRuntimeValueValidator.ValidateIterationOutputs(
            node,
            definition,
            new Dictionary<string, object> { ["output"] = "value" }));

    await Task.CompletedTask;
    return unknown && wrongStage;
});

await RunAsync("linked iteration input does not fall back to a port default", async () =>
{
    var fixture = CreateMissingIterationSourceFixture();
    await using var session = fixture.Executor.CreateSession();
    await session.StartAsync(CancellationToken.None);
    var context = await session.ExecuteIterationAsync(CancellationToken.None);

    return context.Statuses["target"] == FlowNodeExecutionStatus.Skipped
        && fixture.Target.ExecuteCount == 0;
});
```

`CreateMissingIterationSourceFixture()` 必须构造一个 source output 为 `Iteration`、但 executor 返回空字典的 source，以及一个 required `Iteration` input：该 input 的 `node.Inputs` 配置为指向 source 的 `LinkRef`，同时 definition 设置 `DefaultValue = 99d`。target executor 暴露 `ExecuteCount`；source 没有当前轮值时 target 必须被 skip，不能收到 `99d`。

- [ ] **Step 2: 运行测试确认红灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 编译失败或断言失败，因为 `GraphExecutor` 还没有 `SessionInputUnavailable` 检查，iteration runner 还会静默跳过未知 output，运行时 validator 尚未存在。

- [ ] **Step 3: 实现 `FlowRuntimeValueValidator`。** 对每个 output key 通过 `FindOutputSlot` 查找定义端口，未知 key 直接抛出；取端口 `DataType ?? FlowDataType.Object` 后调用 `AcceptsValue`。错误消息必须包含 node ID、output ID 和违反的阶段，例如：

```csharp
throw new InvalidOperationException(
    $"Node '{node.Id}' returned output '{pair.Key}', "
    + "but the port does not declare Session availability.");
```

先扫描全部返回值，再由 `GraphExecutionSession` 调用 `SessionValueStore.SetPortValue`。不要在 validator 中修改 store。

- [ ] **Step 4: 在 `GraphExecutor.Validate` 增加 link 阶段校验。** 在现有 source/target port 存在性和 `DataType.IsCompatibleWith` 检查旁增加：

```csharp
if (!targetPort.IsControlPort
    && targetPort.Availability == FlowPortAvailability.Session
    && sourcePort.Availability != FlowPortAvailability.Session)
{
    result.Errors.Add(new FlowValidationError
    {
        Code = "SessionInputUnavailable",
        Message = $"Node '{node.DisplayName ?? node.Id}' input '{pair.Key}' "
            + "requires a Session-capable source port.",
        NodeId = node.Id,
        PortId = pair.Key,
    });
}
```

`Iteration` 输入可以从 `Iteration` 输出读取当前轮值，也可以从 `Session` 输出读取稳定值；`Session` input 则只能连接 `Session` output。每个 port 只属于一个阶段，不允许跨阶段复用同一个 port。`control` 端口跳过此规则，保持第一版不参与 session 初始化。

- [ ] **Step 5: 修改 iteration runner 的签名和 source-stage 解析。** 不要在每轮、每个 input 的热路径中执行 `sortedNodes.Single(...)`、`registry.Resolve(...)` 或重复构建 node/definition lookup。`GraphExecutionSession` 构造 session 时已经为每个 node 创建了 `FlowNodeSessionContext`；在同一阶段建立并保存一个按 node ID 索引的 `IReadOnlyDictionary<string, FlowNodeDefinition> definitionsByNodeId`，然后把它传给 runner。将 `FlowGraphIterationRunner.ExecuteAsync` 的 `FlowNodeRegistry registry` 参数替换为该字典，并增加一个 `IReadOnlySessionValueStore sessionValues` 参数：

```csharp
public static Task ExecuteAsync(
    IReadOnlyList<WorkflowNode> sortedNodes,
    IReadOnlyDictionary<string, IFlowNodeExecutor> executors,
    IReadOnlyDictionary<string, FlowNodeDefinition> definitionsByNodeId,
    FlowExecutionContext context,
    IReadOnlySessionValueStore sessionValues,
    ILogger logger,
    CancellationToken cancellationToken);
```

在 `GraphExecutionSession` 构造函数中复用已经解析出的 registration definition，建立一次性的只读 lookup；不要在 `ExecuteIterationCoreAsync` 或 `FlowGraphIterationRunner.ExecuteAsync` 内重复构建：

```csharp
private readonly IReadOnlyDictionary<string, FlowNodeDefinition> _definitionsByNodeId;
private readonly SessionValueStore _sessionValueStore = new SessionValueStore();
private readonly IReadOnlySessionValueStore _readOnlySessionValues;

// 构造函数中，_sessionContexts 填充完成后
_definitionsByNodeId = _sessionContexts.ToDictionary(
    pair => pair.Key,
    pair => pair.Value.Definition,
    StringComparer.Ordinal);
_readOnlySessionValues = _sessionValueStore.CreateReadOnlyView();
```

Task 2 仍需让旧 graph 能运行：沿用 Task 1 的 `SessionValueStore`，为该 session 建立只读视图但暂不写入初始化 output；Task 3 会复用同一个 store 写入并在停止路径清理。runner 只需要 `definitionsByNodeId`，不需要单独的 `nodesById`，因为 `LinkRef.SourceNodeId` 已经直接对应 definition dictionary 的 key；若未来需要更多 LinkRef 元数据，再在 session 创建阶段扩展为预解析的 immutable link metadata，不在本 task 增加新的公共 API。

runner 每轮开始处理 node 时直接执行 `definitionsByNodeId[node.Id]`；已配置 `LinkRef` 时直接以 `linkRef.SourceNodeId` 做一次字典访问，再以已校验的 `linkRef.SourceSlot` 做 `OutputPorts[slot]` 访问。source-stage 选择仍按单一 `Availability` 读取当前轮 context 或 session store；只有 `node.Inputs` 没有该 input key 时才使用 `DefaultValue`：

```csharp
// FlowGraphIterationRunner.ExecuteAsync 的 sortedNodes 循环内
var definition = definitionsByNodeId[node.Id];
var inputs = ResolveInputs(
    node,
    definition,
    definitionsByNodeId,
    context,
    sessionValues);
```

```csharp
private static Dictionary<string, object> ResolveInputs(
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlyDictionary<string, FlowNodeDefinition> definitionsByNodeId,
    FlowExecutionContext context,
    IReadOnlySessionValueStore sessionValues)
{
    var inputs = new Dictionary<string, object>();

    foreach (var inputPort in definition.InputPorts)
    {
        if (!node.Inputs.TryGetValue(inputPort.Id, out var configured))
        {
            if (inputPort.DefaultValue != null)
            {
                inputs[inputPort.Id] = inputPort.DefaultValue;
            }

            continue;
        }

        if (configured is LinkRef linkRef)
        {
            if (!definitionsByNodeId.TryGetValue(
                    linkRef.SourceNodeId,
                    out var sourceDefinition))
            {
                throw new InvalidOperationException(
                    $"Link source node '{linkRef.SourceNodeId}' was not found.");
            }

            var sourcePort = sourceDefinition.OutputPorts[linkRef.SourceSlot];
            if (sourcePort.Availability == FlowPortAvailability.Iteration
                && context.TryGetPortValue(
                    linkRef.SourceNodeId,
                    linkRef.SourceSlot,
                    out var currentValue))
            {
                inputs[inputPort.Id] = currentValue;
            }
            else if (sourcePort.Availability == FlowPortAvailability.Session
                && sessionValues.TryGetPortValue(
                    linkRef.SourceNodeId,
                    linkRef.SourceSlot,
                    out var sessionValue))
            {
                inputs[inputPort.Id] = sessionValue;
            }

            // 已配置 LinkRef 但 source value 缺失时，不回退 DefaultValue。
            continue;
        }

        inputs[inputPort.Id] = configured;
    }

    return inputs;
}
```

因此，已连接但 source value 缺失的必需 iteration 输入会继续触发现有 `HasMissingRequiredRuntimeInput` 并将节点标记为 `Skipped`，不会偷偷改用端口默认值；同样，已配置但缺失的 `Session` source 也保持缺失。这里不能把 Iteration source miss 再 fallback 到 Session store，也不能把 Session source miss fallback 到当前 context。保留当前 control 分支的 skip 判断，但让它读取同一个解析结果。`ExecuteAsync` 返回后先调用 `ValidateIterationOutputs`，再逐项用 `FindOutputSlot` 写入 `FlowExecutionContext.SetPortValue`；删除当前对 `slot < 0` 的静默忽略。不要向 runner 传入 `SessionValueStore` 的具体类型，只传 `IReadOnlySessionValueStore`。

- [ ] **Step 6: 运行校验和旧 graph 测试确认绿灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 阶段能力测试、未知/错误阶段 output 测试，以及现有 built-in、条件分支、iteration runner 测试全部通过；此时 GraphExecutionSession 只传入 session 构造阶段创建的空只读 session view，尚未写入初始化 output，但 runner 已经通过 `_definitionsByNodeId` 复用 definition lookup。

- [ ] **Step 7: 提交校验和解析层。**

```powershell
git add NodeCraft.Flow/Flow/FlowRuntimeValueValidator.cs NodeCraft.Flow/Flow/GraphExecutor.cs NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs NodeCraft.Flow/Flow/GraphExecutionSession.cs NodeCraft.Tests/SessionNodeInitializationTests.cs
git commit -m "feat: validate session port availability"
```

### Task 3: 接入 GraphExecutionSession 初始化时序、失败回滚和跨轮 session 值复用

**Files:**
- Modify: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Modify: `NodeCraft.Tests/SessionNodeInitializationTests.cs`

**Interfaces:**

`GraphExecutionSession` 增加私有字段：

```csharp
// Task 2 已建立并在每轮 runner 调用中复用
private readonly IReadOnlyDictionary<string, FlowNodeDefinition> _definitionsByNodeId;
private readonly SessionValueStore _sessionValueStore = new SessionValueStore();
private readonly IReadOnlySessionValueStore _readOnlySessionValues;
```

Task 2 已在构造函数中建立 `_definitionsByNodeId`、`_sessionValueStore` 和 `_readOnlySessionValues`；Task 3 必须复用同一批字段和同一个 store，不能重新分配第二个 session store。只读视图可在 store 清理后自然返回 false。为测试保留一个 `internal IReadOnlySessionValueStore SessionValues => _readOnlySessionValues;`，不把写入端公开给插件。

本 task 的关键时序约束是：`ResolveSessionInputs` 和 `EnsureRequiredSessionInputs` 对 `_orderedNodes` 中的每一个节点都执行；`IFlowNodeSessionInitializer` 只决定当前节点是否继续调用初始化器并写入 session outputs，不能决定 required session input 是否检查。

- [ ] **Step 1: 添加初始化时序和输入传递的失败测试。** 用两个节点定义建立如下 fixture：

```csharp
camera.OutputPorts.Add(new FlowPortDefinition
{
    Id = "calibration",
    IOType = EIOType.Output,
    DataType = FlowDataType.CameraCalibration,
    Availability = FlowPortAvailability.Session,
});
camera.OutputPorts.Add(new FlowPortDefinition
{
    Id = "image",
    IOType = EIOType.Output,
    DataType = FlowDataType.Number,
    Availability = FlowPortAvailability.Iteration,
});
algorithm.InputPorts.Add(new FlowPortDefinition
{
    Id = "calibration",
    IOType = EIOType.Input,
    DataType = FlowDataType.CameraCalibration,
    IsRequired = true,
    Availability = FlowPortAvailability.Session,
});
algorithm.InputPorts.Add(new FlowPortDefinition
{
    Id = "image",
    IOType = EIOType.Input,
    DataType = FlowDataType.Number,
    IsRequired = true,
    Availability = FlowPortAvailability.Iteration,
});
algorithm.OutputPorts.Add(new FlowPortDefinition
{
    Id = "result",
    IOType = EIOType.Output,
    DataType = FlowDataType.Number,
    Availability = FlowPortAvailability.Iteration,
});

workflow.Nodes.Add(new WorkflowNode { Id = "camera", TypeKey = camera.TypeKey });
workflow.Nodes.Add(new WorkflowNode
{
    Id = "algorithm",
    TypeKey = algorithm.TypeKey,
    Inputs =
    {
        ["calibration"] = new LinkRef { SourceNodeId = "camera", SourceSlot = 0 },
        ["image"] = new LinkRef { SourceNodeId = "camera", SourceSlot = 1 },
    },
});
```

`CameraTestExecutor` 实现 `IFlowNodeSessionLifecycle`, `IFlowNodeSessionInitializer`, `IFlowIterationSource`：`StartSessionAsync` 记录 `start:camera`，initializer 返回一个固定的 `CameraCalibration`，`PrepareIterationAsync` 只增加计数，`ExecuteAsync` 返回递增的 `image`。`AlgorithmTestExecutor` 的 initializer 记录收到的 `inputs["calibration"]`，`ExecuteAsync` 读取 `inputs["image"]`，记录 calibration 和 image，并严格返回已声明的 `result` output：

```csharp
var image = inputs["image"];
SeenImages.Add(image);
return Task.FromResult<IReadOnlyDictionary<string, object>>(
    new Dictionary<string, object> { ["result"] = image });
```

二者都把生命周期调用写入同一个 `List<string>`。测试断言：

```csharp
await session.StartAsync(CancellationToken.None);
var first = await session.ExecuteIterationAsync(CancellationToken.None);
var second = await session.ExecuteIterationAsync(CancellationToken.None);
await session.StopAsync();

return fixture.Algorithm.InitializeCount == 1
    && fixture.Algorithm.ExecuteCount == 2
    && fixture.Camera.PrepareCount == 2
    && fixture.Algorithm.InitializedCalibration != null
    && ReferenceEquals(
        fixture.Camera.Calibration,
        fixture.Algorithm.InitializedCalibration)
    && fixture.Algorithm.SeenImages.SequenceEqual(new object[] { 1d, 2d })
    && fixture.Calls.SequenceEqual(new[]
    {
        "start:camera",
        "initialize:camera",
        "start:algorithm",
        "initialize:algorithm",
        "execute:camera:1",
        "execute:algorithm:1",
        "execute:camera:2",
        "execute:algorithm:2",
        "stop:algorithm",
        "stop:camera",
    })
    && first.TryGetPortValue("camera", 1, out _)
    && second.TryGetPortValue("camera", 1, out _)
    && first.TryGetPortValue("algorithm", 0, out var firstResult)
    && Equals(firstResult, 1d)
    && second.TryGetPortValue("algorithm", 0, out var secondResult)
    && Equals(secondResult, 2d);
```

同时添加断言，`InitializeSessionAsync` 从未调用 camera 的 `PrepareIterationAsync` 或 `ExecuteAsync`；初始化阶段只读取其返回的 session output。

另加一个专门覆盖无 initializer 节点的失败用例。`CreateSessionInputOnlyFixture()` 复用 camera 的 `Session` calibration output 定义，但让 camera initializer 返回空字典；下游 `consumer` 定义一个 required 的 `Session` calibration input 并通过 `LinkRef` 连接 camera，同时注册一个只实现 `IFlowNodeExecutor`、不实现 `IFlowNodeSessionInitializer` 的 `RequiredSessionConsumerTestExecutor`：

```csharp
await RunAsync("required session input is checked without an initializer", async () =>
{
    var fixture = CreateSessionInputOnlyFixture();
    await using var session = fixture.Executor.CreateSession();

    try
    {
        await session.StartAsync(CancellationToken.None);
        return false;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message.Contains(
                   "SessionInputUnavailable",
                   StringComparison.Ordinal)
            && exception.Message.Contains("consumer", StringComparison.Ordinal)
            && fixture.Consumer.ExecuteCount == 0;
    }
});
```

`CreateSessionInputOnlyFixture()` 必须把 `camera` 放在 `consumer` 的拓扑前方，camera initializer 返回空字典，`consumer.Inputs["calibration"]` 写入 camera slot 0 的 `LinkRef`；`RequiredSessionConsumerTestExecutor.ExecuteAsync` 仅递增 `ExecuteCount` 并返回空字典。这样即使 consumer 没有 initializer，启动阶段也必须在进入任何 iteration 前抛出 `SessionInputUnavailable`。

为使这个 fixture 可直接实现，`CameraTestExecutor` 增加 `bool producesCalibration = true` 构造参数；为 false 时 `InitializeSessionAsync` 返回空字典，为 true 时返回固定的 `CameraCalibration`。无 initializer executor 使用以下最小形状：

```csharp
private sealed class RequiredSessionConsumerTestExecutor : IFlowNodeExecutor
{
    public int ExecuteCount { get; private set; }

    public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
        FlowExecutionContext context,
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        ExecuteCount++;
        return Task.FromResult<IReadOnlyDictionary<string, object>>(
            new Dictionary<string, object>());
    }
}
```

`CreateSessionInputOnlyFixture()` 返回的 fixture 至少暴露 `GraphExecutor Executor` 和 `RequiredSessionConsumerTestExecutor Consumer`，并使用该 executor 注册 `consumer`；不得通过让 consumer 的 initializer 返回空字典来模拟本用例，因为那样无法证明检查逻辑与 `IFlowNodeSessionInitializer` 无关。

把上述 registry、workflow、executor 和 counters 封装在 `private sealed class SessionFixture` 中，至少提供 `GraphExecutor Executor`、`CameraTestExecutor Camera`、`AlgorithmTestExecutor Algorithm` 和 `IList<string> Calls` 属性；实现一个无参数的 `CreateSessionFixture()` 工厂完成 registry 注册、两个 workflow node 的 link 配置和 executor 注入，后续 one-shot/continuous 测试直接调用它，不复制另一套 graph 构造逻辑。`CameraTestExecutor` 和 `AlgorithmTestExecutor` 都必须显式实现下面四个接口中的适用成员：`IFlowNodeExecutor.ExecuteAsync`、`IFlowNodeSessionLifecycle.StartSessionAsync/StopSessionAsync`、`IFlowNodeSessionInitializer.InitializeSessionAsync`、`IFlowIterationSource.PrepareIterationAsync`。

- [ ] **Step 2: 运行测试确认红灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 新测试失败，因为 `StartCoreAsync` 当前只调用 lifecycle，不解析 session 输入、不调用 initializer；有 initializer 的算法会把缺失 calibration 拖到 iteration，而无 initializer 的 consumer 甚至会错误地让 `StartAsync` 成功。

- [ ] **Step 3: 在 `GraphExecutionSession.StartCoreAsync` 实现逐节点时序。** 对 `_orderedNodes` 中的每个节点严格执行：

```csharp
var executor = _executors[node.Id];
var context = _sessionContexts[node.Id];

if (executor is IFlowNodeSessionLifecycle lifecycle)
{
    await lifecycle.StartSessionAsync(context, cancellationToken)
        .ConfigureAwait(false);
    await AddStartedLifecycleOrCleanupIfStoppingAsync(
            lifecycle,
            context,
            cancellationToken)
        .ConfigureAwait(false);
}

var inputs = ResolveSessionInputs(node, context.Definition, _readOnlySessionValues);
EnsureRequiredSessionInputs(node, context.Definition, inputs);

if (executor is IFlowNodeSessionInitializer initializer)
{
    var outputs = await initializer.InitializeSessionAsync(
            context,
            inputs,
            cancellationToken)
        .ConfigureAwait(false);

    FlowRuntimeValueValidator.ValidateSessionOutputs(node, context.Definition, outputs);
    foreach (var pair in outputs)
    {
        _sessionValueStore.SetPortValue(
            node.Id,
            FlowRuntimeValueValidator.FindOutputSlot(context.Definition, pair.Key),
            pair.Value);
    }
}

// foreach 结束后、设置 Running 前
_sessionValueStore.Seal();
```

上面的两行输入解析和 required 检查必须位于 initializer 条件之外，并且对每个节点执行。没有 initializer 的节点只完成 session input 检查，不调用普通 `ExecuteAsync`，也不会向 `SessionValueStore` 写入任何 output；有 initializer 的节点复用同一个 `inputs` 字典调用 `InitializeSessionAsync`。

新增私有方法 `AddStartedLifecycleOrCleanupIfStoppingAsync(IFlowNodeSessionLifecycle lifecycle, FlowNodeSessionContext context, CancellationToken cancellationToken)`，把现有 `StartCoreAsync` 中的注册竞态逻辑原样抽出：在 `_stateGate` 内仅当 state 仍为 `Starting` 且 token 未取消时加入 `_startedLifecycles`；否则使用 `CancellationToken.None` 调用当前 lifecycle 的 `StopSessionAsync`，记录 cleanup error，随后抛出取消异常。`StartSessionAsync` 成功后立即加入 `_startedLifecycles`，然后才允许执行当前节点的 session input validation 和（若存在）initializer。这样当前节点输入检查或初始化失败时，当前节点也在逆序清理集合中；`StartSessionAsync` 自身失败则继续依靠节点内部 catch 清理部分资源，不把未成功启动的 lifecycle 加入集合。

在 `Seal()` 前重新调用 `cancellationToken.ThrowIfCancellationRequested()` 并确认 state 仍为 `Starting`；只有所有节点初始化成功时才封存 store，随后在同一个 state gate 过渡到 `Running`。

新增两个私有 helper，并使用以下确切签名：

```csharp
private static Dictionary<string, object> ResolveSessionInputs(
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlySessionValueStore sessionValues);

private static void EnsureRequiredSessionInputs(
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlyDictionary<string, object> inputs);
```

`ResolveSessionInputs` 由每个节点无条件调用，但只处理非 control 且 `Availability == FlowPortAvailability.Session` 的输入端口，并严格区分三种状态：

```csharp
if (!node.Inputs.TryGetValue(inputPort.Id, out var configured))
{
    if (inputPort.DefaultValue != null)
    {
        inputs[inputPort.Id] = inputPort.DefaultValue;
    }
}
else if (configured is LinkRef linkRef)
{
    if (sessionValues.TryGetPortValue(
            linkRef.SourceNodeId,
            linkRef.SourceSlot,
            out var sessionValue))
    {
        inputs[inputPort.Id] = sessionValue;
    }
    // LinkRef 已配置但 source 没有 session value：保持缺失，不能使用 DefaultValue。
}
else
{
    inputs[inputPort.Id] = configured;
}
```

`EnsureRequiredSessionInputs` 也由每个节点无条件调用，只对 `!inputPort.IsControlPort && inputPort.IsRequired && inputPort.Availability == FlowPortAvailability.Session` 的端口执行缺失检查：缺失时抛出包含 `SessionInputUnavailable` 和 node/port ID 的 `InvalidOperationException`，无论该端口定义是否有 `DefaultValue`。optional session input 可以保持缺失；但只要 workflow 已配置 `LinkRef`，也不使用 `DefaultValue`。只有 workflow 未配置该 input key 时，`ResolveSessionInputs` 才会把 `DefaultValue` 放入 inputs。这样连接存在但上游 initializer 没有产生值时，无论当前节点是否实现 initializer，错误都会在 session 初始化阶段暴露，不会被默认值掩盖。

- [ ] **Step 4: 把预解析 definition lookup 和只读 store 传给 iteration runner。** 在 `ExecuteIterationCoreAsync` 的现有调用中传入 session 构造阶段保存的 `_definitionsByNodeId` 和 `_readOnlySessionValues`：

```csharp
await FlowGraphIterationRunner.ExecuteAsync(
        _orderedNodes,
        _executors,
        _definitionsByNodeId,
        context,
        _readOnlySessionValues,
        _logger,
        linkedCancellation.Token)
    .ConfigureAwait(false);
```

不改变 `FlowExecutionContext` 的 `_values` 结构；当前轮输出仍只走 `SetPortValue`，不能访问 `_sessionValueStore` 的写入端。此处的 `_definitionsByNodeId` 和 `_readOnlySessionValues` 都是 session 级复用对象，不能在每轮重新由 `Registry.Resolve` 或 LINQ 查询构造。

- [ ] **Step 5: 为所有停止路径清理 session store。** 在 `StopStartedLifecyclesCoreAsync` 的 cleanup `finally` 中执行 `_sessionValueStore.Clear()`，确保以下路径都清理：正常 `StopAsync`、初始化异常的 outer catch、初始化取消、iteration fault 后停止、用户在启动或 iteration 阻塞时停止。保持现有 `CancellationToken.None` 清理 token、逆序 lifecycle 和 `AggregateException` 语义。

清理顺序必须是：等待/停止已启动 lifecycle → 清空 session store → 将 state 设为 `Stopped` → release `_iterationGate`。只有 lifecycle 停止和 store 清理完成后才允许 `state == Stopped`；如果任一 `StopSessionAsync` 失败，仍然继续清理其余节点并清空 store，最后按现有规则抛出 aggregate cleanup error。

- [ ] **Step 6: 添加 session 值复用、隔离和失败清理测试。** 在同一测试文件加入这些可独立定位的 `RunAsync` 用例：

  - source 使用两个独立 output：`baseline` 为 `Session`、`current` 为 `Iteration`；initializer 只返回 `baseline`，iteration 只返回 `current`。下游通过两个独立 `LinkRef` 分别读取稳定值和当前轮值，证明 iteration output 不会覆盖 session output。
  - initializer 返回的 `CameraCalibration` 在两个 iteration 中都是同一个稳定引用；另一个正确实现的节点显式复制一个可变 payload 后修改副本，原始 session payload 保持不变，验证节点遵守逻辑只读约定，同时确认引擎不做隐式深拷贝也不把副本写回 store。
  - initializer 返回未知 output、错误类型或只含 `Iteration` 能力的 output 时 `StartAsync` 失败，state 不为 `Running`，且该节点以及之前已启动节点按当前节点优先、随后逆拓扑顺序停止。
  - 一个只实现 `IFlowNodeExecutor`、不实现 `IFlowNodeSessionInitializer` 的 consumer 具有 required `Session` calibration input，并连接到一个未产生 calibration 的 Camera；consumer 在没有进入任何 iteration 的情况下，`StartAsync` 仍必须以 `SessionInputUnavailable` 失败。
  - Algorithm 的 required `Session` 输入配置为指向 Camera 的 `LinkRef`，Camera initializer 不返回 calibration，即使 Algorithm 输入端口定义了 `DefaultValue`，session 仍以 `SessionInputUnavailable` 失败；删除该 `LinkRef` 后才允许使用同一个 `DefaultValue`。
  - iteration 对只有 `Session` 能力的 output 返回值时失败，`FlowExecutionContext` 不写入该值，`session.SessionValues` 中的初始化对象仍是原引用；随后 `StopAsync` 完成后读取视图为空且 `session.State == GraphExecutionSessionState.Stopped`。
  - 两个独立 session 使用相同 workflow 时各自持有不同的 initializer 输出；停止第一个 session 不影响第二个 session 的读取视图。
  - 同一个 `GraphExecutionSession` 在 `StopAsync` 后再次 `StartAsync` 抛出 `InvalidOperationException`；重新创建 session 才能再次执行。
  - 无 initializer 的现有 `CreateIterationFixture` 继续只调用一次 `StartSessionAsync`、每轮调用一次 `PrepareIterationAsync`/`ExecuteAsync`，覆盖旧插件兼容。

失败测试应使用现有 `Throws<TException>` 和 `RunAsync` 跑棒 helper，不引入 xUnit/NUnit 依赖。

将 `CreateSessionFixture` 提供以下确定的测试配置参数，避免测试只通过注释表达连接状态：

```csharp
private static SessionFixture CreateSessionFixture(
    bool cameraProducesCalibration = true,
    bool connectCalibration = true,
    object defaultCalibration = null);
```

其中 `connectCalibration = true` 时才把 Algorithm 的 `calibration` key 写成 Camera 的 `LinkRef`；`cameraProducesCalibration = false` 时 Camera initializer 返回空字典；`defaultCalibration` 非 null 时写入 Algorithm calibration input port 的 `DefaultValue`。加入以下两个断言，锁定“连接失败不回退、未连接才使用默认值”：

```csharp
await RunAsync("missing linked session value is not replaced by a default", async () =>
{
    var defaultCalibration = CreateTestCalibration();
    var fixture = CreateSessionFixture(
        cameraProducesCalibration: false,
        connectCalibration: true,
        defaultCalibration: defaultCalibration);
    await using var session = fixture.Executor.CreateSession();

    try
    {
        await session.StartAsync(CancellationToken.None);
        return false;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message.Contains("SessionInputUnavailable", StringComparison.Ordinal)
            && fixture.Algorithm.InitializeCount == 0;
    }
});

await RunAsync("unconfigured session input uses its default value", async () =>
{
    var defaultCalibration = CreateTestCalibration();
    var fixture = CreateSessionFixture(
        cameraProducesCalibration: false,
        connectCalibration: false,
        defaultCalibration: defaultCalibration);
    await using var session = fixture.Executor.CreateSession();
    await session.StartAsync(CancellationToken.None);
    await session.StopAsync();

    return fixture.Algorithm.InitializeCount == 1
        && ReferenceEquals(
            fixture.Algorithm.InitializedCalibration,
            defaultCalibration);
});
```

- [ ] **Step 7: 运行核心 session 测试确认绿灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 新增的拓扑初始化、一次性调用、值优先级、失败清理、store 清理和旧节点回归测试通过；现有 `GraphExecutionSessionTests`、`FlowExecutionControllerTests` 也保持通过。

- [ ] **Step 8: 提交 runtime 编排。**

```powershell
git add NodeCraft.Flow/Flow/GraphExecutionSession.cs NodeCraft.Tests/SessionNodeInitializationTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: initialize graph nodes from session values"
```

### Task 4: 将立体相机 calibration 迁移到 session 初始化输出

**Files:**
- Modify: `NodeCraft.Vision/Camera/FrameBundle.cs`
- Modify: `NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs`
- Modify: `NodeCraft.Vision/Nodes/StereoCameraExecutor.cs`
- Modify: `NodeCraft.Vision/Plugin/StereoCameraRegistration.cs`
- Modify: `NodeCraft.Tests/VisionPluginTests.cs`
- Modify: `NodeCraft.Tests/SessionNodeInitializationTests.cs` if the real stereo executor test is kept with the core test group

**Interfaces:**

`FrameBundle` 改为只携带同步的每轮数据：

```csharp
internal FrameBundle(
    ulong sequence,
    FlowImage colorImage,
    FlowImage depthImage)
```

`StereoCameraCaptureSession` 继续公开启动阶段已经读取的：

```csharp
internal CameraCalibration ColorCalibration { get; }
internal CameraCalibration DepthCalibration { get; }
```

`StereoCameraExecutor` 增加：

```csharp
public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
    FlowNodeSessionContext context,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken)
```

初始化器从已启动的 `_captureSession` 读取两个 calibration 并返回 `colorCalibration`/`depthCalibration`；如果 capture session 未启动或 calibration 缺失则抛出。`ExecuteAsync` 只返回 `colorImage`/`depthImage`，不再返回 calibration。

- [ ] **Step 1: 添加失败的 metadata 和 executor 测试。** 扩展 `VisionPluginTests` 的注册断言：

```csharp
var colorImage = stereo.Definition.OutputPorts.Single(port => port.Id == "colorImage");
var depthImage = stereo.Definition.OutputPorts.Single(port => port.Id == "depthImage");
var colorCalibration = stereo.Definition.OutputPorts.Single(port => port.Id == "colorCalibration");
var depthCalibration = stereo.Definition.OutputPorts.Single(port => port.Id == "depthCalibration");

return colorImage.Availability == FlowPortAvailability.Iteration
    && depthImage.Availability == FlowPortAvailability.Iteration
    && colorCalibration.Availability == FlowPortAvailability.Session
    && depthCalibration.Availability == FlowPortAvailability.Session;
```

使用现有 `IStereoCameraDevice` fake 增加一次直接 executor 测试：启动 executor 后调用 initializer，断言返回的两个 calibration 与设备读取的对象相同；调用 initializer 不增加 `PrepareIterationAsync` 计数；调用 `ExecuteAsync` 后返回 key 集合严格为 `colorImage`/`depthImage`。

- [ ] **Step 2: 运行 Vision 测试确认红灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 阶段能力断言失败；当前 `StereoCameraExecutor` 没有 `InitializeSessionAsync`，且每轮仍返回 calibration。

- [ ] **Step 3: 修改注册定义。** 在 `StereoCameraRegistration.CreateOutputPort` 增加 `FlowPortAvailability availability` 参数并写入 `Availability`；image 调用显式传 `Iteration` 或依赖默认值，calibration 调用传 `Session`：

```csharp
CreateOutputPort("colorImage", "Color Image", FlowDataType.Image,
    FlowPortAvailability.Iteration),
CreateOutputPort("depthImage", "Depth Image", FlowDataType.Image,
    FlowPortAvailability.Iteration),
CreateOutputPort("colorCalibration", "Color Calibration", FlowDataType.CameraCalibration,
    FlowPortAvailability.Session),
CreateOutputPort("depthCalibration", "Depth Calibration", FlowDataType.CameraCalibration,
    FlowPortAvailability.Session),
```

不把 `Availability` 添加到 `PortParameter`，不修改 `StereoCameraNodeModel` 的 XML 持久化模型；阶段能力仅存在于 registry definition。

- [ ] **Step 4: 拆分逐轮 bundle 和稳定 calibration。** 从 `FrameBundle` 构造函数、字段和属性中移除 `ColorCalibration`/`DepthCalibration`；修改 `StereoCameraCaptureSession` capture loop 的 `new FrameBundle(...)` 调用，只传 sequence、color image、depth image。保留 `_colorCalibration`/`_depthCalibration` 在 capture session 启动阶段的读取、非空验证和停止时清理，因为 initializer 需要读取缓存的稳定对象。

- [ ] **Step 5: 实现 `StereoCameraExecutor.InitializeSessionAsync` 并收窄逐轮输出。** 加入以下最小实现：

```csharp
public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
    FlowNodeSessionContext context,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    var session = _captureSession
        ?? throw new InvalidOperationException("StereoCamera session has not started.");
    if (session.ColorCalibration == null || session.DepthCalibration == null)
    {
        throw new InvalidOperationException("StereoCamera calibration was not available.");
    }

    IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
    {
        ["colorCalibration"] = session.ColorCalibration,
        ["depthCalibration"] = session.DepthCalibration,
    };
    return Task.FromResult(outputs);
}
```

保留 `PrepareIterationAsync` 对 mailbox 的等待；把 `ExecuteAsync` 的 dictionary 改为仅包含 `colorImage` 和 `depthImage`。不要在 initializer 中读取新 frame、调用 `WaitForNextAsync`、调用 `PrepareIterationAsync` 或调用普通 `ExecuteAsync`。

- [ ] **Step 6: 运行 Vision 和核心 session 测试确认绿灯。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: 立体相机注册阶段断言、initializer calibration identity、每轮输出 key、现有设备/capture 清理测试和核心算法初始化测试全部通过；普通 `.flow.xml` 节点模型序列化测试不出现 `Availability` 字段。

- [ ] **Step 7: 提交立体相机迁移。**

```powershell
git add NodeCraft.Vision/Camera/FrameBundle.cs NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs NodeCraft.Vision/Nodes/StereoCameraExecutor.cs NodeCraft.Vision/Plugin/StereoCameraRegistration.cs NodeCraft.Tests/VisionPluginTests.cs NodeCraft.Tests/SessionNodeInitializationTests.cs
git commit -m "feat: expose stereo calibration as session outputs"
```

### Task 5: 完成一次性执行、连续执行和全量回归验证

**Files:**
- Modify: `NodeCraft.Tests/SessionNodeInitializationTests.cs`
- Do not modify: `NodeCraft/Execution/FlowExecutionController.cs`; its existing `RunOnceAsync` and `RunContinuouslyAsync` APIs are the integration surface.

**Interfaces:**

不再增加 production API。通过现有 `GraphExecutor.ExecuteAsync` 和 `FlowExecutionController` 验证二者都复用同一套 `GraphExecutionSession` 初始化语义：一次性执行初始化一次并清理一次，连续执行初始化一次、每轮执行多次、停止时逆序清理。

- [ ] **Step 1: 添加一次性和连续执行验收测试。** 复用 Task 3 的 `SessionFixture`，加入：

```csharp
await RunAsync("one-shot graph execution initializes and cleans session nodes", async () =>
{
    var fixture = CreateSessionFixture();
    var context = await fixture.Executor.ExecuteAsync(CancellationToken.None);

    return fixture.Camera.InitializeCount == 1
        && fixture.Algorithm.InitializeCount == 1
        && fixture.Camera.ExecuteCount == 1
        && fixture.Algorithm.ExecuteCount == 1
        && fixture.Camera.StopCount == 1
        && fixture.Algorithm.StopCount == 1
        && context.TryGetPortValue("algorithm", 0, out var result)
        && Equals(result, 1d);
});

await RunAsync("continuous controller reuses one initialized algorithm", async () =>
{
    var fixture = CreateSessionFixture();
    var controller = new FlowExecutionController();
    using var cancellation = new CancellationTokenSource();
    var callbackCount = 0;

    await controller.RunContinuouslyAsync(
        fixture.Executor.CreateSession(),
        (context, iteration, elapsed) =>
        {
            callbackCount++;
            if (callbackCount == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        },
        cancellation.Token);

    return fixture.Algorithm.InitializeCount == 1
        && callbackCount == 2
        && fixture.Algorithm.ExecuteCount == 2
        && fixture.Algorithm.StopCount == 1;
});
```

- [ ] **Step 2: 运行 Windows 测试跑棒确认红/绿结果。**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows`

Expected: 若 Task 1–4 完成，新增验收测试和既有测试全部通过并输出 `ALL PASS`；若失败，只修复与本设计直接相关的签名、阶段校验、清理或 fixture，不放宽断言。

- [ ] **Step 3: 运行 solution build 和 CLI 回归。**

Run: `dotnet build NodeCraft.sln`

Expected: solution build 成功，无新的 nullable、WPF 或插件 API 编译错误。

Run: `dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj`

Expected: CLI 测试跑棒通过；session 端口阶段元数据不影响 CLI 或 `.flow.xml` 生成。

- [ ] **Step 4: 做最终静态检查。**

Run: `rg -n "SetPortValue\(|SessionValueStore|InitializeSessionAsync|Availability|SessionInputUnavailable" NodeCraft.Flow NodeCraft.Vision NodeCraft.Tests -g '*.cs'`

Expected: 只有 `SessionValueStore` 内部初始化路径调用 session store 的写入 API；`FlowExecutionContext.SetPortValue` 仍只出现在当前轮路径；`FlowGraphIterationRunner` 不再包含 `sortedNodes.Single` 或 `Registry.Resolve` 的 per-input/per-iteration lookup；旧节点未被批量添加 initializer；立体相机 calibration output 只在 initializer 和 session store 进入稳定输入。

逐段阅读本计划，确认每个 task 都有明确文件、接口、失败测试、运行命令、通过标准和 commit；确认没有未定义的 executor、controller 方法、数据类型或模糊的“稍后实现”步骤。

- [ ] **Step 5: 提交最终测试调整。**

```powershell
git add NodeCraft.Tests/SessionNodeInitializationTests.cs NodeCraft.Tests/FlowExecutionControllerTests.cs NodeCraft.Tests/GraphExecutionSessionTests.cs NodeCraft.Tests/Program.cs
git commit -m "test: cover session initialization execution paths"
```

## Spec Coverage Self-Review

- `IFlowNodeSessionInitializer`、`FlowPortAvailability` 和默认 `Iteration`：Task 1。
- V1 input/output 的 `Availability` 都只能精确选择 `Iteration` 或 `Session`；需要稳定值和临时值时使用独立 output 或 session 初始化计算节点：Task 1 文档契约、Task 2/3 按 source output stage 解析。
- 按拓扑顺序执行 `StartSessionAsync` → 所有节点的 session input 解析/required 校验 →（若存在）initializer → output 校验写入：Task 3；拓扑排序本身沿用 `GraphExecutor.TopologicalSort`，不引入并行 DAG。
- session input 只从 `Availability == Session` 的 link、常量和默认值解析；iteration input 按 source output stage 选择 context 或 session store：Task 2/3；control 端口明确排除。
- `DefaultValue` 只对未配置输入生效，已配置但缺失的 `LinkRef` 不回退默认值：Task 2/3。
- `SessionValueStore` 一次性写入、封存、只读视图、跨 session 隔离和停止清理：Task 1/3。
- `LinkRef` 按 source output 的 `Availability` 选择当前 `FlowExecutionContext` 或 `SessionValueStore`；已配置 link 缺值不回退 `DefaultValue`，独立 `Iteration` output 不覆盖 `Session` output：Task 2/3。
- 初始化 output 未知 ID、阶段错误和类型错误，以及所有节点（包括无 initializer 节点）的 required session input 缺失：Task 2/3。
- iteration output 未知 ID、`Availability == Session` 或类型错误：Task 2/3。
- 初始化失败、取消、iteration 异常和正常停止的逆序清理：Task 3。
- 相机 calibration 从逐轮 bundle 迁移到一次性初始化 output，算法实例只初始化一次：Task 4/5。
- 旧 executor、`GraphExecutor.ExecuteAsync`、连续 controller 和 `.flow.xml` 兼容：Task 3/4/5。
- 非目标（并行 DAG、普通 `ExecuteAsync` 初始化、全局服务定位器、动态 control 初始化、UI 改造）没有进入任何 production task。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-15-session-node-initialization.md`. Two execution options:

1. **Subagent-Driven（推荐）**：按 Task 1–5 每个任务派发新 subagent，任务之间做 review 和测试检查。
2. **Inline Execution**：在当前会话使用 `superpowers:executing-plans`，按任务批次实施并在每个 checkpoint 停下复核。

实施时必须按任务顺序执行，因为 Task 2 依赖 Task 1 的公开契约，Task 3 依赖 Task 2 的 validator 和 runner 参数，Task 4 依赖 Task 3 的初始化时序。
