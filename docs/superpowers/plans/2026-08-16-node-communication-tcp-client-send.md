# Node Communication TCP Client Send Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Add an independent \`NodeCraft.Communication\` plugin containing a \`TCP Client Send\` node that sends each dynamic message input separately over one session-scoped TCP connection.

**Architecture:** Keep TCP code in a new plugin project. The node registers an unlimited \`message_N\` dynamic \`Object\` input template, stores Host/Port/timeout/failure-policy settings on its model, and uses an injectable TCP connection boundary so executor tests do not depend on external services. The executor reads ordered dynamic ports from the effective definition, converts each value to bytes, writes each value once, and applies \`StopOnSendFailure\` to write failures.

**Tech Stack:** C# 9, .NET 8 WPF (\`net8.0-windows\`), \`System.Net.Sockets.TcpClient\`, \`NetworkStream\`, existing \`NodeCraft.Flow\` plugin/runtime APIs, embedded WPF XAML, and the self-running \`NodeCraft.Tests\` console harness.

## Global Constraints

- The plugin ID is \`nodecraft.communication\`; the node type key is \`nodecraft.communication.tcp-client-send\`.
- Only the TCP Client Send definition opts into dynamic inputs; other nodes remain unchanged.
- Dynamic inputs use \`message\` as the prefix, \`Message\` as the display prefix, \`FlowDataType.Object\`, \`MinCount = 1\`, \`InitialCount = 1\`, and unlimited \`MaxCount\`.
- Each dynamic input is one independent send operation in effective definition order; dictionary enumeration order is never used as the send order.
- \`byte[]\` values are sent unchanged; strings and \`ToString()\` results are encoded with UTF-8; null payloads are rejected.
- One TCP connection is opened during Session startup and reused for every input; no per-message reconnect or delimiter is added.
- \`StopOnSendFailure\` defaults to \`true\`; when false, only write failures are logged and dropped, and later inputs continue.
- Host and port are required; timeout must be positive; cancellation is not swallowed as a successful send.
- Do not change \`IFlowNodeExecutor.ExecuteAsync\` or move TCP implementation into \`NodeCraft.Flow\`.
- Persist node configuration through existing public custom-property XML and \`IWorkflowNodeValueProvider\` workflow-input paths.
- Keep projects on their existing .NET 8 WPF/C# 9 settings and follow the console test runner pattern.
- Add focused failing tests before each production behavior slice, then run the full test runner, solution build, and \`git diff --check\`.

---

## File Map

- Create \`NodeCraft.Communication/NodeCraft.Communication.csproj\` — net8.0-windows WPF plugin project referencing \`NodeCraft.Flow\` without copying the shared Flow assembly.
- Create \`NodeCraft.Communication/plugin.json\` — plugin loader manifest for \`NodeCraft.Communication.dll\` and \`NodeCraft.Communication.Plugin.CommunicationPlugin\`.
- Create \`NodeCraft.Communication/Properties/AssemblyInfo.cs\` — grant \`NodeCraft.Tests\` access to internal transport/executor test seams.
- Create \`NodeCraft.Communication/Plugin/CommunicationPlugin.cs\` — metadata and registration for the TCP node.
- Create \`NodeCraft.Communication/Nodes/TcpClientSendNodeModel.cs\` — persisted node settings and workflow-input projection.
- Create \`NodeCraft.Communication/Nodes/TcpClientSendExecutor.cs\` — session lifecycle and ordered send execution.
- Create \`NodeCraft.Communication/Transport/ITcpClientConnection.cs\` — injectable connection and factory interfaces.
- Create \`NodeCraft.Communication/Transport/TcpClientConnection.cs\` — production \`TcpClient\`/\`NetworkStream\` implementation.
- Create \`NodeCraft.Communication/Transport/TcpPayloadEncoder.cs\` — deterministic value-to-byte conversion.
- Create \`NodeCraft.Communication/Views/TcpClientSendEditor.xaml\` — embedded editor layout.
- Create \`NodeCraft.Communication/Views/TcpClientSendEditor.xaml.cs\` — editor/model synchronization.
- Modify \`NodeCraft.sln\` — include the Communication project and build configurations.
- Modify \`NodeCraft.Tests/NodeCraft.Tests.csproj\` — reference the Communication project for focused tests.
- Create \`NodeCraft.Tests/CommunicationTests.cs\` — plugin, transport, executor, persistence, and loopback tests as a \`Program\` partial.
- Modify \`NodeCraft.Tests/Program.cs\` — invoke the asynchronous communication test group.
- Modify \`NodeCraft.Flow/Flow/FlowNodeRegistry.cs\` — assign a network icon to the Communication category and TCP node type.

The implementation does not add a new graph serializer field or a new graph format. Dynamic input persistence, slot reindexing, and common \`+ / −\` controls are consumed from the already completed framework feature.

---

### Task 0: Verify the current baseline

**Files:**

- Modify: none
- Test: \`NodeCraft.sln\`, \`NodeCraft.Tests/NodeCraft.Tests.csproj\`

**Interfaces:**

- Consumes: current \`codex/dynamic-input-ports\` worktree and committed design/spec documents.
- Produces: a recorded all-pass baseline before adding Communication files.

- [ ] **Step 1: Check the worktree and latest commits.**

Run:

~~~powershell
git status --short
git log -3 --oneline
~~~

Expected: the design commit is present and no unrelated uncommitted source changes exist.

- [ ] **Step 2: Build the solution without restore.**

Run:

~~~powershell
dotnet build NodeCraft.sln --no-restore --verbosity minimal
~~~

Expected: exit code 0 and 0 errors. Existing nullable warnings may remain; record them but do not change unrelated files.

- [ ] **Step 3: Run the existing console test harness.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
~~~

Expected: the runner ends with \`ALL PASS\`. If the baseline fails, capture the exact existing failure before continuing so the Communication RED state is distinguishable.

---

### Task 1: Scaffold the Communication plugin and test contracts

**Files:**

- Create: \`NodeCraft.Communication/NodeCraft.Communication.csproj\`
- Create: \`NodeCraft.Communication/plugin.json\`
- Create: \`NodeCraft.Communication/Properties/AssemblyInfo.cs\`
- Create: \`NodeCraft.Communication/Plugin/CommunicationPlugin.cs\`
- Modify: \`NodeCraft.sln\`
- Modify: \`NodeCraft.Tests/NodeCraft.Tests.csproj\`
- Create: \`NodeCraft.Tests/CommunicationTests.cs\`
- Modify: \`NodeCraft.Tests/Program.cs\`

**Interfaces:**

- Consumes: \`IFlowPlugin\`, \`PluginMetadata\`, \`FlowNodeRegistration\`, and the existing console \`Run\`/\`RunAsync\` helpers.
- Produces: a loadable plugin assembly identity, the \`CommunicationPlugin\` entry type, the \`TcpClientSendNodeModel\` registration contract, and a test entry point that currently fails because the node behavior does not exist.

- [ ] **Step 1: Add the project, manifest, assembly friend declaration, solution entry, and test reference.**

Match \`NodeCraft.Vision/NodeCraft.Vision.csproj\` for the target framework, WPF, C# 9, x64, shared Flow reference, embedded editor resources, and \`plugin.json\` copy behavior. Use:

~~~xml
<ProjectReference Include="..\\NodeCraft.Flow\\NodeCraft.Flow.csproj" Private="false" />
<Page Remove="Views\\TcpClientSendEditor.xaml" />
<EmbeddedResource Include="Views\\TcpClientSendEditor.xaml" />
<None Update="plugin.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
~~~

Set the manifest to:

~~~json
{
  "id": "nodecraft.communication",
  "entryAssembly": "NodeCraft.Communication.dll",
  "entryType": "NodeCraft.Communication.Plugin.CommunicationPlugin",
  "apiVersion": "1.0",
  "privateLibraryPath": "lib"
}
~~~

Add \`[assembly: InternalsVisibleTo("NodeCraft.Tests")]\` and the project reference to the test project. Add the project to all solution configurations by applying the same configuration pattern used by \`NodeCraft.Vision\`.

- [ ] **Step 2: Add failing project and registration assertions before implementing node behavior.**

Add \`RunCommunicationTestsAsync\` to \`Program.Main\` immediately after the existing Vision/flow tests. In \`CommunicationTests.cs\`, add these test names and assertions:

~~~csharp
await RunAsync("Communication project exposes the plugin manifest", () =>
{
    var projectPath = FindRepositoryFile("NodeCraft.Communication", "NodeCraft.Communication.csproj");
    var manifestPath = FindRepositoryFile("NodeCraft.Communication", "plugin.json");
    return File.Exists(projectPath)
        && File.ReadAllText(manifestPath).Contains("nodecraft.communication", StringComparison.Ordinal)
        && File.ReadAllText(manifestPath).Contains("NodeCraft.Communication.Plugin.CommunicationPlugin", StringComparison.Ordinal);
});

await RunAsync("Communication plugin registers TCP Client Send", () =>
{
    var plugin = new CommunicationPlugin();
    var registrationContext = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
    plugin.Register(registrationContext);
    var registration = registrationContext.Registrations.Single(item =>
        item.Definition.TypeKey == TcpClientSendNodeModel.FlowNodeTypeKey);

    return plugin.Metadata.Id == "nodecraft.communication"
        && registration.Definition.Category == "Communication"
        && registration.NodeModelType == typeof(TcpClientSendNodeModel)
        && registration.NodeFactory != null;
});
~~~

The second test is intentionally RED until the node model and registration are implemented. Import \`NodeCraft.Communication.Nodes\` and \`NodeCraft.Communication.Plugin\` after the project compiles enough to resolve the test file.

- [ ] **Step 3: Run the focused test group and confirm the RED state.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
~~~

Expected: the project-manifest assertion can pass, while the missing \`TcpClientSendNodeModel\`/registration produces a compile or test failure. Do not implement the node before this RED observation.

- [ ] **Step 4: Commit the plugin scaffold and test contract.**

~~~powershell
git add NodeCraft.Communication NodeCraft.sln NodeCraft.Tests/NodeCraft.Tests.csproj NodeCraft.Tests/CommunicationTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: scaffold communication plugin"
~~~

---

### Task 2: Implement and test payload conversion and the TCP transport boundary

**Files:**

- Create: \`NodeCraft.Communication/Transport/ITcpClientConnection.cs\`
- Create: \`NodeCraft.Communication/Transport/TcpClientConnection.cs\`
- Create: \`NodeCraft.Communication/Transport/TcpPayloadEncoder.cs\`
- Modify: \`NodeCraft.Tests/CommunicationTests.cs\`

**Interfaces:**

- Consumes: the Communication project and cancellation/timeout requirements from the design.
- Produces:

~~~csharp
internal interface ITcpClientConnection : IDisposable
{
    Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
    Task SendAsync(byte[] payload, CancellationToken cancellationToken);
}

internal interface ITcpClientConnectionFactory
{
    ITcpClientConnection Create();
}

internal static class TcpPayloadEncoder
{
    public static byte[] Encode(object value, string inputId);
}
~~~

- [ ] **Step 1: Add failing payload conversion tests.**

Add tests with exact expected bytes:

~~~csharp
Run("TCP payload encoder uses UTF-8 and preserves byte arrays", () =>
{
    var raw = new byte[] { 0, 255, 7 };
    var text = TcpPayloadEncoder.Encode("你好", "message_1");
    var bytes = TcpPayloadEncoder.Encode(raw, "message_2");
    var number = TcpPayloadEncoder.Encode(42, "message_3");

    return text.SequenceEqual(Encoding.UTF8.GetBytes("你好"))
        && ReferenceEquals(raw, bytes)
        && number.SequenceEqual(Encoding.UTF8.GetBytes("42"));
});

Run("TCP payload encoder rejects null values with the input id", () =>
{
    try
    {
        TcpPayloadEncoder.Encode(null, "message_2");
        return false;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message.Contains("message_2", StringComparison.Ordinal);
    }
});
~~~

- [ ] **Step 2: Run the harness and verify the payload RED state.**

Run the full \`dotnet run\` command. Expected: the new tests fail because the encoder does not exist.

- [ ] **Step 3: Implement \`TcpPayloadEncoder\`.**

Use byte-array identity for raw bytes, \`Encoding.UTF8.GetBytes\` for strings and non-null \`ToString()\` results, and throw \`InvalidOperationException\` naming \`inputId\` for null values or a null \`ToString()\` result. Do not catch or transform user-defined \`ToString()\` exceptions.

- [ ] **Step 4: Add failing transport lifecycle tests with a loopback listener.**

Add a test that creates \`TcpListener(IPAddress.Loopback, 0)\`, accepts one client, reads \`Encoding.UTF8.GetBytes("hello")\`, and asserts the production connection delivers those bytes before disposal. Add a cancellation/timeout test that uses an unroutable loopback port or a listener that does not accept and asserts \`ConnectAsync\` does not hang beyond the supplied timeout.

- [ ] **Step 5: Implement \`TcpClientConnection\` and its factory.**

Create a \`TcpClient\`, link the caller token with a timeout cancellation source, call the .NET 8 \`ConnectAsync(host, port, linkedToken)\` overload, retain \`GetStream()\`, and implement \`SendAsync\` with \`NetworkStream.WriteAsync(payload.AsMemory(), cancellationToken).AsTask()\`. Translate timeout-only cancellation to \`TimeoutException\`; preserve caller cancellation. Dispose the stream and client in an idempotent \`Dispose\` method. The factory returns a fresh connection for every executor session.

- [ ] **Step 6: Run transport tests and commit the transport slice.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
~~~

Expected: payload, loopback, and timeout tests pass.

~~~powershell
git add NodeCraft.Communication/Transport NodeCraft.Tests/CommunicationTests.cs
git commit -m "feat: add communication tcp transport"
~~~

---

### Task 3: Add the TCP node model, dynamic registration, and editor-independent metadata tests

**Files:**

- Create: \`NodeCraft.Communication/Nodes/TcpClientSendNodeModel.cs\`
- Modify: \`NodeCraft.Communication/Plugin/CommunicationPlugin.cs\`
- Modify: \`NodeCraft.Tests/CommunicationTests.cs\`

**Interfaces:**

- Consumes: the Flow dynamic input template, \`IWorkflowNodeValueProvider\`, \`ITcpClientConnectionFactory\`, and the transport types from Task 2.
- Produces: \`TcpClientSendNodeModel\` with \`FlowNodeTypeKey\`, settings properties, and workflow projection; a registration with an unlimited required dynamic template and a default executor factory.

- [ ] **Step 1: Add failing model and registration tests.**

Assert the exact registration contract:

~~~csharp
var definition = registration.Definition;
var template = definition.DynamicInputTemplate;
return template != null
    && template.PortIdPrefix == "message"
    && template.DisplayNamePrefix == "Message"
    && template.DataType == FlowDataType.Object
    && template.IsRequired
    && template.MinCount == 1
    && template.InitialCount == 1
    && template.MaxCount == null;
~~~

Create the node with \`registration.NodeFactory()\`, materialize it using the existing \`FlowDynamicInputResolver\`, and assert one \`message_1\` dynamic port. Add a workflow projection test:

~~~csharp
var node = new TcpClientSendNodeModel
{
    Host = "127.0.0.1",
    Port = 43123,
    ConnectTimeoutMilliseconds = 2300,
    StopOnSendFailure = false,
};
var workflowNode = new WorkflowNode();
node.WriteWorkflowInputs(workflowNode);

return Equals(workflowNode.Inputs["host"], "127.0.0.1")
    && Equals(workflowNode.Inputs["port"], 43123)
    && Equals(workflowNode.Inputs["connectTimeoutMilliseconds"], 2300)
    && Equals(workflowNode.Inputs["stopOnSendFailure"], false);
~~~

- [ ] **Step 2: Run the harness and verify the model/registration RED state.**

Run the full harness. Expected: registration/model assertions fail because the type and template are not implemented.

- [ ] **Step 3: Implement \`TcpClientSendNodeModel\`.**

Set \`ExecutorType\` to \`FlowNodeTypeKey\`, \`Name\` to \`TCP Client Send\`, and initialize empty input/output parameter lists. Expose:

~~~csharp
public string Host { get; set; } = string.Empty;
public int Port { get; set; }
public int ConnectTimeoutMilliseconds { get; set; } = 5000;
public bool StopOnSendFailure { get; set; } = true;
~~~

Implement \`WriteWorkflowInputs\` with the stable keys used by the spec. Do not add dynamic ports in the constructor; the common Flow materializer owns that lifecycle.

- [ ] **Step 4: Implement \`CommunicationPlugin\` registration.**

Set metadata ID/display/version to \`nodecraft.communication\`, \`Communication\`, and \`1.0.0\`. Register one \`FlowNodeRegistration\` with \`Category = "Communication"\`, \`DisplayName = "TCP Client Send"\`, empty output ports, the dynamic template from the global constraints, \`NodeModelType = typeof(TcpClientSendNodeModel)\`, \`NodeFactory = () => new TcpClientSendNodeModel()\`, and \`PaletteDisplayName = "TCP Client Send"\`. Wire the executor factory to the production \`TcpClientSendExecutor\` constructor; keep the registration free of camera or Flow-core dependencies.

- [ ] **Step 5: Run metadata tests and commit the node schema slice.**

Run the full harness and expect all new registration/model tests to pass. Then commit:

~~~powershell
git add NodeCraft.Communication/Nodes/TcpClientSendNodeModel.cs NodeCraft.Communication/Plugin/CommunicationPlugin.cs NodeCraft.Tests/CommunicationTests.cs
git commit -m "feat: register tcp client send node"
~~~

---

### Task 4: Implement ordered session execution and configurable send failures

**Files:**

- Create: \`NodeCraft.Communication/Nodes/TcpClientSendExecutor.cs\`
- Modify: \`NodeCraft.Communication/Plugin/CommunicationPlugin.cs\`
- Modify: \`NodeCraft.Tests/CommunicationTests.cs\`

**Interfaces:**

- Consumes: \`IFlowNodeExecutor\`, \`IFlowNodeSessionLifecycle\`, \`FlowNodeSessionContext\`, \`FlowNodeDefinition.InputPorts\`, \`ITcpClientConnectionFactory\`, and \`TcpPayloadEncoder\`.
- Produces: an executor whose constructor is testable with an injected factory and whose public behavior is:

~~~csharp
internal TcpClientSendExecutor(
    ITcpClientConnectionFactory connectionFactory,
    ILogger logger = null);

Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
    FlowExecutionContext context,
    WorkflowNode node,
    FlowNodeDefinition definition,
    IReadOnlyDictionary<string, object> inputs,
    CancellationToken cancellationToken);
~~~

- [ ] **Step 1: Add a recording/failing fake connection and failing executor tests.**

In \`CommunicationTests.cs\`, add \`RecordingTcpConnectionFactory\` and \`RecordingTcpConnection\` implementing the internal transport interfaces. Record connect count, sent payload references, send order, disposal, and a configurable \`FailOnSendNumber\`. Add tests with effective definitions containing \`message_1\`, \`message_2\`, and \`message_3\`:

- \`TCP executor connects once and sends string, bytes, and ToString values in port order\` expects three writes with UTF-8 \`alpha\`, the same raw byte-array reference, and \`99\`.
- \`TCP executor sends each dynamic input once rather than concatenating\` expects \`Payloads.Count == 3\` and the fake's send sequence exactly matches the definition IDs.
- \`TCP executor stops after the first failed send when configured\` sets \`StopOnSendFailure = true\`, fails write 2, expects an exception, two attempted sends, no write 3, and disposal after \`StopSessionAsync\`.
- \`TCP executor logs and continues after a failed send when configured\` sets the flag false, fails write 2, expects writes 1 and 3, no propagated exception, and a logger entry containing \`message_2\` and \`discarded\`.
- \`TCP executor rejects null payloads\` expects an \`InvalidOperationException\` naming the dynamic port regardless of the write-failure flag.
- \`TCP executor cleans up after startup failure\` makes \`ConnectAsync\` fail and asserts the created connection is disposed.

Use \`new FlowNodeSessionContext(node, definition, NullLogger.Instance)\` and call the lifecycle methods directly. Always call \`StopSessionAsync\` in test \`finally\` blocks when startup succeeds.

- [ ] **Step 2: Run the harness and verify the executor RED state.**

Run the full harness. Expected: the new executor tests fail because the executor is not defined or has no behavior.

- [ ] **Step 3: Implement configuration parsing and Session startup.**

Read static settings from \`context.Node.Inputs\` because \`host\`, \`port\`, \`connectTimeoutMilliseconds\`, and \`stopOnSendFailure\` are node configuration values rather than declared Flow ports. Require a non-empty host, port 1–65535, and positive timeout; accept the workflow's \`string\`/\`int\`/\`bool\` values and parse numeric strings with invariant culture for direct workflow callers. Use \`StopOnSendFailure = true\` when the optional boolean is absent. Create a fresh connection, call \`ConnectAsync\`, store the connection and policy, and dispose/reset the connection if startup throws.

- [ ] **Step 4: Implement ordered conversion and failure policy in \`ExecuteAsync\`.**

Filter \`definition.InputPorts\` with \`IsDynamic\` and iterate that list. For each port, check cancellation, require \`inputs.TryGetValue(port.Id, out value)\`, encode with \`TcpPayloadEncoder\`, and await exactly one \`SendAsync\`. Catch only non-cancellation send exceptions when \`StopOnSendFailure\` is false; log the exception with node ID and port ID and continue. With the flag true, log and rethrow. Return an empty \`Dictionary<string, object>\` because the node has no output ports. Never swallow \`OperationCanceledException\`.

- [ ] **Step 5: Implement idempotent Session cleanup.**

Capture the connection, clear executor state before disposing, and dispose it in \`StopSessionAsync\`. Make cleanup safe when startup failed, execution failed, or it was called more than once. Preserve any cleanup exception for the existing graph-session cleanup policy rather than hiding it inside the plugin.

- [ ] **Step 6: Run executor tests and commit the execution slice.**

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
~~~

Expected: all fake transport and executor policy tests pass. Commit:

~~~powershell
git add NodeCraft.Communication/Nodes/TcpClientSendExecutor.cs NodeCraft.Communication/Plugin/CommunicationPlugin.cs NodeCraft.Tests/CommunicationTests.cs
git commit -m "feat: execute tcp client send inputs"
~~~

---

### Task 5: Add the editor, persistence assertions, and palette icon metadata

**Files:**

- Create: \`NodeCraft.Communication/Views/TcpClientSendEditor.xaml\`
- Create: \`NodeCraft.Communication/Views/TcpClientSendEditor.xaml.cs\`
- Modify: \`NodeCraft.Communication/Plugin/CommunicationPlugin.cs\`
- Modify: \`NodeCraft.Flow/Flow/FlowNodeRegistry.cs\`
- Modify: \`NodeCraft.Tests/CommunicationTests.cs\`

**Interfaces:**

- Consumes: \`FlowNodeRegistration.ContentFactory\`, the Vision embedded-XAML editor pattern, \`FlowCanvas.NotifyGraphChanged\`, and existing Graph XML custom property support.
- Produces: an editor that edits the four node settings without owning dynamic socket controls, plus Communication palette icon metadata and round-trip coverage.

- [ ] **Step 1: Add failing editor and persistence tests.**

Add a test that resolves the registration, calls \`ContentFactory\` on an STA \`FlowCanvas\` and \`TcpClientSendNodeModel\`, and asserts the returned \`FrameworkElement\` is non-null. Add a static resource test that reads the embedded XAML through \`GetManifestResourceStream("NodeCraft.Communication.Views.TcpClientSendEditor.xaml")\` and checks the named controls \`HostEditor\`, \`PortEditor\`, \`ConnectTimeoutEditor\`, and \`StopOnSendFailureEditor\` exist.

Add a graph round-trip test:

~~~csharp
var original = new TcpClientSendNodeModel
{
    Host = "localhost",
    Port = 43210,
    ConnectTimeoutMilliseconds = 1800,
    StopOnSendFailure = false,
};
FlowDynamicInputResolver.MaterializeNodePorts(
    original,
    NodeExecutorFactory.Registry.Resolve(TcpClientSendNodeModel.FlowNodeTypeKey).Definition);
var path = Path.Combine(Path.GetTempPath(), "nodecraft-communication-" + Guid.NewGuid().ToString("N") + ".flow.xml");
try
{
    GraphModelXmlSerializer.Save(new GraphModel
    {
        Nodes = new List<NodeModel> { original },
        Links = new List<GraphLink>(),
    }, path);
    var loaded = GraphModelXmlSerializer.Load(path).Nodes.Single();
    var node = (TcpClientSendNodeModel)loaded;
    return node.Host == "localhost"
        && node.Port == 43210
        && node.ConnectTimeoutMilliseconds == 1800
        && !node.StopOnSendFailure
        && node.InputParameters.Count(port => port.IsDynamic) == 1;
}
finally
{
    File.Delete(path);
}
~~~

- [ ] **Step 2: Run the harness and verify the editor/persistence RED state.**

Run the full harness. Expected: content creation, embedded-resource, or round-trip assertions fail before the editor and registration content factory are present.

- [ ] **Step 3: Add the embedded editor XAML and model synchronization.**

Use the same \`LoadEditorRoot\`/\`XamlReader.Parse\` pattern as \`VisionCameraEditor\`. The constructor finds the four named controls, populates them from the model, and uses an \`_initializing\` guard. Host updates on \`TextChanged\`; port and timeout update only when \`int.TryParse\` succeeds and the values are within their valid ranges; the checkbox updates \`StopOnSendFailure\`. Every accepted change calls \`_canvas.NotifyGraphChanged(refreshNodeContents: false)\`. \`CreateContent\` must reject a non-\`TcpClientSendNodeModel\` with an explicit \`InvalidOperationException\`.

- [ ] **Step 4: Wire the editor and palette icons.**

Set \`ContentFactory = TcpClientSendEditor.CreateContent\` in the TCP registration. In \`FlowNodeRegistry.ResolveCategoryIconKind\`, map \`Communication\` to a network icon such as \`LanConnect\`; in \`ResolveNodeIconKind\`, map \`nodecraft.communication.tcp-client-send\` to the same network icon. Keep all existing mappings unchanged.

- [ ] **Step 5: Run editor, persistence, and palette tests and commit.**

Run the full harness and expect all new tests to pass. Commit:

~~~powershell
git add NodeCraft.Communication/Views NodeCraft.Communication/Plugin/CommunicationPlugin.cs NodeCraft.Flow/Flow/FlowNodeRegistry.cs NodeCraft.Tests/CommunicationTests.cs
git commit -m "feat: add tcp client send editor"
~~~

---

### Task 6: Verify plugin loading, graph integration, loopback execution, and the complete deliverable

**Files:**

- Modify: \`NodeCraft.Tests/CommunicationTests.cs\`
- Modify: \`NodeCraft.Tests/Program.cs\` only if test registration order or async invocation needs final adjustment
- Modify: no production files unless a test exposes a concrete integration defect

**Interfaces:**

- Consumes: the complete Communication plugin, the common dynamic-port resolver, \`GraphModelWorkflowAdapter\`, \`GraphExecutor\`, \`PluginLoader\`, and the production TCP transport.
- Produces: evidence that the feature works through plugin registration, persistence/workflow conversion, and a real local TCP server.

- [ ] **Step 1: Add failing end-to-end assertions before final verification.**

Add these focused tests:

- \`PluginLoader loads the communication manifest\` copies/builds the Communication output into a temporary plugin directory, runs \`PluginLoader.LoadAll\`, and asserts a successful \`nodecraft.communication\` result and a registry entry for the TCP type.
- \`Graph adapter preserves ordered communication dynamic inputs\` creates a materialized TCP node plus two string source nodes and links them to \`message_1\` and \`message_2\`; \`GraphModelWorkflowAdapter.Convert\` must emit the ordered IDs and \`LinkRef\` values under those exact keys.
- \`TCP Client Send writes ordered bytes to a loopback server\` starts a listener, builds the TCP node workflow with the assigned ephemeral port and two dynamic values, creates the production executor through the registration, starts the session, executes once, stops, and asserts the server received the ordered bytes \`firstsecond\` and the client was cleaned up.

- [ ] **Step 2: Run the harness and inspect any integration RED state.**

Run the full harness. Expected: all Communication tests pass if project output and plugin discovery are correct. If plugin discovery cannot load from a project output directory because the host loader expects a private \`lib\` directory, adjust only the test fixture directory layout to match the existing \`PluginPathResolver\` contract; do not bypass \`PluginLoader\`.

- [ ] **Step 3: Run the complete build and test evidence.**

Run:

~~~powershell
dotnet build NodeCraft.sln --no-restore --verbosity minimal
$output = dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore 2>&1
$exitCode = $LASTEXITCODE
$output | Select-String -Pattern 'Communication|TCP|FAILURES|ALL PASS'
Write-Output "exit_code=$exitCode"
exit $exitCode
git diff --check
git status --short
~~~

Expected: build exit code 0, test output includes \`ALL PASS\`, \`git diff --check\` emits no whitespace errors, and only intentional committed changes remain. Existing warnings can be reported separately from errors.

- [ ] **Step 4: Perform a final code review against the approved spec.**

Check every acceptance criterion: independent plugin identity, unlimited dynamic inputs, one write per input, exact conversion rules, one connection per session, configurable failure policy, cleanup on all paths, editor persistence, workflow order, plugin loading, and loopback delivery. Confirm no TCP receive/UDP/reconnect/framing feature slipped into the change.

- [ ] **Step 5: Commit the final integration/test slice if it contains changes.**

~~~powershell
git add NodeCraft.Tests/CommunicationTests.cs NodeCraft.Tests/Program.cs
git commit -m "test: verify communication tcp send integration"
~~~

If Task 6 only added tests that were already committed in earlier slices, do not create an empty commit.

---

## Verification Checklist

- [ ] \`dotnet build NodeCraft.sln --no-restore --verbosity minimal\` exits 0.
- [ ] \`dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore\` ends with \`ALL PASS\`.
- [ ] Communication plugin manifest and entry type are copied and loadable.
- [ ] TCP Client Send has exactly one initial required dynamic \`Object\` input and unlimited add capacity.
- [ ] \`string\`, \`byte[]\`, and fallback object payloads match the approved conversion rules.
- [ ] Each dynamic port creates exactly one ordered \`SendAsync\` call.
- [ ] Stop-on-failure true/false behavior, null handling, cancellation, and cleanup are covered.
- [ ] Host, port, timeout, failure policy, dynamic IDs, values, and links round-trip through existing paths.
- [ ] A loopback server receives ordered bytes from the production socket implementation.
- [ ] \`git diff --check\` passes and the worktree is clean apart from any intentionally retained branch state.

