# NodeCraft Vision Dual-Camera Coexistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the original technical-MVSDK 3D camera node inside the renamed `NodeCraft.Vision` plugin while keeping the new IMV 2D camera node available beside it.

**Architecture:** Keep both implementations in one plugin assembly with distinct namespaces and TypeKeys. Reuse the current FlowImage/preview contracts, restore the old 3D native interop and capture lifecycle, and route both runtime scopes through the existing shared Vision DLL search-path manager. Stage the new and old SDK runtime files from separate roots into one package.

**Tech Stack:** .NET 8 Windows x64, WPF, C# 9, MSBuild packaging targets, the existing in-process test executable.

## Global Constraints

- `FlowImage` must not contain calibration data.
- The 3D node keeps outputs `colorImage`, `depthImage`, `colorCalibration`, `depthCalibration` in that order.
- The IMV node keeps TypeKey `nodecraft.vision.camera` and output `image`.
- The 3D node keeps TypeKey `nodecraft.vision.stereo-camera.camera`.
- No `StereoCamera.Net.dll` or vendor managed wrapper is added.
- Preserve the user's existing `NodeCraft.Flow/Flow/ConnectionLine.cs` modification.

---

### Task 1: Add the coexistence regression tests

**Files:**
- Modify: `NodeCraft.Tests/VisionPluginTests.cs`
- Modify: `NodeCraft.Tests/Program.cs` only if a new test method needs explicit invocation

**Interfaces:**
- Consumes: `VisionPlugin.Register`, the current plugin registration context, and literal legacy TypeKey `nodecraft.vision.stereo-camera.camera`.
- Produces: failing tests that require both camera registrations and the original four 3D output ports.

- [ ] **Step 1: Write the failing test**

Add one assertion to the existing plugin registration test that locates `nodecraft.vision.stereo-camera.camera`, checks its display name/category, and verifies the exact output IDs and `FlowDataType` values:

```csharp
var stereo = context.Registrations.Single(registration =>
    registration.Definition.TypeKey == "nodecraft.vision.stereo-camera.camera");
var stereoOutputs = stereo.Definition.OutputPorts.Select(port => port.Id).ToArray();
var stereoTypes = stereo.Definition.OutputPorts.Select(port => port.DataType).ToArray();

return stereoOutputs.SequenceEqual(new[]
        { "colorImage", "depthImage", "colorCalibration", "depthCalibration" })
    && stereoTypes.SequenceEqual(new[]
        {
            FlowDataType.Image,
            FlowDataType.Image,
            FlowDataType.CameraCalibration,
            FlowDataType.CameraCalibration,
        })
    && camera.Definition.TypeKey != stereo.Definition.TypeKey;
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -c Debug --no-build`

Expected: the existing Vision plugin test reports a failure because the legacy stereo registration is not present.

- [ ] **Step 3: Keep the test as the implementation contract**

Do not weaken the assertion to accept a single combined node or a changed legacy port name.

### Task 2: Restore and adapt the technical MVSDK 3D implementation

**Files:**
- Restore/adapt: `NodeCraft.Vision/Camera/CameraSdkAbstractions.cs`
- Restore/adapt: `NodeCraft.Vision/Camera/FrameBundle.cs`
- Restore/adapt: `NodeCraft.Vision/Camera/StereoCameraCaptureSession.cs`
- Restore/adapt: `NodeCraft.Vision/Camera/VendorStereoCameraDevice.cs`
- Restore/adapt: `NodeCraft.Vision/Nodes/StereoCameraExecutor.cs`
- Restore/adapt: `NodeCraft.Vision/Nodes/StereoCameraNodeModel.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/IStereoCameraFrameApi.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/NativeEnums.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/NativeMethods.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/NativeStructs.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/StereoCameraNativeException.cs`
- Restore/adapt: `NodeCraft.Vision/VendorInterop/StereoCameraSafeHandles.cs`
- Create: `NodeCraft.Vision/Views/StereoCameraEditor.xaml`
- Create: `NodeCraft.Vision/Views/StereoCameraEditor.xaml.cs`
- Modify: `NodeCraft.Vision/NodeCraft.Vision.csproj`

**Interfaces:**
- Consumes: current `NodeCraft.Flow` image/calibration types and `NodeCraft.Vision.Runtime.NativeRuntimeScope`.
- Produces: an internal 3D factory/session/executor and a public `StereoCameraNodeModel` that preserves the old workflow contract.

- [ ] **Step 1: Restore the historical implementation files**

Recover the files from the parent of commit `d978715`, keeping the historical `NodeCraft.Vision.StereoCamera.*` namespaces where they preserve compatibility.

- [ ] **Step 2: Point the 3D runtime factory at the shared Vision runtime scope**

Use `NodeCraft.Vision.Runtime.NativeRuntimeScope.Acquire(pluginAssemblyPath)` from the 3D `ProductionCameraRuntimeScopeFactory`; do not add a second independent DLL-directory reference counter.

- [ ] **Step 3: Restore the editor as an embedded resource**

Embed `Views/StereoCameraEditor.xaml` and load it by its `NodeCraft.Vision.StereoCamera.Views.StereoCameraEditor.xaml` resource name. Keep only the IP address editor.

- [ ] **Step 4: Re-run the coexistence test**

Run the in-process test executable. Expected: the registration test passes and both camera node factories can be created without loading native hardware.

### Task 3: Register both camera nodes in the single Vision plugin

**Files:**
- Modify: `NodeCraft.Vision/Plugin/VisionPlugin.cs`
- Create or modify: `NodeCraft.Vision/Plugin/StereoCameraRegistration.cs`
- Modify: `NodeCraft.Tests/VisionPluginTests.cs`

**Interfaces:**
- Consumes: `StereoCameraNodeModel`, `StereoCameraExecutor`, current `VisionCameraNodeModel`, and the shared preview registration.
- Produces: one `VisionPlugin` manifest entry with two independent camera registrations and one preview registration.

- [ ] **Step 1: Add the 3D registration assertion**

Assert that the stereo registration has a non-null `NodeModelType`, factory, executor factory, and content factory, and that the IMV and 3D TypeKeys are distinct.

- [ ] **Step 2: Implement the 3D registration**

Register the 3D camera with category `Vision`, display name `Stereo Camera`, the four legacy outputs, and a factory using `StereoCameraExecutor`.

- [ ] **Step 3: Keep preview registration singular**

Register the current `FlowImagePreviewNodeModel` only once. Both 2D and 3D image outputs use `FlowDataType.Image` and can connect to it.

- [ ] **Step 4: Run the focused plugin tests**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -c Debug --no-build`

Expected: both camera registration assertions pass and no duplicate TypeKey error occurs.

### Task 4: Package the two SDK runtimes

**Files:**
- Modify: `NodeCraft.Vision/Build/VisionRuntimeFiles.txt`
- Modify: `NodeCraft.Vision/Build/VisionPackaging.targets`
- Modify: `NodeCraft.Tests/VisionPackagingTests.cs`
- Modify: `NodeCraft.Tests/VisionProjectTests.cs`
- Modify: `docs/testing/vision-camera-hardware-acceptance.md`

**Interfaces:**
- Consumes: `VisionSdkRoot` for IMV files and `StereoCameraSdkRoot` for the old technical MVSDK files.
- Produces: `StageVisionPlugin` output at `artifacts/Plugins/NodeCraft.Vision` with a combined `lib` directory and preserved license tree.

- [ ] **Step 1: Add a failing dual-root packaging test**

Create temporary fake `Runtime/x64` and `Licenses` trees for both SDK roots, invoke `StageVisionPlugin`, and assert the package contains the IMV-only file, the 3D-only `LibStereoCamera.dll`, and the assembly/manifest.

- [ ] **Step 2: Extend the target with `StereoCameraSdkRoot`**

Read the existing IMV manifest plus the historical stereo manifest, de-duplicate file names, require both roots, copy the IMV list first, then copy 3D-only files, and fail with all missing paths before deleting the destination.

- [ ] **Step 3: Preserve forbidden-file guards**

Reject `StereoCamera.Net.dll`, `NLog.dll`, `NodeCraft.Flow.dll`, `CommonControls.WPF.dll`, and Microsoft logging assemblies in the combined manifest.

- [ ] **Step 4: Run packaging tests with both fake roots**

Expected: combined package succeeds; omitting either SDK root fails with a clear missing-root message.

### Task 5: Full verification and handoff

**Files:**
- No source changes unless a verification failure identifies one of the files above.

**Interfaces:**
- Consumes: the complete solution, test executable, package target, and git working tree.
- Produces: verified coexistence behavior and a clean diff that excludes the user's unrelated `ConnectionLine.cs` change.

- [ ] **Step 1: Run the complete automated test executable**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -c Debug`

Expected: process exit code 0 and every test reports `PASS`.

- [ ] **Step 2: Build the solution**

Run: `dotnet build NodeCraft.sln -c Debug --no-restore`

Expected: exit code 0 and zero compilation errors.

- [ ] **Step 3: Review the final diff**

Run: `git status --short` and `git diff --stat HEAD`.

Expected: the old 3D implementation, dual registration, packaging updates, and tests are present; `NodeCraft.Flow/Flow/ConnectionLine.cs` remains the only pre-existing unrelated modification.
