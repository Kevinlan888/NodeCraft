# Dynamic Input Ports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in, same-type dynamic input ports that node instances can add and remove at runtime while preserving deterministic order, graph links, persistence, and execution behavior.

**Architecture:** Add a `FlowDynamicInputTemplate` to `FlowNodeDefinition` and store generated ports in the existing ordered `NodeModel.InputParameters` list with an `IsDynamic` marker. Centralize fixed-plus-dynamic slot resolution in `FlowDynamicInputResolver`; route canvas rendering, graph reconciliation, XML persistence, workflow conversion, validation, and execution through that resolver. Keep `GraphLink` slot-based, reindexing later target slots only when a dynamic port is removed.

**Tech Stack:** C# 9, .NET 8 WPF (`net8.0-windows`), existing `NodeCraft.Flow` graph/runtime model, XML serialization through `System.Xml.Linq`, and the self-running `NodeCraft.Tests` console test harness.

## Global Constraints

- Only node definitions with a dynamic input template expose add/remove controls; nodes with fixed inputs remain unchanged.
- Every generated port uses one node-level template and therefore the same data type, availability, required flag, default value, and preferred direction.
- Each dynamic port is an independent single-connection input. Multiple links to the same input socket are not implemented.
- Dynamic port order is explicit and is determined by the effective input-port list, never by `Dictionary<string, object>` enumeration order.
- Do not change the `IFlowNodeExecutor.ExecuteAsync` signature.
- Graph XML writes format version 5, reads version 4 and version 5, and interprets a missing version-5 `IsDynamic` attribute as `false`.
- Removing a connected dynamic port removes only its own link, then decrements later target slots on that node.
- Adding a dynamic port grows a node when its explicit height cannot contain the new input rows; removing a port never automatically shrinks a manually chosen height.
- A saved graph that uses dynamic ports on a node type that no longer supports them fails validation/load rather than silently dropping ports or links.
- Do not add a user-authored port-schema editor or a multi-link fan-in feature.
- Keep host and flow projects on their existing C# 9/.NET 8 WPF configuration and nullable settings.
- Follow the existing `NodeCraft.Tests` console runner pattern and run focused tests in a failing state before each production change.
- Before the first TDD cycle, verify the solution assets and baseline test runner. If restore is needed, run `dotnet restore NodeCraft.sln` with package access, then use `--no-restore` for subsequent commands.

---

## File Map

- Create `NodeCraft.Flow/Flow/FlowDynamicInputResolver.cs` — validate templates, materialize node ports, build effective definitions, and expose ordered port descriptors.
- Create `NodeCraft.Tests/DynamicInputPortTests.cs` — focused schema, graph, UI, serialization, workflow, and execution regression tests as another `Program` partial.
- Modify `NodeCraft.Flow/Flow/FlowSchema.cs` — add the dynamic template, the node-level template property, and the effective-port dynamic marker.
- Modify `NodeCraft.Flow/Flow/FlowNodeRegistry.cs` — validate dynamic templates after control-port injection during registration.
- Modify `NodeCraft.Flow/Flow/Parameter.cs` — mark persisted runtime ports as dynamic or fixed.
- Modify `NodeCraft.Flow/Flow/WorkflowDocument.cs` — persist ordered dynamic input IDs in workflow nodes.
- Modify `NodeCraft.Flow/Flow/FlowSocketResolver.cs` — resolve dynamic runtime sockets with the same slots used everywhere else.
- Modify `NodeCraft.Flow/Flow/FlowCanvas.cs` — materialize dynamic ports, expose add/remove operations, use effective definitions for link validation, reindex target slots, and update node height.
- Modify `NodeCraft.Flow/Flow/NodeView.cs` — render opt-in add controls and per-dynamic-port remove controls and refresh sockets after mutations.
- Modify `NodeCraft.Flow/Themes/Flow.xaml` — add the compact action-button style used by dynamic input controls.
- Modify `NodeCraft.Flow/Flow/GraphModelLinkReconciler.cs` — validate fixed-plus-dynamic target slots and restore each runtime `LinkId`.
- Modify `NodeCraft.Flow/Flow/GraphModelXmlSerializer.cs` — write v5 dynamic markers and accept v4/v5 input-port data.
- Modify `NodeCraft.Flow/Flow/GraphModelWorkflowAdapter.cs` — emit ordered dynamic input IDs and dynamic `LinkRef` values.
- Modify `NodeCraft.Flow/Flow/GraphExecutor.cs` — validate required/type/session inputs against each workflow node's effective definition.
- Modify `NodeCraft.Flow/Flow/GraphExecutionSession.cs` — construct session contexts with per-node effective definitions.
- Modify `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs` — show dynamic inputs in the standard input summary and resolve their links by effective slot.
- Modify `NodeCraft.Tests/Program.cs` — invoke the focused test entry point and update format-version assertions whose expected current version changes from v4 to v5.

The plan does not add a TCP Send node because the repository currently has no TCP Send implementation. The focused tests register a TCP Send-shaped fake node to prove the framework contract; a later node-specific feature can opt into the template without changing the framework design.

---

### Task 0: Establish a verified baseline

**Files:**

- Modify: none
- Test: `NodeCraft.sln`, `NodeCraft.Tests/NodeCraft.Tests.csproj`

**Interfaces:**

- Consumes: the current solution and existing NuGet assets.
- Produces: a known-good baseline build and test result before any source change.

- [ ] **Step 1: Verify the current solution builds without restore.**

Run:

```powershell
dotnet build NodeCraft.sln --no-restore
```

Expected: the solution builds successfully. If this fails with missing assets or `NU*` restore errors, run `dotnet restore NodeCraft.sln` with package access and repeat the `--no-restore` build; do not interpret an asset failure as the feature's RED state.

- [ ] **Step 2: Run the existing test harness before editing code.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: the existing runner completes with its current all-pass summary. Record any pre-existing failure separately instead of folding it into the dynamic-input work.

---

### Task 1: Add the dynamic-input schema and shared resolver

**Files:**

- Create: `NodeCraft.Flow/Flow/FlowDynamicInputResolver.cs`
- Create: `NodeCraft.Tests/DynamicInputPortTests.cs`
- Modify: `NodeCraft.Flow/Flow/FlowSchema.cs`
- Modify: `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`
- Modify: `NodeCraft.Flow/Flow/Parameter.cs`
- Modify: `NodeCraft.Flow/Flow/WorkflowDocument.cs`
- Modify: `NodeCraft.Tests/Program.cs:54-74`

**Interfaces:**

- Consumes: `FlowNodeDefinition`, `FlowPortDefinition`, `PortParameter`, `NodeModel`, and `WorkflowNode`.
- Produces:
  - `FlowDynamicInputTemplate` with `PortIdPrefix`, `DisplayNamePrefix`, `DataType`, `PreferredDirection`, `IsRequired`, `DefaultValue`, `Availability`, `MinCount`, `InitialCount`, and nullable `MaxCount`.
  - `FlowNodeDefinition.DynamicInputTemplate`.
  - `FlowPortDefinition.IsDynamic`, defaulting to `false`.
  - `PortParameter.IsDynamic`, defaulting to `false`.
  - `WorkflowNode.DynamicInputPortIds`, initialized to an empty list.
  - `FlowInputPortDescriptor` with `Slot`, `Definition`, and `RuntimePort`.
  - `FlowDynamicInputResolver.ValidateTemplate(FlowNodeDefinition definition)`.
  - `FlowDynamicInputResolver.MaterializeNodePorts(NodeModel node, FlowNodeDefinition definition)`.
  - `FlowDynamicInputResolver.GetDynamicPortIds(NodeModel node)`.
  - `FlowDynamicInputResolver.ResolveNodeInputPorts(NodeModel node, FlowNodeDefinition definition)`.
  - `FlowDynamicInputResolver.ResolveDefinition(FlowNodeDefinition registeredDefinition, IReadOnlyList<string> dynamicInputPortIds)`.
  - `FlowDynamicInputResolver.TryAddDynamicPort(NodeModel node, FlowNodeDefinition definition, out PortParameter port, out string error)`.
  - `FlowDynamicInputResolver.TryRemoveDynamicPort(NodeModel node, FlowNodeDefinition definition, string portId, out int removedSlot, out string error)`.

- [ ] **Step 1: Register the focused test entry point and write the first failing schema tests.**

Add the call in `Program.Main` after the existing theme tests:

```csharp
RunThemeTests();
RunDynamicInputPortTests();
```

Create `DynamicInputPortTests.cs` as an `internal static partial class Program`. Add tests for template materialization and opt-in behavior:

```csharp
private static void RunDynamicInputPortTests()
{
    Run("dynamic template materializes ordered same-type ports", () =>
    {
        var definition = CreateDynamicDefinition(initialCount: 2, maxCount: null);
        var node = new NodeModel { ExecutorType = definition.TypeKey };

        FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
        var ports = FlowDynamicInputResolver.ResolveNodeInputPorts(node, definition);

        return ports.Count == 3
            && ports[1].Definition.IsDynamic
            && ports[2].Definition.IsDynamic
            && ports[1].RuntimePort.PortId == "input_1"
            && ports[2].RuntimePort.PortId == "input_2"
            && ports[1].Definition.DataType == FlowDataType.String
            && ports[2].Definition.DataType == FlowDataType.String
            && ports[1].Slot == 1
            && ports[2].Slot == 2;
    });

    Run("nodes without a dynamic template keep only fixed ports", () =>
    {
        var definition = CreateStaticDefinition();
        var node = new NodeModel { ExecutorType = definition.TypeKey };

        FlowDynamicInputResolver.MaterializeNodePorts(node, definition);
        return node.InputParameters.Count == definition.InputPorts.Count
            && node.InputParameters.All(port => !port.IsDynamic);
    });
}
```

Add helpers that construct a definition with a fixed control `flowIn` input, one string `output` port, and a `FlowDynamicInputTemplate` using `FlowDataType.String`, `MinCount = 1`, the requested initial count, and the requested maximum. Define one guarded global test registration so XML loading can recreate the dynamic node by `ExecutorType`:

```csharp
private const string DynamicTestTypeKey = "test.dynamic-input-ports";

private static void EnsureDynamicTestRegistration()
{
    if (NodeExecutorFactory.Registry.Contains(DynamicTestTypeKey))
    {
        return;
    }

    NodeExecutorFactory.Registry.RegisterNode(
        new FlowNodeRegistration(
            CreateDynamicDefinition(initialCount: 1, maxCount: null),
            () => new DynamicTestExecutor()),
        typeof(DynamicTestNodeModel),
        () => new DynamicTestNodeModel());
}

private sealed class DynamicTestNodeModel : NodeModel
{
    public DynamicTestNodeModel()
    {
        ExecutorType = DynamicTestTypeKey;
    }
}

private sealed class DynamicTestExecutor : IFlowNodeExecutor
{
    public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
        FlowExecutionContext context,
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object>>(
            new Dictionary<string, object> { ["output"] = string.Empty });
    }
}
```

Use the existing `NodeCraft.Tests` partial-program usings plus `System.Collections.Generic`, `System.Linq`, `System.Threading`, and `System.Threading.Tasks`. Call `EnsureDynamicTestRegistration()` at the start of graph, workflow, and UI tests that use the registered type.

Also add a failing validation test that calls `ValidateTemplate` for a negative count, `InitialCount < MinCount`, a finite `MaxCount < InitialCount`, and a prefix colliding with `flowIn`; each case must throw `InvalidOperationException` with a message naming the invalid rule.

- [ ] **Step 2: Run the focused harness and verify the schema RED state.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: compilation fails because `FlowDynamicInputTemplate` and the resolver do not exist yet.

- [ ] **Step 3: Add the schema properties and resolver implementation.**

Add the model properties in `FlowSchema.cs`, `Parameter.cs`, and `WorkflowDocument.cs` using the existing nullable-disabled style. In `FlowNodeRegistry.ApplyRegistration`, call `FlowDynamicInputResolver.ValidateTemplate(registration.Definition)` immediately after `EnsureControlInputPort` so a dynamic prefix cannot collide with the injected `flowIn` port. The template shape is:

```csharp
public class FlowDynamicInputTemplate
{
    public string PortIdPrefix { get; set; } = "input";
    public string DisplayNamePrefix { get; set; } = "Input";
    public FlowDataType DataType { get; set; } = FlowDataType.Object;
    public EPortDirection PreferredDirection { get; set; } = EPortDirection.Left;
    public bool IsRequired { get; set; }
    public object DefaultValue { get; set; }
    public FlowPortAvailability Availability { get; set; }
        = FlowPortAvailability.Iteration;
    public int MinCount { get; set; }
    public int InitialCount { get; set; }
    public int? MaxCount { get; set; }
}
```

Implement the resolver so it validates empty prefixes, null types, negative counts, invalid initial bounds, and prefix collisions; normalizes fixed ports to definition order; preserves saved dynamic relative order; creates missing initial dynamic ports; rejects duplicate/invalid IDs; generates IDs as `<PortIdPrefix>_<n>` using the next unused numeric suffix; never renames surviving ports; creates generated definitions with copied template rules and `IsDynamic = true`; builds effective definitions without mutating shared registrations; and returns descriptors whose slots are effective-list indices.

Use this central shape:

```csharp
internal sealed class FlowInputPortDescriptor
{
    public int Slot { get; set; }
    public FlowPortDefinition Definition { get; set; }
    public PortParameter RuntimePort { get; set; }
}

internal static class FlowDynamicInputResolver
{
    public static void ValidateTemplate(FlowNodeDefinition definition);
    public static void MaterializeNodePorts(NodeModel node, FlowNodeDefinition definition);
    public static IReadOnlyList<string> GetDynamicPortIds(NodeModel node);
    public static IReadOnlyList<FlowInputPortDescriptor> ResolveNodeInputPorts(NodeModel node, FlowNodeDefinition definition);
    public static FlowNodeDefinition ResolveDefinition(FlowNodeDefinition registeredDefinition, IReadOnlyList<string> dynamicInputPortIds);
    public static bool TryAddDynamicPort(NodeModel node, FlowNodeDefinition definition, out PortParameter port, out string error);
    public static bool TryRemoveDynamicPort(NodeModel node, FlowNodeDefinition definition, string portId, out int removedSlot, out string error);
}
```

- [ ] **Step 4: Run the focused harness and verify the resolver GREEN state.**

Run the same `dotnet run` command. Expected: the new schema tests and all pre-existing tests pass. If an existing socket test fails because runtime order changed, keep effective slots definition-driven while preserving the runtime normalization required by the design.

- [ ] **Step 5: Commit the schema/resolver slice.**

```powershell
git add -- NodeCraft.Flow/Flow/FlowSchema.cs NodeCraft.Flow/Flow/FlowNodeRegistry.cs NodeCraft.Flow/Flow/Parameter.cs NodeCraft.Flow/Flow/WorkflowDocument.cs NodeCraft.Flow/Flow/FlowDynamicInputResolver.cs NodeCraft.Tests/DynamicInputPortTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: add dynamic input port schema"
```

---

### Task 2: Make graph persistence, reconciliation, and workflow conversion dynamic-aware

**Files:**

- Modify: `NodeCraft.Flow/Flow/GraphModelXmlSerializer.cs`
- Modify: `NodeCraft.Flow/Flow/GraphModelLinkReconciler.cs`
- Modify: `NodeCraft.Flow/Flow/GraphModelWorkflowAdapter.cs`
- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs:240-270,1533-1590`
- Modify: `NodeCraft.Tests/DynamicInputPortTests.cs`
- Modify: `NodeCraft.Tests/Program.cs:2480-2695`

**Interfaces:**

- Consumes: `FlowDynamicInputResolver.MaterializeNodePorts`, `GetDynamicPortIds`, and `ResolveDefinition` from Task 1.
- Produces: v4/v5 XML compatibility, effective-link reconciliation, ordered workflow dynamic IDs, and shared node-port materialization during canvas load.

- [ ] **Step 1: Add failing v5, v4-compatibility, dynamic-link, and adapter tests.**

Call `EnsureDynamicTestRegistration()`. Add `CreateStringSourceNode(string nodeId, string linkId)` and `CreateDynamicNode(string nodeId, params string[] dynamicPortIds)` helpers that create registered test nodes, materialize their ports, and assign the requested IDs/link IDs. Build a graph with two sources and one dynamic target:

```csharp
var graph = new GraphModel
{
    Nodes = new List<NodeModel>
    {
        CreateStringSourceNode("source-a", "l-a"),
        CreateStringSourceNode("source-b", "l-b"),
        CreateDynamicNode("target", "input_1", "input_2"),
    },
    Links = new List<GraphLink>
    {
        new GraphLink { Id = "l-a", OriginNodeId = "source-a", OriginSlot = 0, TargetNodeId = "target", TargetSlot = 1 },
        new GraphLink { Id = "l-b", OriginNodeId = "source-b", OriginSlot = 0, TargetNodeId = "target", TargetSlot = 2 },
    },
};
```

Assert a save contains `FormatVersion="5"` and `IsDynamic="true"`, then load and verify dynamic IDs, order, and `LinkId` values. Add a literal v4 XML fixture for an existing fixed-port node whose `Port` elements omit `IsDynamic` and assert it loads with all ports fixed. Verify the adapter explicitly:

```csharp
var workflow = GraphModelWorkflowAdapter.Convert(loaded);
var target = workflow.Nodes.Single(node => node.Id == "target");
return target.DynamicInputPortIds.SequenceEqual(new[] { "input_1", "input_2" })
    && ((LinkRef)target.Inputs["input_1"]).SourceNodeId == "source-a"
    && ((LinkRef)target.Inputs["input_2"]).SourceNodeId == "source-b";
```

Add a malformed-graph test with dynamic runtime ports but a registration whose `DynamicInputTemplate` is null; `GraphModelLinkReconciler.Reconcile` must throw instead of dropping those ports.

- [ ] **Step 2: Run the harness and verify the graph RED state.**

Run the existing `dotnet run` command. Expected failures include the current format remaining v4, no `IsDynamic` XML attribute, no v4 compatibility branch, and no `DynamicInputPortIds` output.

- [ ] **Step 3: Upgrade XML serialization and version compatibility.**

In `GraphModelXmlSerializer.cs`, set `CurrentFormatVersion` to `5`, accept only versions `4` and `5`, keep version-3 and legacy `Connections` rejection, add `IsDynamic` to `SerializePort`, and parse an optional `IsDynamic` attribute as `false` when absent. After deserializing nodes and before reconciliation, call `MaterializeNodePorts` for every registered node so missing fixed ports are filled without recreating saved dynamic ports.

Use an explicit version check:

```csharp
if (formatVersion != 4 && formatVersion != CurrentFormatVersion)
{
    throw new InvalidOperationException(
        $"Graph format v{formatVersion} is unsupported. Current format is v{CurrentFormatVersion}.");
}
```

- [ ] **Step 4: Update reconciliation and graph-to-workflow conversion.**

In `GraphModelLinkReconciler.cs`, derive dynamic IDs with `GetDynamicPortIds(targetNode)`, validate target slots against `ResolveDefinition`, match runtime ports by effective ID, reject duplicate/unknown dynamic IDs, and continue assigning exactly one `LinkId` per target port. In `GraphModelWorkflowAdapter.cs`, populate ordered IDs before copying configured values/links:

```csharp
workflowNode.DynamicInputPortIds = FlowDynamicInputResolver
    .GetDynamicPortIds(node)
    .ToList();
```

In `FlowCanvas.InitializeNodePorts`, call the shared materializer for inputs and retain the existing output-port normalization.

- [ ] **Step 5: Run focused graph tests and verify GREEN.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: v5 round-trip, v4 compatibility, dynamic reconciliation, adapter assertions, and all existing graph tests pass.

- [ ] **Step 6: Commit the graph-model slice.**

```powershell
git add -- NodeCraft.Flow/Flow/GraphModelXmlSerializer.cs NodeCraft.Flow/Flow/GraphModelLinkReconciler.cs NodeCraft.Flow/Flow/GraphModelWorkflowAdapter.cs NodeCraft.Flow/Flow/FlowCanvas.cs NodeCraft.Tests/DynamicInputPortTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: persist dynamic input ports"
```

### Task 3: Resolve dynamic definitions during workflow validation and execution

**Files:**

- Modify: `NodeCraft.Flow/Flow/GraphExecutor.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Verify `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`; modify it only if a focused test identifies a static-definition assumption.
- Modify: `NodeCraft.Tests/DynamicInputPortTests.cs`

**Interfaces:**

- Consumes: `WorkflowNode.DynamicInputPortIds`, `FlowDynamicInputResolver.ResolveDefinition`, and the unchanged `IFlowNodeExecutor` contract.
- Produces: per-node effective definitions in validation and session contexts, with deterministic dynamic-input execution and no executor signature change.

- [ ] **Step 1: Add failing validation and ordered-execution tests.**

Extend the existing `DynamicTestExecutor` from Task 1 rather than declaring a second executor type. Give it an `Observed` list and iterate `definition.InputPorts.Where(port => port.IsDynamic)`:

```csharp
public static List<string> Observed { get; } = new List<string>();

public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
    FlowExecutionContext context,
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken)
{
    Observed.Clear();
    foreach (var port in definition.InputPorts.Where(item => item.IsDynamic))
    {
        Observed.Add(inputs[port.Id] as string ?? string.Empty);
    }

    return Task.FromResult<IReadOnlyDictionary<string, object>>(
        new Dictionary<string, object> { ["output"] = string.Join("|", Observed) });
}
```

Create a workflow with `DynamicInputPortIds = ["input_1", "input_2"]` and values inserted in reverse dictionary order. Assert validation succeeds, execution returns `first|second`, and `Observed` follows the dynamic ID list. Add a missing-required-dynamic-input case and an incompatible-source-type case using the existing validation error codes.

- [ ] **Step 2: Run the harness and verify execution RED.**

Run the focused harness. Expected: validation reports unknown dynamic input ports or the executor receives no dynamic values because the engine currently uses only registered static definitions.

- [ ] **Step 3: Resolve an effective definition during graph validation.**

In `GraphExecutor.Validate`, after resolving a registration, build the per-node definition:

```csharp
var definition = FlowDynamicInputResolver.ResolveDefinition(
    registration.Definition,
    node.DynamicInputPortIds ?? new List<string>());
```

Use that definition for required-input checks, input-port lookup, source/target type compatibility, session-availability checks, and dynamic-key validation. Reject a dynamic key missing from `DynamicInputPortIds` instead of treating it as an arbitrary dictionary value.

- [ ] **Step 4: Resolve effective definitions when constructing a graph session.**

In `GraphExecutionSession`'s constructor, resolve the effective definition before creating each `FlowNodeSessionContext` and store that same object in `_definitionsByNodeId`:

```csharp
var definition = FlowDynamicInputResolver.ResolveDefinition(
    registration.Definition,
    node.DynamicInputPortIds ?? new List<string>());
_sessionContexts.Add(
    node.Id,
    new FlowNodeSessionContext(node, definition, _logger));
```

The existing `FlowGraphIterationRunner.ResolveInputs`, `HasMissingRequiredRuntimeInput`, and session-input resolution already iterate `definition.InputPorts`; only change that file if a focused test demonstrates a static-only assumption.

- [ ] **Step 5: Run focused execution tests and verify GREEN.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: ordered values, required-input failures, incompatible-type failures, and all existing session/iteration tests pass.

- [ ] **Step 6: Commit the runtime slice.**

```powershell
git add -- NodeCraft.Flow/Flow/GraphExecutor.cs NodeCraft.Flow/Flow/GraphExecutionSession.cs NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs NodeCraft.Tests/DynamicInputPortTests.cs
git commit -m "feat: execute dynamic input ports"
```

---

### Task 4: Add canvas mutation APIs and dynamic socket controls

**Files:**

- Modify: `NodeCraft.Flow/Flow/FlowCanvas.cs`
- Modify: `NodeCraft.Flow/Flow/FlowSocketResolver.cs`
- Modify: `NodeCraft.Flow/Flow/NodeView.cs`
- Modify: `NodeCraft.Flow/Themes/Flow.xaml`
- Modify: `NodeCraft.Tests/DynamicInputPortTests.cs`
- Modify: `NodeCraft.Tests/Program.cs:219-270`

**Interfaces:**

- Consumes: `FlowDynamicInputResolver.ResolveNodeInputPorts`, `MaterializeNodePorts`, `TryAddDynamicPort`, `TryRemoveDynamicPort`, and the existing `NodeView._parentCanvas` relationship.
- Produces:
  - `internal bool FlowCanvas.TryAddDynamicInput(NodeModel node, out string error)`.
  - `internal bool FlowCanvas.TryRemoveDynamicInput(NodeModel node, string portId, out string error)`.
  - `NodeView` add/remove controls only for dynamic-capable nodes.
  - Dynamic socket descriptors with the same slots used by link creation and redraw.

- [ ] **Step 1: Add failing model/canvas mutation tests.**

Add tests that create a `FlowCanvas` with an empty `GraphModel`, call `EnsureDynamicTestRegistration()`, add a materialized `DynamicTestNodeModel` to `canvas.GraphModel.Nodes`, and call the internal canvas APIs on that node. Assert:

```csharp
var added = canvas.TryAddDynamicInput(node, out var addError);
var removed = canvas.TryRemoveDynamicInput(node, "input_1", out var removeError);

return added
    && removed
    && string.IsNullOrEmpty(addError)
    && string.IsNullOrEmpty(removeError)
    && FlowDynamicInputResolver.GetDynamicPortIds(node)
        .SequenceEqual(new[] { "input_2", "input_3" });
```

Cover maximum-count rejection, minimum-count rejection, fixed-port removal rejection, removal of a connected dynamic port, decrementing later `GraphLink.TargetSlot` values, and height growth from a saved explicit height that is too small for the new row. Use `RunOnSta` and the existing `RunWithTemplatedFlowCanvas` helper for a WPF assertion that a dynamic node has one add button and one remove button per dynamic row, while a static node has neither.

- [ ] **Step 2: Run the harness and verify UI/mutation RED.**

Run the existing `dotnet run` command. Expected: compilation fails because the canvas operations and dynamic UI controls do not exist.

- [ ] **Step 3: Implement shared dynamic socket resolution and controls.**

In `FlowSocketResolver.Resolve`, retain the existing output-definition path, but for inputs consume `FlowDynamicInputResolver.ResolveNodeInputPorts`. Return each descriptor's slot, generated definition, and matched runtime port. Keep visual-style and label resolution unchanged except that generated definitions supply type/display data.

Update `NodeView` to rebuild from effective descriptors, append a compact add `Button` only when `DynamicInputTemplate` is present, append a remove `Button` only for `PortParameter.IsDynamic`, route clicks through the canvas APIs, rebuild sockets after success, preserve connector hit testing, and call `EnsureDynamicInputHeight`. If explicit `Height` is smaller than measured desired height, grow `Height` and `NodeModel.Height`; never shrink on removal.

Add a compact `FlowDynamicInputActionButtonStyle` in `Flow.xaml` with an 18px square footprint and existing neutral/hover theme resources. Set automation names or tooltips to `Add input` and `Remove input` so tests do not depend on glyph rendering.

- [ ] **Step 4: Implement canvas add/remove and slot reindexing.**

In `FlowCanvas`, make initialization call the shared input materializer and retain output normalization. Implement `TryAddDynamicInput` by resolving registration, calling `TryAddDynamicPort`, rebuilding the node view, growing height, updating the canvas, and raising the graph-changed event. Implement `TryRemoveDynamicInput` by resolving the effective slot, removing that target link, deleting the runtime port, decrementing later target slots on the same node, reconciling, refreshing the node view, and raising graph-changed.

Replace static-only lookups in `TryResolveSlotTypes`, `ResolveInputPortId`, `IsSlotAllowingMultipleConnections`, `ClearTargetPortLinkId`, and the target-node input check with effective descriptors. Dynamic ports always use one connection. Keep `GraphLink` slot semantics unchanged for existing links and update only links targeting the mutated node.

The remove path must mutate the graph only after validation succeeds:

```csharp
if (!FlowDynamicInputResolver.TryRemoveDynamicPort(
        node,
        registration.Definition,
        portId,
        out var removedSlot,
        out error))
{
    return false;
}

GraphModel.Links.RemoveAll(link =>
    link.TargetNodeId == node.Id && link.TargetSlot == removedSlot);
foreach (var link in GraphModel.Links.Where(link =>
    link.TargetNodeId == node.Id && link.TargetSlot > removedSlot))
{
    link.TargetSlot--;
}
```

- [ ] **Step 5: Run focused UI and graph tests and verify GREEN.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: add/remove controls, bounds, connected-port deletion, slot reindexing, dynamic socket slots, and height-growth assertions pass with existing visual contract tests.

- [ ] **Step 6: Commit the canvas/UI slice.**

```powershell
git add -- NodeCraft.Flow/Flow/FlowCanvas.cs NodeCraft.Flow/Flow/FlowSocketResolver.cs NodeCraft.Flow/Flow/NodeView.cs NodeCraft.Flow/Themes/Flow.xaml NodeCraft.Tests/DynamicInputPortTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: add dynamic input controls"
```

---

### Task 5: Update standard node content and close regression coverage

**Files:**

- Modify: `NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs`
- Modify: `NodeCraft.Tests/DynamicInputPortTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**

- Consumes: effective descriptors from `FlowDynamicInputResolver.ResolveNodeInputPorts`.
- Produces: standard node body summaries that include dynamic inputs without changing fixed binary-operation behavior.

- [ ] **Step 1: Add a failing standard-content regression.**

Create a dynamic fake registration with two runtime dynamic ports and two links, call `NodeExecutorFactory.Registry.BuildNodeContent(canvas, node)`, and inspect the returned `StackPanel` descendants. Assert both generated labels appear and resolve their connected source names. Keep the existing binary-operation swap test unchanged.

- [ ] **Step 2: Run the harness and verify the content RED state.**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: the dynamic summary test fails because `BuildInputBindings` currently iterates only `registration.Definition.InputPorts`.

- [ ] **Step 3: Use effective descriptors in the standard content factory.**

In `BuildInputBindings`, replace the static definition list with:

```csharp
var inputPorts = FlowDynamicInputResolver
    .ResolveNodeInputPorts(node, registration.Definition)
    .Where(socket => !socket.Definition.IsControlPort)
    .ToList();
```

Use `socket.Definition` for labels and `socket.RuntimePort?.LinkId` for source lookup. Update `ResolveDefinitionSlot` and `SetPortLinkId` to use the same effective descriptor list. Leave `BuildSwapInputsButton` limited to the existing two-fixed-input condition.

- [ ] **Step 4: Run the harness and verify content GREEN.**

Run the same command. Expected: the dynamic summary and all existing content tests pass.

- [ ] **Step 5: Run the complete build and test verification.**

Run:

```powershell
git diff --check
dotnet build NodeCraft.sln --no-restore
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
```

Expected: no whitespace errors, the solution builds, the console runner reports all tests passing, v4 graphs still load, new saves report v5, and dynamic tests cover schema, persistence, reconciliation, UI, height growth, workflow validation, execution order, and regression behavior.

- [ ] **Step 6: Commit the final regression slice.**

```powershell
git add -- NodeCraft.Flow/Flow/DefaultFlowNodeContentFactory.cs NodeCraft.Tests/DynamicInputPortTests.cs NodeCraft.Tests/Program.cs
git commit -m "test: cover dynamic input port regressions"
```

---

## Final Handoff Checklist

- [ ] Every node without `DynamicInputTemplate` has unchanged sockets and no add/remove controls.
- [ ] Dynamic ports use one template, stable IDs, one link each, and explicit order.
- [ ] Adding and removing ports updates runtime state, link slots, node height, and graph-change notifications.
- [ ] Graph v4 reads successfully, graph v5 writes dynamic markers, and malformed dynamic metadata fails clearly.
- [ ] `GraphModelWorkflowAdapter`, `GraphExecutor`, `GraphExecutionSession`, and node content all use the same effective input-port resolution.
- [ ] Executors still implement the original `IFlowNodeExecutor` signature.
- [ ] Focused and full tests pass after a no-restore solution build.
