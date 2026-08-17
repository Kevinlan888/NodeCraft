# NodeCraft Node Plugin Development Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a detailed Chinese Markdown guide that lets a new developer or AI build, register, run, persist, test, package, and debug a NodeCraft plugin node using the current repository APIs.

**Architecture:** Keep one navigable guide at `docs/node-plugin-development-guide.md`. Organize it as a tutorial-first document followed by API concepts, lifecycle and dynamic-port patterns, testing/debugging recipes, and AI-specific checklists. Every technical claim and code sample is cross-checked against `NodeCraft.PluginSample`, `NodeCraft.Vision`, `NodeCraft.Communication`, `NodeCraft.Flow`, and `NodeCraft.Tests`; no runtime API changes are introduced.

**Tech Stack:** Markdown, Mermaid, C# 9, .NET 8 WPF, `NodeCraft.Flow` plugin APIs, `plugin.json`, PowerShell validation commands, and the repository's self-running `NodeCraft.Tests` harness.

## Global Constraints

- The final guide is written in Chinese and lives at `docs/node-plugin-development-guide.md`.
- Code samples use the repository's C# 9, .NET 8 WPF, explicit `using`, and existing namespace conventions.
- The guide must distinguish `NodeModel`, `WorkflowNode`, `FlowNodeDefinition`, `Session`, and `Iteration`.
- Stable TypeKeys use a namespace-like prefix and must not be renamed casually because `.flow.xml` identity depends on them.
- Port slot values come from the effective `FlowNodeDefinition`; runtime `InputParameters` list positions are not slot values.
- `NodeCraft.Flow.dll`, `CommonControls.WPF.dll`, and WPF framework assemblies are shared assemblies and must not be copied into a plugin package.
- Configuration must be traced through the UI/model, `WriteWorkflowInputs`, XML persistence, session startup, and the Executor.
- Cancellation is propagated; resources are cleaned on normal stop, startup failure, execution failure, and cancellation.
- Dynamic nodes enumerate effective definition ports in declared order, never input dictionary enumeration order.
- The guide documents current behavior; it does not modify NodeCraft core APIs, add a CLI scaffold command, or change the graph format.
- Final verification includes placeholder scanning, local-path checks, Markdown structure checks, `git diff --check`, and the existing Windows test/build commands where the environment permits.

## File Map

- Create: `docs/node-plugin-development-guide.md` — the complete newcomer/AI node development guide.
- Reference only: `docs/superpowers/specs/2026-08-17-node-plugin-development-guide-design.md` — approved content design.
- Reference only: `CLAUDE.md` — repository conventions and build/test commands.
- Reference only: `NodeCraft.PluginSample/` — minimal plugin, multi-node registration, private dependency, and editor examples.
- Reference only: `NodeCraft.Vision/` — Session/Iteration lifecycle and external-resource patterns.
- Reference only: `NodeCraft.Communication/` — dynamic inputs, settings projection, transport, and send-failure policy.
- Reference only: `NodeCraft.Flow/` — public plugin, port, graph, serialization, and execution contracts.
- Reference only: `NodeCraft.Tests/` — self-running test and integration patterns.

---

### Task 1: Create the guide shell, quick start, and architecture map

**Files:**
- Create: `docs/node-plugin-development-guide.md`
- Reference: `docs/superpowers/specs/2026-08-17-node-plugin-development-guide-design.md`
- Reference: `README.md`
- Reference: `CLAUDE.md`
- Reference: `NodeCraft.Flow/Plugins/IFlowPlugin.cs`
- Reference: `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`
- Reference: `NodeCraft/Plugins/PluginLoader.cs`
- Reference: `NodeCraft/Pages/FlowPage.xaml.cs`

**Interfaces:**
- Consumes: `IFlowPlugin.Register(IPluginContext)`, `FlowNodeRegistry`, `PluginLoader`, and `GraphModelWorkflowAdapter.Convert(GraphModel)`.
- Produces: the guide title, audience statement, prerequisites, table of contents, quick-start checklist, plugin-loading Mermaid diagram, graph-execution Mermaid diagram, and source-of-truth map.

- [ ] **Step 1: Write the document header and navigation.**

  Add the Chinese title, intended readers, learning outcome, a short “先看哪一节” table, and links to the quick start, fixed-port tutorial, lifecycle patterns, dynamic-port pattern, testing, troubleshooting, and AI checklist sections.

- [ ] **Step 2: Add the environment and first-run commands.**

  Include the exact Windows-oriented commands:

  ```powershell
  dotnet build NodeCraft.sln
  dotnet run --project NodeCraft/NodeCraft.csproj
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
  ```

  State that WPF projects require Windows and that the local `Packages/` feed supplies `CommonControls.WPF`.

- [ ] **Step 3: Add the plugin loading diagram and explanation.**

  Use a Mermaid flowchart with these exact stages: `plugin.json` → `PluginLoader.LoadAll` → `IFlowPlugin.Register` → staged `FlowNodeRegistration` → `FlowNodeRegistry.RegisterPlugin` → palette/node factory → graph conversion.

- [ ] **Step 4: Add the graph execution diagram and vocabulary table.**

  Show `GraphModel` → `GraphModelWorkflowAdapter` → `WorkflowDocument`/`WorkflowNode` → `GraphExecutor` → `GraphExecutionSession` → `FlowGraphIterationRunner` → `IFlowNodeExecutor.ExecuteAsync`. Define `NodeModel`, `WorkflowNode`, `FlowNodeDefinition`, `Session`, and `Iteration` in one table.

- [ ] **Step 5: Verify the shell before continuing.**

  Run:

  ```powershell
  Test-Path docs/node-plugin-development-guide.md
  rg -n '^# |^## |^### |```mermaid' docs/node-plugin-development-guide.md
  ```

  Expected: the file exists, the title and planned top-level headings are present, and both Mermaid blocks are found.

### Task 2: Add plugin packaging, registration, and the minimal fixed-port node tutorial

**Files:**
- Modify: `docs/node-plugin-development-guide.md`
- Reference: `NodeCraft.PluginSample/plugin.json`
- Reference: `NodeCraft.PluginSample/NodeCraft.PluginSample.csproj`
- Reference: `NodeCraft.PluginSample/Plugin/SamplePlugin.cs`
- Reference: `NodeCraft.PluginSample/Nodes/SampleValueNodeModel.cs`
- Reference: `NodeCraft.PluginSample/Nodes/SampleValueExecutor.cs`
- Reference: `NodeCraft.Flow/Plugins/IPluginContext.cs`
- Reference: `NodeCraft.Flow/Plugins/PluginMetadata.cs`

**Interfaces:**
- Consumes: `IFlowPlugin`, `PluginMetadata`, `IPluginContext`, `IPluginNodeRegistrar.Register`, `FlowNodeRegistration`, `FlowNodeDefinition`, `NodeModel`, `IWorkflowNodeValueProvider`, and `IFlowNodeExecutor`.
- Produces: copyable `plugin.json`, `.csproj`, plugin entry, NodeModel, Executor, registration, package-layout example, and first-node verification steps.

- [ ] **Step 1: Document the plugin directory and manifest.**

  Include a tree with `plugin.json`, entry DLL, and `lib/`. Provide a manifest example whose `id`, `entryAssembly`, `entryType`, `apiVersion`, and `privateLibraryPath` match the Sample plugin pattern. Explain that manifest `id` must equal `PluginMetadata.Id` and that IDs cannot contain whitespace.

- [ ] **Step 2: Document the project file.**

  Provide a minimal `.csproj` with `TargetFramework=net8.0-windows`, `UseWPF=true` when UI is needed, `LangVersion=9.0`, an explicit `NodeCraft.Flow` project reference with `Private=false`, and manifest copy behavior. Explain the extra `Page Remove`/`EmbeddedResource` entries only when an editor XAML is present.

- [ ] **Step 3: Add the minimal value-node code skeleton.**

  Use a stable example TypeKey such as `company.example.nodes.hello-value`. The guide must show this exact data path:

  ```csharp
  public void WriteWorkflowInputs(WorkflowNode node)
  {
      node.Inputs[BuiltInNodePorts.Value] = ValueText ?? string.Empty;
  }
  ```

  The Executor must call `cancellationToken.ThrowIfCancellationRequested()`, read the named input, and return an `IReadOnlyDictionary<string, object>` keyed by the declared output port.

- [ ] **Step 4: Add registration code and explain every field.**

  Show `FlowNodeRegistration` with a `FlowNodeDefinition`, one output port, an Executor factory, `NodeModelType`, `NodeFactory`, `PaletteDisplayName`, and `PaletteDescription`. Explain why the Executor factory must create a fresh Executor for each graph session.

- [ ] **Step 5: Add first-node verification.**

  Document the sequence: build the plugin, copy the manifest and assembly into `<NodeCraft app root>/Plugins/<PackageFolder>/`, launch NodeCraft, find the palette item, add the node, connect it to a preview/consumer node, run once, save a graph, and reload it. Include the registration and plugin-load test assertions that the reader should add.

- [ ] **Step 6: Verify code-block completeness.**

  Run:

  ```powershell
  rg -n 'plugin\.json|IFlowPlugin|FlowNodeRegistration|IFlowNodeExecutor|WriteWorkflowInputs|company\.example\.nodes' docs/node-plugin-development-guide.md
  ```

  Expected: each of the six concepts appears in both explanatory text and a code sample or command sequence.

### Task 3: Add ports, types, slot rules, workflow projection, and persistence

**Files:**
- Modify: `docs/node-plugin-development-guide.md`
- Reference: `NodeCraft.Flow/Flow/FlowSchema.cs`
- Reference: `NodeCraft.Flow/Flow/FlowPorts.cs`
- Reference: `NodeCraft.Flow/Flow/FlowDynamicInputResolver.cs`
- Reference: `NodeCraft.Flow/Flow/GraphModelWorkflowAdapter.cs`
- Reference: `NodeCraft.Flow/Flow/GraphModelXmlSerializer.cs`
- Reference: `NodeCraft.Flow/Flow/GraphModelLinkReconciler.cs`
- Reference: `NodeCraft.Communication/Nodes/TcpClientSendNodeModel.cs`

**Interfaces:**
- Consumes: `FlowPortDefinition`, `FlowNodeDefinition`, `FlowDataType`, `FlowPortAvailability`, `LinkRef`, `IWorkflowNodeValueProvider`, `GraphModelWorkflowAdapter`, `GraphModelXmlSerializer`, and `FlowDynamicInputResolver`.
- Produces: port/type reference tables, the NodeModel → WorkflowNode → Executor data-flow section, slot warning, configuration persistence recipes, and a fixed/dynamic port decision table.

- [ ] **Step 1: Add the fixed-port field reference.**

  Explain `Id`, `DisplayName`, `IOType`, `DataType`, `PreferredDirection`, `IsRequired`, `DefaultValue`, `Availability`, and `IsControlPort`. Include one input and one output definition using the repository's `BuiltInNodePorts` constants.

- [ ] **Step 2: Add the data-type and control-flow rules.**

  Document `string`, `number`, `boolean`, `object`, `control`, `*`, and `MATCH_TYPE`; explain required inputs, default values, `flowIn`, and skipped control branches. Link the reader to `FlowTypeValidator` and `FlowGraphIterationRunner` as the source of behavior.

- [ ] **Step 3: Add the slot rule with a broken/correct comparison.**

  Show the incorrect pattern `node.InputParameters[index]` and the correct pattern that resolves a port from the effective definition. State that `flowIn` is definition slot 0 for the standard fixed-port nodes and that dynamic ports must be resolved through the dynamic-input resolver.

- [ ] **Step 4: Add configuration projection and XML persistence.**

  Show a model property, `WriteWorkflowInputs`, Executor startup read, and the serializer's public custom-property behavior. Explain why the same setting must be tested in the editor/model, workflow projection, XML round-trip, and runtime startup.

- [ ] **Step 5: Add the fixed-vs-dynamic decision table.**

  Compare fixed ports, optional fixed ports, session ports, iteration ports, and dynamic input templates. State that dynamic input IDs are stable metadata and that links are reconciled through `GraphModelLinkReconciler`.

- [ ] **Step 6: Verify API names and persistence references.**

  Run:

  ```powershell
  rg -n 'FlowPortDefinition|FlowDataType|FlowPortAvailability|WriteWorkflowInputs|GraphModelWorkflowAdapter|GraphModelXmlSerializer|FlowDynamicInputTemplate|slot' docs/node-plugin-development-guide.md
  ```

  Expected: every public name used in the section appears in the current source tree and the guide does not call `InputParameters` a runtime slot map.

### Task 4: Add Executor modes, Session lifecycle, cancellation, errors, and logging

**Files:**
- Modify: `docs/node-plugin-development-guide.md`
- Reference: `NodeCraft.Flow/Flow/FlowRuntime.cs`
- Reference: `NodeCraft.Flow/Flow/FlowSessionContracts.cs`
- Reference: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Reference: `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`
- Reference: `NodeCraft.Vision/Nodes/VisionCameraExecutor.cs`
- Reference: `NodeCraft.Communication/Nodes/TcpClientSendExecutor.cs`
- Reference: `NodeCraft.Communication/Transport/TcpPayloadEncoder.cs`

**Interfaces:**
- Consumes: `IFlowNodeExecutor.ExecuteAsync`, `IFlowNodeSessionLifecycle`, `IFlowIterationSource`, `FlowNodeSessionContext`, `FlowExecutionContext`, `CancellationToken`, and `ILogger`.
- Produces: four Executor patterns, lifecycle sequence diagrams, cleanup rules, cancellation rules, error classification table, logging examples, and the `stopOnSendFailure` scope note.

- [ ] **Step 1: Add the stateless Executor pattern.**

  Show a complete method signature, cancellation check, named input lookup, output dictionary, and `Task.FromResult` return. State that Executor code receives runtime inputs and should not reach into WPF controls.

- [ ] **Step 2: Add the Session lifecycle pattern.**

  Show `StartSessionAsync` creating one resource, `ExecuteAsync` using the resource, and `StopSessionAsync` capturing/clearing the field before disposal. Explain reverse-order graph cleanup and why stop must be safe after partial startup.

- [ ] **Step 3: Add the Iteration source pattern.**

  Use the Vision camera pattern to show `PrepareIterationAsync` waiting for the next item, storing only current iteration state, and `ExecuteAsync` publishing it. Explain session values versus iteration values and continuous execution.

- [ ] **Step 4: Add cancellation and failure boundaries.**

  Define which exceptions should propagate: cancellation, invalid configuration, missing required input, resource startup failure, and unrecoverable execution errors. Show a catch block that preserves `OperationCanceledException` and does not convert cancellation into success.

- [ ] **Step 5: Add the encoding/send-failure example.**

  Explain that the current TCP node reads `stopOnSendFailure` at session startup and applies it to exceptions from `SendAsync`, while `TcpPayloadEncoder.Encode` is outside that try/catch. State that null payload and encoding failures are therefore not automatically governed by the send-failure policy.

- [ ] **Step 6: Add structured logging examples.**

  Show `_logger.LogError(exception, "...{NodeId}...{InputId}...", node.Id, inputId)` and explain the minimum context required for node-specific diagnostics. State that plugins should not silently swallow exceptions or replace the host's cleanup policy.

- [ ] **Step 7: Verify lifecycle terminology.**

  Run:

  ```powershell
  rg -n 'StartSessionAsync|StopSessionAsync|PrepareIterationAsync|ExecuteAsync|CancellationToken|LogError|stopOnSendFailure' docs/node-plugin-development-guide.md
  ```

  Expected: all methods are described in the correct lifecycle phase and no section claims that one iteration source is recreated for every iteration.

### Task 5: Add custom editors, dynamic inputs, execution results, and private dependencies

**Files:**
- Modify: `docs/node-plugin-development-guide.md`
- Reference: `NodeCraft.PluginSample/Views/SampleValueEditor.xaml.cs`
- Reference: `NodeCraft.PluginSample/Views/SampleValueEditor.xaml`
- Reference: `NodeCraft.Communication/Views/TcpClientSendEditor.xaml.cs`
- Reference: `NodeCraft.Communication/Views/TcpClientSendEditor.xaml`
- Reference: `NodeCraft.Communication/Plugin/CommunicationPlugin.cs`
- Reference: `NodeCraft.Communication/NodeCraft.Communication.csproj`
- Reference: `NodeCraft.PluginSample/NodeCraft.PluginSample.csproj`
- Reference: `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`

**Interfaces:**
- Consumes: `FlowNodeRegistration.ContentFactory`, `FlowCanvas.NotifyGraphChanged`, `DynamicResource`, `FlowDynamicInputTemplate`, `ExecutionResultHandler`, `RefreshContentAfterExecution`, and the plugin `lib` staging pattern.
- Produces: copyable WPF editor template, dynamic-input recipe and test matrix, result/preview section, and private-dependency package guide.

- [ ] **Step 1: Add the embedded XAML editor pattern.**

  Show the project item configuration, `LoadEditorRoot`, `Find<T>`, initialization guard, event subscriptions, model synchronization, accepted-value validation, and `NotifyGraphChanged`. Explicitly state that each NodeModel instance needs a new content instance.

- [ ] **Step 2: Add WPF theme and threading rules.**

  Include `DynamicResource` examples using existing `color*` keys, explain why hard-coded colors are prohibited, and state that editor tests must run on an STA thread using the repository's `RunOnSta` pattern.

- [ ] **Step 3: Add the dynamic-input registration recipe.**

  Show `FlowDynamicInputTemplate` with `PortIdPrefix`, `DisplayNamePrefix`, `DataType`, `PreferredDirection`, `IsRequired`, `Availability`, `MinCount`, `InitialCount`, and `MaxCount`. Explain materialization, effective definition resolution, stable IDs, order, link reconciliation, and the executor loop over definition ports.

- [ ] **Step 4: Add the dynamic-input test matrix.**

  Include test cases for initial count, unlimited add, removal of a middle port, link round-trip, missing required value, invalid metadata, type incompatibility, and ordered execution. Use `NodeCraft.Tests/CommunicationTests.cs` as the concrete reference.

- [ ] **Step 5: Add execution-result and preview behavior.**

  Show an `ExecutionResultHandler` that finds an output value by node ID and slot, updates a preview-only model property, and clears the property when no result exists. Explain `RefreshContentAfterExecution` and the difference between runtime display state and serialized configuration.

- [ ] **Step 6: Add private dependency packaging.**

  Show the Sample plugin `lib` staging target, final package tree, shared/private assembly boundary, and PluginLoader integration test. Explain why a plugin that compiles can still fail to load when the staged package is incomplete.

- [ ] **Step 7: Verify UI and dynamic references.**

  Run:

  ```powershell
  rg -n 'ContentFactory|EmbeddedResource|DynamicResource|NotifyGraphChanged|FlowDynamicInputTemplate|ExecutionResultHandler|RefreshContentAfterExecution|privateLibraryPath|lib' docs/node-plugin-development-guide.md
  ```

  Expected: each editor, dynamic-port, preview, and packaging rule has a corresponding example or test reference.

### Task 6: Add testing, troubleshooting, AI protocol, and appendices

**Files:**
- Modify: `docs/node-plugin-development-guide.md`
- Reference: `NodeCraft.Tests/Program.cs`
- Reference: `NodeCraft.Tests/CommunicationTests.cs`
- Reference: `NodeCraft.Tests/SessionNodeInitializationTests.cs`
- Reference: `NodeCraft.Tests/GraphExecutionSessionTests.cs`
- Reference: `NodeCraft.Tests/DynamicInputPortTests.cs`
- Reference: `NodeCraft.Tests/VisualContractTests.cs`
- Reference: `NodeCraft/Plugins/PluginLoader.cs`
- Reference: `docs/testing/vision-camera-hardware-acceptance.md`

**Interfaces:**
- Consumes: the repository's self-running test runner, `Run`, `RunAsync`, `RunOnSta`, plugin loader tests, session tests, dynamic-port tests, and existing hardware acceptance documentation.
- Produces: a test recipe, symptom-based troubleshooting table, AI development protocol, checklists, file index, and copyable appendix templates.

- [ ] **Step 1: Add the testing chapter.**

  Explain the difference between direct Executor tests and real GraphExecutor/PluginLoader tests. Include concrete test names and commands for registration, model projection, XML round-trip, editor STA, session cleanup, dynamic ports, final package loading, and loopback resources.

- [ ] **Step 2: Add the verification command block.**

  Include:

  ```powershell
  dotnet build NodeCraft.sln --no-restore
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
  git diff --check
  ```

  Explain that a complete test run may expose environment-specific timeout or log-directory permission failures and that those must be reported separately from node-specific failures.

- [ ] **Step 3: Add the symptom-based troubleshooting table.**

  For each symptom, provide four fields: observation point, source file, smallest validation, and likely root cause. Cover missing palette item, load failure, old graph failure, wrong port order, type mismatch, leaked resource, uncancelled Task, missing configuration propagation, failure-policy scope, editor initialization events, and package-only load failure.

- [ ] **Step 4: Add the AI development protocol.**

  Give AI a numbered workflow: read `CLAUDE.md` and reference plugins; classify the node; write TypeKey/port/config/lifecycle contracts; write a failing test; implement Model/Definition/Registration/Executor/Editor/Integration; verify component boundaries; run build/test/package checks; report file paths, line numbers, results, and environment limitations.

- [ ] **Step 5: Add the AI review checklist.**

  Require explicit answers for stable TypeKey, manifest/metadata ID equality, definition-based slot usage, UI→Workflow→XML→Executor configuration flow, cancellation/cleanup, dynamic-port order tests, themed WPF resources, and PluginLoader package loading.

- [ ] **Step 6: Add appendices.**

  Include compact templates for a minimal fixed-port node, a configuration node, a Session node, a dynamic-input node, a custom editor, and a self-running test. Add a source-file index linking each concept to the current implementation.

- [ ] **Step 7: Verify chapter coverage.**

  Run:

  ```powershell
  $required = @(
      '快速开始', '架构', 'plugin.json', 'NodeModel', 'FlowNodeDefinition',
      'IFlowNodeExecutor', 'Session', '动态', 'WPF', '持久化', '测试',
      '排错', 'AI'
  )
  $guide = Get-Content -Raw docs/node-plugin-development-guide.md
  $required | ForEach-Object {
      if ($guide -notmatch [regex]::Escape($_)) { throw "Missing guide topic: $_" }
  }
  ```

  Expected: the command completes without throwing.

### Task 7: Perform final documentation QA and handoff

**Files:**
- Modify: `docs/node-plugin-development-guide.md` only if QA finds a defect.
- Reference: `docs/superpowers/specs/2026-08-17-node-plugin-development-guide-design.md`
- Reference: all source files listed in the guide's source map.

**Interfaces:**
- Consumes: the approved design, final guide, current repository paths, Markdown code fences, Mermaid blocks, and validation commands.
- Produces: a self-contained guide with no stale paths, contradictory rules, or unfinished sections, plus a verification report.

- [ ] **Step 1: Check the design-to-guide coverage.**

  Compare every section of the approved design with a corresponding guide heading or appendix. Add missing material before proceeding; do not silently drop lifecycle, dynamic-port, persistence, packaging, or AI sections.

- [ ] **Step 2: Scan for unfinished or ambiguous text.**

  Run:

  ```powershell
  rg -n -i 'unfinished|ambiguous|later|fill in|适当处理' docs/node-plugin-development-guide.md
  ```

  Expected: no matches. Replace vague instructions with the exact API, file, command, or test needed by the reader.

- [ ] **Step 3: Validate local source links and named files.**

  Check each linked repository file with `Test-Path`. Verify that the guide does not claim a type, method, property, or command absent from the current source. Confirm all code samples use names available in the referenced API.

- [ ] **Step 4: Validate Markdown structure and Mermaid fences.**

  Run:

  ```powershell
  $guide = Get-Content -Raw docs/node-plugin-development-guide.md
  $mermaidOpen = ([regex]::Matches($guide, '(?m)^```mermaid\s*$')).Count
  $mermaidClose = ([regex]::Matches($guide, '(?m)^```\s*$')).Count
  if ($mermaidOpen -lt 2) { throw 'Expected at least two Mermaid blocks.' }
  if ($guide -notmatch '(?m)^# NodeCraft') { throw 'Missing document title.' }
  if ($guide -notmatch '(?m)^## ') { throw 'Missing second-level headings.' }
  ```

  Expected: at least two Mermaid blocks, one title, and multiple second-level headings.

- [ ] **Step 5: Run repository-level verification.**

  Run:

  ```powershell
  git diff --check
  dotnet build NodeCraft.sln --no-restore
  dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore
  ```

  Report the exact exit code and distinguish documentation validation from pre-existing environment failures. Do not claim the entire repository is green if the runner reports failures.

- [ ] **Step 6: Inspect the final diff and hand off.**

  Run:

  ```powershell
  git status --short
  git diff --stat
  ```

  Confirm that the final change contains the requested guide and the approved design/plan artifacts only. The workspace previously rejected `.git/index.lock` creation; if that permission error remains, leave files uncommitted and report it verbatim instead of deleting lock files or changing repository permissions.
