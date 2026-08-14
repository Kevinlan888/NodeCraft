# NodeCraft Vision 双相机共存设计

## 目标

在 `NodeCraft.Vision` 插件中同时提供两类互不相同的相机节点：

1. 新增的 IMV/Grab 2D 相机节点；
2. 原有技术 MVSDK 3D/立体相机节点。

两类节点共用一个插件程序集和插件目录，但使用各自的采集实现、TypeKey、节点模型和原生 SDK 入口。

## 节点与数据契约

- IMV 节点继续使用 `nodecraft.vision.camera`，只输出 `image`。
- 原 3D 节点恢复 `nodecraft.vision.stereo-camera.camera`，输出顺序和名称保持不变：
  `colorImage`、`depthImage`、`colorCalibration`、`depthCalibration`。
- 旧 3D 节点的 `ipAddress` 仍作为节点持久化配置，不作为可连线输入。
- `FlowImage` 不持有标定数据；3D 会话在启动时独立读取并缓存两份 `CameraCalibration`，每个帧包只把图像和对应标定分别输出。
- 两类相机共用现有 `FlowImage` 预览节点，不重复注册同一个预览 TypeKey。

## 原生运行时边界

- 新 IMV 代码继续调用 `MVSDKmd.dll` 的 C API。
- 3D 代码恢复原有 `LibStereoCamera.dll` C API 互操作，不引入旧的 `StereoCamera.Net.dll` 托管包装。
- 两类会话共用插件 `lib` 目录的进程级 DLL 搜索路径和运行时引用计数，避免一个相机停止时卸载另一个相机仍在使用的 DLL 搜索路径。

## 打包

- `VisionSdkRoot` 指向用户提供的 IMV SDK 根目录。
- `StereoCameraSdkRoot` 指向原 3D 技术 MVSDK 根目录；新 IMV 下载目录没有 `LibStereoCamera.dll`，因此不能用一个目录代替两套 SDK。
- `StageVisionPlugin` 将两套 SDK 的必需 x64 文件合并到同一个插件 `lib` 目录；共享文件名由新 IMV 清单提供，3D SDK 只补充其独有文件。
- 包仍排除 `StereoCamera.Net.dll`、宿主程序集、厂商 `NLog.dll` 和其他共享依赖。

## 验证

- 插件注册测试必须同时发现 IMV 2D、技术 MVSDK 3D 和共享预览节点，且 TypeKey 不重复。
- 3D 节点模型测试必须验证四个输出的顺序、数据类型和独立标定端口。
- 现有 IMV 采集、预览、互操作和打包测试继续通过。
- 普通解决方案构建不依赖硬件；显式打包测试使用临时的双 SDK 目录验证文件合并、缺失文件报错和禁止文件过滤。
