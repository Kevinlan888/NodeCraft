# Node.Algorithm 面单识别插件实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 新增 Windows x64 的 Node.Algorithm NodeCraft 插件，调用 waybill-recongize 的 C++ C API，输出检测数量、检测详情和带四边形框的 FlowImage。

**Architecture:** 插件在进程内通过 P/Invoke 调用现有 waybill_infer.dll。节点 Session 启动时创建并配置一个 C++ 句柄；每个 iteration 把 FlowImage 转为算法需要的连续像素、复制 native 检测结果，并由纯托管像素绘制器生成 annotatedImage。显式 MSBuild staging target 把插件、模型和 native 依赖组装成宿主可冷加载的包。

**Tech Stack:** C# 9、.NET 8、WPF、NodeCraft.Flow、P/Invoke C ABI、Windows AddDllDirectory、MSBuild targets、NodeCraft 控制台测试跑棒、现有 waybill_infer.dll / ONNX Runtime / OpenCV x64 runtime。

**Spec:** docs/superpowers/specs/2026-08-18-node-algorithm-plugin-design.md

## Global Constraints

- 宿主和插件使用 .NET 8、net8.0-windows、x64 进程。
- 图像类型只能使用 NodeCraft 现有的 FlowImage，显示使用现有 Image Preview 节点。
- 节点结果必须拆分为 count、detections、annotatedImage 三个输出。
- C++ 句柄按 Session 复用；每次 iteration 只处理当前图像。
- 不把 CMake 生成的 native DLL 或模型作为 NodeCraft 源码提交物；由显式 staging target 从算法工程复制到插件包。
- 不把 NodeCraft.Flow.dll、CommonControls.WPF.dll 或 WPF 框架程序集放入插件 lib 目录。
- 原生输入只支持 Bgr24、Rgb24、Mono8；Depth16 必须得到明确错误。
- waybill_process 返回的检测指针必须在下一次原生处理前复制到托管对象。
- 所有实现步骤遵循 RED → GREEN → REFACTOR；生产代码之前必须先观察对应测试因缺少功能而失败。

---

## File Map

### New plugin

- Node.Algorithm/Node.Algorithm.csproj — net8.0-windows、x64、NodeCraft.Flow 引用和 packaging target 导入。
- Node.Algorithm/plugin.json — 插件 manifest。
- Node.Algorithm/Properties/AssemblyInfo.cs — NodeCraft.Tests 的 InternalsVisibleTo。
- Node.Algorithm/Plugin/AlgorithmPlugin.cs — metadata 和节点注册，在 Task 4 创建。
- Node.Algorithm/Nodes/WaybillRecognizerNodeModel.cs — 画布身份、端口模型、可序列化配置。
- Node.Algorithm/Nodes/WaybillRecognizerConfiguration.cs — 读取并验证 WorkflowNode.Inputs。
- Node.Algorithm/Nodes/WaybillRecognizerExecutor.cs — Session 生命周期和三个输出。
- Node.Algorithm/Models/WaybillRecognitionResult.cs — 点、检测项和结果类型。
- Node.Algorithm/Imaging/WaybillOverlayRenderer.cs — FlowImage 像素边框绘制。
- Node.Algorithm/Interop/WaybillInferenceContracts.cs — native session/factory 抽象。
- Node.Algorithm/Interop/WaybillNativeMethods.cs — C ABI 结构体、错误码、P/Invoke。
- Node.Algorithm/Interop/WaybillNativeException.cs — native error 映射。
- Node.Algorithm/Interop/WaybillImageBuffer.cs — FlowImage stride/pin/copy 转换。
- Node.Algorithm/Interop/WaybillNativeSession.cs — 句柄、配置、处理、结果复制和释放。
- Node.Algorithm/Interop/WaybillRuntimeScope.cs — x64 检查和 AddDllDirectory 生命周期。
- Node.Algorithm/Build/AlgorithmPackaging.targets — StageAlgorithmPlugin。
- Node.Algorithm/Build/WaybillRuntimeFiles.txt — native runtime 清单。

### Tests and docs

- NodeCraft.sln、NodeCraft.Tests/NodeCraft.Tests.csproj、NodeCraft.Tests/Program.cs。
- NodeCraft.Tests/AlgorithmPluginTests.cs。
- NodeCraft.Tests/AlgorithmResultTests.cs、AlgorithmOverlayTests.cs。
- NodeCraft.Tests/AlgorithmInteropTests.cs、AlgorithmExecutorTests.cs。
- NodeCraft.Tests/AlgorithmPackagingTests.cs、AlgorithmNativeSmokeTests.cs。
- docs/node-algorithm-plugin.md。

---

### Task 1: 建立插件项目与 NodeModel 契约

**Files:** Create Node.Algorithm/Node.Algorithm.csproj, plugin.json, Properties/AssemblyInfo.cs, Nodes/WaybillRecognizerNodeModel.cs, NodeCraft.Tests/AlgorithmPluginTests.cs; modify NodeCraft.sln, NodeCraft.Tests/NodeCraft.Tests.csproj and Program.cs.

**Interfaces:** Plugin manifest reserves ID nodecraft.algorithm and entry type Node.Algorithm.Plugin.AlgorithmPlugin; NodeModel TypeKey is nodecraft.algorithm.waybill-recognizer; input is image; outputs are count, detections, annotatedImage. The entry type is implemented in Task 4 after the executor seam exists.

- [ ] Step 1: Add configuration only. Create a net8.0-windows, UseWPF, Nullable disable, LangVersion 9.0, PlatformTarget x64 project. Reference NodeCraft.Flow with Private=false, copy plugin.json to output, import Build/AlgorithmPackaging.targets conditionally, add solution/test references, and add InternalsVisibleTo("NodeCraft.Tests"). Do not write plugin registration or native behavior yet.

Use this manifest:

~~~json
{
  "id": "nodecraft.algorithm",
  "entryAssembly": "Node.Algorithm.dll",
  "entryType": "Node.Algorithm.Plugin.AlgorithmPlugin",
  "apiVersion": "1.0",
  "privateLibraryPath": "lib"
}
~~~

- [ ] Step 2: Write the failing tests. Add RunAlgorithmPluginTests() to Program.cs. Test manifest identity and NodeModel port metadata without instantiating the missing plugin entry type:

~~~csharp
Run("Waybill NodeModel exposes image, count, detections and annotated image", () =>
{
    var node = new WaybillRecognizerNodeModel();
    return node.ExecutorType == WaybillRecognizerNodeModel.FlowNodeTypeKey
        && node.InputParameters.Single().PortId == "image"
        && node.InputParameters.Single().Parameter.ParameterType == FlowDataType.Image.Key
        && node.OutputParameters.Select(parameter => parameter.PortId)
            .SequenceEqual(new[] { "count", "detections", "annotatedImage" })
        && node.OutputParameters[0].Parameter.ParameterType == FlowDataType.Number.Key
        && node.OutputParameters[1].Parameter.ParameterType == FlowDataType.Object.Key
        && node.OutputParameters[2].Parameter.ParameterType == FlowDataType.Image.Key;
});
~~~

Run:

~~~powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: compile failure because WaybillRecognizerNodeModel does not exist. Do not add temporary production types.

- [ ] Step 3: Implement WaybillRecognizerNodeModel with the stable TypeKey, one image input parameter and three output parameters. Do not add AlgorithmPlugin yet; Task 4 will add the entry point and registration after the executor/factory contracts are available.

- [ ] Step 4: Run the test runner and confirm the manifest/NodeModel cases pass. Commit:

~~~powershell
git add Node.Algorithm/Node.Algorithm.csproj Node.Algorithm/plugin.json Node.Algorithm/Properties/AssemblyInfo.cs Node.Algorithm/Nodes/WaybillRecognizerNodeModel.cs NodeCraft.sln NodeCraft.Tests/NodeCraft.Tests.csproj NodeCraft.Tests/Program.cs NodeCraft.Tests/AlgorithmPluginTests.cs
git commit -m "feat: scaffold Node.Algorithm plugin contract"
~~~

### Task 2: Add result types and FlowImage overlay rendering

**Files:** Create Node.Algorithm/Models/WaybillRecognitionResult.cs, Node.Algorithm/Imaging/WaybillOverlayRenderer.cs, NodeCraft.Tests/AlgorithmResultTests.cs and AlgorithmOverlayTests.cs.

**Interfaces:** WaybillPoint(int x, int y); WaybillGeometryMethod values ContourQuad=0 and RotatedRectFallback=1; WaybillDetection(float, four points, WaybillGeometryMethod, float); WaybillRecognitionResult(width, height, detections); WaybillOverlayRenderer.Render(FlowImage, IReadOnlyList<WaybillDetection>) -> FlowImage.

- [ ] Step 1: Write failing tests. Create an 8x6 Bgr24 FlowImage with stride 24 and a quadrilateral at (1,1), (6,1), (6,4), (1,4). Assert the result object retains four ordered points and the renderer changes the expected BGR bytes to red while preserving FrameId, timestamps, dimensions, stride and pixel format. Add cases for RGB red-channel placement, Mono8 white lines, padded stride, out-of-bounds coordinates, empty detections, and Depth16 rejection. Run the test runner and observe missing-type compilation failure.

- [ ] Step 2: Implement immutable result objects. Copy input point/detection lists into read-only collections. Reject null lists, null entries, non-positive dimensions and any detection that does not contain exactly four points. Expose Score, Points, GeometryMethod, MaskIou, Width, Height and Detections.

- [ ] Step 3: Implement Render without WPF or BitmapSource. Copy the full stride*height buffer; draw each edge from point i to point (i+1)%4 with a 3-pixel Bresenham line after clamping endpoints to image bounds. Use BGR (0,0,255), RGB (255,0,0), Mono8 255; reject Depth16 with InvalidDataException. Return FlowImage.FromOwnedBuffer with all original metadata.

- [ ] Step 4: Run all managed tests and commit:

~~~powershell
git add Node.Algorithm/Models Node.Algorithm/Imaging NodeCraft.Tests/AlgorithmResultTests.cs NodeCraft.Tests/AlgorithmOverlayTests.cs
git commit -m "feat: render waybill detection overlays as FlowImage"
~~~

### Task 3: Implement C ABI and native session

**Files:** Create Node.Algorithm/Interop/WaybillInferenceContracts.cs, WaybillNativeMethods.cs, WaybillNativeException.cs, WaybillImageBuffer.cs, WaybillNativeSession.cs, WaybillRuntimeScope.cs and NodeCraft.Tests/AlgorithmInteropTests.cs.

**Interfaces:**
- WaybillInferenceOptions has float Confidence, Iou, MinMaskAreaRatio and int MaxDetections, NumThreads.
- IWaybillInferenceSession : IDisposable exposes Process(FlowImage, CancellationToken) -> WaybillRecognitionResult.
- IWaybillInferenceSessionFactory exposes Create(string pluginAssemblyPath, string modelPath, WaybillInferenceOptions) -> IWaybillInferenceSession.
- WaybillNativeSessionFactory implements the factory.

- [ ] Step 1: Write failing ABI tests. Assert Marshal.SizeOf equals 24 for NativeWaybillConfig, 44 for NativeWaybillDetection and 24 for NativeWaybillResult; assert GeometryMethod offset 36 and MaskIou offset 40. Add pixel buffer tests for BGR stride 12 to packed rows, packed RGB, Mono8, and Depth16 InvalidDataException. Add error name checks for code 2 = WAYBILL_ERR_MODEL_LOAD and unknown = WAYBILL_ERR_UNKNOWN. Run and observe missing-type compilation failure.

- [ ] Step 2: Add sequential interop structs matching waybill_infer.h:

~~~csharp
[StructLayout(LayoutKind.Sequential)]
internal struct NativeWaybillConfig
{
    internal float Confidence, Iou, MinMaskAreaRatio;
    internal int MaxDetections, NumThreads, InputFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWaybillDetection
{
    internal float Score;
    internal int Point0X, Point0Y, Point1X, Point1Y;
    internal int Point2X, Point2Y, Point3X, Point3Y;
    internal int GeometryMethod;
    internal float MaskIou;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWaybillResult
{
    internal int Width, Height, Count;
    internal IntPtr Detections;
}
~~~

Declare all six C functions from waybill_infer.dll with CallingConvention.Cdecl and ExactSpelling=true; model_path uses LPUTF8Str.

- [ ] Step 3: Implement WaybillImageBuffer.Create(FlowImage). For packed array-backed images retain the array segment and pin only during the P/Invoke; for padded or non-array memory allocate a packed buffer and copy each row. Expose Width, Height, InputFormat, Pointer and Dispose. Map Bgr24/Rgb24/Mono8 to native values 0/1/2 and reject Depth16 before pinning.

- [ ] Step 4: Implement WaybillRuntimeScope.Acquire. Reject non-Windows and 32-bit processes, resolve plugin-root/lib, call AddDllDirectory, reference-count repeated acquisition of the same directory, and call RemoveDllDirectory at count zero. Never change PATH or machine/user environment variables.

Implement WaybillNativeSessionFactory and WaybillNativeSession. Acquire scope before P/Invoke; create the handle; map WaybillInferenceOptions to NativeWaybillConfig; call set_cfg; on Process check cancellation, prepare/pin the image, update InputFormat when pixel format changes, call process, copy every native detection into managed WaybillDetection, call release_detections, and return a WaybillRecognitionResult. On all failure paths release handle and scope; Dispose releases exactly once.

- [ ] Step 5: Run the managed test runner; verify no native DLL is needed for ABI/image/error tests. Commit:

~~~powershell
git add Node.Algorithm/Interop NodeCraft.Tests/AlgorithmInteropTests.cs
git commit -m "feat: bind waybill C ABI and native session"
~~~

### Task 4: Connect NodeModel configuration and executor

**Files:** Modify Node.Algorithm/Nodes/WaybillRecognizerNodeModel.cs; create Node.Algorithm/Plugin/AlgorithmPlugin.cs, Nodes/WaybillRecognizerConfiguration.cs, Nodes/WaybillRecognizerExecutor.cs and NodeCraft.Tests/AlgorithmExecutorTests.cs; modify NodeCraft.Tests/AlgorithmPluginTests.cs.

**Interfaces:**
- WaybillRecognizerNodeModel properties: ModelPath, Confidence, Iou, MinMaskAreaRatio, MaxDetections, NumThreads.
- WaybillRecognizerConfiguration.Read(WorkflowNode) returns validated ModelPath and WaybillInferenceOptions.
- WaybillRecognizerExecutor implements IFlowNodeExecutor and IFlowNodeSessionLifecycle.

- [ ] Step 1: Write failing fake-session tests. Use RecordingInferenceSessionFactory and fake session returning one fixed detection. Build a WorkflowNode through WaybillRecognizerNodeModel.WriteWorkflowInputs, call StartSessionAsync, ExecuteAsync with TestImages.Bgr(8,6), and StopSessionAsync. Assert factory CreateCount=1, ProcessCount=1, output count=1, detections is IReadOnlyList<WaybillDetection>, annotatedImage is FlowImage, and DisposeCount=1. Add missing image, Depth16, invalid confidence and native-failure cases. Also add a registration test that constructs AlgorithmPlugin.CreateForTesting(factory, assemblyPath), calls Register, and asserts the four TypeKeys/types and NodeFactory metadata. Run and observe compile failure.

- [ ] Step 2: Implement NodeModel properties and WriteWorkflowInputs. Defaults are ModelPath=models/baseline-2-960.onnx, Confidence=.35, Iou=.50, MinMaskAreaRatio=.0001, MaxDetections=100, NumThreads=0. Write keys modelPath, confidence, iou, minMaskAreaRatio, maxDetections, numThreads; null ModelPath becomes empty string. WaybillRecognizerConfiguration.Read applies defaults for absent keys, rejects NaN/infinity and all out-of-range values, converts float fields to float, and preserves ModelPath.

- [ ] Step 3: Implement executor. StartSessionAsync resolves a relative ModelPath against the plugin assembly directory, calls the injected factory and stores the session; failure leaves no retained session. ExecuteAsync checks cancellation, requires inputs["image"] to be FlowImage, calls Process and returns exactly:

~~~csharp
new Dictionary<string, object>
{
    ["count"] = result.Detections.Count,
    ["detections"] = result.Detections,
    ["annotatedImage"] = WaybillOverlayRenderer.Render(image, result.Detections),
};
~~~

StopSessionAsync detaches and disposes the session and is safe after a failed start.

- [ ] Step 4: Implement AlgorithmPlugin.Metadata and Register. Set NodeModelType, NodeFactory, palette name Waybill Recognizer, category Algorithm and description. Ensure each ExecutorFactory call creates a fresh executor and uses the injected factory plus assembly path. Run all tests and commit:

~~~powershell
git add Node.Algorithm/Nodes Node.Algorithm/Plugin/AlgorithmPlugin.cs NodeCraft.Tests/AlgorithmPluginTests.cs NodeCraft.Tests/AlgorithmExecutorTests.cs
git commit -m "feat: add waybill recognizer flow node"
~~~

### Task 5: Add explicit native packaging and fake staging verification

**Files:** Create Node.Algorithm/Build/WaybillRuntimeFiles.txt, AlgorithmPackaging.targets and NodeCraft.Tests/AlgorithmPackagingTests.cs; modify Node.Algorithm/Node.Algorithm.csproj.

**Interfaces:** Target StageAlgorithmPlugin; properties AlgorithmPackageRoot, WaybillSourceRoot, WaybillRuntimeRoot, WaybillOpenCvRuntimeRoot and WaybillModelPath; package root managed files, models/baseline-2-960.onnx, and native lib files.

- [ ] Step 1: Write failing tests. Static checks require StageAlgorithmPlugin, all five properties, exact destination cleanup, and no shared host assemblies in the runtime list. A fake staging test creates temp runtime/model directories, writes one byte to each required file, invokes dotnet msbuild project -t:StageAlgorithmPlugin with all properties overridden, asserts plugin.json, Node.Algorithm.dll, model and every listed native file, then deletes only the unique package root. Run and observe missing-target failure.

- [ ] Step 2: Create WaybillRuntimeFiles.txt:

~~~text
waybill_infer.dll
onnxruntime.dll
msvcp140.dll
msvcp140_1.dll
msvcp140_2.dll
msvcp140_atomic_wait.dll
msvcp140_codecvt_ids.dll
vcruntime140.dll
vcruntime140_1.dll
~~~

OpenCV is copied separately as opencv_world4110.dll.

- [ ] Step 3: Implement AlgorithmPackaging.targets. Defaults:

~~~xml
<AlgorithmPackageRoot Condition="'$(AlgorithmPackageRoot)' == ''">$(MSBuildThisFileDirectory)..\..\artifacts\Plugins\Node.Algorithm</AlgorithmPackageRoot>
<WaybillSourceRoot Condition="'$(WaybillSourceRoot)' == ''">$(MSBuildThisFileDirectory)..\..\..\waybill-recongize</WaybillSourceRoot>
<WaybillRuntimeRoot Condition="'$(WaybillRuntimeRoot)' == ''">$(WaybillSourceRoot)\build-win</WaybillRuntimeRoot>
<WaybillOpenCvRuntimeRoot Condition="'$(WaybillOpenCvRuntimeRoot)' == ''">$(WaybillSourceRoot)\..\opencv-extract\opencv\build\x64\vc16\bin</WaybillOpenCvRuntimeRoot>
<WaybillModelPath Condition="'$(WaybillModelPath)' == ''">$(WaybillSourceRoot)\artifacts\candidates\baseline-2-960.onnx</WaybillModelPath>
~~~

StageAlgorithmPlugin depends on Build but is not BeforeTargets/AfterTargets. Read the runtime list, validate every runtime file under WaybillRuntimeRoot, validate OpenCV, model, TargetPath and plugin.json, aggregate missing paths in one Error, then remove only AlgorithmPackageRoot, create lib/models, copy managed files, copy runtime list and OpenCV to lib, and copy the model to models/baseline-2-960.onnx. Add forbidden shared-assembly checks.

- [ ] Step 4: Run fake staging and all managed tests. Commit:

~~~powershell
git add Node.Algorithm/Build Node.Algorithm/Node.Algorithm.csproj NodeCraft.Tests/AlgorithmPackagingTests.cs
git commit -m "feat: stage Node.Algorithm native runtime package"
~~~

### Task 6: Add docs and real native smoke

**Files:** Create docs/node-algorithm-plugin.md and NodeCraft.Tests/AlgorithmNativeSmokeTests.cs; modify NodeCraft.Tests/Program.cs.

**Interfaces:** Smoke switch NODECRAFT_WAYBILL_NATIVE_SMOKE=1; paths WAYBILL_PLUGIN_PACKAGE_ROOT, WAYBILL_MODEL_PATH and WAYBILL_IMAGE_PATH. Staging paths remain command-line properties.

- [ ] Step 1: Write a disabled-by-default smoke test. Without the switch, report PASS with a skip label. With the switch, require the package root, model path and image path, load the JPEG through WPF BitmapDecoder on an STA callback, convert to Bgr24 FlowImage, create the production native session with Path.Combine(packageRoot, "Node.Algorithm.dll"), process, assert positive width/height and non-null detections, render annotated FlowImage, and dispose in finally.

- [ ] Step 2: Add docs with this current-machine staging command:

~~~powershell
dotnet build NodeCraft.sln
dotnet msbuild Node.Algorithm\Node.Algorithm.csproj -t:StageAlgorithmPlugin -p:Configuration=Release -p:WaybillSourceRoot="C:\Users\kevin\cs\waybill-recongize" -p:WaybillOpenCvRuntimeRoot="C:\Users\kevin\cs\opencv-extract\opencv\build\x64\vc16\bin"
~~~

Document graph wiring FlowImage source → Waybill Recognizer.image, annotatedImage → Image Preview.image, and count/detections as separate outputs. State that waybill_infer.dll and onnxruntime.dll come from the C++ build while OpenCV/MSVC files are staged into the same lib directory.

- [ ] Step 3: Run full verification and commit:

~~~powershell
dotnet build NodeCraft.sln
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
dotnet run --project NodeCraft.Cli.Tests/NodeCraft.Cli.Tests.csproj
dotnet msbuild Node.Algorithm\Node.Algorithm.csproj -t:StageAlgorithmPlugin -p:Configuration=Release -p:WaybillSourceRoot="C:\Users\kevin\cs\waybill-recongize" -p:WaybillOpenCvRuntimeRoot="C:\Users\kevin\cs\opencv-extract\opencv\build\x64\vc16\bin"
$env:NODECRAFT_WAYBILL_NATIVE_SMOKE = "1"
$env:WAYBILL_PLUGIN_PACKAGE_ROOT = "$(Resolve-Path artifacts\Plugins\Node.Algorithm)"
$env:WAYBILL_MODEL_PATH = "C:\Users\kevin\cs\waybill-recongize\artifacts\candidates\baseline-2-960.onnx"
$env:WAYBILL_IMAGE_PATH = "C:\Users\kevin\cs\waybill-recongize\tests\fixtures\images\positive.jpg"
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj -f net8.0-windows
~~~

Expected: solution and both managed runners pass; enabled native smoke creates a handle and returns a valid result without a DLL load error. Commit:

~~~powershell
git add docs/node-algorithm-plugin.md NodeCraft.Tests/AlgorithmNativeSmokeTests.cs NodeCraft.Tests/Program.cs
git commit -m "docs: document and smoke test Node.Algorithm integration"
~~~

### Task 7: Final package inspection and handoff

**Files:** Verify artifacts/Plugins/Node.Algorithm and all files from Tasks 1–6.

- [ ] Step 1: Stage with the actual algorithm/OpenCV paths and inspect recursively. Confirm plugin.json and Node.Algorithm.dll at root; baseline model under models; waybill_infer.dll, onnxruntime.dll, opencv_world4110.dll and listed MSVC runtime files under lib; no NodeCraft.Flow/CommonControls/WPF shared assembly.

- [ ] Step 2: Cold-load a copy of the staged package through the existing PluginLoader.LoadAll test path and assert successful nodecraft.algorithm result plus registry resolution of nodecraft.algorithm.waybill-recognizer.

- [ ] Step 3: Run solution build, both managed runners, package inspection and enabled native smoke again. Record actual output and use verification-before-completion before claiming completion.

- [ ] Step 4: Commit only final test/documentation adjustments:

~~~powershell
git add Node.Algorithm NodeCraft.sln NodeCraft.Tests docs/node-algorithm-plugin.md
git commit -m "chore: finalize Node.Algorithm plugin integration"
~~~
