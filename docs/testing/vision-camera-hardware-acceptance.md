# NodeCraft.Vision 双相机 Windows-x64 验收清单

## 前置条件

- Windows x64 主机，已安装 .NET 8 Windows Desktop SDK。
- IMV 相机与主机处于同一网段，准备相机 IPv4 地址。
- SDK 根目录为 `D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv`，其中包含 `Runtime\x64` 和 `Licenses`。
- 原技术 MVSDK 3D 相机 SDK 根目录另行准备，并记录为 `StereoCameraSdkRoot`；该目录必须包含
  `LibStereoCamera.dll`、`Runtime\x64`、根目录运行时 DLL/TOML 和 `Licenses`。

## 构建与打包

```powershell
dotnet msbuild NodeCraft.Vision/NodeCraft.Vision.csproj `
  -t:StageVisionPlugin `
  -p:Configuration=Release `
  -p:VisionSdkRoot="D:\Downloads\MVviewer_2.7.0.CXP_Build20260703\mv" `
  -p:StereoCameraSdkRoot="D:\path\to\technical-mvsdk"
```

- [ ] 包含 `NodeCraft.Vision.dll`、`plugin.json`、`lib\MVSDKmd.dll`、`lib\LibStereoCamera.dll` 和许可证目录。
- [ ] `lib` 中包含 `VisionRuntimeFiles.txt` 列出的全部双相机 x64 SDK 文件，以及旧 3D SDK 的根目录运行时文件。
- [ ] 包中不包含 `NodeCraft.Flow.dll`、`CommonControls.WPF.dll`、宿主 logging 程序集或 `StereoCamera.Net.dll`。
- [ ] 未修改系统 PATH、注册表或机器级环境变量。

## 相机与流程

- [ ] Vision Camera 节点填入相机 IPv4 后可启动取流。
- [ ] Stereo Camera 节点仍可创建，并保留 `colorImage`、`depthImage`、`colorCalibration`、`depthCalibration` 四个输出。
- [ ] Image Preview 能显示最新 `FlowImage`，帧 ID 和尺寸与 IMV Grab 输出一致。
- [ ] 3D 相机的彩色/深度图像与标定数据分别可取；标定数据不附着到 `FlowImage`。
- [ ] 单次执行完成后，停止抓流、关闭设备、销毁句柄并释放运行时目录。
- [ ] 连续执行时下游变慢会丢弃中间帧，但不会并行执行流程迭代。
- [ ] Stop 后再次运行仍可重新连接相机。
- [ ] 拔出网线或停止相机时，流程收到错误且设备句柄和运行时目录仍被清理。
- [ ] 关闭主窗口时没有残留抓流线程或 SDK 句柄。

`FlowImage` 只携带像素和帧元数据；如果后续 SDK 接口提供标定数据，标定数据必须作为独立的
`CameraCalibration` 值或独立端口传递，不能附着到图像对象。
