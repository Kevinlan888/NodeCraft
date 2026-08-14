# NodeCraft.Vision IMV Camera Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the existing stereo-camera plugin to `NodeCraft.Vision` and replace its vendor backend with the supplied IMV `MVSDKmd.dll` camera implementation while preserving NodeCraft's streaming-session behavior.

**Architecture:** Keep the existing `IFlowNodeSessionLifecycle`/`IFlowIterationSource` session model and latest-frame mailbox. Replace the stereo-specific device contract with an IMV device adapter that owns the `IMV_HANDLE`, copies or converts each `IMV_Frame` before releasing it, and publishes one `FlowImage` output with no calibration coupling. Package the external x64 SDK through an explicit MSBuild staging target; no vendor binaries enter the repository.

**Tech Stack:** C# 9, .NET 8 Windows x64, WPF, P/Invoke with `CallingConvention.StdCall`, MSBuild, existing console test harness, IMV C API from `D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv`.

## Global Constraints

- The final project directory, assembly, root namespace, and plugin entry type are `NodeCraft.Vision`.
- The final plugin ID is `nodecraft.vision`; the camera node TypeKey is `nodecraft.vision.camera`; the FlowImage preview TypeKey is `nodecraft.vision.image-preview`.
- The final camera input is persisted `ipAddress`; the only camera output port is `image` with `FlowDataType.Image`.
- IMV calls use `MVSDKmd.dll` with `CallingConvention.StdCall`; `IMV_TIMEOUT` is `-119`; `IMV_OK` is `0`.
- `FlowImage` contains only pixel and frame metadata; it has no `Calibration` property and its factories take no calibration parameter. `CameraCalibration` remains an independent Flow value and existing 3D nodes keep separate calibration output ports.
- Direct image support is Mono8, BGR8, and RGB8; 8-bit Bayer frames are converted to BGR8 through `IMV_PixelConvert`; unsupported packed, 10/12/16-bit, YUV, and planar formats fail explicitly.
- `IMV_ReleaseFrame` runs in a `finally` block for every successful `IMV_GetFrame` call, including conversion failures.
- SDK files are read from `VisionSdkRoot` only during explicit staging; no DLL, CTI, or license file from the supplied download is copied into Git.
- Do not stage the existing user changes in `NodeCraft.Flow/Flow/ConnectionLine.cs` or `NodeCraft.Tests/Program.cs` unless a test change explicitly overlaps the latter file; preserve unrelated edits.
- Every production behavior change follows red-green-refactor: add one failing test, run it and record the expected failure, implement the smallest passing change, then run the focused test and the full Windows test runner.

---

### Task 1: Establish the `NodeCraft.Vision` project identity

**Files:**
- Modify: `NodeCraft.Tests/NodeCraft.Tests.csproj`
- Modify: `NodeCraft.sln`
- Rename: `NodeCraft.Vision.StereoCamera/` → `NodeCraft.Vision/`
- Rename: `NodeCraft.Vision/NodeCraft.Vision.StereoCamera.csproj` → `NodeCraft.Vision/NodeCraft.Vision.csproj`
- Rename: `NodeCraft.Tests/StereoCameraProjectTests.cs` → `NodeCraft.Tests/VisionProjectTests.cs`
- Modify: `NodeCraft.Tests/Program.cs` only for the existing stereo-project test method call
- Modify: `NodeCraft.Vision/NodeCraft.Vision.csproj`
- Modify: `NodeCraft.Vision/plugin.json`

**Interfaces:**
- Produces a buildable project at `NodeCraft.Vision/NodeCraft.Vision.csproj` with `RootNamespace=NodeCraft.Vision`, `AssemblyName=NodeCraft.Vision`, `TargetFramework=net8.0-windows`, `PlatformTarget=x64`, and the same `NodeCraft.Flow` project reference.
- Produces `plugin.json` with `id=nodecraft.vision`, `entryAssembly=NodeCraft.Vision.dll`, and `entryType=NodeCraft.Vision.Plugin.VisionPlugin`.

- [ ] **Step 1: Write the failing identity test.** Rename the test file and change its method to assert the new path and values:

```csharp
private static void RunVisionProjectTests()
{
    Run("Vision project has the requested Windows x64 identity", () =>
    {
        var projectPath = FindRepositoryFile("NodeCraft.Vision", "NodeCraft.Vision.csproj");
        var projectText = File.ReadAllText(projectPath);
        var project = XDocument.Load(projectPath);
        var propertyGroup = project.Root?.Elements("PropertyGroup").FirstOrDefault();
        return string.Equals((string?)propertyGroup?.Element("RootNamespace"), "NodeCraft.Vision", StringComparison.Ordinal)
            && string.Equals((string?)propertyGroup?.Element("TargetFramework"), "net8.0-windows", StringComparison.OrdinalIgnoreCase)
            && string.Equals((string?)propertyGroup?.Element("PlatformTarget"), "x64", StringComparison.OrdinalIgnoreCase)
            && !projectText.Contains("StereoCamera.Net", StringComparison.Ordinal);
    });

    Run("Vision manifest has the new plugin identity", () =>
    {
        var manifestPath = FindRepositoryFile("NodeCraft.Vision", "plugin.json");
        var json = File.ReadAllText(manifestPath);
        return json.Contains("\"id\": \"nodecraft.vision\"", StringComparison.Ordinal)
            && json.Contains("\"entryAssembly\": \"NodeCraft.Vision.dll\"", StringComparison.Ordinal)
            && json.Contains("\"entryType\": \"NodeCraft.Vision.Plugin.VisionPlugin\"", StringComparison.Ordinal);
    });
}
```

- [ ] **Step 2: Run the focused build to verify red.**

Run: `dotnet build NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: FAIL because the new project path and manifest do not exist yet; do not change the assertions to make the old identity pass.

- [ ] **Step 3: Move the project and update solution references.** Use `git mv` for the directory and project file, update the solution's display name/path while retaining project GUID `{41C9B0CC-4EAA-4FA2-BD8C-DC53C5EF9CE3}`, update the test project reference, and set `RootNamespace`/`AssemblyName` in the new project file.

- [ ] **Step 4: Update the plugin manifest and test runner.** Set the new manifest values, rename the `Program.cs` call to `RunVisionProjectTests`, and keep the existing test runner order unchanged around the project test.

- [ ] **Step 5: Run the focused project tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: the new identity checks pass; other old stereo behavior checks may still fail and are handled in later tasks.

- [ ] **Step 6: Commit the identity-only change.**

```powershell
git add NodeCraft.sln NodeCraft.Tests/NodeCraft.Tests.csproj NodeCraft.Tests/VisionProjectTests.cs NodeCraft.Tests/Program.cs NodeCraft.Vision
git commit -m "refactor: rename stereo camera project to vision"
```

### Task 2: Decouple `FlowImage` from `CameraCalibration`

**Files:**
- Modify: `NodeCraft.Flow/Flow/Visual/FlowImage.cs`
- Modify: `NodeCraft.Tests/VisualContractTests.cs`
- Modify: `NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs` after Task 1's project move
- Modify: `NodeCraft.Tests/StereoCameraCaptureTests.cs`
- Modify: `NodeCraft.Tests/StereoCameraIntegrationTests.cs`
- Modify: `NodeCraft.Tests/StereoCameraPluginTests.cs`
- Modify: `NodeCraft.Tests/FlowImagePreviewTests.cs`

**Interfaces:**
- `FlowImage.CopyFrom` and `FlowImage.FromOwnedBuffer` end at `capturedAtUtc`; neither accepts a calibration argument.
- `FlowImage` has no `Calibration` property.
- `CameraCalibration` remains a standalone `NodeCraft.Flow` type; 3D camera calibration output assertions compare the separate node output values, not image properties.

- [ ] **Step 1: Add the failing decoupling contract test.** Add a reflection assertion and update one factory call to the new signature:

```csharp
Run("FlowImage does not own camera calibration", () =>
{
    var image = FlowImage.CopyFrom(
        2, 1, 2, FlowPixelFormat.Mono8, FlowImageKind.Color,
        new byte[] { 7, 8 }, 1, 2, DateTimeOffset.UtcNow);
    return typeof(FlowImage).GetProperty("Calibration") == null
        && image.Width == 2
        && image.Buffer.Span.SequenceEqual(new byte[] { 7, 8 });
});
```

- [ ] **Step 2: Run the focused test to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: compile failure because the existing factory still requires calibration and the property still exists.

- [ ] **Step 3: Remove calibration from the FlowImage implementation.** Remove the constructor/factory parameter, assignment, property, and null validation from `NodeCraft.Flow/Flow/Visual/FlowImage.cs`; retain all pixel-buffer validation. Update all existing test image factories and 3D capture call sites to stop passing calibration while keeping `FrameBundle.ColorCalibration`/`DepthCalibration` and their independent output ports unchanged.

- [ ] **Step 4: Run the visual and existing camera tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: the decoupling test, existing FlowImage tests, preview tests, and 3D camera tests pass with calibration only in separate outputs.

- [ ] **Step 5: Commit the data-contract correction.**

```powershell
git add NodeCraft.Flow/Flow/Visual/FlowImage.cs NodeCraft.Tests/VisualContractTests.cs NodeCraft.Tests/StereoCameraCaptureTests.cs NodeCraft.Tests/StereoCameraIntegrationTests.cs NodeCraft.Tests/StereoCameraPluginTests.cs NodeCraft.Tests/FlowImagePreviewTests.cs NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs
git commit -m "refactor: keep camera calibration separate from images"
```

### Task 3: Add tested IMV interop primitives

**Files:**
- Create: `NodeCraft.Vision/VendorInterop/ImvEnums.cs`
- Create: `NodeCraft.Vision/VendorInterop/ImvStructs.cs`
- Create: `NodeCraft.Vision/VendorInterop/IImvNativeApi.cs`
- Create: `NodeCraft.Vision/VendorInterop/ImvNativeMethods.cs`
- Create: `NodeCraft.Vision/VendorInterop/VisionNativeException.cs`
- Create: `NodeCraft.Vision/VendorInterop/VisionCameraSafeHandle.cs`
- Rename/replace: `NodeCraft.Tests/VendorInteropTests.cs` → `NodeCraft.Tests/ImvInteropTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**

```csharp
internal interface IImvNativeApi
{
    int EnumDevices(out ImvDeviceList deviceList, ImvInterfaceType interfaceType);
    int CreateHandle(out IntPtr handle, ImvCreateHandleMode mode, IntPtr identifier);
    int DestroyHandle(IntPtr handle);
    int Open(IntPtr handle);
    int Close(IntPtr handle);
    int SetEnumFeatureSymbol(IntPtr handle, string featureName, string enumSymbol);
    int StartGrabbing(IntPtr handle);
    int StopGrabbing(IntPtr handle);
    int GetFrame(IntPtr handle, out ImvFrame frame, uint timeoutMilliseconds);
    int ReleaseFrame(IntPtr handle, ref ImvFrame frame);
    int PixelConvert(IntPtr handle, ref ImvPixelConvertParam parameter);
}
```

`ImvFrameInfo` mirrors the header fields in order and has a 64-bit Windows size of 136 bytes; `ImvFrame` has `IntPtr frameHandle`, `IntPtr pData`, `ImvFrameInfo frameInfo`, ten reserved `uint` fields, and a 64-bit Windows size of 192 bytes. `ImvPixelConvertParam` mirrors the header and has a 64-bit Windows size of 96 bytes. Use explicit reserved fields rather than `ByValArray` so `out`/`ref` marshaling never depends on managed array initialization.

- [ ] **Step 1: Write failing layout and error tests.** Add assertions for the exact IMV constants and layouts:

```csharp
Run("IMV structs match the x64 C layout", () =>
    IntPtr.Size != 8
        || Marshal.SizeOf<ImvFrameInfo>() == 136
        && Marshal.SizeOf<ImvFrame>() == 192
        && Marshal.SizeOf<ImvPixelConvertParam>() == 96);

Run("IMV pixel constants match IMVDefines.h", () =>
    (int)ImvPixelType.Mono8 == 0x01080001
        && (int)ImvPixelType.Bgr8 == 0x02180015
        && (int)ImvPixelType.Rgb8 == 0x02180014
        && (int)ImvPixelType.BayerRg8 == 0x01080009);

Run("Vision native errors preserve operation and code", () =>
{
    try
    {
        VisionNativeException.ThrowIfError("IMV_GetFrame", -119);
        return false;
    }
    catch (VisionNativeException ex)
    {
        return ex.Operation == "IMV_GetFrame" && ex.ErrorCode == -119;
    }
});
```

- [ ] **Step 2: Run the interop test to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: compile failure because the `Imv*` types and exception do not exist.

- [ ] **Step 3: Implement the enums and structs.** Define `ImvInterfaceType.All = 0` and `ImvInterfaceType.Invalid = 0xffffffff` as declared by the supplied header, together with `ImvCreateHandleMode.ByIpAddress = 3`, `ImvBayerDemosaic.Bilinear = 1`, `ImvError.Timeout = -119`, and the explicit pixel values from `IMVDefines.h`.

- [ ] **Step 4: Implement the native seam and production forwarding.** Add `ImvNativeMethods` P/Invokes for the eleven methods in the interface with `MVSDKmd.dll`, `StdCall`, and exact spelling. Use ANSI marshaling for `SetEnumFeatureSymbol` strings only; keep `CreateHandle`'s `void*` identifier as `IntPtr` so the device adapter controls allocation lifetime.

- [ ] **Step 5: Implement error translation and the device safe handle.** `VisionNativeException.ThrowIfError` returns on zero and throws with operation/code otherwise. `VisionCameraSafeHandle.ReleaseHandle` calls the injected destroy delegate and returns true only for `IMV_OK`; explicit device methods still perform Stop/Close before disposing this handle.

- [ ] **Step 6: Run the interop tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: all new layout, constant, P/Invoke, and error tests pass without loading `MVSDKmd.dll`.

- [ ] **Step 7: Commit the interop layer.**

```powershell
git add NodeCraft.Vision/VendorInterop NodeCraft.Tests/ImvInteropTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: add IMV native interop"
```

### Task 4: Implement IMV frame conversion and device ownership

**Files:**
- Create: `NodeCraft.Vision/Camera/VisionCameraAbstractions.cs`
- Create: `NodeCraft.Vision/Camera/VisionImageConverter.cs`
- Create: `NodeCraft.Vision/Camera/VisionCameraDevice.cs`
- Create: `NodeCraft.Tests/VisionCameraDeviceTests.cs`
- Remove after migration: `NodeCraft.Vision/Camera/FrameBundle.cs`
- Remove after migration: `NodeCraft.Vision/Camera/VendorStereoCameraDevice.cs`
- Remove after migration: old `NodeCraft.Vision/VendorInterop/Native*.cs`, `IStereoCameraFrameApi.cs`, `StereoCamera*.cs`

**Interfaces:**

```csharp
internal interface IVisionCameraDeviceFactory
{
    int Discover();
    IVisionCameraDevice OpenByIp(string ipAddress);
}

internal interface IVisionCameraDevice : IDisposable
{
    void Connect();
    void StartGrabbing();
    VisionRawFrame TryGetFrame(uint timeoutMilliseconds);
    void StopGrabbing();
    void Disconnect();
}

internal sealed class VisionRawFrame
{
    internal VisionRawFrame(ulong frameId, ulong deviceTimestamp, VisionRawImage image)
    {
        FrameId = frameId;
        DeviceTimestamp = deviceTimestamp;
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }
    internal ulong FrameId { get; }
    internal ulong DeviceTimestamp { get; }
    internal VisionRawImage Image { get; }
}
```

- [ ] **Step 1: Write failing direct-format and stride tests.** Add tests that map Mono8/BGR8/RGB8 to `FlowPixelFormat`, derive `stride = size / height`, reject non-whole rows and too-small rows, reject a null `pData`, reject nonzero frame status, and preserve `FlowImageKind.Color`.

- [ ] **Step 2: Write failing Bayer and release tests.** Allocate a small source buffer with `Marshal.AllocHGlobal`, configure a fake `IImvNativeApi.PixelConvert` to write a known BGR buffer and `nDstDataLen`, then assert the result has BGR24/width/height and that `ReleaseFrame` is called once. Inject exceptions from `PixelConvert` and assert `ReleaseFrame` is still called once.

- [ ] **Step 3: Run the focused device tests to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: compile failure because `VisionImageConverter`, `VisionCameraDevice`, and the fake seam do not exist.

- [ ] **Step 4: Implement direct conversion.** Read `ImvFrame.frameInfo`, validate status/dimensions/size/pointer, map Mono8/BGR8/RGB8, compute `stride`, allocate exactly `size` bytes, and call `Marshal.Copy` once. Return a `VisionRawImage` with `FlowImageKind.Color`.

- [ ] **Step 5: Implement Bayer conversion.** For the four 8-bit Bayer enums, allocate `width * height * 3`, fill `ImvPixelConvertParam` with source pointer, `paddingX`, `paddingY`, `Bilinear`, destination `Bgr8`, and destination capacity, call `PixelConvert`, validate `nDstDataLen == width * height * 3`, and return stride `width * 3`.

- [ ] **Step 6: Implement factory and device lifecycle.** `VisionCameraDeviceFactory.Discover` calls `EnumDevices(All)` and returns the checked device count. `OpenByIp` validates strict dotted-decimal IPv4, allocates an ANSI IP string, calls `CreateHandle(ByIpAddress)`, frees the string in `finally`, and wraps the result in `VisionCameraSafeHandle`.

`VisionCameraDevice.Connect` calls `Open`; `StartGrabbing` calls `SetEnumFeatureSymbol("TriggerMode", "Off")` then `StartGrabbing`; `TryGetFrame` treats `-119` as `null`, throws all other nonzero results, converts successful frames, and calls `ReleaseFrame` in `finally`; `StopGrabbing` calls the API once when active; `Disconnect` calls `Close` once when connected; `Dispose` unregisters no callbacks, disposes the safe handle exactly once, and remains idempotent.

- [ ] **Step 7: Run the focused device tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: all direct format, Bayer, invalid-data, timeout, release, IP validation, and lifecycle tests pass.

- [ ] **Step 8: Commit the device adapter.**

```powershell
git add NodeCraft.Vision/Camera NodeCraft.Vision/VendorInterop NodeCraft.Tests/VisionCameraDeviceTests.cs
git commit -m "feat: adapt Vision camera to IMV frames"
```

### Task 5: Port the latest-frame capture session and executor

**Files:**
- Rename: `NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs` → `NodeCraft.Vision/Camera/VisionCameraCaptureSession.cs`
- Modify: `NodeCraft.Vision/Camera/VisionCameraAbstractions.cs`
- Rename: `NodeCraft.Vision/Nodes/StereoCameraExecutor.cs` → `NodeCraft.Vision/Nodes/VisionCameraExecutor.cs`
- Rename: `NodeCraft.Tests/StereoCameraCaptureTests.cs` → `NodeCraft.Tests/VisionCameraCaptureTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**
- `VisionCameraCaptureSession` accepts `string ipAddress`, `IVisionCameraDeviceFactory`, `ICameraRuntimeScopeFactory`, `IMonotonicClock`, `VisionCameraCaptureOptions`, and optional `ILogger`.
- `WaitForNextAsync(long afterSequence, CancellationToken)` returns `LatestFrameMailbox<FlowImage>.LatestFrame<FlowImage>`.
- `VisionCameraExecutor.ExecuteAsync` returns exactly `{ ["image"] = currentFlowImage }`.

- [ ] **Step 1: Rewrite the session tests before changing production code.** Replace stereo expectations with `scope:acquire`, `discover`, `open:<ip>`, `connect`, `trigger:off`, `start`; assert cleanup order `stop`, `disconnect`, `device:dispose`, `scope:dispose`. Add tests for startup error preservation, latest image publication, malformed image fault, timeout after `NoValidFrameTimeout`, cancellation, and repeated stop.

- [ ] **Step 2: Rewrite the executor test to assert one output.** Enqueue one `VisionRawFrame`, start the executor, prepare one iteration, and assert `outputs.Keys.SequenceEqual(new[] { "image" })`, the output is a `FlowImage`, and `FrameId` is preserved; no calibration is expected on the image or node.

- [ ] **Step 3: Run the focused capture tests to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: compile failures from the old stereo contracts and missing Vision session/executor behavior.

- [ ] **Step 4: Implement `VisionCameraCaptureSession`.** Keep the existing lock/task state machine and one-slot mailbox, but start the new device without callback or calibration phases. Convert each `VisionRawFrame` to `FlowImage.FromOwnedBuffer(...)` with only the raw frame metadata, publish it, and update the no-valid-frame clock only after a complete image is accepted.

- [ ] **Step 5: Implement `VisionCameraExecutor`.** Read `WorkflowNode.Inputs["ipAddress"]`, create/start the session once per execution session, wait for a newer mailbox sequence in `PrepareIterationAsync`, return the current image in `ExecuteAsync`, and stop/reset all state in `StopSessionAsync`.

- [ ] **Step 6: Run the focused capture tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: startup, iteration, timeout, cancellation, cleanup, and one-output executor tests pass.

- [ ] **Step 7: Commit the capture layer.**

```powershell
git add NodeCraft.Vision/Camera NodeCraft.Vision/Nodes/VisionCameraExecutor.cs NodeCraft.Tests/VisionCameraCaptureTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: stream latest Vision camera images"
```

### Task 6: Register the Vision nodes and port the WPF UI

**Files:**
- Rename: `NodeCraft.Vision/Nodes/StereoCameraNodeModel.cs` → `NodeCraft.Vision/Nodes/VisionCameraNodeModel.cs`
- Rename: `NodeCraft.Vision/Plugin/StereoCameraPlugin.cs` → `NodeCraft.Vision/Plugin/VisionPlugin.cs`
- Rename: `NodeCraft.Vision/Views/StereoCameraEditor.xaml` → `NodeCraft.Vision/Views/VisionCameraEditor.xaml`
- Rename: `NodeCraft.Vision/Views/StereoCameraEditor.xaml.cs` → `NodeCraft.Vision/Views/VisionCameraEditor.xaml.cs`
- Modify: `NodeCraft.Vision/Nodes/FlowImagePreviewNodeModel.cs`
- Modify: `NodeCraft.Vision/Nodes/FlowImagePreviewExecutor.cs`
- Modify: `NodeCraft.Vision/Views/FlowImagePreviewView.xaml.cs`
- Modify: all `NodeCraft.Vision/Preview/*.cs` namespaces
- Rename/modify: `NodeCraft.Tests/StereoCameraPluginTests.cs` → `NodeCraft.Tests/VisionPluginTests.cs`
- Rename/modify: `NodeCraft.Tests/StereoCameraIntegrationTests.cs` → `NodeCraft.Tests/VisionIntegrationTests.cs`
- Modify: `NodeCraft.Tests/FlowImagePreviewTests.cs`
- Modify: `NodeCraft.Tests/LatestFrameMailboxTests.cs`
- Modify: `NodeCraft.Tests/Program.cs`

**Interfaces:**
- `VisionCameraNodeModel.FlowNodeTypeKey = "nodecraft.vision.camera"` and `FlowImagePreviewNodeModel.FlowNodeTypeKey = "nodecraft.vision.image-preview"`.
- `VisionPlugin.Metadata.Id = "nodecraft.vision"`, `DisplayName = "Vision"`, `Version = new Version(1, 0, 0)`.
- The camera registration has one `image` output and `ContentFactory = VisionCameraEditor.CreateContent`; preview registration remains input/output `image` with `FlowDataType.Image`.

- [ ] **Step 1: Write failing registration and graph tests.** Assert the new plugin ID/display name, camera/preview TypeKeys, `Vision Camera` palette label, one camera output named `image`, IP serialization, and the existing preview execution-result behavior. Rewrite the graph fixture so the camera's slot 0 connects directly to the preview input.

- [ ] **Step 2: Run the focused plugin/integration tests to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: failures from the old class names, TypeKeys, four-port assertions, and stereo fake device contract.

- [ ] **Step 3: Implement the renamed node model and plugin.** Rename classes/namespaces, update metadata and registration factories, inject `IVisionCameraDeviceFactory`, `ICameraRuntimeScopeFactory`, `IMonotonicClock`, and `VisionCameraCaptureOptions` through an internal `VisionPlugin.CreateForTesting` seam, and keep a parameterless constructor for `PluginLoader`.

- [ ] **Step 4: Port the camera editor XAML.** Change the embedded resource URI to `NodeCraft.Vision.Views.VisionCameraEditor.xaml`, change visible text to `Vision Camera`, keep the existing IP `TextBox` binding and `FlowCanvas.NotifyGraphChanged()` behavior, and reject non-`VisionCameraNodeModel` input with the renamed error text.

- [ ] **Step 5: Port preview namespaces and resource URIs.** Move all preview/view namespaces to `NodeCraft.Vision`, update the embedded XAML URI for `FlowImagePreviewView`, and retain the latest-render queue, frozen bitmap, stale-render protection, and `FlowImage` object identity behavior.

- [ ] **Step 6: Run the focused plugin/integration/UI tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: plugin registration, graph execution, IP persistence, preview, and STA content-factory tests pass with the new one-image contract.

- [ ] **Step 7: Commit the node and UI layer.**

```powershell
git add NodeCraft.Vision/Nodes NodeCraft.Vision/Plugin NodeCraft.Vision/Views NodeCraft.Vision/Preview NodeCraft.Tests/VisionPluginTests.cs NodeCraft.Tests/VisionIntegrationTests.cs NodeCraft.Tests/FlowImagePreviewTests.cs NodeCraft.Tests/LatestFrameMailboxTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: register Vision camera and preview nodes"
```

### Task 7: Replace native runtime packaging and remove stereo artifacts

**Files:**
- Rename: `NodeCraft.Vision/Build/StereoCameraPackaging.targets` → `NodeCraft.Vision/Build/VisionPackaging.targets`
- Rename: `NodeCraft.Vision/Build/StereoCameraRuntimeFiles.txt` → `NodeCraft.Vision/Build/VisionRuntimeFiles.txt`
- Modify: `NodeCraft.Vision/NodeCraft.Vision.csproj`
- Modify: `NodeCraft.Vision/Runtime/NativeRuntimeScope.cs`
- Rename/modify: `NodeCraft.Tests/StereoCameraPackagingTests.cs` → `NodeCraft.Tests/VisionPackagingTests.cs`
- Create: `docs/testing/vision-camera-hardware-acceptance.md`
- Delete: `docs/testing/stereo-camera-hardware-acceptance.md`
- Delete after all references are removed: every old `StereoCamera` interop, capture, frame-bundle, node, plugin, and packaging file

**Interfaces:**
- MSBuild target name: `StageVisionPlugin`.
- MSBuild properties: `VisionSdkRoot` (required by staging) and `VisionPackageRoot` (default `$(MSBuildThisFileDirectory)..\..\artifacts\Plugins\NodeCraft.Vision`).
- Runtime manifest source: `$(VisionSdkRoot)\Runtime\x64\%(_VisionRuntimeFileName.Identity)`.

- [ ] **Step 1: Write failing packaging tests.** Assert `VisionRuntimeFiles.txt` contains each file in the supplied runtime inventory, including `MVSDKmd.dll`, `MVProducerGEV.cti`, `MVProducerU3V.cti`, `ImageConvert.dll`, and `SDKLOG_default.properties`, with no duplicates or old stereo/NLog entries. Assert the target contains `StageVisionPlugin`, `VisionSdkRoot`, `VisionPackageRoot`, missing-file guards, `RemoveDir`, and forbidden shared-assembly checks.

- [ ] **Step 2: Run packaging tests to verify red.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: failures because the project still imports `StereoCameraPackaging.targets` and the old runtime manifest is present.

- [ ] **Step 3: Write the exact runtime manifest.** Copy these names from the inspected SDK runtime into `VisionRuntimeFiles.txt`, one per line:

```text
CamUpgradeModule.dll
CLAllSerial_MD_VC120_v3_0.dll
CLProtocol_MD_VC120_v3_0.dll
CLSerCOM.dll
clserVsp.dll
compress_decode.dll
DeCompressFile.dll
GCBase_MD_VC120_v3_0.dll
GenApi_MD_VC120_v3_0.dll
GenCP_MD_VC120_v3_0.dll
iImageProcessing64.dll
ImageConvert.dll
ImageSave.dll
Log_MD_VC120_v3_0.dll
log4cpp_MD_VC120_v3_0.dll
MathParser_MD_VC120_v3_0.dll
MVlog4cppmd.dll
MVProducerGEV.cti
MVProducerU3V.cti
MVSDKmd.dll
NodeMapData_MD_VC120_v3_0.dll
SDKLOG_default.properties
TinyXmlmd.dll
VideoRender.dll
XmlParser_MD_VC120_v3_0.dll
```

- [ ] **Step 4: Implement the staging target.** Copy `plugin.json`, `$(TargetPath)`, all manifest runtime files to `lib`, and `$(VisionSdkRoot)\Licenses\**\*` to `licenses`; fail before deleting the package when any source is missing. Reject `NodeCraft.Flow.dll`, `CommonControls.WPF.dll`, `Microsoft.Extensions.Logging*`, `NLog.dll`, `StereoCamera.Net.dll`, and `LibStereoCamera.dll` in the manifest.

- [ ] **Step 5: Port and test the runtime scope.** Rename error strings to Vision, keep Windows/x64 checks, `AddDllDirectory`/`RemoveDllDirectory`, process-local `MV_GENICAM_64` restoration, reference counting, and idempotent disposal. Do not add registry or machine-wide PATH writes.

- [ ] **Step 6: Add the hardware acceptance checklist.** Document the exact staging command, IP subnet prerequisite from the supplied `Grab` sample, expected node/preview behavior, stop/disconnect verification, and the package checks for `MVSDKmd.dll` and licenses.

- [ ] **Step 7: Run packaging tests to verify green.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-restore`

Expected: all runtime manifest, target-source, x64/process-local scope, and no-old-artifact tests pass.

- [ ] **Step 8: Commit packaging and cleanup.**

```powershell
git add NodeCraft.Vision/Build/VisionPackaging.targets NodeCraft.Vision/Build/VisionRuntimeFiles.txt NodeCraft.Vision/NodeCraft.Vision.csproj NodeCraft.Vision/Runtime/NativeRuntimeScope.cs NodeCraft.Tests/VisionPackagingTests.cs docs/testing/vision-camera-hardware-acceptance.md docs/testing/stereo-camera-hardware-acceptance.md
git commit -m "build: package IMV runtime for Vision plugin"
```

### Task 8: Run repository verification and optional hardware acceptance

**Files:**
- Verify: `NodeCraft.sln`
- Verify: `NodeCraft.Tests/Program.cs`
- Verify: `NodeCraft.Vision/plugin.json`
- Verify: `docs/testing/vision-camera-hardware-acceptance.md`

- [ ] **Step 1: Build the full solution without staging.**

Run: `dotnet build NodeCraft.sln --no-restore`

Expected: build succeeds without `VisionSdkRoot`; only the explicit staging target requires the vendor SDK.

- [ ] **Step 2: Run the Windows test harness.**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows --no-build`

Expected: output ends with `ALL PASS` and contains no failed test lines.

- [ ] **Step 3: Run the CLI regression harness.**

Run: `dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj --no-restore`

Expected: the existing CLI test runner passes; no CLI files were changed by this feature.

- [ ] **Step 4: Stage the real SDK package.**

Run:

```powershell
dotnet msbuild NodeCraft.Vision/NodeCraft.Vision.csproj -t:StageVisionPlugin -p:Configuration=Release -p:VisionSdkRoot="D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv"
```

Expected: `artifacts\Plugins\NodeCraft.Vision` contains `plugin.json`, `NodeCraft.Vision.dll`, `lib\MVSDKmd.dll`, all listed runtime files, and `licenses`; it contains no `StereoCamera`, `LibStereoCamera`, `NLog.dll`, `NodeCraft.Flow.dll`, or `CommonControls.WPF.dll`.

- [ ] **Step 5: Inspect final references and worktree.**

Run: `rg -n --hidden -g '!bin/**' -g '!obj/**' "NodeCraft\.Vision\.StereoCamera|LibStereoCamera|StereoCamera\.Net|sc[A-Z]|Sc[A-Z]" NodeCraft.sln NodeCraft.Vision NodeCraft.Tests docs/testing`

Expected: no old production or test identity remains; old design/history documents may retain historical references outside the checked paths. Then run `git status --short` and confirm the pre-existing `ConnectionLine.cs` and unrelated `Program.cs` changes remain unstaged unless the Program change was intentionally updated for this feature.

- [ ] **Step 6: Perform hardware acceptance only if the camera is connected.** Launch the host with the staged plugin, create a `Vision Camera` node, enter the camera IP, connect its `image` slot to `Image Preview (FlowImage)`, run once and continuously, verify changing frame IDs and correct image rendering, stop execution, and confirm the camera can be reopened after the run.

- [ ] **Step 7: Commit only verification documentation if it changed.** Do not commit staged SDK output under `artifacts` if the repository ignores it; commit only a changed acceptance checklist or test-source correction.

## Plan Self-Review

- Spec coverage: project identity is covered by Task 1; independent image/calibration data contracts by Task 2; IMV ABI and errors by Task 3; frame conversion and release ownership by Task 4; session/iteration behavior by Task 5; plugin/UI/preview by Task 6; runtime packaging and removal by Task 7; full and hardware verification by Task 8.
- Placeholder scan: no production step depends on `TBD`, `TODO`, ellipses, or an unspecified file; all code samples use concrete types, fields, and commands.
- Type consistency: `IImvNativeApi`, `IVisionCameraDeviceFactory`, `IVisionCameraDevice`, `VisionRawFrame`, `VisionCameraCaptureSession`, `VisionCameraExecutor`, `VisionCameraNodeModel`, and `VisionPlugin` are the names used consistently by later tasks.
- Scope: the plan removes the legacy image-to-calibration coupling and keeps calibration as an independent Flow value because the supplied IMV API has no calibration path; it does not add exposure controls, auto-reconnect, or unrelated host refactoring.
