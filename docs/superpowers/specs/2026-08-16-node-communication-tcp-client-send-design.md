# Node Communication TCP Client Send Design

## Problem

NodeCraft currently has a plugin boundary for domain-specific nodes, demonstrated by
`NodeCraft.Vision`, and the flow framework now supports node-specific dynamic input
ports. The next plugin should provide a communication category whose first node sends
multiple independent messages through one TCP client connection.

The node must preserve the order of its dynamic input ports. Each input is one send
operation: strings are encoded as UTF-8, byte arrays are sent unchanged, and other
values are converted with `ToString()` before UTF-8 encoding. Send failures need a
node-level policy so a workflow can either fail fast or continue while recording the
lost message.

## Goals

- Add an independent `NodeCraft.Communication` plugin following the `NodeCraft.Vision`
  plugin structure.
- Register a `TCP Client Send` node under a `Communication` palette category.
- Use the existing dynamic input framework with one or more `message_N` inputs.
- Establish one TCP connection per execution session and reuse it for all sends in the
  session.
- Send each dynamic input separately and deterministically in port order.
- Support `string`, `byte[]`, and arbitrary non-null objects as described above.
- Make send-failure continuation configurable while keeping failures observable in logs.
- Persist node configuration and dynamic port state through existing graph/workflow
  mechanisms.
- Test the transport behavior without requiring external network services, plus one
  loopback test for the production socket implementation.

## Non-goals

- Do not add TCP receive, UDP, automatic reconnect, message delimiters, framing, or
  protocol negotiation.
- Do not add an encoding selector; the first version always uses UTF-8 for text values.
- Do not add a send-result output port or a separate flow-out control port.
- Do not move TCP concerns into `NodeCraft.Flow` or change the executor interface.
- Do not make dynamic inputs available to nodes that do not declare a dynamic template.

## Design

### Plugin and project structure

Create a new `NodeCraft.Communication` project targeting the same framework as the
existing plugins and referencing `NodeCraft.Flow`. Add it to `NodeCraft.sln` and the
test project. The project contains:

- `CommunicationPlugin`, implementing `IFlowPlugin` with metadata ID
  `nodecraft.communication`.
- `TcpClientSendNodeModel`, implementing `IWorkflowNodeValueProvider`.
- `TcpClientSendExecutor`, implementing the session lifecycle and normal executor
  contracts.
- A small editor and embedded XAML resource following the Vision editor pattern.
- A production TCP connection implementation and an injectable connection factory for
  deterministic tests.
- `plugin.json` with the communication assembly and entry type.

The node type key is `nodecraft.communication.tcp-client-send`. Its display name is
`TCP Client Send`, its palette category is `Communication`, and its palette description
explains that it sends each connected message input in order. The node has no business
output ports; it is a send sink and receives the framework-provided `Flow In` input.

### Dynamic input definition

The registration declares one dynamic input template:

- `PortIdPrefix`: `message`
- `DisplayNamePrefix`: `Message`
- `DataType`: `FlowDataType.Object`
- `PreferredDirection`: left
- `IsRequired`: true
- `Availability`: iteration
- `MinCount`: 1
- `InitialCount`: 1
- `MaxCount`: null (unlimited)

The effective runtime ports are therefore `message_1`, `message_2`, and so on. The
existing `NodeView` controls add and remove ports only because this node opts in. Port
order comes from the effective definition and not from dictionary enumeration. Removing
a port uses the existing framework behavior for link removal and target-slot reindexing.

### Node configuration

`TcpClientSendNodeModel` exposes these public properties:

- `Host`: required TCP host name or IP address; new nodes start empty.
- `Port`: required TCP port in the range 1 through 65535; new nodes start at 0.
- `ConnectTimeoutMilliseconds`: positive connection timeout, default 5000.
- `StopOnSendFailure`: whether a failed send aborts the current execution, default true.

The editor provides text boxes for the host, port, and timeout, plus a check box for
`Stop on send failure`. Invalid numeric text is not copied into the model. The editor
notifies the canvas using the same graph-change path as the Vision editors. Dynamic
port controls remain owned by the common NodeCraft flow UI.

### Connection lifecycle and transport boundary

The executor receives the effective node definition and the workflow inputs through the
existing `IFlowNodeExecutor` contract. It uses an injectable connection factory so the
executor can be tested with a recording or failing connection. Production execution uses
`System.Net.Sockets.TcpClient` and its `NetworkStream`.

Session behavior is:

1. `StartSessionAsync` reads and validates host, port, and timeout, then establishes one
   connection using the session cancellation token.
2. `ExecuteAsync` requires the connection to be active and sends every dynamic input in
   effective definition order.
3. `StopSessionAsync` closes and disposes the connection, clears executor state, and is
   safe after a failed start or failed send.

There is no per-message reconnect and no connection close between inputs. A connection
failure during startup prevents execution. A connection or write failure during an
iteration follows `StopOnSendFailure`.

### Payload conversion and failure policy

For each dynamic input value:

1. A `byte[]` is passed to the connection unchanged.
2. A `string` is encoded with `Encoding.UTF8`.
3. Any other non-null value is converted by calling `value.ToString()` and then encoded
   with `Encoding.UTF8`.
4. A null value is rejected with an actionable error because it has no defined payload
   representation.

Each value produces exactly one asynchronous write call, including an empty byte array.
No delimiter or extra bytes are added.

When `StopOnSendFailure` is true, the first failed write is logged and its exception is
propagated, so the current execution stops and later inputs are not attempted. When it
is false, the failed input is logged and discarded, the exception is not propagated, and
the executor continues with the next dynamic input. The final execution succeeds if all
other sends complete. Cleanup always runs in either mode.

### Persistence and workflow conversion

The existing Graph XML serializer automatically persists the four node properties as
custom properties. `WriteWorkflowInputs` copies the same values into the workflow
inputs dictionary under stable keys (`host`, `port`, `connectTimeoutMilliseconds`, and
`stopOnSendFailure`). The existing graph adapter copies the ordered dynamic IDs and
link/value inputs. No new graph format or serializer field is required for this plugin.

### Palette and visual integration

The registry receives the communication category and TCP Send registration through the
normal plugin registration path. The core palette icon mapping gains a network icon for
the `Communication` category and the TCP Send type. If the icon mapping is unavailable
in a host version, the existing category fallback remains valid.

## Data flow

```text
node editor properties + dynamic links
              |
              v
      GraphModel / XML save
              |
              v
       WorkflowDocument
              |
              v
       session startup: connect once
              |
              v
  message_1 -> UTF-8/raw bytes -> WriteAsync
  message_2 -> UTF-8/raw bytes -> WriteAsync
  message_N -> UTF-8/raw bytes -> WriteAsync
              |
              v
        session stop: dispose
```

The executor never depends on the order of `WorkflowNode.Inputs`; it uses the ordered
dynamic `FlowPortDefinition` list and looks up each value by its port ID.

## Error handling

- Empty host, invalid port, non-positive timeout, or a missing dynamic input fails
  validation/startup with a node-specific message.
- TCP connect failures are propagated from session startup after the connection object
  is cleaned up.
- Null payloads fail with a clear message naming the dynamic input.
- Write failures are handled by `StopOnSendFailure` as defined above and always logged.
- Cancellation is honored by connection and write operations and is not converted into
  a successful send.
- Cleanup exceptions are handled according to the existing session cleanup behavior and
  do not leave a usable connection in the executor.

## Testing

Add communication tests to the existing console test runner:

- Project and manifest checks confirm the new plugin references Flow, embeds its editor,
  copies `plugin.json`, and exposes the expected entry type.
- Registration tests confirm the type key, category, editor, dynamic template, required
  minimum, unlimited maximum, and initial port count.
- Executor tests use a recording connection to verify UTF-8 strings, unchanged byte
  arrays, `ToString()` fallback, exactly one write per input, and port-order behavior.
- Failure tests cover startup failure, null payloads, write failure with termination
  enabled, and write failure with termination disabled followed by a successful later
  input.
- Lifecycle tests verify one connection per session and disposal after normal execution,
  startup failure, send failure, and cancellation.
- A loopback `TcpListener` test verifies that the production TCP implementation connects
  and delivers the expected ordered bytes.
- Graph/workflow tests verify configuration values and dynamic port IDs survive graph
  conversion and that the node executes with linked dynamic inputs.

The implementation should add focused failing tests before production code, then run the
full existing test runner, `dotnet build NodeCraft.sln --no-restore`, and `git diff
--check` before completion.

## Acceptance criteria

- `NodeCraft.Communication` loads as a normal plugin and adds a Communication palette
  category containing `TCP Client Send`.
- The node starts with one dynamic `Message` input and can add unlimited inputs through
  the common `+` control while retaining ordered IDs and links.
- One session uses one TCP connection, and every dynamic input invokes one send in order.
- Strings are UTF-8, byte arrays are unchanged, and other non-null values use
  `ToString()` followed by UTF-8 encoding.
- Send failure behavior matches the configured `StopOnSendFailure` value.
- Host, port, timeout, failure policy, dynamic ports, values, and links survive the
  existing persistence and workflow conversion paths.
- Tests cover fake transport behavior and one real loopback TCP path without external
  services.
