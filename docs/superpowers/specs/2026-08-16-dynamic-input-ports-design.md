# Dynamic Input Ports Design

## Problem

NodeCraft already supports multiple statically declared input ports through `FlowNodeDefinition.InputPorts`. It does not yet support a node instance adding or removing repeated inputs at runtime. The existing graph, canvas, serialization, workflow conversion, and execution paths assume that every input port comes from the registered definition.

The desired behavior is node-specific: only nodes that opt in should expose dynamic input controls. A TCP Send-style node should be able to add any number of same-type input ports and process them in port order. Each generated port remains an independent single-connection input; multiple connections to one port are out of scope.

## Goals

- Let a node definition opt in to dynamic input ports through a reusable input template.
- Let a node instance add and remove dynamic input ports at runtime.
- Keep each dynamic input as an independent port with one connection.
- Preserve dynamic port IDs, order, values, and links across graph save/load and workflow conversion.
- Make dynamic port order explicit so nodes such as TCP Send can process inputs deterministically.
- Reuse the existing type, required-input, availability, connection, and execution validation rules.
- Keep fixed ports and nodes without a dynamic template unchanged.
- Provide a clear extension point for plugins and future built-in nodes.

## Non-goals

- Do not allow several links to the same input socket. That is a separate fan-in feature.
- Do not make every node dynamic by default.
- Do not allow users to change a dynamic port's type independently from its node template.
- Do not add arbitrary user-authored port schemas or a general-purpose port editor in this change.
- Do not change the `IFlowNodeExecutor` method signature.

## Design

### Node-level dynamic input capability

Add an optional `FlowDynamicInputTemplate` to `FlowNodeDefinition`. A null template means the node does not support dynamic inputs and receives no dynamic-input controls.

The template contains:

- `PortIdPrefix`, used to generate IDs such as `input_1` and `input_2`.
- `DisplayNamePrefix`, used to label generated sockets.
- `DataType`.
- `PreferredDirection`.
- `IsRequired`.
- `DefaultValue`.
- `Availability`.
- `MinCount`.
- `InitialCount`.
- Optional `MaxCount`; null means no upper limit.

Registration validation requires a non-empty ID prefix, a non-null data type, non-negative counts, `MinCount <= InitialCount`, and `InitialCount <= MaxCount` when a maximum exists. The generated ID prefix must not collide with any fixed input port ID. The template's availability and type rules are copied to every generated port.

The TCP Send registration opts in by supplying this template. Other nodes remain unchanged and do not show dynamic-input controls.

### Node-instance port state

Add `IsDynamic` to `PortParameter`, and add an `IsDynamic` flag to `FlowPortDefinition` whose default is `false`. Runtime definitions materialized from the template set the flag to `true`; registered fixed definitions retain the default. `NodeModel.InputParameters` remains the ordered source of runtime input ports, with fixed ports followed by dynamic ports. No separate parallel list is introduced, so existing socket, link, and serialization code can continue to operate on one ordered port collection.

When a dynamic port is created, the framework copies the template into a runtime `PortParameter`, initializes its `Parameter.ParameterType`, applies the preferred direction, and leaves its `LinkId` empty. New IDs use the next unused numeric suffix for the prefix. Existing IDs are never renamed when another dynamic port is removed; the list order, rather than the numeric suffix, defines execution order.

The framework materializes an effective input definition for each node instance by combining registered fixed definitions with the node's dynamic runtime ports. Generated definitions carry the template's type and validation properties and are marked dynamic. A shared resolver is used by the canvas, link reconciler, workflow adapter, and execution layer so every consumer uses the same slot mapping.

### Add and remove lifecycle

The `NodeView` input area shows an add control only when the bound node's registration contains a dynamic template. Each dynamic input row shows a remove control; fixed rows do not.

The controls call framework-level add/remove operations rather than editing the list directly:

1. Add validates the node capability and maximum count, appends a generated runtime port, rebuilds sockets, redraws connections, and raises the normal graph-changed notification.
2. Remove validates the dynamic marker and minimum count. If the port has a link, the framework removes that link first. It then removes the runtime port, decrements `TargetSlot` for later links targeting the same node, reconciles link IDs, rebuilds sockets, redraws connections, and raises the graph-changed notification.
3. Removing a port never renames the remaining ports. Adding a port always appends it, so existing target slots remain stable during addition.

New node instances are materialized with the template's `InitialCount`. Loaded instances preserve their serialized dynamic port list; the materializer fills only missing fixed ports and does not recreate or discard saved dynamic ports.

### Graph model and canvas integration

`FlowSocketResolver` (or its replacement shared effective-port resolver) returns fixed and dynamic input descriptors in their actual runtime order. `NodeView` uses those descriptors to render sockets and labels. `FlowCanvas` uses the same descriptors for:

- target-slot hit testing;
- source/target type lookup;
- target port ID lookup;
- connection creation;
- connection redraw;
- deletion and slot reindexing.

`GraphLink` remains slot-based for this change. Dynamic port removal is the single operation that can shift target slots, and it updates the affected links before reconciliation. Each dynamic port remains subject to the existing one-link-per-target-slot rule.

`GraphModelLinkReconciler` validates effective fixed-plus-dynamic definitions, rejects unknown or duplicate dynamic IDs, validates target slots against the effective list, and restores each runtime port's single `LinkId` from the authoritative link collection.

### Workflow conversion and execution

Extend `WorkflowNode` with an ordered `DynamicInputPortIds` list. `GraphModelWorkflowAdapter` populates it from the node's dynamic runtime ports and continues to write each configured value or `LinkRef` under its port ID.

Before validation and execution, the workflow engine resolves a per-node effective definition from the registered definition, the dynamic ID list, and the node's dynamic template. It does not mutate the shared registration definition.

The existing executor contract remains:

```csharp
Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
    FlowExecutionContext context,
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken);
```

The effective `definition.InputPorts` list carries the deterministic order. A TCP Send executor iterates the dynamic ports in that definition order and reads each value by port ID; it does not depend on dictionary enumeration order. Required-input, session-input, type compatibility, control-input, and missing-runtime-value behavior continues to use the existing validation paths.

### Persistence and compatibility

Graph XML format version advances from 4 to 5. Each serialized input port gains an `IsDynamic` attribute. Missing `IsDynamic` on a version-4 graph is interpreted as `false`. Version-4 graphs remain loadable and are saved in version 5 after a successful edit or save. Existing unsupported legacy connection formats remain unsupported.

The workflow model's dynamic ID list is optional for old in-memory documents and defaults to an empty list. A dynamic input key without a corresponding dynamic ID is rejected during workflow validation instead of being silently ignored.

### Error handling

- Adding to a non-dynamic node, adding at `MaxCount`, or removing at `MinCount` is rejected without mutating the graph.
- Removing a fixed port, an unknown port, or a port marked dynamic on a node whose definition no longer supports dynamic inputs is rejected with a node/port-specific error.
- Duplicate dynamic IDs, prefix collisions, invalid template counts, missing dynamic metadata, and invalid dynamic slots fail graph reconciliation or workflow validation with actionable messages.
- A dynamic input with an incompatible source type uses the existing connection rejection and workflow validation errors.
- A saved graph that references a node type that no longer supports its dynamic ports fails load/reconciliation rather than dropping ports or links.
- Automatic removal of a link when its dynamic port is removed is the only destructive part of the UI operation and is limited to the selected dynamic port's link.

## Data flow

```text
node registration
    -> dynamic input template
    -> node instance materializer
    -> ordered runtime InputParameters
    -> shared effective-port resolver
       /        |          \
      /         |           \
 canvas     graph XML     workflow/execution
      \         |           /
       \        |          /
        consistent slot, ID, type, and order
```

For a TCP Send instance, adding `input_3` appends a third dynamic runtime port. The graph stores its port entry and any target link, the adapter emits `DynamicInputPortIds = [input_1, input_2, input_3]`, and the executor reads the three values in that order.

## Testing

Add coverage to the existing test projects and console test runner for:

- valid and invalid dynamic template registration;
- initial-count materialization and opt-in behavior;
- add, remove, ID generation, order preservation, minimum/maximum boundaries, and no-renaming of surviving ports;
- dynamic socket rendering and controls only on opted-in nodes;
- connection creation to dynamic slots, type rejection, connected-port removal, and target-slot reindexing;
- graph XML version 5 round-tripping, version-4 compatibility, and preservation of dynamic IDs, order, values, and links;
- `GraphModelWorkflowAdapter` output including ordered dynamic IDs and `LinkRef` values;
- workflow validation for unknown dynamic IDs, required dynamic inputs, incompatible types, and invalid slots;
- execution of a TCP Send-style fake executor that observes inputs in definition order;
- regression coverage proving fixed-port nodes and existing static multi-input nodes behave unchanged.

The implementation should add focused failing tests before production changes, then run the complete existing test runner and solution build after the feature tests pass.

## Acceptance criteria

- A node definition can opt in to dynamic inputs with one same-type template.
- Only opted-in nodes display add/remove controls.
- A node instance can add any number of dynamic ports up to its declared maximum, or without a maximum when configured as unlimited.
- Each dynamic port has one socket, one optional link, a stable ID, and a deterministic position in the input order.
- Removing a connected dynamic port removes only its own link and keeps later links valid after slot reindexing.
- Save/load preserves dynamic ports and links, and version-4 graphs remain readable.
- Workflow validation and execution use the same effective port order and template type.
- A TCP Send-style executor can send all dynamic inputs in order without changing the executor interface.
- Nodes with only fixed inputs continue to render, connect, serialize, and execute as before.
