# NodeCraft.Vision IMV 相机集成设计

状态：已确认方向

日期：2026-08-14

目标平台：Windows x64、.NET 8、WPF

SDK 来源：`D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv`

## 1. 目标

将现有专用的 `NodeCraft.Vision.StereoCamera` 插件整体改名为
`NodeCraft.Vision`，并把底层相机实现从 `LibStereoCamera.dll` 替换为用户提供的
IMV C API（`IMVApi.h`、`MVSDKmd.dll`）。完成后插件在 NodeCraft 流程中提供一个可持续取流的
Vision 相机节点和一个通用图片预览节点。

本次改造包含：

1. 项目目录、项目文件、程序集、根命名空间、插件清单、类型名称和文档统一使用
   `NodeCraft.Vision` 命名。
2. 使用 IMV SDK 的同步取帧 API，在现有执行会话生命周期中持续采集最新帧。
3. 将每一帧转换为公共的 `NodeCraft.Flow.FlowImage`，供后续算法节点和图片预览节点消费。
4. 从外部 `VisionSdkRoot` 打包 SDK 运行时 DLL、GenICam producer 文件和许可证；不把厂商二进制
   提交到 Git。
5. 在没有物理相机的情况下，用假原生 API 验证句柄清理、像素格式转换、帧生命周期和插件注册。

## 2. 非目标

- 不再提供旧立体相机的深度图、彩色/深度双标定或 `LibStereoCamera.dll` 适配。
- 不实现相机参数编辑器（曝光、增益、白平衡、触发源等）。
- 不实现断线自动重连、设备热插拔 UX 或相机发现列表 UI。
- 不提交 `D:\Downloads\...` 中的 SDK 文件，也不要求宿主机全局安装 SDK 或修改系统 PATH。
- 保持 `NodeCraft.Flow` 的执行会话契约不变，并让 `FlowImage` 与 `CameraCalibration` 完全解耦；
  标定数据只作为独立的 `FlowDataType.CameraCalibration` 值传递。

## 3. 命名和插件身份

项目目录和项目文件改为：

```text
NodeCraft.Vision\
└── NodeCraft.Vision.csproj
```

程序集、根命名空间和主要类型使用以下身份：

| 项目项 | 新值 |
| --- | --- |
| 根命名空间 | `NodeCraft.Vision` |
| 插件 ID | `nodecraft.vision` |
| 显示名称 | `Vision` |
| 程序集 | `NodeCraft.Vision.dll` |
| 入口类型 | `NodeCraft.Vision.Plugin.VisionPlugin` |
| 相机节点类型键 | `nodecraft.vision.camera` |
| 图片预览类型键 | `nodecraft.vision.image-preview` |

旧 `nodecraft.vision.stereo-camera.camera` 类型键不保留为新节点的别名，因为旧节点的
输出契约包含 `colorImage`、`depthImage` 和标定，而新 IMV 相机只提供一张图像；伪造兼容会让
旧流程在运行时得到缺失或错误类型的数据。旧流程需要重新放置 Vision Camera 节点并重新连线。

## 4. 节点和数据流

### 4.1 Vision Camera 节点

节点模型为 `VisionCameraNodeModel`，持久化属性保留一个 `IpAddress` 字符串。节点注册信息为：

- 类别：`Vision`
- 输入：无数据端口；IP 地址由节点模型属性写入 `WorkflowNode.Inputs["ipAddress"]`
- 输出：`image`，类型为 `FlowDataType.Image`；IMV `Grab` API 不提供标定输出，本节点不增加
  标定端口
- 编辑器：复用现有 IP 地址编辑器，标题改为 `Vision Camera`

节点执行器 `VisionCameraExecutor` 实现现有的：

- `IFlowNodeExecutor`
- `IFlowNodeSessionLifecycle`
- `IFlowIterationSource`

执行会话启动时创建 `VisionCameraCaptureSession`。会话启动、每轮迭代和清理过程如下：

```text
Acquire native runtime scope
        |
IMV_EnumDevices / validate available device
        |
IMV_CreateHandle(modeByIPAddress)
        |
IMV_Open -> TriggerMode = Off -> IMV_StartGrabbing
        |
capture loop: IMV_GetFrame -> copy/convert -> release frame -> publish latest image
        |
PrepareIterationAsync waits for a newer mailbox item
        |
ExecuteAsync returns the current FlowImage
        |
IMV_StopGrabbing -> IMV_Close -> IMV_DestroyHandle -> release runtime scope
```

相机仍使用现有的单槽 `LatestFrameMailbox<T>` 语义：采集线程始终优先保留最新完整帧，算法执行
跟不上时允许丢弃中间帧；多个流程迭代不得并行。

### 4.2 Image Preview 节点

现有 `FlowImagePreviewNodeModel`、`FlowImagePreviewExecutor` 和预览视图迁移到
`NodeCraft.Vision` 命名空间，保留其公共图片输入、原样输出和 UI 更新行为。其类型键更新为
`nodecraft.vision.image-preview`，不保存运行时图像到 `.flow.xml`。

## 5. IMV 原生互操作边界

所有厂商类型只允许存在于 `NodeCraft.Vision.VendorInterop` 内，不能泄漏到
`NodeCraft.Flow` 或宿主项目。

### 5.1 调用约定和 API

IMV 头文件在 Windows 下定义 `IMV_CALL` 为 `__stdcall`，因此所有 P/Invoke 使用：

```csharp
[DllImport("MVSDKmd.dll", CallingConvention = CallingConvention.StdCall,
    ExactSpelling = true)]
```

首版封装以下 API：

- `IMV_EnumDevices`
- `IMV_CreateHandle`
- `IMV_DestroyHandle`
- `IMV_Open`
- `IMV_Close`
- `IMV_SetEnumFeatureSymbol`
- `IMV_StartGrabbing`
- `IMV_StopGrabbing`
- `IMV_GetFrame`
- `IMV_ReleaseFrame`
- `IMV_PixelConvert`

`IMV_CreateHandle` 的 IP 参数通过临时 ANSI 非托管字符串传递，并确保调用返回后立即释放该
字符串。设备列表只在原生边界内使用，不把 SDK 内部缓存的 `IMV_DeviceInfo*` 暴露给业务层。

### 5.2 帧结构和清理

按 `IMVDefines.h` 定义镜像 `IMV_FrameInfo` 和 `IMV_Frame`，至少验证以下字段：

- `status == 0`
- `width > 0`、`height > 0`
- `size > 0` 且不超过 `int.MaxValue`
- `pData != IntPtr.Zero`
- `size` 能够描述完整的行数据

`IMV_GetFrame` 成功后，无论复制、格式识别或格式转换是否抛出异常，都必须在 `finally` 中调用
`IMV_ReleaseFrame`。复制出的托管缓冲区独立于 SDK 内部缓存，发布到邮箱后不再持有原生帧指针。

### 5.3 像素格式

直接映射以下格式：

| IMV 格式 | FlowImage 格式 | 处理 |
| --- | --- | --- |
| `gvspPixelMono8` | `Mono8` | 直接复制 |
| `gvspPixelBGR8` | `Bgr24` | 直接复制 |
| `gvspPixelRGB8` | `Rgb24` | 直接复制 |

常见 8-bit Bayer 格式通过 `IMV_PixelConvert` 转换为 `gvspPixelBGR8` 后再创建 `FlowImage`。
首版对 10/12/16-bit、packed、YUV 和其他未实现格式抛出包含原始枚举值的
`InvalidDataException`，不得静默把格式当作 Mono8。

直接复制时 stride 从 `size / height` 推导，并验证不小于 `width * bytesPerPixel`；转换输出使用
无 padding 的 `width * height * 3` BGR 缓冲区。帧 ID、设备时间戳和采集时间写入 `FlowImage`
元数据，不写入标定对象。

`FlowImage` 工厂不再接收 `CameraCalibration` 参数，`FlowImage` 不持有标定属性。
`CameraCalibration` 仍保留在 `NodeCraft.Flow`，作为独立数据类型；现有 3D 相机的
`colorCalibration` 和 `depthCalibration` 端口继续输出独立对象，未来 Vision 若通过其他 SDK
接口获得标定，也必须通过独立端口或独立节点提供。

### 5.4 错误和安全句柄

IMV 返回值不等于 `IMV_OK` 时抛出 `VisionNativeException`，异常至少保存操作名和错误码。设备
句柄使用 `SafeHandle` 封装，但 `IMV_StopGrabbing` 和 `IMV_Close` 必须在销毁句柄前由设备对象
显式执行，避免把有顺序要求的状态操作隐藏到句柄析构器中。

## 6. Native runtime 和打包

项目新增 `Build\VisionRuntimeFiles.txt` 和 `Build\VisionPackaging.targets`。MSBuild 属性约定为：

```text
VisionSdkRoot       = D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv
VisionPackageRoot   = $(MSBuildThisFileDirectory)..\..\artifacts\Plugins\NodeCraft.Vision
```

`VisionSdkRoot` 不写入项目文件默认值，构建时显式传入，例如：

```powershell
dotnet build NodeCraft.sln -p:VisionSdkRoot="D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv"
```

打包目标从 `$(VisionSdkRoot)\Runtime\x64` 复制清单中的运行时文件到插件包的 `lib`，从
`$(VisionSdkRoot)\Licenses` 复制许可证到 `licenses`。清单至少包含当前 SDK 运行时中用于
IMV 抓流的 `MVSDKmd.dll`、GenICam 依赖、`MVProducerGEV.cti`、`MVProducerU3V.cti`、图像转换
依赖和日志配置；每个文件缺失都使 staging 失败。

插件运行时范围继续使用 Windows `AddDllDirectory`，并设置进程级 `MV_GENICAM_64` 到包内 `lib`
目录。引用计数、目录恢复和重复释放保持幂等。插件包不能复制 `NodeCraft.Flow.dll`、
`CommonControls.WPF.dll`、宿主 logging 程序集或任何旧 `StereoCamera`/`LibStereoCamera` 文件。

## 7. 测试设计

继续使用仓库现有的 Windows 控制台测试跑棒，不引入新的测试框架。测试分为：

1. **公共图片契约**：验证 `FlowImage` 只包含像素/帧元数据，工厂不接收标定参数；验证
   `CameraCalibration` 仍可作为独立值创建和传递。
2. **项目和身份**：验证 `.sln`、`.csproj`、根命名空间、程序集、`plugin.json`、节点类型键和
   新目录中不存在旧 `StereoCamera` 身份。
3. **IMV 互操作布局**：验证 `IMV_FrameInfo`、`IMV_Frame`、像素枚举和 P/Invoke 调用约定的
   关键尺寸/值；测试不需要加载真实 DLL。
4. **图像转换**：用托管分配的缓冲区验证 Mono8/BGR8/RGB8、Bayer 转换参数、stride 检查、
   不支持格式和无效指针错误。
5. **句柄生命周期**：用 `IImvNativeApi` 假实现验证成功路径和每个失败路径均释放帧，停止顺序
   为 Stop → Close → Destroy，重复 Stop/Dispose 不重复释放。
6. **采集会话**：复用现有会话测试模式，验证最新帧邮箱、取消、启动失败清理、无有效帧超时和
   设备断开错误传播。
7. **插件和节点**：验证 Vision 插件注册相机和预览节点、端口类型、IP 持久化、执行结果应用
   和预览内容刷新。
8. **打包**：验证 runtime 清单不包含旧立体相机、共享程序集或 vendor NLog，并验证 staging
   目标引用 `VisionSdkRoot`、`MVSDKmd.dll` 和许可证目录。

真实相机验收单独记录在 `docs/testing/vision-camera-hardware-acceptance.md`，只在连接到实际
IMV 相机和提供 SDK runtime 时执行；普通构建和自动测试不依赖相机在线。

## 8. 完成标准

- `dotnet build NodeCraft.sln` 在没有 SDK 参数时仍能编译托管项目；仅执行插件 staging 时要求
  `VisionSdkRoot`。
- Windows x64 测试跑棒输出 `ALL PASS`。
- 使用 SDK root staging 后，插件包入口为 `NodeCraft.Vision.dll`，且 `lib` 中包含
  `MVSDKmd.dll` 及其清单依赖。
- 宿主加载 `NodeCraft.Vision` 后，Vision Camera 节点能按 IP 启动/停止取流，图片预览能显示
  最新 `FlowImage`。
- 所有旧 `StereoCamera` C API、旧打包目标和旧插件入口均不再参与构建或加载。
