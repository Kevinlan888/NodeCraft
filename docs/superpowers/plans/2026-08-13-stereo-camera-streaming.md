# Stereo Camera Streaming Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add run-once and continuous graph sessions to NodeCraft, plus a Windows-x64 stereo-camera plugin that publishes the latest synchronized color/depth frame and calibration data to a persistent FlowImage preview node.

**Architecture:** `NodeCraft.Flow` owns immutable visual contracts and a reusable `GraphExecutionSession`; the WPF host owns mutually exclusive run controls and UI dispatch; `NodeCraft.Vision.StereoCamera` owns the embedded .NET 8 C API interop, camera lifecycle, one-slot latest-frame mailbox, node models, and preview renderer. A camera executor is both a session lifecycle participant and an iteration source, so one-shot execution acquires one complete frame and cleans up, while continuous execution reuses one camera connection and serially runs the full DAG for each selected latest frame.

**Tech Stack:** C# 9, .NET 8 (`net8.0-windows`), WPF, Microsoft.Extensions.Logging, Cdecl P/Invoke/SafeHandle, MSBuild, the repository's console-based `NodeCraft.Tests` harness.

## Global Constraints

- Treat [the approved design](../specs/2026-08-13-stereo-camera-streaming-design.md) as the source of truth. If implementation pressure suggests changing behavior, stop and amend/re-approve the design first.
- Target only Windows x64 for the host process and camera plugin. Keep `NodeCraft.Flow` platform-neutral apart from its existing WPF target.
- Do not reference, copy, load, or commit `StereoCamera.Net.dll`; migrate only the required C API surface into the plugin.
- Do not commit any vendor binaries, CTI files, configuration files, or license copies. They enter only an ignored `artifacts/` package through an explicit packaging target.
- Ordinary `dotnet build NodeCraft.sln` and all fake-camera tests must work on a Windows-x64 development machine without `StereoCameraSdkRoot` and without camera hardware.
- Run every command that builds or executes `NodeCraft`, `NodeCraft.Flow`, the plugin, or `NodeCraft.Tests` on Windows with the WindowsDesktop SDK installed. The repository README documents that the current Linux environment lacks `Microsoft.NET.Sdk.WindowsDesktop`; use Linux only for static/source audits and do not disguise that environment limitation by claiming WPF verification passed.
- Preserve C# 9 and the repository's nullable settings. Do not introduce `System.Drawing` or `System.Drawing.Common`.
- Keep every graph iteration serial. Do not queue historical camera frames or Dispatcher work.
- Use `apply_patch` for source edits. Before every commit run `git diff --check`, and stage only files named by that task.
- The existing tests are an executable harness, not xUnit. Add focused partial `Program` files and invoke each suite from `Main`; use `Run`/`RunAsync` for assertions.

## File Structure Map

### Existing files to modify

- `NodeCraft.Flow/Flow/FlowSchema.cs` — register `image` and `camera-calibration` types.
- `NodeCraft.Flow/Flow/FlowRuntime.cs` — keep the existing executor API; session contracts live beside it in a new file.
- `NodeCraft.Flow/Flow/GraphExecutor.cs` — retain validation and expose the compatibility session wrapper.
- `NodeCraft.Flow/Flow/FlowNodeRegistry.cs` — add the persistent-content refresh policy and Vision icons.
- `NodeCraft/NodeCraft.csproj` — force the WPF host process to x64.
- `NodeCraft/Pages/FlowPage.xaml` and `.xaml.cs` — execution overlay, session commands, serial UI result application, compact image summaries.
- `NodeCraft/MainWindow.xaml` and `.xaml.cs` — Run Once / Run Continuously / Stop commands, command state, awaited close.
- `NodeCraft.Tests/Program.cs` — make `Program` partial and call the new focused test suites.
- `NodeCraft.Tests/NodeCraft.Tests.csproj` — reference the new plugin for hardware-free internal tests.
- `NodeCraft.sln` — include the new plugin project.

### New core and host files

- `NodeCraft.Flow/Flow/Visual/FlowImage.cs`
- `NodeCraft.Flow/Flow/Visual/CameraCalibration.cs`
- `NodeCraft.Flow/Flow/Visual/FlowPixelFormat.cs`
- `NodeCraft.Flow/Flow/Visual/FlowImageKind.cs`
- `NodeCraft.Flow/Flow/FlowSessionContracts.cs`
- `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`
- `NodeCraft/Execution/FlowExecutionController.cs`
- `NodeCraft/Execution/FlowRunState.cs`

### New plugin files

- `NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj`
- `NodeCraft.Vision.StereoCamera/plugin.json`
- `NodeCraft.Vision.StereoCamera/Properties/AssemblyInfo.cs`
- `NodeCraft.Vision.StereoCamera/Build/StereoCameraPackaging.targets`
- `NodeCraft.Vision.StereoCamera/Build/StereoCameraRuntimeFiles.txt`
- `NodeCraft.Vision.StereoCamera/VendorInterop/NativeMethods.cs`
- `NodeCraft.Vision.StereoCamera/VendorInterop/NativeEnums.cs`
- `NodeCraft.Vision.StereoCamera/VendorInterop/NativeStructs.cs`
- `NodeCraft.Vision.StereoCamera/VendorInterop/StereoCameraSafeHandles.cs`
- `NodeCraft.Vision.StereoCamera/VendorInterop/StereoCameraNativeException.cs`
- `NodeCraft.Vision.StereoCamera/Runtime/NativeRuntimeScope.cs`
- `NodeCraft.Vision.StereoCamera/Camera/CameraSdkAbstractions.cs`
- `NodeCraft.Vision.StereoCamera/Camera/VendorStereoCameraDevice.cs`
- `NodeCraft.Vision.StereoCamera/Camera/LatestFrameMailbox.cs`
- `NodeCraft.Vision.StereoCamera/Camera/FrameBundle.cs`
- `NodeCraft.Vision.StereoCamera/Camera/StereoCameraCaptureSession.cs`
- `NodeCraft.Vision.StereoCamera/Nodes/StereoCameraExecutor.cs`
- `NodeCraft.Vision.StereoCamera/Nodes/StereoCameraNodeModel.cs`
- `NodeCraft.Vision.StereoCamera/Nodes/FlowImagePreviewExecutor.cs`
- `NodeCraft.Vision.StereoCamera/Nodes/FlowImagePreviewNodeModel.cs`
- `NodeCraft.Vision.StereoCamera/Plugin/StereoCameraPlugin.cs`
- `NodeCraft.Vision.StereoCamera/Views/StereoCameraEditor.xaml` and `.xaml.cs`
- `NodeCraft.Vision.StereoCamera/Views/FlowImagePreviewView.xaml` and `.xaml.cs`
- `NodeCraft.Vision.StereoCamera/Preview/FlowImageBitmapConverter.cs`
- `NodeCraft.Vision.StereoCamera/Preview/LatestPreviewRenderQueue.cs`
- `NodeCraft.Vision.StereoCamera/Preview/PreviewRenderResult.cs`

### New focused test files

- `NodeCraft.Tests/VisualContractTests.cs`
- `NodeCraft.Tests/GraphExecutionSessionTests.cs`
- `NodeCraft.Tests/FlowExecutionControllerTests.cs`
- `NodeCraft.Tests/StereoCameraProjectTests.cs`
- `NodeCraft.Tests/VendorInteropTests.cs`
- `NodeCraft.Tests/StereoCameraPackagingTests.cs`
- `NodeCraft.Tests/LatestFrameMailboxTests.cs`
- `NodeCraft.Tests/StereoCameraCaptureTests.cs`
- `NodeCraft.Tests/StereoCameraPluginTests.cs`
- `NodeCraft.Tests/FlowImagePreviewTests.cs`
- `NodeCraft.Tests/StereoCameraIntegrationTests.cs`
- `docs/testing/stereo-camera-hardware-acceptance.md`

---

## Task 1: Add immutable public image and calibration contracts

**Files:**

- Create: `NodeCraft.Flow/Flow/Visual/FlowPixelFormat.cs`
- Create: `NodeCraft.Flow/Flow/Visual/FlowImageKind.cs`
- Create: `NodeCraft.Flow/Flow/Visual/CameraCalibration.cs`
- Create: `NodeCraft.Flow/Flow/Visual/FlowImage.cs`
- Modify: `NodeCraft.Flow/Flow/FlowSchema.cs`
- Create: `NodeCraft.Tests/VisualContractTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Make `Program` a `partial` static class and call `RunVisualContractTests()` near the start of `Main`.

- [ ] Write failing contract tests covering defensive copies, exact matrix lengths, owned-buffer identity, stride/data validation, supported formats, and the two `FlowDataType` keys. Use concrete assertions such as:

```csharp
private static void RunVisualContractTests()
{
    Run("camera calibration defensively copies its matrices", () =>
    {
        var intrinsic = Enumerable.Range(1, 9).Select(value => (double)value).ToArray();
        var calibration = new CameraCalibration(
            640,
            480,
            intrinsic,
            new double[12],
            new double[16],
            isLeftReference: false);

        intrinsic[0] = 999;
        return calibration.Intrinsic.Span[0] == 1
            && calibration.ImageWidth == 640
            && calibration.ImageHeight == 480
            && !calibration.IsLeftReference;
    });

    Run("FlowImage copy and ownership factories have distinct copy behavior", () =>
    {
        var calibration = CreateTestCalibration(2, 1);
        var copiedSource = new byte[] { 1, 2, 3, 4, 5, 6 };
        var copied = FlowImage.CopyFrom(
            2, 1, 6, FlowPixelFormat.Bgr24, FlowImageKind.Color,
            copiedSource, 7, 8, DateTimeOffset.UnixEpoch, calibration);
        copiedSource[0] = 42;

        var ownedSource = new byte[] { 7, 8, 9, 10 };
        var owned = FlowImage.FromOwnedBuffer(
            2, 1, 4, FlowPixelFormat.Depth16, FlowImageKind.Depth,
            ownedSource, 9, 10, DateTimeOffset.UnixEpoch, calibration);
        MemoryMarshal.TryGetArray(owned.Buffer, out var ownedSegment);

        return copied.Buffer.Span[0] == 1
            && ReferenceEquals(ownedSource, ownedSegment.Array)
            && FlowDataType.Image.Key == "image"
            && FlowDataType.CameraCalibration.Key == "camera-calibration";
    });
}
```

- [ ] Run `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug`. Expected: compilation fails because the visual contracts and data types do not exist.

- [ ] Add `FlowPixelFormat` with exactly `Bgr24`, `Rgb24`, `Mono8`, and `Depth16`; add `FlowImageKind` with `Unknown`, `Color`, and `Depth`.

- [ ] Implement `CameraCalibration` as a sealed class with this public shape:

```csharp
public sealed class CameraCalibration
{
    public CameraCalibration(
        int imageWidth,
        int imageHeight,
        IReadOnlyList<double> intrinsic,
        IReadOnlyList<double> distortion,
        IReadOnlyList<double> extrinsic,
        bool isLeftReference);

    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public ReadOnlyMemory<double> Intrinsic { get; }
    public ReadOnlyMemory<double> Distortion { get; }
    public ReadOnlyMemory<double> Extrinsic { get; }
    public bool IsLeftReference { get; }
}
```

Validate positive dimensions and exact lengths 9/12/16, clone all three input lists, and throw `ArgumentOutOfRangeException` or `ArgumentException` with the bad parameter name.

- [ ] Implement `FlowImage` as a sealed class. Provide the two complete factory signatures below and funnel both through one private validator. Expose `ReadOnlyMemory<byte> Buffer`, dimensions, stride, format/kind, `FrameId`, raw `DeviceTimestamp`, `CapturedAtUtc`, and `Calibration`.

```csharp
public static FlowImage CopyFrom(
    int width, int height, int stride,
    FlowPixelFormat pixelFormat, FlowImageKind kind,
    ReadOnlySpan<byte> buffer,
    ulong frameId, ulong deviceTimestamp,
    DateTimeOffset capturedAtUtc,
    CameraCalibration calibration);

public static FlowImage FromOwnedBuffer(
    int width, int height, int stride,
    FlowPixelFormat pixelFormat, FlowImageKind kind,
    byte[] buffer,
    ulong frameId, ulong deviceTimestamp,
    DateTimeOffset capturedAtUtc,
    CameraCalibration calibration);
```

- [ ] In the shared validator, use checked multiplication for `Stride * Height`, require exact buffer length, and derive minimum row bytes from the pixel format (`width * 3`, `width`, or `width * 2`). Reject enum values outside the four supported formats. Do not require calibration dimensions to equal image dimensions because SDK calibration resolution can differ from a delivered stream.

- [ ] Add these singletons to `FlowDataType` and recognize their keys in `FromLegacyTypeName`:

```csharp
public static readonly FlowDataType Image = new FlowDataType("image", typeof(FlowImage));
public static readonly FlowDataType CameraCalibration =
    new FlowDataType("camera-calibration", typeof(NodeCraft.Flow.CameraCalibration));
```

- [ ] Re-run `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug`. Expected: all existing and new contract tests pass.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(flow): add visual data contracts"`.

## Task 2: Add session lifecycle contracts and deterministic start/stop

**Files:**

- Create: `NodeCraft.Flow/Flow/FlowSessionContracts.cs`
- Create: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutor.cs`
- Create: `NodeCraft.Tests/GraphExecutionSessionTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Call `await RunGraphExecutionSessionLifecycleTestsAsync()` from `Main`, then add recording executor fixtures that implement `IFlowNodeExecutor` and optionally the new lifecycle interface.

- [ ] Write failing tests for: executor factory called once per node; start order follows `TopologicalSort`; stop order is reversed; a failure starting node B stops only already-started node A; `StopAsync` and `DisposeAsync` are idempotent.

```csharp
await RunAsync("session starts topologically and stops in reverse", async () =>
{
    var calls = new List<string>();
    var fixture = CreateLifecycleGraph(calls);
    await using var session = fixture.Executor.CreateSession();

    await session.StartAsync(CancellationToken.None);
    await session.StopAsync();
    await session.StopAsync();

    return calls.SequenceEqual(new[]
    {
        "create:a", "create:b", "start:a", "start:b", "stop:b", "stop:a",
    });
});
```

- [ ] Run the test executable. Expected: compilation fails because session types and `CreateSession` do not exist.

- [ ] Add these contracts without changing `IFlowNodeExecutor`:

```csharp
public sealed class FlowNodeSessionContext
{
    public WorkflowNode Node { get; }
    public FlowNodeDefinition Definition { get; }
    public ILogger Logger { get; }
}

public interface IFlowNodeSessionLifecycle
{
    Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
    Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
}

public interface IFlowIterationSource
{
    Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
}

public enum GraphExecutionSessionState
{
    Created, Starting, Running, Faulted, Stopping, Stopped,
}
```

- [ ] Add `GraphExecutor.CreateSession()`. It must run `Validate()`, throw the same aggregate validation message as the old `ExecuteAsync`, obtain the topological list once, and construct `GraphExecutionSession` with the workflow, registry, ordered nodes, and logger.

- [ ] In `GraphExecutionSession`, create and cache every executor exactly once by workflow node ID. Store a `FlowNodeSessionContext` beside it. Reject duplicate node IDs before creating any executor.

- [ ] Implement `StartAsync`: transition `Created -> Starting -> Running`; call lifecycle executors in topological order; append to an `_startedLifecycles` list only after a successful start. On failure, retain the primary exception, set `Faulted`, run best-effort cleanup, and rethrow the original exception with its stack.

- [ ] Give the session an internal stop `CancellationTokenSource`. Implement `StopAsync` with a single cached stop task so concurrent/repeated calls share one cleanup; signal the internal token before cleanup. Stop only successfully started lifecycle nodes, in reverse order, using `CancellationToken.None`; log every cleanup failure. Throw an aggregate cleanup exception only when no primary failure or user cancellation already explains termination.

- [ ] Implement `DisposeAsync` by awaiting `StopAsync` once and disposing internal synchronization primitives after the stop task completes.

- [ ] Re-run the test executable. Expected: lifecycle, failure cleanup, and idempotency tests pass; existing executor tests remain green.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(flow): add graph session lifecycle"`.

## Task 3: Execute fresh serial iterations and preserve one-shot compatibility

**Files:**

- Create: `NodeCraft.Flow/Flow/FlowGraphIterationRunner.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutionSession.cs`
- Modify: `NodeCraft.Flow/Flow/GraphExecutor.cs`
- Modify: `NodeCraft.Tests/GraphExecutionSessionTests.cs`

- [ ] Add failing tests for a new context per iteration, source preparation before every iteration, no overlap under two concurrent callers, `StopAsync` canceling a blocked source before resource cleanup, downstream exception status/faulted state, and the compatibility wrapper performing exactly one iteration plus cleanup.

```csharp
await RunAsync("each session iteration waits for sources and returns a fresh context", async () =>
{
    var fixture = CreateIterationSourceGraph();
    await using var session = fixture.Executor.CreateSession();
    await session.StartAsync(CancellationToken.None);

    var first = await session.ExecuteIterationAsync(CancellationToken.None);
    var second = await session.ExecuteIterationAsync(CancellationToken.None);

    return !ReferenceEquals(first, second)
        && fixture.Source.PrepareCount == 2
        && fixture.Source.MaxConcurrentExecutions == 1;
});
```

- [ ] Run the test executable. Expected: compilation or assertions fail because iteration execution is not implemented.

- [ ] Move the current per-node execution behavior—input resolution, control activation, required-runtime-input skip, status updates, output-slot assignment, and logging—into `FlowGraphIterationRunner.ExecuteAsync(IReadOnlyList<WorkflowNode>, IReadOnlyDictionary<string, IFlowNodeExecutor>, FlowNodeRegistry, FlowExecutionContext, ILogger, CancellationToken)`. Do not change its observable semantics.

- [ ] Add an iteration `SemaphoreSlim` to `GraphExecutionSession`. `ExecuteIterationAsync` must require `Running`, acquire the semaphore, re-check state, link the caller token with the session stop token, sequentially call every cached `IFlowIterationSource.PrepareIterationAsync`, create a new `FlowExecutionContext`, run the complete DAG through `FlowGraphIterationRunner`, and release the semaphore in `finally`.

- [ ] On a source or node failure other than cancellation caused by `StopAsync`, retain the primary exception, set the session to `Faulted`, and rethrow; no later iteration may start. Update stop cleanup to wait for the iteration semaphore after signaling the session token, so lifecycle resources cannot be torn down while an executor is still using them.

- [ ] Expose `HasIterationSources` as a read-only boolean computed from cached executors. Do not add delays inside the session; the host controller owns the ordinary-graph 10 ms guard.

- [ ] Replace `GraphExecutor.ExecuteAsync` with the compatibility wrapper:

```csharp
public async Task<FlowExecutionContext> ExecuteAsync(CancellationToken cancellationToken = default)
{
    await using var session = CreateSession();
    try
    {
        await session.StartAsync(cancellationToken);
        return await session.ExecuteIterationAsync(cancellationToken);
    }
    finally
    {
        await session.StopAsync();
    }
}
```

Preserve the primary start/execute exception if cleanup also fails; cover that precedence in a test.

- [ ] Re-run the test executable. Expected: all session tests and all pre-existing `GraphExecutor.ExecuteAsync` tests pass.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(flow): execute reusable graph iterations"`.

## Task 4: Add a testable host execution controller

**Files:**

- Create: `NodeCraft/Execution/FlowRunState.cs`
- Create: `NodeCraft/Execution/FlowExecutionController.cs`
- Create: `NodeCraft.Tests/FlowExecutionControllerTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Call `await RunFlowExecutionControllerTestsAsync()` from `Main` and write failing tests for state transitions, mutual exclusion, serial result callbacks, continuous cancellation, and the 10 ms ordinary-flow delay.

- [ ] Use a real `GraphExecutionSession` with fake executors in tests. For a source-driven graph, block/release `PrepareIterationAsync` with `TaskCompletionSource`; for an ordinary graph, inject a delay delegate so the test can count guard-delay calls without sleeping.

- [ ] Run the test executable. Expected: compilation fails because the host controller is absent.

- [ ] Add exactly these host states:

```csharp
internal enum FlowRunState
{
    Idle,
    Starting,
    RunningOnce,
    RunningContinuous,
    Stopping,
}
```

- [ ] Implement `FlowExecutionController` with `RunOnceAsync`, `RunContinuouslyAsync`, and `StopAsync`. Accept a prepared session and an awaited callback of shape `Func<FlowExecutionContext, long, TimeSpan, Task>`. Store one active run task and one linked `CancellationTokenSource` under a lock.

- [ ] Run lifecycle and DAG work on the thread pool. In continuous mode, await the result callback before the next iteration. Increment iteration numbers only after a context is produced. If `session.HasIterationSources` is false, await the injected/default 10 ms delay after the callback.

- [ ] Treat cancellation caused by `StopAsync` as a normal stop, but propagate other exceptions after setting `LastError`. In every path, transition through `Stopping`, await session stop/disposal, clear active fields, and return to `Idle`. Raise a state-changed event after each visible transition.

- [ ] Make concurrent starts fail immediately with a clear `InvalidOperationException`; make repeated `StopAsync` await the same active task and otherwise no-op.

- [ ] Re-run the test executable. Expected: state, serial callback, cancellation, and guard-delay tests pass.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(host): add flow execution controller"`.

## Task 5: Wire run controls, read-only UI, summaries, and awaited shutdown

**Files:**

- Modify: `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`
- Modify: `NodeCraft/Pages/FlowPage.xaml`
- Modify: `NodeCraft/Pages/FlowPage.xaml.cs`
- Modify: `NodeCraft/MainWindow.xaml`
- Modify: `NodeCraft/MainWindow.xaml.cs`
- Modify: `NodeCraft/NodeCraft.csproj`
- Modify: `NodeCraft.Tests/FlowExecutionControllerTests.cs`

- [ ] Add failing tests/source assertions for four Flow menu items, a transparent canvas blocker, disabled graph-mutation commands during a run, an awaited `Closing` handler, x64 host target, and a registration that can suppress content reconstruction after execution.

- [ ] Run the test executable. Expected: the new UI/registration assertions fail.

- [ ] Add `RefreshContentAfterExecution` to `FlowNodeRegistration`, defaulting to `true`. Add `FlowNodeRegistry.ShouldRefreshContentAfterExecution(NodeModel)` and keep `ApplyExecutionResults` responsible only for invoking handlers and returning updated models.

- [ ] Replace `FlowPage.RunGraph()` with task-returning `RunOnceAsync()`, `RunContinuouslyAsync()`, and `StopExecutionAsync()`. Validate/build the workflow on the UI thread, create a session, then delegate to `FlowExecutionController`.

- [ ] In the controller result callback, use `Dispatcher.InvokeAsync` and await it. Call `ApplyExecutionResults`, refresh only registrations whose policy is `true`, and update the result text before allowing the next iteration. Include run mode, iteration number, elapsed time, node statuses, compact outputs, and concise errors in the panel.

- [ ] Keep all three task-returning FlowPage methods inside one exception-reporting boundary: log the full exception, render a concise user message, and treat controller-requested cancellation as “已停止” rather than failure. The MainWindow event handlers may be `async void`, but they must only await these task-returning methods.

- [ ] Special-case visual outputs before the generic enumerable branch in `FormatValue`:

```csharp
if (value is FlowImage image)
{
    return $"{image.Kind} {image.Width}x{image.Height} {image.PixelFormat}, frame {image.FrameId}";
}

if (value is CameraCalibration calibration)
{
    return $"Calibration {calibration.ImageWidth}x{calibration.ImageHeight}, left-reference={calibration.IsLeftReference}";
}
```

Never enumerate `FlowImage.Buffer`, calibration arrays, or a WPF bitmap.

- [ ] Change the canvas column to a `Grid` containing the existing `CanvasHost` and an `ExecutionInputBlocker` border above it. The blocker must use `Background="Transparent"`, switch `Visibility`, and leave the preview visuals enabled.

- [ ] Name all file/flow menu items. Replace “执行” with “执行一次”, add “持续运行” and “停止”, and have `MainWindow` subscribe to controller state changes to update `IsEnabled`: mutation/start commands only in Idle, Stop only outside Idle.

- [ ] Add an `async void` `Closing` event entry point only. On first close with active work, set `e.Cancel = true`, await `FlowEditor.StopExecutionAsync()` inside `try`, and in `finally` set an `_allowClose` guard and call `Close()` again so a reported cleanup error cannot trap the window open. Keep notification cleanup in `Closed`.

- [ ] Add `<PlatformTarget>x64</PlatformTarget>` and `<Prefer32Bit>false</Prefer32Bit>` to `NodeCraft/NodeCraft.csproj`; do not add an RID to `NodeCraft.Flow`.

- [ ] Re-run the test executable and `dotnet build NodeCraft.sln -c Debug`. Expected: all tests pass and the host builds as x64.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(host): expose continuous flow controls"`.

## Task 6: Create the Windows-x64 camera plugin project foundation

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj`
- Create: `NodeCraft.Vision.StereoCamera/Properties/AssemblyInfo.cs`
- Create: `NodeCraft.Vision.StereoCamera/plugin.json`
- Modify: `NodeCraft.sln`
- Modify: `NodeCraft.Tests/NodeCraft.Tests.csproj`
- Create: `NodeCraft.Tests/StereoCameraProjectTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Add and invoke `RunStereoCameraProjectTests()`. First assert that the project targets `net8.0-windows`, enables WPF, uses C# 9, targets x64, references `NodeCraft.Flow` with `Private="false"`, and has no reference to `StereoCamera.Net` or `System.Drawing.Common`.

- [ ] Run the test executable. Expected: the project-foundation test fails because the project does not exist.

- [ ] Create the SDK-style plugin project with this property core:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <Nullable>disable</Nullable>
  <LangVersion>9.0</LangVersion>
  <PlatformTarget>x64</PlatformTarget>
  <Prefer32Bit>false</Prefer32Bit>
  <RootNamespace>NodeCraft.Vision.StereoCamera</RootNamespace>
</PropertyGroup>
```

Reference only `NodeCraft.Flow` with `Private="false"`. Embed both plugin XAML files when they are added later, following `NodeCraft.PluginSample`'s embedded-XAML pattern.

- [ ] Add `[assembly: InternalsVisibleTo("NodeCraft.Tests")]`. Add the project to `NodeCraft.sln` and add a normal project reference from `NodeCraft.Tests` so internal fake-camera tests resolve the plugin assembly at runtime.

- [ ] Add a manifest with the exact values below. It is acceptable that the entry type is implemented in Task 12; do not stage or load this intermediate package yet.

```json
{
  "id": "nodecraft.vision.stereo-camera",
  "entryAssembly": "NodeCraft.Vision.StereoCamera.dll",
  "entryType": "NodeCraft.Vision.StereoCamera.Plugin.StereoCameraPlugin",
  "apiVersion": "1.0",
  "privateLibraryPath": "lib"
}
```

- [ ] Run `dotnet build NodeCraft.sln -c Debug` and the test executable with no `StereoCameraSdkRoot`. Expected: both succeed and no deployable camera package appears.

- [ ] Run `git diff --check`, then commit: `git commit -m "build: add stereo camera plugin project"`.

## Task 7: Embed the minimum native C API safely

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/NativeEnums.cs`
- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/NativeStructs.cs`
- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/NativeMethods.cs`
- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/StereoCameraSafeHandles.cs`
- Create: `NodeCraft.Vision.StereoCamera/VendorInterop/StereoCameraNativeException.cs`
- Create: `NodeCraft.Tests/VendorInteropTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Re-open `/mnt/kevin/kevin/Downloads/test/app/Development/include/CAPI/CAPI.h` while implementing. Use it as authoritative for enum values, argument order, return types, and structure layout; use decompiled `StereoCamera.Net.dll` behavior only as a cross-check.

- [ ] Add and invoke `RunVendorInteropTests()`. Test `Marshal.SizeOf<ScCameraCalibInfo>() == 416`, `Marshal.SizeOf<ScConnectEventArg>() == 68`, fixed array sizes 9/12/16/28, Cdecl callback attributes, every DllImport's Cdecl/ExactSpelling values, I1 marshaling on every C `bool`, and SafeHandle inheritance.

```csharp
Run("vendor calibration layout matches CAPI.h", () =>
{
    var fields = typeof(ScCameraCalibInfo)
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    return Marshal.SizeOf<ScCameraCalibInfo>() == 416
        && ReadByValArraySize(fields, "Intrinsic") == 9
        && ReadByValArraySize(fields, "Distortion") == 12
        && ReadByValArraySize(fields, "Extrinsic") == 16
        && ReadByValArraySize(fields, "Reserved") == 28;
});
```

- [ ] Run the test executable. Expected: compilation fails because the interop types do not exist.

- [ ] Define only the required enums: `ScError`, `ScInterfaceType`, `ScCameraDataType`, `ScImageType`, and `ScPixelFormat`, preserving the C numeric values.

- [ ] Define `ScConnectEventArg` and `ScCameraCalibInfo` with `LayoutKind.Sequential`; use `[MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]`, 12, 16, and 28 on the intrinsic, distortion, extrinsic, and reserved arrays respectively. Define the disconnect delegate with `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` and pointer arguments that can be checked before marshaling.

- [ ] Add only these native entry points: `scDiscovery`, `scGetCamera`, `scReleaseHandle`, `scConnect`, `scDisconnect`, `scRegisterConnectEvent`, `scUnregisterConnectEvent`, `scStartGrabbing`, `scStopGrabbing`, `scGetFrame`, `scGetFrameID`, `scGetFrameTimestamp`, `scGetFrameImage`, image data/size/width/height/pixel-format getters, `scCreateCalibDataManager`, `scDownloadCalibData`, and `scGetCameraCalibInfo`.

- [ ] Put `CallingConvention.Cdecl` and `ExactSpelling = true` on every import. Use `[return: MarshalAs(UnmanagedType.I1)]` for C bool returns and `[MarshalAs(UnmanagedType.I1)]` for `isLeftReference`. Marshal the IP as `LPStr`; it is validated ASCII IPv4 text before native entry.

- [ ] Implement dedicated camera, frame, image, and calibration-manager SafeHandles, each releasing through `scReleaseHandle`. Keep all interop types `internal`; do not expose a vendor type through any public node port.

- [ ] Add `StereoCameraNativeException.ThrowIfError(operation, errorCode)` so all nonzero codes retain the operation and numeric SDK code.

- [ ] Re-run the test executable. Also run `rg -n "StereoCamera.Net|System.Drawing" NodeCraft.Vision.StereoCamera`. Expected: tests pass and the search has no matches except an intentional packaging exclusion string added later.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): embed stereo camera C API interop"`.

## Task 8: Add process-local native setup and explicit SDK packaging

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Runtime/NativeRuntimeScope.cs`
- Create: `NodeCraft.Vision.StereoCamera/Build/StereoCameraRuntimeFiles.txt`
- Create: `NodeCraft.Vision.StereoCamera/Build/StereoCameraPackaging.targets`
- Modify: `NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj`
- Create: `NodeCraft.Tests/StereoCameraPackagingTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Write tests for resolving `<plugin-root>/lib`, reference-counting acquisition, restoring the previous process `MV_GENICAM_64` value on final release, no registry/system PATH writes, ordinary-build behavior, complete missing-file diagnostics, staged inventory, and exclusions.

- [ ] Make direct Windows API tests conditional on `OperatingSystem.IsWindows()`. Keep manifest/packaging logic independent of hardware and a real SDK by using a temporary fake SDK tree and invoking MSBuild with `StereoCameraPackageRoot` set to a temporary directory; the WPF test harness itself still runs at the Windows verification checkpoint.

- [ ] Run the test executable. Expected: native-runtime and packaging tests fail.

- [ ] Implement `NativeRuntimeScope.Acquire(pluginAssemblyPath)`. Resolve and validate the sibling `lib` directory, call `AddDllDirectory`, set only the process-scoped `MV_GENICAM_64`, hold the directory cookie, and use a static lock/refcount. On final release call `RemoveDllDirectory` and restore the prior process value. Reject non-Windows or non-x64 processes before native loading.

- [ ] Populate `StereoCameraRuntimeFiles.txt` with every file currently supplied in SDK `Runtime/x64` except `StereoCamera.Net.dll` and `NLog.dll`. Include all DLL/CTI files and `SDKLOG_default.properties`; keep one filename per line so tests and MSBuild share one authoritative list.

- [ ] Implement `StereoCameraPackaging.targets` with an explicit `StageStereoCameraPlugin` target only. Read the runtime manifest, add root-level `msvcp120.dll`, `msvcr120.dll`, and `oxylog.toml`, and recursively include `Licenses`. Build one `_MissingStereoCameraSdkFile` item list and fail once with all missing paths in the message.

- [ ] Default the destination to `artifacts/Plugins/NodeCraft.Vision.StereoCamera`, allow tests to override `StereoCameraPackageRoot`, clear only that exact destination on explicit staging, and create:

```text
plugin.json
NodeCraft.Vision.StereoCamera.dll
lib/<manifest files plus VC120 and oxylog.toml>
licenses/<vendor license tree>
```

- [ ] Assert in the target and tests that the package excludes `StereoCamera.Net.dll`, vendor `NLog.dll`, `NodeCraft.Flow.dll`, `CommonControls.WPF.dll`, and Microsoft logging assemblies.

- [ ] Run the test executable, then run a real inventory stage against the supplied local SDK:

```bash
dotnet msbuild NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj -t:StageStereoCameraPlugin -p:Configuration=Debug -p:StereoCameraSdkRoot=/mnt/kevin/kevin/Downloads/test/app
```

Expected: the explicit target succeeds; ordinary solution builds still do not require the property. Do not add anything under `artifacts/` to Git.

- [ ] Run `git diff --check`, then commit only source manifests/targets/tests: `git commit -m "build(camera): stage private native runtime explicitly"`.

## Task 9: Implement the one-slot latest-frame mailbox

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Camera/FrameBundle.cs`
- Create: `NodeCraft.Vision.StereoCamera/Camera/LatestFrameMailbox.cs`
- Create: `NodeCraft.Vision.StereoCamera/Camera/CameraSdkAbstractions.cs`
- Create: `NodeCraft.Tests/LatestFrameMailboxTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Add async tests for: publish 1 then 2 before consumption returns only 2; a consumed sequence is never returned twice; a waiter wakes for a later sequence; fault clears the pending value and faults current/future waiters; completion/cancellation wakes waiters normally.

```csharp
await RunAsync("latest-frame mailbox drops an unconsumed older frame", async () =>
{
    var mailbox = new LatestFrameMailbox<string>();
    mailbox.Publish(1, "old");
    mailbox.Publish(2, "latest");

    var item = await mailbox.WaitForNextAsync(0, CancellationToken.None);
    var duplicate = mailbox.TryTakeAfter(item.Sequence, out _);
    return item.Sequence == 2 && item.Value == "latest" && !duplicate;
});
```

- [ ] Run the test executable. Expected: compilation fails because mailbox/frame types are absent.

- [ ] Add immutable `FrameBundle` containing local `long Sequence`, color/depth `FlowImage`, and color/depth `CameraCalibration`. Validate non-null members, matching color/depth public `FrameId`, and `ReferenceEquals` between each image's embedded calibration and its independent calibration member.

- [ ] Implement `LatestFrameMailbox<T>` under one lock with one pending item and `TaskCompletionSource` created with `RunContinuationsAsynchronously`. Publishing replaces the pending item; taking clears it; the `afterSequence` guard prevents duplicate consumption.

- [ ] On `Fault(Exception)`, clear the pending item before faulting waiters and retain the terminal fault for future waits. On `Complete()`, clear and complete. Never invoke continuations while holding the lock.

- [ ] Define hardware-free interfaces used by capture orchestration:

```csharp
internal interface IStereoCameraDeviceFactory
{
    int Discover();
    IStereoCameraDevice OpenByIp(string ipAddress);
}

internal interface ICameraRuntimeScopeFactory
{
    IDisposable Acquire();
}

internal interface IStereoCameraDevice : IDisposable
{
    void Connect();
    void RegisterDisconnectCallback(Action<Exception> callback);
    void UnregisterDisconnectCallback();
    CameraCalibration ReadCalibration(CameraStream stream, bool isLeftReference);
    void StartGrabbing();
    RawStereoFrame TryGetFrame(uint timeoutMilliseconds);
    void StopGrabbing();
    void Disconnect();
}
```

Add `CameraStream.Color/Depth`, `RawStereoFrame`, and `RawCameraImage`; make their managed byte-array ownership explicit. A raw frame is complete only when both images are present. The production runtime-scope factory wraps `NativeRuntimeScope.Acquire`, while tests use a no-op/recording scope.

- [ ] Re-run the test executable. Expected: all mailbox tests pass without SDK files or hardware.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): add latest frame mailbox"`.

## Task 10: Adapt native camera/frame/calibration calls to the hardware-free interface

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Camera/VendorStereoCameraDevice.cs`
- Modify: `NodeCraft.Vision.StereoCamera/Camera/CameraSdkAbstractions.cs`
- Modify: `NodeCraft.Tests/VendorInteropTests.cs`

- [ ] Add failing unit tests around pure helpers for SDK pixel-format mapping, stride derivation, invalid data sizes, calibration conversion, and image-kind validation. Add reflection/source assertions that the native image path uses `Marshal.Copy` once and never creates a `Bitmap`.

- [ ] Run the test executable. Expected: adapter-helper tests fail.

- [ ] Implement `VendorStereoCameraDeviceFactory`: `Discover` calls `scDiscovery` with a bounded handle array, releases every non-null discovery-list handle in `finally`, and leaves the SDK's discovery cache ready for `OpenByIp`; `OpenByIp` calls `scGetCamera(ip, ScCameraDataTypeIP)`. The capture session already holds a `NativeRuntimeScope` across both calls. Throw a stage-specific error when discovery fails, exceeds the supported handle capacity, or the IP has no camera.

- [ ] Implement `VendorStereoCameraDevice` with explicit protocol state. After connect, `RegisterDisconnectCallback` registers the native Cdecl callback and rejects event ID 0; `UnregisterDisconnectCallback` unregisters that exact ID. Keep strong fields for both the native delegate and managed callback until successful unregister/disposal. `Connect`, callback unregister, `StartGrabbing`, `StopGrabbing`, and `Disconnect` must be idempotent at the managed wrapper level.

- [ ] Implement calibration loading in the required order: create manager, `scDownloadCalibData`, get `Color` and `Depth` with `isLeftReference = false`, validate the 9/12/16 arrays and dimensions, and convert to `CameraCalibration`. Keep the manager SafeHandle for the connected device lifetime.

- [ ] Implement `TryGetFrame(100)`: a null frame means a poll timeout and returns null. For a non-null frame, get ID/timestamp and both image handles from that same SafeFrameHandle. If either image is absent, return an incomplete raw frame that capture orchestration will discard; never fetch the missing image from another SDK frame.

- [ ] For each present image, map only BGR/RGB/Mono8/Depth16, get width/height/data pointer/size, require `size % height == 0`, derive `stride = size / height`, validate minimum row bytes and `size <= Int32.MaxValue`, allocate the final `byte[]`, and perform exactly one `Marshal.Copy` before releasing the image handle.

- [ ] Ensure all frame/image SafeHandles are scoped with `using`; retain only managed arrays after `TryGetFrame` returns. The device owns camera/calibration SafeHandles, while `StereoCameraCaptureSession` owns and disposes the separate runtime scope last.

- [ ] Re-run the test executable and `dotnet build NodeCraft.sln -c Debug`. Expected: helper/layout tests pass without invoking the native library.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): adapt vendor C API"`.

## Task 11: Implement camera capture lifecycle and executor outputs

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Camera/StereoCameraCaptureSession.cs`
- Create: `NodeCraft.Vision.StereoCamera/Nodes/StereoCameraExecutor.cs`
- Modify: `NodeCraft.Vision.StereoCamera/Camera/CameraSdkAbstractions.cs`
- Create: `NodeCraft.Tests/StereoCameraCaptureTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Add a scripted fake device/factory and invoke `RunStereoCameraCaptureTestsAsync()`. Cover exact discovery/connect/callback/calibration/start order, reverse best-effort stop, one connection per graph session, invalid IPv4, same-frame pairing, missing-image discard, malformed-image fatal failure, latest-frame overwrite, 5-second no-valid-frame fault, disconnect clearing cached data, four output keys, and calibration reference identity.

- [ ] Add `IMonotonicClock` to `CameraSdkAbstractions.cs` and an internal `StereoCameraCaptureOptions` beside the capture session. Inject both so timeout tests advance fake time rather than sleep for five seconds. Keep production defaults at 100 ms polling and 5 seconds without a complete frame.

- [ ] Run the test executable. Expected: capture/executor tests fail because implementations are absent.

- [ ] Implement startup in this exact order: validate a nonempty four-component dotted-decimal IPv4 literal; acquire the process-local native runtime scope; `Discover`; `OpenByIp`; `Connect`; `RegisterDisconnectCallback`; read color calibration; read depth calibration; `StartGrabbing`; launch the capture task. Reject hostnames, IPv6, abbreviated numeric forms, and whitespace. Do not configure exposure or any other parameter.

- [ ] In the capture loop, call `TryGetFrame(100)` and discard the entire raw frame only when color or depth is absent. Treat malformed dimensions, unsupported format, invalid buffer layout, or copy failure as fatal. For a complete valid raw frame, create both `FlowImage` objects through `FromOwnedBuffer`, create one atomic `FrameBundle`, and publish it with a monotonically increasing local sequence. Use the same SDK ID/timestamp and one capture UTC timestamp for both images.

- [ ] Track time since the last complete published bundle. Individual timeouts/incomplete groups continue polling; reaching five seconds faults the mailbox with a concise “no valid frame” exception and stops capture.

- [ ] On disconnect/fatal error, fault the mailbox first so its pending bundle is cleared, then request loop cancellation. Do not auto-reconnect.

- [ ] Implement stop as best-effort/idempotent: cancel; await the capture task past at most the current 100 ms poll; `UnregisterDisconnectCallback`; `StopGrabbing`; `Disconnect`; dispose device; dispose the native runtime scope last. Continue later steps after earlier cleanup failures and log all cleanup exceptions.

- [ ] Implement `StereoCameraExecutor` as all three interfaces: `IFlowNodeExecutor`, `IFlowNodeSessionLifecycle`, and `IFlowIterationSource`. `StartSessionAsync` reads `ipAddress` from `WorkflowNode.Inputs`; `PrepareIterationAsync` waits after the last local sequence and sets exactly one current bundle; `ExecuteAsync` returns:

```csharp
new Dictionary<string, object>
{
    ["colorImage"] = bundle.ColorImage,
    ["depthImage"] = bundle.DepthImage,
    ["colorCalibration"] = bundle.ColorCalibration,
    ["depthCalibration"] = bundle.DepthCalibration,
};
```

Clear the current bundle when the next preparation begins or the session stops.

- [ ] Re-run the test executable. Expected: all fake-camera tests pass; no SDK/hardware is accessed.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): stream synchronized latest frames"`.

## Task 12: Register camera and FlowImage preview nodes with persistence

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Nodes/StereoCameraNodeModel.cs`
- Create: `NodeCraft.Vision.StereoCamera/Nodes/FlowImagePreviewNodeModel.cs`
- Create: `NodeCraft.Vision.StereoCamera/Nodes/FlowImagePreviewExecutor.cs`
- Create: `NodeCraft.Vision.StereoCamera/Plugin/StereoCameraPlugin.cs`
- Create: `NodeCraft.Vision.StereoCamera/Views/StereoCameraEditor.xaml`
- Create: `NodeCraft.Vision.StereoCamera/Views/StereoCameraEditor.xaml.cs`
- Modify: `NodeCraft.Flow/Flow/FlowNodeRegistry.cs`
- Create: `NodeCraft.Tests/StereoCameraPluginTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Add and invoke plugin tests for metadata ID, stable TypeKeys, Vision category, exact port IDs/order/types, default empty IP, only IP persisted to XML, plugin loader registration without native SDK, pass-through preview reference identity, and persistent-content policy.

- [ ] Run the test executable. Expected: plugin registration/model tests fail.

- [ ] Implement `StereoCameraNodeModel : NodeModel, IWorkflowNodeValueProvider` with only one custom serializable property, `string IpAddress`, default empty. Its four output `PortParameter`s must be ordered color image, depth image, color calibration, depth calibration. `WriteWorkflowInputs` writes only `node.Inputs["ipAddress"]`.

- [ ] Implement `FlowImagePreviewNodeModel : NodeModel, INotifyPropertyChanged` with one required `image` input, one `image` output, and runtime-only `CurrentImage`, `StatusText`, and `BitmapSource` properties. The serializer already ignores unsupported runtime property types; add a regression test proving they do not appear in XML.

- [ ] Implement `FlowImagePreviewExecutor` to require a `FlowImage` and return the exact same reference under output key `image`.

- [ ] Implement `StereoCameraEditor` from embedded XAML with one IP `TextBox`. Update `IpAddress` and call `FlowCanvas.NotifyGraphChanged()` on edits; include no exposure, gain, connect, or stream controls.

- [ ] Implement `StereoCameraPlugin` with metadata ID `nodecraft.vision.stereo-camera`, version 1.0.0, and two registrations. Put both in category `Vision`; use TypeKeys `nodecraft.vision.stereo-camera.camera` and `nodecraft.vision.stereo-camera.image-preview`.

- [ ] Define the camera output ports exactly as slots 0–3 from the approved design. Define preview input/output both with port ID `image` and `FlowDataType.Image`; input required. Configure preview `RefreshContentAfterExecution = false` and an `ExecutionResultHandler` that sets `CurrentImage` from output slot 0 on the UI thread.

- [ ] Keep a parameterless plugin constructor for `PluginLoader`, but route registration through an internal factory method accepting `IStereoCameraDeviceFactory`, `ICameraRuntimeScopeFactory`, `IMonotonicClock`, and `StereoCameraCaptureOptions`. Tests and the final integration suite use these seams; production uses the vendor factory, native runtime scope, system monotonic clock, and 100 ms / 5 second defaults.

- [ ] Add `Vision`/camera/image icon mappings to `FlowNodeRegistry` without changing existing mappings. Use palette label `Image Preview (FlowImage)` so the built-in file-path preview remains distinct.

- [ ] Re-run the test executable. Stage a managed-only temporary plugin directory and verify `PluginLoader.LoadAll` registers both nodes without touching native code.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): register visual camera nodes"`.

## Task 13: Render color and depth previews with a latest-only background queue

**Files:**

- Create: `NodeCraft.Vision.StereoCamera/Preview/PreviewRenderResult.cs`
- Create: `NodeCraft.Vision.StereoCamera/Preview/FlowImageBitmapConverter.cs`
- Create: `NodeCraft.Vision.StereoCamera/Preview/LatestPreviewRenderQueue.cs`
- Create: `NodeCraft.Vision.StereoCamera/Views/FlowImagePreviewView.xaml`
- Create: `NodeCraft.Vision.StereoCamera/Views/FlowImagePreviewView.xaml.cs`
- Modify: `NodeCraft.Vision.StereoCamera/Plugin/StereoCameraPlugin.cs`
- Modify: `NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj`
- Create: `NodeCraft.Tests/FlowImagePreviewTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

- [ ] Add STA and async tests for BGR/RGB/Mono formats, Depth16 little-endian normalization, zeros ignored for min/max, all-zero/no-range black status, frozen `BitmapSource`, pass-through object identity, one pending render only, stale completion rejection, unload cancellation, and DynamicResource theme usage.

```csharp
Run("Depth16 preview normalizes only nonzero current-frame values", () =>
    RunOnSta(() =>
    {
        var image = CreateDepthImage(new ushort[] { 0, 100, 200 });
        var result = FlowImageBitmapConverter.Convert(image);
        var pixels = CopyGray8Pixels(result.Bitmap);
        return pixels.SequenceEqual(new byte[] { 0, 0, 255 })
            && result.StatusText.Contains("Depth16", StringComparison.Ordinal);
    }));
```

- [ ] Run the test executable. Expected: preview tests fail.

- [ ] Implement `FlowImageBitmapConverter.Convert`: map Bgr24/Rgb24/Mono8 to corresponding WPF pixel formats using the supplied stride. For Depth16, scan each row respecting source stride, decode little-endian values, ignore zero in min/max, and write a packed Gray8 buffer. If there is no nonzero range, return a black image plus explicit status.

- [ ] Always call `Freeze()` before returning `BitmapSource` from the converter, so it can cross from the worker to the UI thread.

- [ ] Implement `LatestPreviewRenderQueue` with one pending `(version, FlowImage)`. A submit replaces unstarted pending work. One worker renders outside the lock; before applying, compare its version to the latest submitted version and discard stale results. Await/inject the UI apply callback; never accumulate Dispatcher operations.

- [ ] Implement `FlowImagePreviewView` from embedded XAML. Show the image, frame ID, dimensions, pixel format, and status. Use only DynamicResource theme keys. Subscribe once to model changes, submit `CurrentImage` to the render queue, set frozen results on the UI Dispatcher, and dispose/cancel the queue on `Unloaded`.

- [ ] Set preview `ContentFactory = FlowImagePreviewView.CreateContent`; retain `RefreshContentAfterExecution = false` so the view and queue survive every frame.

- [ ] Re-run the test executable and build the solution. Expected: conversion, queue, view-lifetime, and theme tests pass.

- [ ] Run `git diff --check`, then commit: `git commit -m "feat(camera): add latest-only image preview"`.

## Task 14: Prove end-to-end semantics and document hardware acceptance

**Files:**

- Create: `NodeCraft.Tests/StereoCameraIntegrationTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`
- Create: `docs/testing/stereo-camera-hardware-acceptance.md`
- Modify as defects require: files from Tasks 1–13 only

- [ ] Build a registry through the plugin's internal factory with a gated fake camera, then add integration tests for camera -> color preview and camera -> depth preview graphs under a real `GraphExecutionSession` and `FlowExecutionController`.

- [ ] Verify run once produces exactly one DAG context, applies one preview result, stops/disconnects once, and can create a second independent session afterward.

- [ ] Verify continuous mode keeps one connection, never overlaps DAG/result callbacks, skips fake frames published while the result callback is blocked, and the next iteration receives the newest pending frame rather than replaying intermediate IDs.

- [ ] Verify a disconnect after publishing a pending frame faults the next wait and does not deliver that cached pre-disconnect frame. Verify a downstream exception ends the controller and still performs reverse lifecycle cleanup.

- [ ] Run `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug`. Expected: all old, core-session, plugin, preview, package, and integration tests pass.

- [ ] Run all repository verification commands from a clean Windows-x64 developer shell with the WindowsDesktop SDK installed (map `StereoCameraSdkRoot` to the supplied SDK's `app` directory if its path differs on that machine):

```bash
dotnet build NodeCraft.sln -c Debug
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows -c Debug
dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj -c Debug
dotnet msbuild NodeCraft.Vision.StereoCamera/NodeCraft.Vision.StereoCamera.csproj -t:StageStereoCameraPlugin -p:Configuration=Release -p:StereoCameraSdkRoot=/mnt/kevin/kevin/Downloads/test/app
```

- [ ] Inspect the staged package with `rg --files artifacts/Plugins/NodeCraft.Vision.StereoCamera | sort`. Confirm the two forbidden managed vendor DLLs and shared host assemblies are absent, all manifest runtime files/config/licenses are present, and no vendor artifact is tracked by `git status --short`.

- [ ] Add `docs/testing/stereo-camera-hardware-acceptance.md` with prerequisites and seven checkboxes: connect by IP; run once and observe cleanup; continuous latest-frame behavior under a deliberately slow downstream node; same frame/calibration identity across all four outputs; Stop then rerun; unplug failure/cleanup; close-window cleanup.

- [ ] Perform the hardware checklist only on Windows x64 with the physical camera. Record model, IP, SDK version, date, and pass/fail evidence. If hardware is unavailable, explicitly leave this checklist unexecuted; do not claim it passed.

- [ ] Run `git diff --check` and `git status --short`. Review every changed path against the file map and ensure no unrelated user changes or SDK binaries are staged.

- [ ] Commit the final integration/docs changes: `git commit -m "test(camera): verify streaming integration"`.

## Final Coverage Audit

- [ ] Run `rg -n "TODO|FIXME|NotImplementedException|StereoCamera\.Net|System\.Drawing" NodeCraft.Flow NodeCraft NodeCraft.Vision.StereoCamera NodeCraft.Tests docs/testing`. Resolve production placeholders. The only allowed `StereoCamera.Net` occurrences are packaging exclusion tests/docs; no project or DllImport reference is allowed.
- [ ] Confirm one-shot cleanup, continuous executor reuse, reverse lifecycle cleanup, fresh per-iteration contexts, 10 ms ordinary-flow guard, latest-only camera mailbox, latest-only preview queue, UI callback backpressure, compact image summaries, and awaited close each have at least one automated test.
- [ ] Confirm all four camera output slots and both calibration object-identity relationships are asserted by tests.
- [ ] Confirm all automatic tests run without SDK/hardware and the explicit package target reports all missing paths together.
- [ ] Confirm the real-hardware checklist is reported separately from automated verification.
