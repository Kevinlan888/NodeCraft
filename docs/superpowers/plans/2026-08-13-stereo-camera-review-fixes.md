# Stereo Camera Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve all nine reviewed correctness, resource-safety, concurrency, performance, and UI-diagnostics findings without changing the Windows-only stereo-camera streaming contract.

**Architecture:** Keep `GraphExecutionSession` as the graph lifecycle owner, but make start/stop a single state machine coordinated by a linked cancellation token and an awaited start task. Keep camera native calls behind a small frame-operation seam so handle ownership can be fault-injected in tests. Keep the latest-only preview queue single-slot, adding worker generation ownership and a dispatcher-side version check.

**Tech Stack:** C# 9, .NET 8 WindowsDesktop, WPF, Microsoft.Extensions.Logging/NLog, Cdecl P/Invoke/SafeHandle, repository console test harness, Windows x64 remote verification.

## Global Constraints

- Follow the approved design in `docs/superpowers/specs/2026-08-13-stereo-camera-review-fixes-design.md`; amend the design before changing behavior outside its scope.
- Use TDD for every production change: add one focused failing test, run it and record the expected failure, implement the smallest fix, then run the focused and full suites.
- Keep the host and plugin Windows x64 only; do not add `StereoCamera.Net.dll`, vendor binaries, or new exposure controls.
- Preserve newest-complete-frame semantics, serial graph iterations, four camera output slots, one-shot cleanup, and continuous camera reuse.
- Use `apply_patch` for source edits. Run `git diff --check` before each commit and stage only the task files.
- `NodeCraft.Tests` is a console harness. Add partial `Program` test methods and invoke them from its existing `Main`; use `Run`/`RunAsync` and `RunOnSta` helpers.
- Linux may be used for static checks only. Run WPF compilation, tests, publish, and packaging on the supplied Windows x64 host with WindowsDesktop SDK.
- Do not claim a fix or passing test without fresh command output from the same task.

---

### Task 1: Make graph startup and stop/dispose race-safe

**Files:**
- Modify: `NodeCraft.Flow/Flow/GraphExecutionSession.cs:18-234`
- Modify: `NodeCraft.Tests/GraphExecutionSessionTests.cs`

**Interfaces:**
- Consumes: existing `IFlowNodeSessionLifecycle.StartSessionAsync` and `StopSessionAsync` contracts.
- Produces: `GraphExecutionSession.StartAsync` that completes cancellation when a concurrent stop wins, and `StopAsync` that completes only after any in-flight start and all started lifecycles are cleaned.

- [ ] **Step 1: Write the failing concurrency test.** Add `graph session stop during blocked start never revives or leaks` to `RunGraphExecutionSessionLifecycleTestsAsync`. Use a lifecycle executor with `StartEntered` and `ReleaseStart` task completions; the assertion shape is:

```csharp
var startTask = session.StartAsync(CancellationToken.None);
await executor.StartEntered.Task;
var stopTask = session.StopAsync();
executor.ReleaseStart.TrySetResult(true);
var startCanceled = false;
try { await startTask; } catch (OperationCanceledException) { startCanceled = true; }
await stopTask;
return startCanceled
    && executor.StopCount == 1
    && session.State == GraphExecutionSessionState.Stopped
    && session.StopAsync().IsCompleted;
```

The current implementation fails because stop snapshots an empty lifecycle list and startup publishes `Running` after stop.
- [ ] **Step 2: Run the focused harness to verify RED.** Run `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug` on Windows. Expected: the new test fails because stop snapshots an empty lifecycle list and startup publishes `Running` after stop.
- [ ] **Step 3: Implement the minimal state-machine fix.** Add a private linked startup `CancellationTokenSource` combining the caller token and `_stopCancellation.Token`. In `StopAsync`, set `Stopping`, cancel `_stopCancellation`, capture `_startTask`, and have `StopCoreAsync` await that task outside `_iterationGate` before snapshotting. In `StartCoreAsync`, after each lifecycle await, add the lifecycle under `_stateGate`; if the state is no longer `Starting`, remove/stop that lifecycle before propagating cancellation. Before assigning `Running`, require the state still be `Starting` and the linked token not canceled. Ensure the startup CTS is disposed after start completion and `DisposeAsync` still waits for the stop task.
- [ ] **Step 4: Run the focused test GREEN and the existing lifecycle tests.** Re-run the complete Windows harness and confirm the new race test plus normal topological/reverse cleanup tests pass with no failures.
- [ ] **Step 5: Review and commit.** Run `git diff --check`; commit `fix(flow): serialize graph startup and stop lifecycle`.

### Task 2: Fix WPF content ownership and stop-menu exception boundary

**Files:**
- Modify: `NodeCraft.Vision.StereoCamera/Views/StereoCameraEditor.xaml.cs:18-29`
- Modify: `NodeCraft.Vision.StereoCamera/Views/FlowImagePreviewView.xaml.cs:21-43,86-105`
- Modify: `NodeCraft/MainWindow.xaml.cs:107-112`
- Modify: `NodeCraft.Tests/FlowImagePreviewTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**
- Consumes: `StereoCameraEditor.CreateContent`, `FlowImagePreviewView.CreateContent`, and the existing `FlowPage.StopExecutionAsync` task.
- Produces: content factories that return a valid WPF tree on an STA thread; the Stop menu event consumes failures and reports a notification instead of throwing through `async void`.

- [ ] **Step 1: Write failing STA content-factory tests.** Add a `RunOnSta` test that constructs a `FlowCanvas`, `StereoCameraNodeModel`, and `FlowImagePreviewNodeModel`, invokes both content factories, and asserts each returns a `FrameworkElement` without `InvalidOperationException`. Add a source-level assertion that `MainWindow.MenuStop_Click` has a catch boundary or call to a dedicated safe handler.
- [ ] **Step 2: Run the focused harness to verify RED.** Run the Windows harness. Expected: the camera and preview factory test fails with the logical-child-already-parented WPF exception; the source assertion fails because `MenuStop_Click` directly awaits the task.
- [ ] **Step 3: Fix parsed-root ownership.** In each constructor, save `root.Content` to a local, set `root.Content = null`, assign the detached element to `Content`, then perform the existing `FindName` lookups on `root`. Do not duplicate the XAML or change the node model.
- [ ] **Step 4: Add the final `async void` boundary.** Wrap `MenuStop_Click` in `try/catch (Exception)`; log through `FlowEditor`’s existing stop path and show a short `NotificationService` message. Keep `FlowPage.StopExecutionAsync` rethrowing for task callers and keep the window-closing catch unchanged.
- [ ] **Step 5: Run focused WPF tests GREEN.** Re-run the content and source tests, then run all existing preview/plugin tests.
- [ ] **Step 6: Review and commit.** Run `git diff --check`; commit `fix(ui): detach plugin content and contain stop errors`.

### Task 3: Preserve startup exceptions and test native image-handle ownership

**Files:**
- Modify: `NodeCraft.Vision.StereoCamera/Camera/StereoCameraCaptureSession.cs:141-197`
- Modify: `NodeCraft.Vision.StereoCamera/Camera/VendorStereoCameraDevice.cs:232-267`
- Modify: `NodeCraft.Vision.StereoCamera/VendorInterop/StereoCameraSafeHandles.cs`
- Modify: `NodeCraft.Vision.StereoCamera/VendorInterop/NativeMethods.cs`
- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/IStereoCameraFrameApi.cs`
- Modify: `NodeCraft.Tests/StereoCameraCaptureTests.cs`
- Create: `NodeCraft.Tests/VendorStereoCameraDeviceTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**
- Consumes: production C API calls through `NativeFrameApi` and `StereoCameraSafeHandleBase`.
- Produces: an internal `IStereoCameraFrameApi` seam with `GetFrame`, frame metadata, image metadata/data, and `ReleaseHandle`; production uses `NativeFrameApi`, tests use a handle-counting fake.

The seam has this exact shape (the production adapter forwards each member to the same-name `NativeMethods` entry point):

```csharp
internal interface IStereoCameraFrameApi
{
    IntPtr GetFrame(IntPtr camera, uint timeoutMilliseconds);
    ulong GetFrameId(IntPtr frame);
    ulong GetFrameTimestamp(IntPtr frame);
    IntPtr GetFrameImage(IntPtr frame, ScImageType type);
    int GetImageWidth(IntPtr image);
    int GetImageHeight(IntPtr image);
    ScPixelFormat GetImagePixelFormat(IntPtr image);
    uint GetImageDataSize(IntPtr image);
    IntPtr GetImageData(IntPtr image);
    bool ReleaseHandle(IntPtr handle);
}
```

- [ ] **Step 1: Write the failing startup-exception test.** Add a capture-session fixture whose device throws from `Connect` and whose runtime scope throws from `Dispose`. Assert `StartAsync` exposes the original connect exception, while the logger receives the cleanup exception.
- [ ] **Step 2: Write the failing handle tests.** Add a fake `IStereoCameraFrameApi` that returns frame/color/depth pointers, records `ReleaseHandle`, and can throw during the depth acquisition or color image conversion. Instantiate `VendorStereoCameraDevice` with a test camera handle and fake API; assert every nonzero frame/image handle is released exactly once for success and for each injected failure.
- [ ] **Step 3: Run focused tests to verify RED.** Run the Windows harness. Expected: startup observes an `AggregateException` from cleanup instead of the original connect error, and one raw image pointer remains unreleased in the injected failure cases.
- [ ] **Step 4: Add the native frame seam.** Define `IStereoCameraFrameApi` with the exact frame/image methods used by `TryGetFrame`; implement `NativeFrameApi` by forwarding to `NativeMethods`. Add a SafeHandle constructor overload that accepts a release delegate for tests while preserving the default production `scReleaseHandle` path.
- [ ] **Step 5: Wrap each pointer immediately.** Change `TryGetFrame` to acquire and immediately create the frame handle, acquire the color pointer and immediately create its image handle, then acquire/wrap depth; pass handles into `ReadRawImage`. Ensure all handles are in `using` scopes before any later native call or conversion.
- [ ] **Step 6: Preserve the primary startup exception.** In `StartCoreAsync` catch, call `CleanupAsync` inside a nested try/catch, log cleanup failures, then `ExceptionDispatchInfo.Capture(primary).Throw()` (or an equivalent `throw;` structure that cannot be replaced by cleanup).
- [ ] **Step 7: Run focused tests GREEN and all camera tests.** Confirm handle counts, primary exception identity/message, normal camera startup, latest-frame behavior, and reverse cleanup all pass.
- [ ] **Step 8: Review and commit.** Run `git diff --check`; commit `fix(camera): close native handles and preserve startup failures`.

### Task 4: Make camera IP persistent configuration instead of a DAG input

**Files:**
- Modify: `NodeCraft.Vision.StereoCamera/Plugin/StereoCameraPlugin.cs:65-105`
- Modify: `NodeCraft.Vision.StereoCamera/Nodes/StereoCameraNodeModel.cs:10-37`
- Modify: `NodeCraft.Flow/Flow/GraphModelLinkReconciler.cs`
- Modify: `NodeCraft.Tests/StereoCameraPluginTests.cs`
- Modify: `NodeCraft.Tests/StereoCameraIntegrationTests.cs`

**Interfaces:**
- Consumes: `StereoCameraNodeModel.WriteWorkflowInputs` and the serialized `WorkflowNode.Inputs["ipAddress"]` value.
- Produces: no connectable camera IP socket; a persisted IP still reaches `StereoCameraExecutor` before session startup; old links to the configuration key produce a validation/reconciliation error.

- [ ] **Step 1: Write failing registration and linked-IP tests.** Assert the camera definition has no `ipAddress` input port and the node model has no runtime input parameter, while `WriteWorkflowInputs` still writes the IP. Build a graph with a `StringValueNodeModel` linked to the camera and assert conversion/validation reports a configuration-link error instead of allowing the graph to start.
- [ ] **Step 2: Run focused tests to verify RED.** Run the Windows harness. Expected: the camera still exposes a connectable IP port and linked-IP validation succeeds.
- [ ] **Step 3: Remove the runtime port and reject legacy config links.** Remove `ipAddress` from the plugin definition’s `InputPorts` and from `StereoCameraNodeModel.InputParameters`. Leave the persisted workflow key untouched so `StereoCameraExecutor.StartSessionAsync` continues reading it. A legacy graph link targeting the former slot must fail in the existing `GraphModelLinkReconciler` unknown-target-slot path before workflow execution; do not add a plugin-to-core dependency merely to customize that error.
- [ ] **Step 4: Run focused plugin, graph, and camera tests GREEN.** Confirm normal persisted IP startup still passes and linked configuration is rejected before `StartSessionAsync` is called.
- [ ] **Step 5: Review and commit.** Run `git diff --check`; commit `fix(camera): keep ip address out of runtime links`.

### Task 5: Make latest-only preview worker ownership and UI application race-safe

**Files:**
- Modify: `NodeCraft.Vision.StereoCamera/Preview/LatestPreviewRenderQueue.cs:45-143`
- Modify: `NodeCraft.Vision.StereoCamera/Views/FlowImagePreviewView.xaml.cs:70-105`
- Modify: `NodeCraft.Tests/FlowImagePreviewTests.cs`

**Interfaces:**
- Consumes: `LatestPreviewRenderQueue.Submit`, `LatestVersion`, and the view’s dispatcher callback.
- Produces: one active worker generation at a time, one pending image, and no stale bitmap mutation after a newer version is submitted.

- [ ] **Step 1: Write failing queue handoff and late-apply tests.** Add a worker test that blocks the old worker after it observes no pending item, submits a new image during that handoff, and asserts no overlapping workers and `DrainAsync` tracks the active worker. Add a dispatcher test where the old apply callback is held, a newer version is submitted, then release the callback and assert only the newer version mutates the preview node.
- [ ] **Step 2: Run focused preview tests to verify RED.** Run the Windows harness. Expected: the old worker clears the new worker reference and the old UI result can be applied after a newer submission.
- [ ] **Step 3: Add worker generation ownership.** Allocate a monotonically increasing worker generation when starting a worker. Pass that generation into `ProcessAsync`; in both the empty-pending path and `finally`, clear `_workerTask` only if the current generation still matches. Keep `_pending` latest-only and preserve disposal cancellation.
- [ ] **Step 4: Add the dispatcher-side version check.** In `ApplyRenderResultAsync`, check `_unloaded` and `version == _renderQueue.LatestVersion` immediately before changing `_node`, `_previewImage`, or status text inside the dispatcher callback.
- [ ] **Step 5: Run focused and full preview tests GREEN.** Confirm normal stale completion behavior, worker handoff, late dispatcher application, unload cleanup, and image conversion all pass.
- [ ] **Step 6: Review and commit.** Run `git diff --check`; commit `fix(preview): serialize latest render worker ownership`.

### Task 6: Reduce per-frame logging and bound UI error text

**Files:**
- Modify: `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs:21-77`
- Modify: `NodeCraft/Pages/FlowPage.xaml.cs:100-195,615-619`
- Create: `NodeCraft/Execution/ExecutionErrorFormatter.cs`
- Modify: `NodeCraft.Tests/Program.cs`
- Create: `NodeCraft.Tests/ExecutionErrorFormatterTests.cs`

**Interfaces:**
- Consumes: `ILogger` iteration logging and `FlowPage` exception reporting.
- Produces: `ExecutionErrorFormatter.Format(string, Exception, int)` returning a bounded message without stack text; iteration diagnostics emitted at `Trace`.

- [ ] **Step 1: Write failing logging and formatter tests.** Assert the runner source uses `LogTrace` for the three per-iteration messages. Add formatter tests for a nested exception, 512-character truncation, and absence of `System.Exception.ToString()`/stack text in the returned user message.
- [ ] **Step 2: Run focused tests to verify RED.** Run the Windows harness. Expected: source assertion finds `LogInformation` in the iteration runner and formatter tests cannot compile because the helper does not exist.
- [ ] **Step 3: Lower iteration logs.** Change only the per-iteration start/finish and per-node execution/skip diagnostics to `LogTrace`; leave error logs and graph validation information logs unchanged.
- [ ] **Step 4: Add and use the formatter.** Implement `ExecutionErrorFormatter.Format` with a 512-character cap, stage prefix, and the innermost exception message; update all FlowPage result-panel exception paths (save/load/validate/stop/run) to log full exceptions but assign the formatted text instead of `ToString()`.
- [ ] **Step 5: Run formatter and full application tests GREEN.** Confirm source/runtime assertions and existing flow controller tests pass.
- [ ] **Step 6: Review and commit.** Run `git diff --check`; commit `fix(ui): reduce frame logs and hide exception stacks`.

### Task 7: Full verification, packaging, and review checklist

**Files:**
- Modify only if verification exposes a regression; otherwise no source changes.
- Verify: `NodeCraft.sln`, `artifacts/NodeCraft-stereo-camera-win-x64.zip`, and the hardware acceptance checklist.

- [ ] Run `git status --short --branch` and `git diff --check`; confirm only intended commits/files exist and no vendor binaries are tracked.
- [ ] Run the complete Windows harness: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug`; record the final pass count and zero failures.
- [ ] Run the CLI test suite and any repository-prescribed Windows build checks; do not substitute Linux output for WindowsDesktop verification.
- [ ] Publish self-contained Windows x64, stage the plugin with `LibStereoCamera.dll` and SDK runtime files, and ensure the plugin subtree contains no `StereoCamera.Net.dll`, duplicate `NLog.dll`, `NodeCraft.Flow.dll`, or `CommonControls.WPF.dll`.
- [ ] Run `unzip -t artifacts/NodeCraft-stereo-camera-win-x64.zip`, compute SHA-256, and compare the archive hash after copying it to the supplied Windows test host.
- [ ] Update `docs/testing/stereo-camera-hardware-acceptance.md` only with observed Windows/hardware results; do not claim real-camera validation without a camera.
- [ ] Review the nine-item checklist against the final diff, then commit any packaging/documentation-only change with `chore: verify stereo camera review fixes`.
- [ ] Report exact verification commands and outcomes, remaining hardware-only validation, package path, and hash; do not report completion if any command failed.
