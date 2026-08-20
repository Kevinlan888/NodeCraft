# Node.Algorithm 面单识别插件

`Node.Algorithm` 将 `C:\Users\kevin\cs\waybill-recongize` 中已有的 Windows x64 C++ 面单识别算法接入 NodeCraft。插件使用现有的 C ABI，不修改算法工程源码，也不新增图像类型。

## 节点端口

`Waybill Recognizer` 的端口已经拆分为：

| 方向 | 端口 | 类型 | 说明 |
| --- | --- | --- | --- |
| 输入 | `image` | `FlowImage` | 支持 BGR24、RGB24、Mono8；Depth16 会明确拒绝 |
| 输出 | `count` | Number | 当前图像的面单数量 |
| 输出 | `detections` | Object | `IReadOnlyList<WaybillDetection>`，包含分数、四个角点、几何方式和 mask IoU |
| 输出 | `annotatedImage` | `FlowImage` | 原图的托管副本，已绘制检测四边形 |

图像输出只能连接到现有的 `Image Preview` 节点显示。典型连线是：

```text
FlowImage source ── image ──> Waybill Recognizer
                                  ├── count ───────> 数值处理节点
                                  ├── detections ──> 结果处理节点
                                  └── annotatedImage ──> Image Preview.image
```

`annotatedImage` 始终是 `FlowImage`，保留宽高、stride、像素格式和帧元数据；没有检测时也会返回内容相同的新 `FlowImage`。

## 默认配置

节点保存的模型和推理参数为：

- `ModelPath`: `models/baseline-2-960.onnx`
- `Confidence`: `0.35`
- `Iou`: `0.50`
- `MinMaskAreaRatio`: `0.0001`
- `MaxDetections`: `100`
- `NumThreads`: `0`（交给 ONNX Runtime 选择）

相对模型路径相对于插件包根目录解析。

## 构建和 staging

普通构建不会隐式复制 C++ 运行时。确认算法工程、OpenCV 和模型已生成后，在仓库根目录执行：

```powershell
dotnet build .\NodeCraft.sln

dotnet msbuild .\Node.Algorithm\Node.Algorithm.csproj `
  -t:StageAlgorithmPlugin `
  -p:Configuration=Release `
  -p:WaybillSourceRoot="C:\Users\kevin\cs\waybill-recongize" `
  -p:WaybillOpenCvRuntimeRoot="C:\Users\kevin\cs\opencv-extract\opencv\build\x64\vc16\bin"
```

默认包目录是 `artifacts\Plugins\Node.Algorithm`：

```text
Node.Algorithm/
├── plugin.json
├── Node.Algorithm.dll
├── models/
│   └── baseline-2-960.onnx
└── lib/
    ├── waybill_infer.dll
    ├── onnxruntime.dll
    ├── opencv_world4110.dll
    ├── msvcp140.dll
    ├── msvcp140_1.dll
    ├── msvcp140_2.dll
    ├── msvcp140_atomic_wait.dll
    ├── msvcp140_codecvt_ids.dll
    ├── vcruntime140.dll
    └── vcruntime140_1.dll
```

staging 只删除并重建明确的 `AlgorithmPackageRoot`，不会修改系统 `PATH` 或全局环境变量。插件包不复制 `NodeCraft.Flow.dll`、`CommonControls.WPF.dll` 或宿主的 Microsoft logging 程序集。当前算法依赖 Windows x64 进程、OpenCV 4.11.0、ONNX Runtime 和对应 MSVC 运行库。

## 真实 native 冒烟

普通测试不要求本机存在算法 DLL。完成 staging 后，可以用仓库默认路径启用真实推理：

```powershell
$env:NODECRAFT_WAYBILL_NATIVE_SMOKE = "1"
$env:WAYBILL_PLUGIN_PACKAGE_ROOT = "C:\Users\kevin\cs\NodeCraft\artifacts\Plugins\Node.Algorithm"
$env:WAYBILL_MODEL_PATH = "C:\Users\kevin\cs\NodeCraft\artifacts\Plugins\Node.Algorithm\models\baseline-2-960.onnx"
$env:WAYBILL_IMAGE_PATH = "C:\Users\kevin\cs\waybill-recongize\tests\fixtures\images\positive.jpg"
dotnet run --project .\NodeCraft.Tests\NodeCraft.Tests.csproj
```

冒烟测试在 STA 线程用 WPF 解码 JPEG，再转换为 `FlowImage`；它会创建真实 native session，执行一次 `waybill_process`，复制检测数组，并调用 `WaybillOverlayRenderer` 生成可交给 `Image Preview` 的图像。未设置 `NODECRAFT_WAYBILL_NATIVE_SMOKE=1` 时，该项只报告跳过，不阻塞普通托管测试。

如果 native 启动失败，先检查 staging 的 `lib` 中是否有完整 DLL 集合、模型路径是否存在，以及 NodeCraft 进程是否为 x64。`WaybillNativeException` 会保留 C API 的 `WAYBILL_ERR_*` 错误名。
