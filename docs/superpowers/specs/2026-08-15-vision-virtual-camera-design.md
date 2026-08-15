# NodeCraft.Vision 虚拟相机设计

状态：已确认方向

日期：2026-08-15

目标平台：Windows x64、.NET 8、WPF

## 1. 目标

在 `NodeCraft.Vision` 插件中增加一个不依赖物理设备的 `Virtual Camera` 节点。节点从一张图片、一个图片文件夹或插件内置图片集合中读取图片，并在每次 workflow iteration 输出当前图片，供图片预览和后续视觉节点使用。

节点需要同时提供：

1. 当前 `FlowImage`，作为 iteration 级输出。
2. 当前图片路径，作为 iteration 级字符串输出。
3. 当前图片所在目录，作为 session 级字符串输出。

虚拟相机的图片序列按固定顺序循环。每次 iteration 的内部 index 增加 1，到达末尾后回到 0。index 不作为额外的 Flow 输出。

## 2. 非目标

- 不模拟相机采集延迟、帧率或设备连接状态。
- 不监视文件夹变化；图片序列在 session 启动时确定。
- 不递归扫描子目录。
- 第一版不支持 GIF、TIFF、WebP 或其他格式。
- 第一版不增加文件选择器或文件夹选择器；编辑器使用可持久化的路径文本框。
- 不在 `NodeCraft.Flow` 增加图片解码依赖或通用图片序列 API。

## 3. 节点身份和配置

节点模型为 `VirtualCameraNodeModel`，稳定类型键为：

```text
nodecraft.vision.virtual-camera
```

模型属性：

```csharp
public string SourcePath { get; set; } = "builtin://vision/sample-set";
public VirtualCameraLoadMode LoadMode { get; set; } = VirtualCameraLoadMode.Dynamic;
public int MaxPreloadedImages { get; set; } = 100;
public long MaxPreloadedBytes { get; set; } = 536870912;
public bool SkipErrorImages { get; set; }
```

配置默认值为：

```text
LoadMode = Dynamic
MaxPreloadedImages = 100
MaxPreloadedBytes = 512 MiB
SkipErrorImages = false
```

`MaxPreloadedImages` 和 `MaxPreloadedBytes` 只在 `Preload` 模式生效；`Dynamic` 模式不使用解码图片缓存，因此忽略这两个限制。`VirtualCameraLoadMode` 只有 `Preload` 和 `Dynamic` 两个值。

### 3.1 XML 持久化

以上五个配置属性（包括 `SourcePath`）都是 `VirtualCameraNodeModel` 的公开、可读写属性。现有 `GraphModelXmlSerializer` 会自动将它们写入节点的 `<Properties>`，加载时根据 `Name`、`Type` 和 `Value` 恢复到新建的节点模型；不需要新增专用 XML serializer，也不需要提升当前 graph format version。

保存的结构类似：

```xml
<Properties>
  <Property Name="SourcePath" Type="System.String" Value="builtin://vision/sample-set" />
  <Property Name="LoadMode" Type="...VirtualCameraLoadMode..." Value="Dynamic" />
  <Property Name="MaxPreloadedImages" Type="System.Int32" Value="100" />
  <Property Name="MaxPreloadedBytes" Type="System.Int64" Value="536870912" />
  <Property Name="SkipErrorImages" Type="System.Boolean" Value="False" />
</Properties>
```

如果旧 graph 没有这些 Property，节点构造函数提供上述默认值。运行时配置不保存 `_index`、`_current`、图片缓存或任何 `FlowImage` 内容。

### 3.2 运行时配置映射

`GraphModelWorkflowAdapter.Convert` 调用 `IWorkflowNodeValueProvider.WriteWorkflowInputs`。Virtual Camera 必须把模型属性按以下固定 key 和类型写入 `WorkflowNode.Inputs`：

| NodeModel property | WorkflowNode input key | 运行时值类型 |
| --- | --- | --- |
| `SourcePath` | `sourcePath` | `string` |
| `LoadMode` | `loadMode` | `VirtualCameraLoadMode` |
| `MaxPreloadedImages` | `maxPreloadedImages` | `int` |
| `MaxPreloadedBytes` | `maxPreloadedBytes` | `long` |
| `SkipErrorImages` | `skipErrorImages` | `bool` |

`VirtualCameraExecutor.StartSessionAsync` 只从这些运行时输入读取配置，并在启动时校验枚举值、数量上限和字节上限。UI 修改的是 NodeModel 属性；保存 graph 由通用 serializer 持久化，执行前由 adapter 生成运行时输入。

这些配置不是 Flow 输入端口，而是由 adapter 写入的运行时配置值。新建节点的 `SourcePath` 默认值为：

```text
builtin://vision/sample-set
```

这个默认值只是节点初始配置；用户填写无效路径时，运行时不得回退到内置图片。

节点编辑器显示 `SourcePath`、加载模式、预加载数量上限、预加载字节上限和“跳过错误图片”选项。编辑器修改属性时沿用现有 Vision 节点的 graph-changed 通知模式。

## 4. 输出契约

注册输出端口按以下顺序排列：

| Slot | Id | DataType | Availability | 语义 |
| --- | --- | --- | --- | --- |
| 0 | `image` | `FlowDataType.Image` | `Iteration` | 当前图片的 `FlowImage` |
| 1 | `imagePath` | `FlowDataType.String` | `Iteration` | 当前图片路径 |
| 2 | `imageDirectory` | `FlowDataType.String` | `Session` | 当前图片所在目录 |

`ExecuteAsync` 每轮必须返回 `image` 和 `imagePath`。`InitializeSessionAsync` 返回 `imageDirectory`。所有输出都必须经过现有 Flow runtime output validation。

### 4.1 本地路径

本地来源支持：

- 单个 `.jpg`、`.png` 或 `.bmp` 文件；形成只有一个元素的序列。
- 文件夹；只枚举该文件夹的直接子文件，扩展名比较不区分大小写，按文件名使用 `OrdinalIgnoreCase` 稳定排序。

文件来源先规范化为绝对路径。示例：

```text
imagePath:
C:\datasets\frames\frame-02.png

imageDirectory:
C:\datasets\frames
```

文件夹内其他扩展名直接忽略；如果没有可用的 `.jpg`、`.png` 或 `.bmp` 文件，session 启动失败。

### 4.2 内置路径

内置图片使用以下 URI 前缀：

```text
builtin://vision/
```

第一版提供稳定的示例集合：

```text
builtin://vision/sample-set
```

集合中的图片使用稳定的资源 ID 作为当前图片路径，例如：

```text
builtin://vision/sample-set/checkerboard
builtin://vision/sample-set/color-bars
```

内置集合的目录输出为：

```text
builtin://vision/sample-set
```

`SourcePath` 可以指向整个集合，也可以指向集合中的单张内置图片；前者形成完整序列，后者形成单元素序列。内置集合由插件内置图片提供器提供，第一版直接构造固定的托管像素数据，不依赖用户机器上的文件。内置图片保持固定顺序，不能因为字典枚举顺序而变化。

`imageDirectory` 始终表示当前 sequence 的容器：

| SourcePath | imagePath | imageDirectory |
| --- | --- | --- |
| `C:\data\a.png` | `C:\data\a.png` | `C:\data` |
| `C:\data` | 当前选中的图片绝对路径 | `C:\data` |
| `builtin://vision/sample-set/checkerboard` | `builtin://vision/sample-set/checkerboard` | `builtin://vision/sample-set` |
| `builtin://vision/sample-set` | 当前选中的内置图片 URI | `builtin://vision/sample-set` |

## 5. 图片解码和像素格式

图片解码放在 `NodeCraft.Vision`，使用现有 WPF 依赖中的 `BitmapDecoder`，不增加第三方图片包。文件流使用 `OnLoad` 缓存选项；解码后立即复制到独立托管缓冲区并关闭文件流，不把文件句柄或 `BitmapSource` 暴露给 Flow 层。

输入扩展名只允许：

```text
.jpg
.png
.bmp
```

像素转换规则：

- 原始 WPF 像素格式为 `Gray8` 时，直接创建 `FlowPixelFormat.Mono8`。
- 其他所有可解码的像素格式统一转换为 `FlowPixelFormat.Bgr24`。

创建的 `FlowImage` 为不可变值对象。虚拟相机不持有 WPF UI 对象；预览节点可以直接接收该 `FlowImage`。

图片元数据不代表真实设备：`DeviceTimestamp` 使用 0，`FrameId` 使用图片在 session 初始序列中的稳定 ordinal，不代表 workflow iteration 序号；`imagePath` 在整个 session 中保持稳定，`CapturedAtUtc` 使用图片被加载时的 UTC 时间。`Preload` 模式重复访问同一项时复用已缓存的 `FlowImage`，其像素和加载时间保持不变；`Dynamic` 模式每次重新加载，因此文件内容和加载时间允许发生变化。

## 6. 执行器和生命周期

新增 `VirtualCameraExecutor`，实现：

- `IFlowNodeExecutor`
- `IFlowNodeSessionLifecycle`
- `IFlowNodeSessionInitializer`
- `IFlowIterationSource`

执行器在 `StartSessionAsync` 中：

1. 读取 `sourcePath`。
2. 识别 `builtin://vision/` URI 或本地文件系统路径。
3. 验证来源类型和图片数量。
4. 构建 session 内固定的图片路径序列，并设置 `_index = -1`、`_current = null`。
5. 按 `LoadMode` 执行预加载或保留动态来源。

`InitializeSessionAsync` 在生命周期启动之后执行，返回 `imageDirectory`。这样 session 输出会在 `StartAsync` 成功进入 Running 前写入 session store。

### 6.1 Preload 模式

`Preload` 模式在 `StartSessionAsync` 中依次解码整个图片序列，并缓存所有有效的 `FlowImage`。缓存必须同时满足：

- 成功解码的图片数量不超过 `MaxPreloadedImages`。
- 所有解码缓冲区的总字节数不超过 `MaxPreloadedBytes`。

超过任一限制时，session 启动失败，不静默截断图片序列。解码失败时，如果 `SkipErrorImages=false`，session 启动失败；如果为 `true`，该图片被排除后继续加载。所有图片都被跳过时，session 启动失败。

### 6.2 Dynamic 模式

`Dynamic` 模式在 `StartSessionAsync` 中只验证来源并建立排序后的路径元数据列表，不解码图片、不建立 `FlowImage` 缓存。路径列表用于维持文件名排序和循环索引；内存中不会累积图片像素数据。

每次 `PrepareIterationAsync` 只读取并解码当前路径。当前图片替换上一轮的当前图片；执行器不保留 LRU 或其他历史图片缓存。文件在 session 运行期间被修改时，后续 iteration 会读取修改后的内容。

`Dynamic` 模式下，解码失败时，如果 `SkipErrorImages=false`，当前 `PrepareIterationAsync` 失败；如果为 `true`，当前路径从本次 session 的有效候选中跳过并继续尝试下一张。整个序列都无法读取时，`PrepareIterationAsync` 失败。

两种模式的 index 规则相同，`PrepareIterationAsync` 执行：

```text
_index = (_index + 1) % _entries.Count
var entry = _entries[_index]
_current = LoadCurrent(entry)
```

在 `Preload` 中 `LoadCurrent(entry)` 返回已缓存的 `FlowImage`；在 `Dynamic` 中它从 `entry` 的路径重新读取并解码。`_entries` 只包含当前 session 的有效候选项。因此第一次 iteration 选择 ordinal 0，最后一项之后才回到 0；单图片序列也会始终选择 0。

停止 session 时清空当前图片、序列、路径元数据和 index。

`ExecuteAsync` 返回：

```text
{
    "image": current.FlowImage,
    "imagePath": current.Path
}
```

没有当前图片、session 未启动或 iteration 未准备时，执行器抛出明确的 `InvalidOperationException`，不得返回 null 或空图。

## 7. 错误处理

以下情况都必须产生明确异常，不能静默输出空图、默认图或空路径：

- `SourcePath` 为空或格式非法。
- 本地路径不存在。
- 本地路径既不是支持的图片文件，也不是文件夹。
- 图片文件扩展名不是 `.jpg`、`.png` 或 `.bmp`。
- 文件夹没有支持的图片文件。
- 内置 URI 不存在或没有图片。
- 图片无法解码、像素尺寸无效或像素复制失败。
- iteration 在没有已准备图片时执行。
- `Preload` 模式超过预加载图片数量或字节上限。

异常消息必须包含 `VirtualCamera` 和相关来源路径或 URI；若为文件夹图片错误，应包含当前图片的绝对路径。

## 8. 插件注册和 UI

`VisionPlugin.Register` 注册 Virtual Camera，并保留现有 Vision Camera、Stereo Camera 和 FlowImage Preview 注册。Virtual Camera 的 palette 描述明确说明它从文件或内置图片集合循环输出 `FlowImage`。

新增 `VirtualCameraEditor.xaml` 和对应 content factory。UI 只负责编辑 `SourcePath`，不在 UI 层解析图片或维护 index。运行时验证仍由 executor 完成。

## 9. 测试设计

继续使用仓库现有的控制台测试跑棒，不引入新的测试框架。新增测试覆盖：

1. 插件注册 Virtual Camera，验证类型键、三个输出端口、数据类型和 availability。
2. 节点模型的五个配置属性通过 `<Properties>` 保存并可从 `.flow.xml` round-trip 恢复，旧 XML 缺失属性时使用默认值。
3. `GraphModelWorkflowAdapter` 将五个属性按约定 key 和类型写入 `WorkflowNode.Inputs`。
4. 单个本地 JPG/PNG/BMP 文件形成单元素序列，并重复输出 index 0。
5. 本地文件夹按文件名排序，并在最后一张后回到第一张。
6. 文件夹忽略不支持扩展名，空文件夹启动失败。
7. 第一次 iteration 选择序列项 0，不跳过第一张；停止和重新启动后同样从 0 开始。
8. `Preload` 模式在启动时发现坏图、遵守图片数量限制和 decoded byte 限制。
9. `Dynamic` 模式每次 iteration 重新读取图片，不保留历史解码缓存；修改文件后后续 iteration 可观察到新内容，同时验证同一项的 `FrameId` 和 `imagePath` 稳定、像素和 `CapturedAtUtc` 可以变化。
10. `SkipErrorImages=false` 和 `true` 分别覆盖失败和跳过坏图路径的行为。
11. 内置 sample-set 及单张内置图片输出稳定 URI、稳定目录值和稳定顺序。
12. `Gray8` 图片输出 `Mono8`，彩色和其他可解码图片输出 `Bgr24`。
13. `image` 可以被现有 FlowImage Preview 节点消费。
14. `imagePath` 可以连接兼容 `FlowDataType.String` 的输入节点。
15. 不存在路径、非法路径类型、不支持扩展名、损坏图片和未知内置 URI 都抛出明确异常。
16. session 启动、停止、重复 iteration 和 session 清理不会保留上一轮的图片或 index。
17. 集成 graph 执行验证 session 级 `imageDirectory` 可被后续节点稳定读取。

## 10. 完成标准

- Vision 插件注册 Virtual Camera，节点可通过模型路径配置运行。
- 默认内置集合可在没有外部图片文件时循环输出 `FlowImage`。
- 本地 `.jpg`、`.png`、`.bmp` 文件和文件夹可按约定运行。
- `image` 能连接现有 FlowImage Preview；`imagePath` 能连接字符串节点；`imageDirectory` 能连接 session 级输入。
- 所有非法来源和解码错误都有明确异常。
- 现有全量测试和新增虚拟相机测试通过，测试跑棒输出 `ALL PASS`。
