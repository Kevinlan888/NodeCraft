# NodeCraft 实时相机与图片预览插件设计

状态：已批准

日期：2026-08-13

目标平台：Windows x64、.NET 8、WPF

## 1. 概述

本设计为 NodeCraft 增加两种流程执行模式，并新增一个独立的视觉插件 `NodeCraft.Vision.StereoCamera`：

- **执行一次**：创建一次执行会话，启动相机，等待一组最新的彩色图和深度图，执行一轮完整 DAG，然后停止并释放全部会话资源。
- **持续运行**：创建一次执行会话并复用节点执行器与相机连接；相机持续采集，每次选取当前最新且尚未处理的帧组，完整执行一轮 DAG，直到停止或失败。
- **相机 Node**：按持久化 IP 地址连接相机，输出同一 SDK 帧中的彩色图、深度图及两份独立标定信息。
- **图片预览 Node**：消费公共图片类型，预览彩色图或归一化后的深度图，并将原图片原样输出。

公共图片、标定和执行生命周期契约位于 `NodeCraft.Flow`。厂商互操作代码和原生依赖完全封装在相机插件中，其他视觉插件不依赖 `StereoCamera.Net` 或厂商原生类型。

## 2. 目标与非目标

### 2.1 目标

1. 为 NodeCraft 提供可复用、可取消且保证清理的执行会话。
2. 保留现有一次性执行 API 的兼容行为。
3. 持续模式始终优先处理最新帧，允许丢弃算法来不及处理的中间帧。
4. 保证彩色图和深度图来自同一个厂商 `Frame`。
5. 提供厂商无关、可供后续算法 Node 使用的图片和相机标定契约。
6. 在 Windows x64 上可靠管理相机、帧、图片和标定管理器等原生句柄。
7. 不把用户提供的厂商 SDK 二进制提交到 Git。

### 2.2 首版非目标

- 曝光、增益及其他相机参数配置。
- 自动重连、断线续跑或故障后跳过当前节点继续运行。
- 多相机硬件同步或跨相机时间对齐。
- 左右 IR、左右校正图、鸟瞰图、点云和算法结果。
- 将流程引擎改成节点主动推送的响应式引擎。
- 保存图片像素或运行时标定对象到 `.flow.xml`。
- 将现有按文件路径工作的内置 `Image Preview` 节点迁移为新类型。

## 3. 已有系统约束

- 当前 `GraphExecutor` 按拓扑顺序串行执行 DAG，每次执行时重新创建节点执行器。
- 当前插件 API 由共享程序集 `NodeCraft.Flow` 提供，插件在独立 `AssemblyLoadContext` 中加载。
- 自定义节点 UI 通过 `FlowNodeRegistration.ContentFactory` 创建；执行结束后由 `ExecutionResultHandler` 更新模型。
- `.flow.xml` 以稳定 `TypeKey` 标识节点，自定义简单属性由现有序列化器持久化。
- 厂商提供的 `StereoCamera.Net.dll` 目标框架为 `.NET Framework 4.5`，内部通过 Cdecl P/Invoke 调用 `LibStereoCamera.dll`，并公开 `System.Drawing.Bitmap` API。
- 厂商同时提供公开 C API 头文件和文档，因此迁移后的互操作签名以 C API 头文件为权威来源，以原托管包装行为为辅助参考。

## 4. 总体架构

```text
MainWindow / FlowPage
        |
        | Run Once / Run Continuously / Stop
        v
GraphExecutionSession (NodeCraft.Flow)
        |
        | cached executor per workflow node
        v
IFlowNodeExecutor + optional session lifecycle / iteration source
        |
        +---------------- ordinary nodes
        |
        +---------------- Stereo Camera executor
                              |
                              +-- capture loop
                              +-- one-slot latest-frame mailbox
                              +-- embedded .NET 8 vendor interop
                              +-- native x64 SDK runtime

FlowImage / CameraCalibration (NodeCraft.Flow)
        |
        +---------------- Image Preview
        +---------------- future algorithm plugins
```

职责边界：

- `NodeCraft.Flow`：执行会话、生命周期接口、公共数据契约和端口类型。
- `NodeCraft`：运行命令、UI 状态、停止与窗口关闭协调。
- `NodeCraft.Vision.StereoCamera`：厂商 SDK 适配、相机与预览节点、插件打包。

## 5. 执行会话

### 5.1 会话对象

新增 `GraphExecutionSession`。会话以已验证的 `WorkflowDocument` 和 `FlowNodeRegistry` 创建，并为每个工作流节点只调用一次 `ExecutorFactory`。执行器按节点 ID 缓存在会话内。

会话至少提供以下异步操作：

- `StartAsync(CancellationToken)`
- `ExecuteIterationAsync(CancellationToken)`
- `StopAsync()`
- `DisposeAsync()`

会话状态只能按以下路径转换：

```text
Created -> Starting -> Running -> Stopping -> Stopped
                    \-> Faulted -> Stopping -> Stopped
```

重复停止和释放必须幂等；启动失败后仍进入清理路径。

### 5.2 可选节点生命周期

在不修改 `IFlowNodeExecutor.ExecuteAsync` 的前提下，新增可选接口：

- `IFlowNodeSessionLifecycle`：会话开始和停止时管理连接、线程、文件或其他长期资源。
- `IFlowIterationSource`：等待并准备下一次迭代所需的新输入；相机用它把最新帧设为当前迭代帧。

生命周期启动上下文包含当前 `WorkflowNode`、`FlowNodeDefinition` 和必要的会话信息，使相机执行器可以读取持久化 IP。普通现有节点无需实现这两个接口。

生命周期节点按 DAG 拓扑顺序启动。停止时只处理已经成功启动的节点，并按启动顺序的反序停止。清理使用独立的清理令牌，不能直接复用已经取消的运行令牌。

### 5.3 每轮执行

每次 `ExecuteIterationAsync`：

1. 让所有 `IFlowIterationSource` 等待并锁定各自下一份输入。
2. 创建新的 `FlowExecutionContext`，不复用上一轮的值、状态或异常。
3. 按拓扑顺序执行完整 DAG。
4. 同一轮内仍使用现有 required-input、control-flow 和 skip 语义。
5. 返回该轮上下文，由宿主在 UI 线程应用展示结果。

多个迭代不得并行。若一个流程包含多个流式源，首版依次等待它们各自的最新值，但不承诺跨设备硬件同步。

### 5.4 执行一次

现有 `GraphExecutor.ExecuteAsync()` 保留，并改为以下兼容包装：

```text
validate -> create session -> start -> execute one iteration -> stop/dispose
```

相机流程会在启动抓流后等待第一组完整帧，执行一轮后立即执行 `StopGrabbing -> Disconnect -> release handles`。

### 5.5 持续运行

持续运行只创建一个会话：

```text
start once
while not cancelled:
    wait for latest source data
    execute one complete DAG iteration
    marshal display results to UI and await completion
stop once
```

UI 更新也是串行背压的一部分：宿主等待当前轮结果在 UI 线程应用完成后才开始下一轮，因此不会向 Dispatcher 堆积无限预览任务。相机仍持续抓取，中间帧由最新帧槽覆盖。

没有实现 `IFlowIterationSource` 的普通流程仍可持续运行；宿主在每轮后加入默认 10 ms 的空转延迟，避免紧循环占满 CPU。该保护不参与相机流程节拍。

### 5.6 线程与取消

- DAG 执行和节点生命周期运行在后台，不阻塞 WPF UI 线程。
- 插件执行器不得直接访问 WPF 控件；`ExecutionResultHandler` 在 UI 线程执行。
- 当前轮结束前不启动下一轮。
- 停止会取消源等待和当前 DAG。
- 厂商 `GetFrame` 不支持托管取消，因此采集循环固定使用 100 ms 超时轮询。
- 窗口关闭会先停止并等待活动会话完成，再允许进程退出。

## 6. 公共视觉数据契约

### 6.1 `FlowImage`

`FlowImage` 放在 `NodeCraft.Flow`，是不可变引用类型，不引用 WPF、`System.Drawing` 或厂商程序集。它包含：

- `Width`、`Height`、`Stride`
- `FlowPixelFormat PixelFormat`
- `FlowImageKind Kind`，首版为 `Color`、`Depth` 或 `Unknown`
- 只读托管像素缓冲，例如 `ReadOnlyMemory<byte>`
- `ulong FrameId`
- `ulong DeviceTimestamp`，保留厂商原始时间戳，不臆测其单位
- `DateTimeOffset CapturedAtUtc`
- 对应的 `CameraCalibration Calibration`

创建 API 分为两条明确路径：

- 面向一般调用方的复制工厂接收只读数据并复制，保证调用方后续修改原缓冲不会影响图片。
- 面向高吞吐生产者的所有权转移工厂接收一块新分配且不再由调用方读写的 `byte[]`；`FlowImage` 接管该数组，避免相机从原生内存复制后又发生第二次大数组复制。

两条路径都不向消费者公开可写内存，并执行相同验证：

- 宽、高和步长为正数。
- 缓冲长度等于 `Stride * Height`。
- 步长至少容纳一行当前像素格式。
- 像素格式只能是首版支持的格式。

厂商 API 不单独报告步长。插件要求 `ImageDataSize` 能被高度整除，并以 `ImageDataSize / Height` 作为步长；若不能整除或小于当前格式的最小行字节数，则整组帧无效。

首版像素格式：

- `Bgr24`
- `Rgb24`
- `Mono8`
- `Depth16`，保留小端 16 位原始深度值，不在公共契约中假设物理单位

首版优先采用清晰的托管所有权。每个 SDK 图片在释放原生句柄前直接复制到一块新分配的托管数组，再把该数组的所有权转移给 `FlowImage`，因此每张图片只有一次原生到托管的像素复制。公共对象绝不引用已释放的厂商内存。容量为 1 的最新帧槽限制保留帧数，缓冲池和显式租约作为后续性能优化，不进入首版契约。

### 6.2 `CameraCalibration`

`CameraCalibration` 同样位于 `NodeCraft.Flow`，包含：

- `ImageWidth`、`ImageHeight`
- 长度为 9 的 `3 x 3` 内参矩阵，按 SDK 行主序保存
- 长度为 12 的畸变参数：`k1, k2, p1, p2, k3, k4, k5, k6, s1, s2, s3, s4`
- 长度为 16 的 `4 x 4` 外参矩阵，按 SDK 行主序保存
- `IsLeftReference`，记录读取标定时使用的参考设置

构造时复制所有数组并严格校验长度。首版按 SDK 默认值 `isLeftReference = false` 读取彩色和深度标定，并在对象中明确记录该值，避免下游误解坐标参考。

同一相机会话只下载并转换一次标定数据。每帧 `FlowImage` 和独立标定输出槽复用相同的只读标定对象。

### 6.3 端口类型

`FlowDataType` 新增：

- `image`，CLR 类型为 `FlowImage`
- `camera-calibration`，CLR 类型为 `CameraCalibration`

现有 `object` 通配兼容行为不变；强类型视觉节点使用新增类型获得连线校验。

## 7. `NodeCraft.Vision.StereoCamera` 插件

### 7.1 插件身份

- 插件 ID：`nodecraft.vision.stereo-camera`
- 相机节点 TypeKey：`nodecraft.vision.stereo-camera.camera`
- 预览节点 TypeKey：`nodecraft.vision.stereo-camera.image-preview`
- Palette 分类：`Vision`

TypeKey 一经发布保持稳定。

### 7.2 相机 Node

节点可持久化配置只有 `IpAddress`。默认值为空，避免无意连接示例地址；首版只接受 IPv4 字面地址，IP 为空或格式无效时在会话启动阶段给出明确错误。首版没有曝光、增益、连接按钮或图片类型选择。

输出槽按固定顺序定义：

| 槽位 | Port ID | 类型 | 内容 |
|---|---|---|---|
| 0 | `colorImage` | `image` | 彩色原始图片及彩色标定 |
| 1 | `depthImage` | `image` | Depth16 原始图片及深度标定 |
| 2 | `colorCalibration` | `camera-calibration` | 与彩色图片相同的标定对象 |
| 3 | `depthCalibration` | `camera-calibration` | 与深度图片相同的标定对象 |

启动顺序：

1. 配置插件私有原生 DLL 搜索目录和当前进程的 `MV_GENICAM_64`。
2. 调用发现接口。
3. 按 IP 获取相机句柄。
4. 连接相机并注册断线回调。
5. 创建标定管理器并下载标定数据。
6. 分别读取 Color 和 Depth 标定，转换为公共契约。
7. 启动抓流。
8. 启动后台采集循环。

采集循环：

1. 以短超时调用 `scGetFrame`。
2. 从同一帧读取 Color 和 Depth 图片句柄。
3. 校验图片尺寸、格式和数据长度。
4. 在原生句柄有效期间分别分配一块最终托管数组，将每张图片复制一次并把数组所有权交给 `FlowImage`。
5. 将四个输出组成一个不可拆分的 `FrameBundle`。
6. 原子替换容量为 1 的最新帧槽并通知等待者。
7. 释放图片和帧句柄。

若彩色或深度任一图片缺失、为空、格式不支持或复制失败，则丢弃整组，不允许将不同 SDK 帧拼成一轮输出。内部本地递增序号确保同一帧组最多被消费一次；公开 `FrameId` 保留厂商值。

单次 `GetFrame` 超时只表示本次没有数据并继续轮询；若启动后或运行中连续 5 秒没有产生任何完整帧组，则以“相机无有效帧”结束会话，避免执行一次或持续运行无限等待。

当下游慢于采集时，新帧覆盖尚未消费的旧帧；正在执行的帧不会被替换。下一轮读取当时最新且未消费的完整帧组，不回放历史队列。

断线或不可恢复 SDK 错误会清空待处理帧并使等待者以原始故障结束，不能在故障后再消费槽内旧帧。首版不自动重连。

停止顺序：

1. 标记停止并取消采集循环。
2. 等待当前短超时 `GetFrame` 返回和采集任务退出。
3. 注销断线回调。
4. `StopGrabbing`。
5. `Disconnect`。
6. 释放标定管理器、相机及剩余句柄。

每一步均幂等；后一步仍会在前一步失败时尝试执行。

### 7.3 图片预览 Node

预览节点定义：

| 方向 | Port ID | 类型 | 语义 |
|---|---|---|---|
| 输入 | `image` | `image` | 必需图片输入 |
| 输出 | `image` | `image` | 原对象原样传递 |

显示行为：

- BGR/RGB 彩色图按对应格式创建 WPF `BitmapSource`。
- `Mono8` 直接灰度显示。
- `Depth16` 保留原始数据给输出端口；仅预览副本忽略 0 值后按当前帧非零最小值和最大值归一化为灰度。全 0 或无有效范围时显示黑色并给出状态文字。
- 显示帧 ID、宽高和像素格式。
- 使用宿主 `DynamicResource` 主题键，不硬编码颜色。

预览视图在节点生命周期中保持同一实例。新增注册选项允许该节点在执行结果更新后不重建 Content；节点模型以 `INotifyPropertyChanged` 通知视图。

图片转 WPF 位图可能包含大量像素处理，因此预览视图使用单槽后台渲染队列：新图片替换尚未开始渲染的旧图片，完成后只把冻结的 `BitmapSource` 切回 UI 线程。这样预览不会阻塞持续执行，也不会向 UI 队列堆积历史帧。视图卸载或节点删除时取消待处理渲染，并通过帧序号拒绝迟到结果。

现有内置文件路径 `Image Preview` 节点保持不变；新节点在 Palette 中显示为 `Image Preview (FlowImage)`，避免用途混淆。

## 8. 宿主交互

“流程”菜单调整为：

- 校验
- 执行一次
- 持续运行
- 停止

宿主维护单一活动运行状态：`Idle`、`Starting`、`RunningOnce`、`RunningContinuous`、`Stopping`。任意时刻最多一个执行会话。

运行期间：

- 禁用新建、清空、加载、再次执行和会改变图结构的画布交互。
- 保存可以禁用，首版与其他结构编辑命令保持一致。
- “停止”保持可用。
- 通过透明输入阻挡层或 FlowCanvas 只读状态阻止编辑，不使用会使预览整体变灰的 `IsEnabled = false`。

每轮执行结果由 Dispatcher 串行应用。结果面板显示运行模式、迭代号或帧号、节点状态、耗时和错误摘要；不得枚举或格式化图片像素缓冲。

关闭窗口时，如果存在活动会话，第一次 Closing 被取消；宿主等待 `StopAsync` 完成，再执行真正关闭。事件入口可以是 `async void`，内部运行方法必须返回 `Task`，避免无法等待的业务层 `async void`。

## 9. 厂商互操作迁移

### 9.1 托管代码位置

厂商托管包装代码迁移并嵌入 `NodeCraft.Vision.StereoCamera/VendorInterop`，目标为 `net8.0-windows`。互操作类型全部为 `internal`，不成为 NodeCraft 插件间契约。

不引用、不复制、不加载原 `.NET Framework 4.5` 的 `StereoCamera.Net.dll`。

### 9.2 迁移范围

只迁移首版所需能力：

- Discovery 和按 IP 取得相机
- Connect、Disconnect、连接状态回调
- StartGrabbing、GetFrame、StopGrabbing
- Frame ID、Timestamp 和按类型取得图片
- 图片宽、高、像素格式、数据地址和数据长度
- 创建标定管理器、下载标定、读取 Color/Depth 标定
- 通用句柄释放

不迁移 `Bitmap`、参数系统、鸟瞰图、点云、视觉结果和其他未使用包装层。

### 9.3 签名与资源安全

- P/Invoke 签名以提供的 `CAPI.h` 为权威来源，原托管 DLL 的反编译结果用于核对包装行为。
- 所有入口显式使用 `CallingConvention.Cdecl` 和 `ExactSpelling = true`。
- C `bool` 参数和返回值按 1 字节布尔值显式封送，不依赖默认 Win32 BOOL 规则。
- `ScCameraCalibInfo` 使用顺序布局，长度必须为 416 字节；数组固定为 9、12、16、28 项。
- 断线回调使用 Cdecl 委托，托管对象持有委托强引用直至成功注销。
- Camera、Frame、Image 和 calibration-manager 句柄由专用 `SafeHandle` 封装，最终调用 `scReleaseHandle`。
- Camera 的协议级停止和断开仍由会话生命周期显式执行，`SafeHandle` 作为最终资源兜底。
- 原始图片使用 `scGetImageData` 与 `scGetImageDataSize` 复制，不经过 `System.Drawing.Bitmap`，因此插件不依赖 `System.Drawing.Common`。

## 10. 构建与插件包

### 10.1 平台

宿主运行进程和相机插件显式目标为 Windows x64。`NodeCraft.Flow` 的公共契约仍可保持 AnyCPU，但加载相机插件的宿主必须为 x64，避免原生库位数不匹配。

### 10.2 SDK 路径

构建属性 `StereoCameraSdkRoot` 指向用户提供的厂商 `app` 根目录，例如其下包含：

- `Runtime/x64`
- `Licenses`
- `msvcp120.dll`
- `msvcr120.dll`

源码编译不依赖 `StereoCamera.Net.dll`，所以未设置 SDK 路径时，普通解决方案构建和无硬件自动化测试仍可进行，但不会生成可部署的相机插件包。

显式插件打包目标必须要求 `StereoCameraSdkRoot`，并在路径或必需文件缺失时列出所有缺失项后失败。

### 10.3 包内容

标准输出目录：

```text
Plugins/NodeCraft.Vision.StereoCamera/
├── plugin.json
├── NodeCraft.Vision.StereoCamera.dll
├── lib/
│   ├── LibStereoCamera.dll
│   ├── MVSDKmd.dll
│   ├── ... Runtime/x64 原生依赖与 .cti 文件
│   ├── msvcp120.dll
│   ├── msvcr120.dll
│   ├── SDKLOG_default.properties
│   └── oxylog.toml
└── licenses/
    └── 厂商随包提供的第三方许可证材料
```

从 `Runtime/x64` 复制运行时清单，但明确排除：

- `StereoCamera.Net.dll`
- 厂商附带的托管 `NLog.dll`
- `NodeCraft.Flow.dll`
- `CommonControls.WPF.dll`
- 宿主已有的 Microsoft logging 程序集

`SDKLOG_default.properties` 从 `Runtime/x64` 取得，`oxylog.toml` 从 SDK 根目录取得；两者缺失时显式打包目标按必需文件报告，而不是静默生成可能无法诊断的包。

插件启动原生层前，只配置当前进程的私有 DLL 搜索目录和 `MV_GENICAM_64`，不写注册表、不修改系统环境变量，也不永久修改系统 PATH。进程级搜索目录的注册在插件存活期间保持有效，以便 Windows 加载 `LibStereoCamera.dll` 的传递依赖。

## 11. 持久化

- 相机节点的 `IpAddress` 由现有自定义属性序列化机制写入 `.flow.xml`。
- 运行状态、原生句柄、最新帧、图片缓冲、WPF 位图和标定缓存不持久化。
- 加载旧流程不受影响。
- 新节点仍依靠 TypeKey 优先创建，避免依赖插件程序集限定类型名。

## 12. 错误处理与日志

持续会话采用快速失败：以下任一情况会结束会话并进入统一清理：

- IP 无效、发现失败、找不到相机或连接失败。
- 下载或解析彩色/深度标定失败。
- 开始抓流失败。
- 相机断开。
- 原生图片格式、尺寸或缓冲长度无效。
- 任意下游节点抛出未处理异常。

单个超时或缺少一组图片可作为可丢弃采集结果继续等待；连续达到不可恢复条件时才使会话失败。首版不自动重连。

清理阶段尽最大努力执行全部步骤。若已有主要启动或执行异常，清理异常只记录到日志且不能覆盖主要异常；若只有清理异常，则在结果摘要中报告清理失败。

用户主动停止以正常取消结束，显示“已停止”，不记为执行失败。完整异常和阶段信息进入现有 NLog 日志，用户结果面板只显示简洁摘要。

## 13. 测试策略

### 13.1 NodeCraft.Flow 自动化测试

- 每个节点执行器在一次会话中只创建一次。
- 生命周期按拓扑顺序启动、反向停止。
- 中途启动失败只清理已启动节点。
- 单次模式恰好执行一轮并释放。
- 持续模式逐轮串行执行，不并发同一 DAG。
- 停止、执行异常和重复释放均得到正确状态与幂等清理。
- 原 `GraphExecutor.ExecuteAsync` 兼容包装仍通过现有测试。
- `image` 与 `camera-calibration` 类型连线校验正确。

### 13.2 公共契约测试

- 复制工厂隔离调用方缓冲；所有权转移工厂不做第二次复制，并明确禁止生产者继续访问已转移数组。
- 标定矩阵在构造时复制且严格验证 9/12/16 项长度。
- 非法尺寸、步长、缓冲长度和像素格式被拒绝。
- `.flow.xml` 只保存 IP 等配置，不保存运行时图片和标定。

### 13.3 插件自动化测试

通过厂商无关的内部相机适配接口与假相机覆盖：

- 发现、连接、标定、抓流与停止调用顺序。
- 最新帧槽容量为 1，新帧覆盖未处理旧帧。
- 同一帧最多消费一次，快速 DAG 会等待新帧。
- 慢 DAG 跳过中间帧并选择当前最新帧。
- 彩色和深度必须来自同一帧，缺任一项时整组丢弃。
- 故障时清空待处理帧，不输出断线前缓存。
- 四个输出槽及两个图片内嵌标定的对象一致性。
- Depth16 预览归一化、全零图和不支持格式。
- 预览后台队列只保留最新待渲染帧。
- P/Invoke 调用约定、布尔封送和关键结构体大小。
- 插件注册、稳定 TypeKey、端口顺序和节点属性持久化。

### 13.4 包测试

- 未提供 SDK 路径时普通构建不误生成完整包。
- 显式打包缺文件时输出完整缺失清单并失败。
- 完整包包含规定原生依赖、`.cti`、配置和许可证。
- 完整包不包含 `StereoCamera.Net.dll`、共享宿主程序集或厂商 `NLog.dll`。
- 插件加载器能从插件私有目录解析直接和传递原生依赖。

### 13.5 Windows x64 真实硬件验收

1. 按 IP 找到并连接相机。
2. 执行一次获得一组彩色/深度图片和两份标定，随后确认相机已停止并释放。
3. 持续模式显示不断变化的最新帧；人为降低下游速度时不回放旧帧队列。
4. 四个输出的帧和标定一致，彩色/深度 `FrameId` 相同。
5. Stop 能在短时间内退出并释放设备，随后可再次运行。
6. 运行中拔线会停止会话、显示明确错误并完成清理。
7. 关闭窗口时活动相机被安全停止，无后台线程或原生句柄残留。

## 14. 验收标准

满足以下条件即认为首版完成：

- NodeCraft 菜单提供“执行一次 / 持续运行 / 停止”，且状态互斥正确。
- 普通旧流程在单次模式下行为不变。
- 相机节点在同一持续会话只连接一次，停止时只清理一次。
- 每轮 DAG 使用一组来自同一 SDK Frame 的最新彩色/深度图片。
- 图片和独立槽均提供正确的公共标定对象。
- 图片预览支持彩色与 Depth16，连续更新时不重建整棵节点 UI，也不积压历史预览。
- 相机插件不引用或打包 `StereoCamera.Net.dll`，互操作托管代码内嵌在插件中。
- 没有 SDK 和硬件时可运行核心与假相机测试；Windows x64 硬件验收项有明确步骤。

## 15. 风险与后续方向

- 托管像素副本在高分辨率和高帧率下会产生显著内存带宽与 GC 压力；首版先保证所有权正确，后续可增加池化图片租约，但不能破坏公共只读语义。
- 厂商原生运行时存在多级传递依赖和环境变量要求，必须在 Windows 包测试中验证实际搜索行为。
- 厂商 C API 的 bool 宽度、结构体布局和回调生命周期必须通过头文件与 `Marshal.SizeOf` 测试双重校验。
- 复制和分发厂商运行库及从托管包装迁移代码时，应遵守随 SDK 提供的许可条款；构建脚本保留许可证材料，但不把用户 SDK 二进制纳入源码仓库。
- 后续可在此会话机制上增加录像源、USB 相机、视频文件、自动重连、帧率统计、背压指标和池化图像内存。
