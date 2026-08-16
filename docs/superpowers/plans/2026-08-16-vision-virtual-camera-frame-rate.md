# Vision Virtual Camera Frame Rate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为现有 Virtual Camera 增加默认 18 FPS 的可持久化小数帧率，并在节点内部以背压方式严格顺序输出图片，修复连续运行时 `imagePath` 变化但预览长期不刷新的问题。

**Architecture:** 不改 Flow framework，也不引入后台采集线程或队列。`VirtualCameraExecutor.PrepareIterationAsync` 使用单调时钟和可取消 delay 门控每一帧；下游快时不超过配置 FPS，下游慢时立即处理严格顺序的下一张图片并从当前时刻重新建立节拍。Vision 插件内部使用不可变图片模板复用 Preload 像素数组，并为每次成功帧创建连续 `FrameId` 和新的采集时间元数据。

**Tech Stack:** .NET 8 Windows x64、C# 9、WPF、现有 `NodeCraft.Flow` contracts、现有 `NodeCraft.Vision.Camera.IMonotonicClock`、`Task.Delay`、现有 `NodeCraft.Tests` 控制台测试跑棒；不增加第三方依赖或测试框架。

## Global Constraints

- 只修改 Virtual Camera 的 `NodeCraft.Vision` 实现、编辑器、测试和本计划；不修改 `NodeCraft.Flow`、`FlowImage`、`FlowExecutionController`、`GraphExecutionSession`、真实 Vision/Stereo Camera 或 `LatestFrameMailbox`。
- 不增加通用 Camera 基类、公共帧率框架、后台采集线程或图片队列。
- `FrameRate` 类型固定为 `double`，默认 `18.0`，有效范围为闭区间 `[0.1, 1000.0]`；拒绝 `NaN` 和正负无穷大。
- NodeModel 必须把 `frameRate` 以 `double` 写入 workflow；旧 XML 缺少属性和直接构造的 runtime node 缺少 key 时都使用 18。
- 首帧等待完整的一个周期；后续帧严格按来源顺序循环，不跳帧、不积压、不追赶。
- 下游慢导致 deadline 过期时，下一帧立即开始，并以该帧实际开始时间重建下一 deadline；实际 FPS 可以低于配置值。
- `FrameId` 在每个 session 内按成功帧 `0, 1, 2...` 连续递增；`DeviceTimestamp` 是 session 节拍起点后的整数微秒；`CapturedAtUtc` 是图片成功准备后、提交前的 UTC 时间。
- 等待、加载或包装期间取消时不得提交 current、成功图片 index、FrameId 或下一 deadline；Dynamic 坏图删除继续遵守现有窄异常和游标规则。
- Preload/builtin 只保留一份像素数组，逐帧不得复制大图像素；`MaxPreloadedBytes` 仍只累计唯一模板 buffer。
- `MaxPreloadedBytes` 的模型、XML 和 workflow runtime 契约仍使用 `long` 字节数；编辑器和文档以二进制 MB 展示/输入（`1 MB = 1,048,576 bytes`），正整数 MB 在 UI 层 checked 转回字节，默认值显示为 `512 MB`。
- 所有 Virtual Camera 自己创建的配置、时钟和溢出异常必须包含 `VirtualCamera` 和 source path/URI。
- 每个实现任务必须执行红灯、绿灯和小提交；最终全量测试必须输出 `ALL PASS`。

## File Map

**Create:**

- `NodeCraft.Vision/Nodes/VirtualCameraImageTemplate.cs`：Vision 私有不可变像素模板；使用现有 `FlowImage.FromOwnedBuffer` 创建共享像素的逐帧包装。

**Modify:**

- `NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs`：帧率常量、合法性函数、`FrameRate` 属性和 `frameRate` workflow mapping。
- `NodeCraft.Vision/Views/VirtualCameraEditor.xaml`：新增 `Frame rate (FPS)` 文本框。
- `NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs`：帧率初始化、InvariantCulture 解析、范围校验和 graph-changed 通知。
- `NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs`：loader 返回无采集元数据的内部图片模板。
- `NodeCraft.Vision/Nodes/VirtualCameraSource.cs`：entry 保存 `PreloadedTemplate`；builtin 资产改为模板。
- `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs`：runtime 帧率兼容/校验、单调 deadline、可取消等待、连续帧号和逐帧元数据。
- `NodeCraft.Tests/VirtualCameraTests.cs`：模型/UI、模板共享、节拍、取消、重启、Dynamic、metadata 和 graph integration 回归测试。

**Do not modify:**

- `NodeCraft.Flow/**`
- `NodeCraft/Execution/FlowExecutionController.cs`
- `NodeCraft.Vision/Camera/LatestFrameMailbox.cs`
- `NodeCraft.Vision/Nodes/VisionCameraExecutor.cs`
- `NodeCraft.Vision/Nodes/StereoCameraExecutor.cs`
- `NodeCraft.Vision/NodeCraft.Vision.csproj`；SDK 默认包含新增 `.cs`，现有 Virtual Camera XAML 已作为 EmbeddedResource 注册。

## Preflight

- [ ] 在隔离 worktree 确认分支和 clean 状态。

Run:

```powershell
git branch --show-current
git status --short --branch
```

Expected: branch 为 `codex/virtual-camera-frame-rate`，没有未说明的代码改动。

- [ ] 运行实现前基线。

Run:

```powershell
dotnet build NodeCraft.sln --no-restore
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-build --no-restore
```

Expected: build 成功；测试末尾输出 `ALL PASS`。仓库已有 nullable warning 可以保留，但先记录 warning 数量，后续不得新增编译错误。

---

### Task 1: 增加帧率模型、持久化和编辑器配置

**Files:**
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs:8-40`
- Modify: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml:20-31`
- Modify: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs:15-151`
- Test: `NodeCraft.Tests/VirtualCameraTests.cs:24-108,1081-1116`

**Interfaces:**
- Produces: `VirtualCameraNodeModel.DefaultFrameRate`, `MinimumFrameRate`, `MaximumFrameRate`, `IsValidFrameRate(double)`, public `FrameRate` 和 runtime key `frameRate`。
- Consumes: 现有 `GraphModelXmlSerializer` 公开属性持久化和 `IWorkflowNodeValueProvider.WriteWorkflowInputs`。

- [ ] **Step 1: 写模型和 XML mapping 的失败测试**

在 `virtual camera model persists configuration and maps workflow inputs` 中给模型设置 `FrameRate = 29.97`，并加入以下断言：

```csharp
&& workflowNode.Inputs["frameRate"] is double frameRate
&& frameRate == 29.97
&& xml.Contains("Name=\"FrameRate\"", StringComparison.Ordinal)
```

在 restored/legacy 断言中加入：

```csharp
&& restored.FrameRate == 29.97
&& legacyDefaults.FrameRate == 18.0
```

把默认值测试补成：

```csharp
await RunAsync("virtual camera model defaults match builtin preload", async () =>
{
    var node = new VirtualCameraNodeModel();
    return node.SourcePath == "builtin://vision/sample-set"
        && node.LoadMode == VirtualCameraLoadMode.Preload
        && node.FrameRate == 18.0
        && node.MaxPreloadedImages == 100
        && node.MaxPreloadedBytes == 536870912L
        && !node.SkipErrorImages
        && VirtualCameraNodeModel.IsValidFrameRate(0.1)
        && VirtualCameraNodeModel.IsValidFrameRate(1000.0)
        && !VirtualCameraNodeModel.IsValidFrameRate(double.NaN)
        && !VirtualCameraNodeModel.IsValidFrameRate(double.PositiveInfinity)
        && !VirtualCameraNodeModel.IsValidFrameRate(0.099)
        && !VirtualCameraNodeModel.IsValidFrameRate(1000.001);
});
```

- [ ] **Step 2: 写编辑器帧率控件的失败测试**

在 editor 测试中读取新字段、输入合法小数，再逐个输入无效值：

```csharp
var frameRate = GetPrivateField<TextBox>(content, "_frameRateEditor");

source.Text = "C:\\frames";
mode.SelectedItem = VirtualCameraLoadMode.Dynamic;
frameRate.Text = "29.97";
maxImages.Text = "7";
maxBytes.Text = "123456";
skipErrors.IsChecked = true;

var changesAfterValidInput = graphChanges;
foreach (var invalid in new[]
{
    string.Empty,
    "not-a-double",
    "NaN",
    "Infinity",
    "0.09",
    "1000.01",
})
{
    frameRate.Text = invalid;
}
maxImages.Text = "not-an-int";
maxBytes.Text = "not-a-long";
```

把返回断言更新为：

```csharp
&& node.FrameRate == 29.97
&& changesAfterValidInput == 6
&& graphChanges == changesAfterValidInput
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: 编译失败，提示 `VirtualCameraNodeModel.FrameRate`、`IsValidFrameRate` 或 `_frameRateEditor` 不存在。

- [ ] **Step 4: 实现模型配置契约**

在 `VirtualCameraNodeModel` 中加入：

```csharp
internal const double DefaultFrameRate = 18.0;
internal const double MinimumFrameRate = 0.1;
internal const double MaximumFrameRate = 1000.0;

public double FrameRate { get; set; } = DefaultFrameRate;

internal static bool IsValidFrameRate(double value)
{
    return !double.IsNaN(value)
        && !double.IsInfinity(value)
        && value >= MinimumFrameRate
        && value <= MaximumFrameRate;
}
```

在 `WriteWorkflowInputs` 中精确加入：

```csharp
node.Inputs["frameRate"] = FrameRate;
```

- [ ] **Step 5: 实现编辑器控件和校验**

在 `Load mode` 控件之后加入：

```xml
<TextBlock Text="Frame rate (FPS)"
           Foreground="{DynamicResource colorNeutralForeground3}"
           Margin="0,0,0,3" />
<TextBox x:Name="FrameRateEditor" Margin="0,0,0,6" />
```

在 code-behind 增加字段、查找、事件和初始化：

```csharp
private readonly TextBox _frameRateEditor;
```

```csharp
_frameRateEditor = Find<TextBox>(root, "FrameRateEditor");
_frameRateEditor.TextChanged += FrameRateEditor_TextChanged;
_frameRateEditor.Text = _node.FrameRate.ToString("G17", CultureInfo.InvariantCulture);
```

增加完整 handler：

```csharp
private void FrameRateEditor_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_initializing
        || !double.TryParse(
            _frameRateEditor.Text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
        || !VirtualCameraNodeModel.IsValidFrameRate(value))
    {
        return;
    }

    _node.FrameRate = value;
    NotifyChanged();
}
```

确保 `_initializing = false` 仍在所有控件赋初值之后。

同时把现有预加载字节上限控件改为 MB 展示和输入：标签使用
`Maximum preloaded memory (MB)`，初始化显示
`_node.MaxPreloadedBytes / 1048576`，输入使用 invariant culture 解析正整数 MB，
通过 checked 乘法转换为 bytes 后写入 `_node.MaxPreloadedBytes`。默认值
`536870912` 显示为 `512`；空值、非数字、非正数和转换溢出均不修改模型或触发通知。
XML 属性、workflow key 和 executor 仍保持 `MaxPreloadedBytes`/`maxPreloadedBytes` 的字节契约。

- [ ] **Step 6: 运行模型和 UI 测试确认绿灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: 新增 model/editor 断言 PASS，测试末尾 `ALL PASS`。

- [ ] **Step 7: 提交配置层改动**

```powershell
git add NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs NodeCraft.Vision/Views/VirtualCameraEditor.xaml NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: add virtual camera frame rate configuration"
```

---

### Task 2: 增加 Vision 私有图片模板并保持 Preload 零拷贝

**Files:**
- Create: `NodeCraft.Vision/Nodes/VirtualCameraImageTemplate.cs`
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs:8-77`
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraSource.cs:9-214`
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs:148-326`
- Test: `NodeCraft.Tests/VirtualCameraTests.cs:110-351,1477-1578`

**Interfaces:**
- Produces: `VirtualCameraImageTemplate`, `BufferLength`、`CreateFrame(ulong, ulong, DateTimeOffset)`，以及 `IVirtualCameraImageLoader.Load(string)`。
- Consumes: 未修改的 `FlowImage.FromOwnedBuffer`；不得为模板增加任何 `NodeCraft.Flow` API。

- [ ] **Step 1: 写模板元数据和 buffer 共享的失败测试**

在测试文件顶部增加：

```csharp
using System.Runtime.InteropServices;
```

把 Gray8/Bgr24 loader 测试改为先获得模板，再创建帧：

```csharp
var capturedAt = new DateTimeOffset(2026, 8, 16, 1, 2, 3, TimeSpan.Zero);
var monoTemplate = new VirtualCameraImageLoader().Load(monoPath);
var colorTemplate = new VirtualCameraImageLoader().Load(colorPath);
var mono = monoTemplate.CreateFrame(4, 1234, capturedAt);
var color = colorTemplate.CreateFrame(5, 5678, capturedAt);
var monoAgain = monoTemplate.CreateFrame(6, 6789, capturedAt.AddSeconds(1));

var firstArrayFound = MemoryMarshal.TryGetArray(mono.Buffer, out var firstArray);
var secondArrayFound = MemoryMarshal.TryGetArray(monoAgain.Buffer, out var secondArray);
return mono.PixelFormat == FlowPixelFormat.Mono8
    && mono.Stride == 2
    && mono.Buffer.Span.SequenceEqual(new byte[] { 9, 10 })
    && mono.FrameId == 4
    && mono.DeviceTimestamp == 1234
    && mono.CapturedAtUtc == capturedAt
    && color.PixelFormat == FlowPixelFormat.Bgr24
    && color.Stride == 3
    && color.Buffer.Span.SequenceEqual(new byte[] { 1, 2, 3 })
    && color.FrameId == 5
    && color.DeviceTimestamp == 5678
    && firstArrayFound
    && secondArrayFound
    && ReferenceEquals(firstArray.Array, secondArray.Array)
    && !ReferenceEquals(mono, monoAgain);
```

把 local source 断言改为 `source.Entries[0].PreloadedTemplate == null`；builtin source 测试通过 `PreloadedTemplate.CreateFrame(0, 0, DateTimeOffset.UnixEpoch)` 检查尺寸和像素。

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: 编译失败，提示 `VirtualCameraImageTemplate`、`PreloadedTemplate` 或新的 loader 签名不存在。

- [ ] **Step 3: 创建完整图片模板**

创建 `VirtualCameraImageTemplate.cs`：

```csharp
using System;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VirtualCameraImageTemplate
    {
        private readonly byte[] _buffer;

        internal VirtualCameraImageTemplate(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var validated = FlowImage.FromOwnedBuffer(
                width,
                height,
                stride,
                pixelFormat,
                kind,
                buffer,
                0,
                0,
                DateTimeOffset.UnixEpoch);
            Width = validated.Width;
            Height = validated.Height;
            Stride = validated.Stride;
            PixelFormat = validated.PixelFormat;
            Kind = validated.Kind;
            _buffer = buffer;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int Stride { get; }

        internal FlowPixelFormat PixelFormat { get; }

        internal FlowImageKind Kind { get; }

        internal int BufferLength => _buffer.Length;

        internal FlowImage CreateFrame(
            ulong frameId,
            ulong deviceTimestamp,
            DateTimeOffset capturedAtUtc)
        {
            return FlowImage.FromOwnedBuffer(
                Width,
                Height,
                Stride,
                PixelFormat,
                Kind,
                _buffer,
                frameId,
                deviceTimestamp,
                capturedAtUtc);
        }
    }
}
```

该类型和 buffer 都保持 internal/private；不要修改 `FlowImage`。

- [ ] **Step 4: 将 loader 改为返回模板**

把接口和实现签名改为：

```csharp
internal interface IVirtualCameraImageLoader
{
    VirtualCameraImageTemplate Load(string path);
}
```

```csharp
public VirtualCameraImageTemplate Load(string path)
```

把 loader 成功返回值替换为：

```csharp
return new VirtualCameraImageTemplate(
    bitmap.PixelWidth,
    bitmap.PixelHeight,
    stride,
    bitmap.Format == PixelFormats.Gray8
        ? FlowPixelFormat.Mono8
        : FlowPixelFormat.Bgr24,
    FlowImageKind.Color,
    buffer);
```

删除 loader 的 `frameId` 参数和 loader 内部的 `DateTimeOffset.UtcNow`；异常过滤保持原样。

- [ ] **Step 5: 将 source entry 和 builtin 改为模板**

把 `VirtualCameraEntry` 的构造参数和属性改为：

```csharp
internal VirtualCameraEntry(
    int ordinal,
    string path,
    VirtualCameraImageTemplate preloadedTemplate)
{
    Ordinal = ordinal;
    Path = path;
    PreloadedTemplate = preloadedTemplate;
}

public VirtualCameraImageTemplate PreloadedTemplate { get; }
```

把 builtin factory 改为 `Func<VirtualCameraImageTemplate>`，并把两个资产工厂改成不接收 frameId 的模板构造：

```csharp
private static VirtualCameraSource CreateBuiltinCollection()
{
    return new VirtualCameraSource(
        "builtin://vision/sample-set",
        isBuiltin: true,
        new[]
        {
            new VirtualCameraEntry(
                0,
                "builtin://vision/sample-set/checkerboard",
                CreateCheckerboardImage()),
            new VirtualCameraEntry(
                1,
                "builtin://vision/sample-set/color-bars",
                CreateColorBarsImage()),
        });
}

private static VirtualCameraSource CreateBuiltinSingle(
    string path,
    Func<VirtualCameraImageTemplate> imageFactory)
{
    return new VirtualCameraSource(
        "builtin://vision/sample-set",
        isBuiltin: true,
        new[] { new VirtualCameraEntry(0, path, imageFactory()) });
}
```

```csharp
private static VirtualCameraImageTemplate CreateCheckerboardImage()
{
    return new VirtualCameraImageTemplate(
        2,
        2,
        6,
        FlowPixelFormat.Bgr24,
        FlowImageKind.Color,
        new byte[]
        {
            255, 255, 255, 0, 0, 0,
            0, 0, 0, 255, 255, 255,
        });
}

private static VirtualCameraImageTemplate CreateColorBarsImage()
{
    return new VirtualCameraImageTemplate(
        3,
        1,
        9,
        FlowPixelFormat.Bgr24,
        FlowImageKind.Color,
        new byte[]
        {
            255, 0, 0, 0, 255, 0, 0, 0, 255,
        });
}
```

- [ ] **Step 6: 让 executor 暂时保留旧序号语义但使用模板**

本任务只替换图片存储，不加入节拍。Preload 中把局部值和字节统计改为：

```csharp
VirtualCameraImageTemplate template;
template = entry.PreloadedTemplate ?? _imageLoader.Load(entry.Path);
if (template == null)
{
    throw new InvalidOperationException(
        $"VirtualCamera source '{source.ImageDirectory}' loader returned no image for '{entry.Path}'.");
}
```

```csharp
var nextTotalBytes = AddPreloadedBytesChecked(
    totalBytes,
    template.BufferLength,
    source.ImageDirectory,
    entry.Path);
```

```csharp
validEntries.Add(new VirtualCameraEntry(entry.Ordinal, entry.Path, template));
```

Preload Prepare 暂时创建：

```csharp
_current = entry.PreloadedTemplate.CreateFrame(
    (ulong)entry.Ordinal,
    0,
    DateTimeOffset.UtcNow);
```

Dynamic 成功路径改为：

```csharp
var template = _imageLoader.Load(entry.Path);
if (template == null)
{
    throw new InvalidOperationException(
        $"VirtualCamera source '{_imageDirectory}' loader returned no image for '{entry.Path}'.");
}

cancellationToken.ThrowIfCancellationRequested();
_current = template.CreateFrame(
    (ulong)entry.Ordinal,
    0,
    DateTimeOffset.UtcNow);
_currentEntry = entry;
_index = nextIndex;
return Task.CompletedTask;
```

- [ ] **Step 7: 更新 test loaders 和旧断言**

四个 fake loader 都实现 `VirtualCameraImageTemplate Load(string path)`。其中 recording loader 使用：

```csharp
internal List<string> Loads { get; } = new List<string>();

public VirtualCameraImageTemplate Load(string path)
{
    Loads.Add(path);
    return _inner.Load(path);
}
```

其他 fake 的成功返回统一使用：

```csharp
return new VirtualCameraImageTemplate(
    1,
    1,
    3,
    FlowPixelFormat.Bgr24,
    FlowImageKind.Color,
    new byte[] { 1, 2, 3 });
```

`SelectiveVirtualCameraImageLoader` 继续在匹配 `_badPath` 时抛 `VirtualCameraImageLoadException`；`ThrowingVirtualCameraImageLoader` 继续原样抛构造时传入的异常。将所有 `loader.Loads.Select(load => load.Path)` 改为直接使用 `loader.Loads`，删除对 loader 输入 frameId 的断言。

按下表更新其余直接 loader 调用，确保本任务结束时没有旧签名：

| 现有调用 | 新调用 |
| --- | --- |
| `loader.Load(path, frameId)`，只验证异常 | `loader.Load(path)` |
| `loader.Load(path, frameId)`，需要检查图片 | `loader.Load(path).CreateFrame(frameId, 0, capturedAtUtc)` |
| worker 中返回两个 `FlowImage` | worker 中先 `Load` 两个模板，再以固定 frameId/UTC 调用 `CreateFrame` |

Run:

```powershell
rg -n "\.Load\([^\)]*,\s*[0-9a-zA-Z_]+\)" NodeCraft.Vision/Nodes NodeCraft.Tests/VirtualCameraTests.cs -g "VirtualCamera*.cs"
```

Expected: 不再出现 Virtual Camera loader 的两参数调用；普通 LINQ/其他类型的 `Load` 不在这些文件中。

把 Preload wrap 测试的 `ReferenceEquals(first["image"], wrapped["image"])` 改为：两个 `FlowImage` 实例不相同，但 `MemoryMarshal.TryGetArray` 得到的底层数组相同。

- [ ] **Step 8: 运行全量测试确认模板改造绿灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: loader/source/Preload/Dynamic 现有行为 PASS，测试末尾 `ALL PASS`；`NodeCraft.Flow` 无 diff。

- [ ] **Step 9: 提交 Vision 私有模板**

```powershell
git add NodeCraft.Vision/Nodes/VirtualCameraImageTemplate.cs NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs NodeCraft.Vision/Nodes/VirtualCameraSource.cs NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "refactor: add virtual camera image templates"
```

---

### Task 3: 在 executor 内实现背压帧率和逐帧元数据

**Files:**
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs:1-387`
- Test: `NodeCraft.Tests/VirtualCameraTests.cs:301-1045,1394-1578`

**Interfaces:**
- Consumes: Task 1 的帧率常量/校验和 Task 2 的 `VirtualCameraImageTemplate`。
- Produces: 内部可注入 constructor、`IncrementFrameIdChecked`、runtime default/validation、可取消 deadline、连续 metadata。

- [ ] **Step 1: 增加确定性 timing helpers 和 executor factory**

测试文件增加：

```csharp
using NodeCraft.Vision.Camera;
```

在测试辅助类型区域增加：

```csharp
private sealed class VirtualCameraTestClock : IMonotonicClock
{
    public TimeSpan Now { get; private set; }

    internal void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Now += duration;
    }
}

private sealed class VirtualCameraTestTiming
{
    internal VirtualCameraTestClock Clock { get; } = new VirtualCameraTestClock();

    internal List<TimeSpan> Delays { get; } = new List<TimeSpan>();

    internal Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(duration);
        Clock.Advance(duration);
        return Task.CompletedTask;
    }
}

private static VirtualCameraExecutor CreateVirtualCameraExecutor(
    IVirtualCameraImageLoader imageLoader = null)
{
    var timing = new VirtualCameraTestTiming();
    return new VirtualCameraExecutor(
        imageLoader,
        timing.Clock,
        timing.DelayAsync,
        () => DateTimeOffset.UnixEpoch);
}
```

将现有直接 executor 单元测试中的 `new VirtualCameraExecutor()` 和 `new VirtualCameraExecutor(loader)` 改为 `CreateVirtualCameraExecutor()` 和 `CreateVirtualCameraExecutor(loader)`。保留本任务新增 timing tests 的显式 constructor；plugin registry integration 使用生产 factory，不替换。

- [ ] **Step 2: 写 runtime 帧率兼容和非法值失败测试**

把 `CreateVirtualCameraContext` 签名扩展为：

```csharp
private static FlowNodeSessionContext CreateVirtualCameraContext(
    string sourcePath,
    VirtualCameraLoadMode loadMode,
    int maxImages,
    long maxBytes,
    bool skipErrors,
    out WorkflowNode node,
    out FlowNodeDefinition definition,
    double? frameRate = null)
```

构造完 node 后仅在显式传值时写 key：

```csharp
if (frameRate.HasValue)
{
    node.Inputs["frameRate"] = frameRate.Value;
}
```

新增测试：

```csharp
await RunAsync("virtual camera defaults missing runtime frame rate and rejects invalid values", async () =>
{
    var timing = new VirtualCameraTestTiming();
    var executor = new VirtualCameraExecutor(
        null,
        timing.Clock,
        timing.DelayAsync,
        () => DateTimeOffset.UnixEpoch);
    var context = CreateVirtualCameraContext(
        "builtin://vision/sample-set",
        VirtualCameraLoadMode.Preload,
        10,
        100,
        false,
        out _,
        out _);
    await executor.StartSessionAsync(context, CancellationToken.None);
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);
    var defaultDelay = timing.Delays.Single();
    var expectedDefault = TimeSpan.FromSeconds(1.0 / 18.0);

    var invalidValues = new object[]
    {
        "18",
        double.NaN,
        double.NegativeInfinity,
        double.PositiveInfinity,
        0.099,
        1000.001,
    };
    var rejected = 0;
    foreach (var invalidValue in invalidValues)
    {
        var invalidContext = CreateVirtualCameraContext(
            "builtin://vision/sample-set",
            VirtualCameraLoadMode.Preload,
            10,
            100,
            false,
            out var invalidNode,
            out _);
        invalidNode.Inputs["frameRate"] = invalidValue;
        var invalidExecutor = CreateVirtualCameraExecutor();
        try
        {
            await invalidExecutor.StartSessionAsync(
                invalidContext,
                CancellationToken.None);
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                && exception.Message.Contains("frameRate", StringComparison.Ordinal)
                && exception.Message.Contains(
                    "builtin://vision/sample-set", StringComparison.Ordinal))
            {
                rejected++;
            }
        }
        finally
        {
            await invalidExecutor.StopSessionAsync(
                invalidContext,
                CancellationToken.None);
        }
    }

    var acceptedBoundaries = 0;
    foreach (var validRate in new[] { 0.1, 1000.0 })
    {
        var validContext = CreateVirtualCameraContext(
            "builtin://vision/sample-set",
            VirtualCameraLoadMode.Preload,
            10,
            100,
            false,
            out _,
            out _,
            frameRate: validRate);
        var validExecutor = CreateVirtualCameraExecutor();
        await validExecutor.StartSessionAsync(validContext, CancellationToken.None);
        acceptedBoundaries++;
        await validExecutor.StopSessionAsync(validContext, CancellationToken.None);
    }

    return defaultDelay == expectedDefault
        && rejected == invalidValues.Length
        && acceptedBoundaries == 2;
});
```

在原有 runtime wrong-type mutations 中加入 `("frameRate", node => node.Inputs["frameRate"] = "18")`，但不要把缺失 `frameRate` 当成错误。

- [ ] **Step 3: 写节拍、不跳帧和 metadata 失败测试**

增加测试辅助函数：

```csharp
private static async Task<(FlowImage Image, string Path)> ReadVirtualCameraFrameAsync(
    VirtualCameraExecutor executor,
    FlowNodeSessionContext context,
    WorkflowNode node,
    FlowNodeDefinition definition,
    CancellationToken cancellationToken)
{
    await executor.PrepareIterationAsync(context, cancellationToken);
    var output = await executor.ExecuteAsync(
        new FlowExecutionContext(),
        node,
        definition,
        new Dictionary<string, object>(),
        cancellationToken);
    return ((FlowImage)output["image"], (string)output["imagePath"]);
}
```

新增核心节拍测试：

```csharp
await RunAsync("virtual camera paces sequential frames and rebases after slow graph", async () =>
{
    var timing = new VirtualCameraTestTiming();
    var utcValues = new Queue<DateTimeOffset>(new[]
    {
        new DateTimeOffset(2026, 8, 16, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 16, 1, 0, 1, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 16, 1, 0, 2, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 16, 1, 0, 3, TimeSpan.Zero),
    });
    var executor = new VirtualCameraExecutor(
        null,
        timing.Clock,
        timing.DelayAsync,
        () => utcValues.Dequeue());
    var context = CreateVirtualCameraContext(
        "builtin://vision/sample-set",
        VirtualCameraLoadMode.Preload,
        10,
        100,
        false,
        out var node,
        out var definition,
        frameRate: 20.0);

    await executor.StartSessionAsync(context, CancellationToken.None);
    var first = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    timing.Clock.Advance(TimeSpan.FromMilliseconds(10));
    var second = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    timing.Clock.Advance(TimeSpan.FromMilliseconds(100));
    var third = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    var fourth = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);

    var images = new[] { first.Image, second.Image, third.Image, fourth.Image };
    return timing.Delays.SequenceEqual(new[]
        {
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(50),
        })
        && new[] { first.Path, second.Path, third.Path, fourth.Path }
            .Select(path => path.Substring(path.LastIndexOf('/') + 1))
            .SequenceEqual(new[] { "checkerboard", "color-bars", "checkerboard", "color-bars" })
        && images.Select(image => image.FrameId).SequenceEqual(
            new[] { 0UL, 1UL, 2UL, 3UL })
        && images.Select(image => image.DeviceTimestamp).SequenceEqual(
            new[] { 50000UL, 100000UL, 200000UL, 250000UL })
        && images.Select(image => image.CapturedAtUtc).SequenceEqual(new[]
            {
                new DateTimeOffset(2026, 8, 16, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 16, 1, 0, 1, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 16, 1, 0, 2, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 16, 1, 0, 3, TimeSpan.Zero),
            });
});
```

该测试的 delay 序列证明：首帧等待一个周期、快处理只等待剩余时间、慢处理后没有追赶 burst、下一帧重新等待完整周期；path 和 FrameId 证明不跳图。

- [ ] **Step 4: 运行测试确认红灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: runtime `frameRate` 尚未被读取，Prepare 没有调用 delay，FrameId/DeviceTimestamp 仍使用旧语义，因此新增测试 FAIL。

- [ ] **Step 5: 增加 executor timing 依赖和 session 状态**

在 executor 增加 `using NodeCraft.Vision.Camera;`，并增加字段：

```csharp
private readonly IMonotonicClock _clock;
private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
private readonly Func<DateTimeOffset> _utcNow;
private TimeSpan _framePeriod;
private TimeSpan _sessionClockOrigin;
private TimeSpan _nextFrameDue;
private ulong _nextFrameId;
```

将 constructor 精确改为：

```csharp
internal VirtualCameraExecutor(
    IVirtualCameraImageLoader imageLoader = null,
    IMonotonicClock clock = null,
    Func<TimeSpan, CancellationToken, Task> delayAsync = null,
    Func<DateTimeOffset> utcNow = null)
{
    _imageLoader = imageLoader ?? new VirtualCameraImageLoader();
    _clock = clock ?? new SystemMonotonicClock();
    _delayAsync = delayAsync ?? Task.Delay;
    _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
}
```

- [ ] **Step 6: 实现 runtime frameRate default/validation 和 Start 初始化**

增加 helper：

```csharp
private static double ReadFrameRateOrDefault(
    FlowNodeSessionContext context,
    string sourceLabel)
{
    if (context?.Node?.Inputs == null
        || !context.Node.Inputs.TryGetValue("frameRate", out var value))
    {
        return VirtualCameraNodeModel.DefaultFrameRate;
    }

    if (!(value is double frameRate)
        || !VirtualCameraNodeModel.IsValidFrameRate(frameRate))
    {
        throw new InvalidOperationException(
            $"VirtualCamera source '{sourceLabel}' has invalid runtime input 'frameRate'.");
    }

    return frameRate;
}
```

在读取现有五个 inputs 后读取：

```csharp
var frameRate = ReadFrameRateOrDefault(context, sourceLabel);
var framePeriod = TimeSpan.FromSeconds(1.0 / frameRate);
```

在所有 source resolve/preload 和 cancellation 检查通过后，用局部时钟值一次性提交：

```csharp
var clockOrigin = _clock.Now;
_entries = preparedEntries;
_index = -1;
_current = null;
_currentEntry = null;
_framePeriod = framePeriod;
_sessionClockOrigin = clockOrigin;
_nextFrameDue = clockOrigin + framePeriod;
_nextFrameId = 0;
_started = true;
```

- [ ] **Step 7: 实现可取消 wait、连续元数据和原子成功提交**

增加完整 helpers：

```csharp
private async Task<TimeSpan> WaitForFrameStartAsync(
    CancellationToken cancellationToken)
{
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.Now;
        var remaining = _nextFrameDue - now;
        if (remaining <= TimeSpan.Zero)
        {
            return now;
        }

        await _delayAsync(remaining, cancellationToken).ConfigureAwait(false);
    }
}

private ulong GetDeviceTimestampMicroseconds(
    TimeSpan frameStart,
    FlowNodeSessionContext context)
{
    var elapsed = frameStart - _sessionClockOrigin;
    if (elapsed < TimeSpan.Zero)
    {
        throw new InvalidOperationException(
            $"VirtualCamera source '{GetSourceLabel(context.Node)}' monotonic clock moved backwards.");
    }

    return checked((ulong)(elapsed.Ticks / 10L));
}

internal static ulong IncrementFrameIdChecked(
    ulong frameId,
    string sourcePath)
{
    try
    {
        return checked(frameId + 1);
    }
    catch (OverflowException exception)
    {
        throw new InvalidOperationException(
            $"VirtualCamera source '{sourcePath}' exhausted frame IDs.",
            exception);
    }
}
```

把 `PrepareIterationAsync` 改为 `async Task`，用以下完整成功路径替换旧的 Preload/Dynamic current 提交：

```csharp
public async Task PrepareIterationAsync(
    FlowNodeSessionContext context,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    EnsureStarted(context);
    if (_entries.Count == 0)
    {
        throw new InvalidOperationException(
            $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no readable images.");
    }

    _current = null;
    _currentEntry = null;
    var frameStart = await WaitForFrameStartAsync(cancellationToken)
        .ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();

    VirtualCameraEntry entry;
    VirtualCameraImageTemplate template;
    int nextIndex;
    if (_loadMode == VirtualCameraLoadMode.Dynamic)
    {
        var candidate = PrepareDynamicCandidate(context, cancellationToken);
        entry = candidate.Entry;
        template = candidate.Template;
        nextIndex = candidate.Index;
    }
    else
    {
        nextIndex = (_index + 1) % _entries.Count;
        entry = _entries[nextIndex];
        template = entry.PreloadedTemplate
            ?? throw new InvalidOperationException(
                $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no preloaded image for '{entry.Path}'.");
    }

    var frameId = _nextFrameId;
    var followingFrameId = IncrementFrameIdChecked(
        frameId,
        GetSourceLabel(context.Node));
    var deviceTimestamp = GetDeviceTimestampMicroseconds(frameStart, context);
    var capturedAtUtc = _utcNow();
    var image = template.CreateFrame(frameId, deviceTimestamp, capturedAtUtc);
    cancellationToken.ThrowIfCancellationRequested();

    _current = image;
    _currentEntry = entry;
    _index = nextIndex;
    _nextFrameId = followingFrameId;
    _nextFrameDue = frameStart + _framePeriod;
}
```

将旧 Dynamic helper 改为返回 candidate，成功前不写 current：

```csharp
private (
    VirtualCameraEntry Entry,
    VirtualCameraImageTemplate Template,
    int Index) PrepareDynamicCandidate(
        FlowNodeSessionContext context,
        CancellationToken cancellationToken)
{
    if (_entries.Count == 0)
    {
        throw new InvalidOperationException(
            $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no readable images.");
    }

    while (_entries.Count > 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextIndex = (_index + 1) % _entries.Count;
        var entry = _entries[nextIndex];
        try
        {
            var template = _imageLoader.Load(entry.Path);
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{_imageDirectory}' loader returned no image for '{entry.Path}'.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return (entry, template, nextIndex);
        }
        catch (Exception exception) when (
            _skipErrorImages
            && VirtualCameraImageLoader.IsSkippableImageLoadError(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entries.RemoveAt(nextIndex);
            if (_entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{_imageDirectory}' has no readable images after '{entry.Path}'.",
                    exception);
            }

            _index = nextIndex - 1;
        }
    }

    throw new InvalidOperationException(
        $"VirtualCamera source '{_imageDirectory}' has no readable images.");
}
```

- [ ] **Step 8: 清理 Stop 状态并更新旧 FrameId 断言**

在 `ClearSessionState` 增加：

```csharp
_framePeriod = TimeSpan.Zero;
_sessionClockOrigin = TimeSpan.Zero;
_nextFrameDue = TimeSpan.Zero;
_nextFrameId = 0;
```

更新现有断言：

- Preload `a, b, a` 的 FrameId 从 `0, 1, 0` 改为 `0, 1, 2`。
- Dynamic 单文件两次输出从 `0, 0` 改为 `0, 1`。
- Dynamic `A, Bad, C, A` 的成功输出从 `0, 2, 0` 改为 `0, 1, 2`；loader 只断言路径尝试顺序 `A, Bad, C, A`。
- 其他首次成功帧继续断言 FrameId 0。

给 byte accounting overflow 测试旁增加：

```csharp
await RunAsync("virtual camera wraps frame id overflow", async () =>
{
    try
    {
        VirtualCameraExecutor.IncrementFrameIdChecked(
            ulong.MaxValue,
            "builtin://vision/sample-set");
        return false;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
            && exception.Message.Contains(
                "builtin://vision/sample-set", StringComparison.Ordinal)
            && exception.InnerException is OverflowException;
    }
});
```

- [ ] **Step 9: 运行全量测试确认节拍绿灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: default/invalid FPS、delay 序列、顺序 path、连续 FrameId 和 metadata 测试 PASS；末尾 `ALL PASS`。

- [ ] **Step 10: 提交 executor 帧率实现**

```powershell
git add NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: pace virtual camera frames"
```

---

### Task 4: 覆盖等待取消、重启和 graph integration

**Files:**
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs:591-700,887-930,1118-1238,1394-1475`

**Interfaces:**
- Consumes: Task 3 的可注入 clock/delay/UTC constructor 和 runtime `frameRate`。
- Produces: 等待中取消不提交、Stop/restart 重置、真实 registry/link/session 路径兼容的回归证据。

- [ ] **Step 1: 写等待期间取消且重试同帧的失败测试**

新增：

```csharp
await RunAsync("virtual camera cancellation during frame wait retries the same frame", async () =>
{
    var clock = new VirtualCameraTestClock();
    var requested = new TaskCompletionSource<TimeSpan>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var delayCalls = 0;
    async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        delayCalls++;
        if (delayCalls == 1)
        {
            requested.TrySetResult(duration);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        clock.Advance(duration);
    }

    var executor = new VirtualCameraExecutor(
        null,
        clock,
        DelayAsync,
        () => DateTimeOffset.UnixEpoch);
    var context = CreateVirtualCameraContext(
        "builtin://vision/sample-set",
        VirtualCameraLoadMode.Preload,
        10,
        100,
        false,
        out var node,
        out var definition,
        frameRate: 20.0);
    await executor.StartSessionAsync(context, CancellationToken.None);

    using var cancellation = new CancellationTokenSource();
    var pending = executor.PrepareIterationAsync(context, cancellation.Token);
    var requestedDelay = await requested.Task;
    cancellation.Cancel();
    var canceled = false;
    try
    {
        await pending;
    }
    catch (OperationCanceledException exception)
    {
        canceled = exception.CancellationToken == cancellation.Token;
    }

    var first = await ReadVirtualCameraFrameAsync(
        executor,
        context,
        node,
        definition,
        CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);

    return canceled
        && requestedDelay == TimeSpan.FromMilliseconds(50)
        && delayCalls == 2
        && first.Image.FrameId == 0
        && first.Path.EndsWith("/checkerboard", StringComparison.Ordinal);
});
```

- [ ] **Step 2: 写 Stop/restart 重置节拍和帧号测试**

新增：

```csharp
await RunAsync("virtual camera restart resets first-frame delay path and metadata", async () =>
{
    var timing = new VirtualCameraTestTiming();
    var executor = new VirtualCameraExecutor(
        null,
        timing.Clock,
        timing.DelayAsync,
        () => DateTimeOffset.UnixEpoch);
    var context = CreateVirtualCameraContext(
        "builtin://vision/sample-set",
        VirtualCameraLoadMode.Preload,
        10,
        100,
        false,
        out var node,
        out var definition,
        frameRate: 20.0);

    await executor.StartSessionAsync(context, CancellationToken.None);
    var firstSession = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);
    timing.Clock.Advance(TimeSpan.FromMilliseconds(10));

    await executor.StartSessionAsync(context, CancellationToken.None);
    var secondSession = await ReadVirtualCameraFrameAsync(
        executor, context, node, definition, CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);

    return timing.Delays.SequenceEqual(new[]
        {
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50),
        })
        && firstSession.Image.FrameId == 0
        && secondSession.Image.FrameId == 0
        && firstSession.Image.DeviceTimestamp == 50000
        && secondSession.Image.DeviceTimestamp == 50000
        && firstSession.Path.EndsWith("/checkerboard", StringComparison.Ordinal)
        && secondSession.Path.EndsWith("/checkerboard", StringComparison.Ordinal);
});
```

- [ ] **Step 3: 调整现有 Dynamic cancellation 回归**

保留 `CancelOnLoadVirtualCameraImageLoader` 在 load 内取消 token 的行为，并确认测试仍断言：

```csharp
return canceled
    && loader.LoadCount == 2
    && (string)output["imagePath"] == Path.Combine(fixture.DirectoryPath, "A.png")
    && ((FlowImage)output["image"]).FrameId == 0;
```

该测试与 Step 1 分别覆盖 delay 取消和 decode 后取消；二者都必须重试首图、FrameId 0。

- [ ] **Step 4: 让 graph integration 显式使用快速但合法 FPS**

在 integration workflow 的 Virtual Camera Inputs 中加入：

```csharp
["frameRate"] = 1000.0,
```

保留以下断言不变：

```csharp
&& firstObservation.ImagePath == Path.GetFullPath(firstPath)
&& secondObservation.ImagePath == Path.GetFullPath(secondPath)
&& firstObservation.Image.FrameId == 0
&& secondObservation.Image.FrameId == 1
&& ReferenceEquals(firstObservation.Image, firstPreviewValue)
&& ReferenceEquals(secondObservation.Image, secondPreviewValue)
```

该测试必须继续通过真实 `VisionPlugin -> FlowNodeRegistry -> GraphExecutionSession -> Preview` 路径，不允许写全局 registry 或修改生产 executor factory 来注入 fake timing。

- [ ] **Step 5: 运行全量测试确认生命周期和集成绿灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: wait cancellation、Dynamic cancellation、restart、graph integration 全部 PASS；末尾 `ALL PASS`。

- [ ] **Step 6: 检查测试没有无意真实等待**

Run:

```powershell
rg -n "new VirtualCameraExecutor" NodeCraft.Tests/VirtualCameraTests.cs
rg -n "Task.Delay" NodeCraft.Tests/VirtualCameraTests.cs
```

Expected: 直接 constructor 只存在于 `CreateVirtualCameraExecutor`、显式 timing/cancellation tests；无限 delay 只存在于可取消等待测试。普通 executor 单元测试全部使用自动推进 fake clock。

- [ ] **Step 7: 提交生命周期和 integration 测试**

```powershell
git add NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "test: cover virtual camera frame pacing lifecycle"
```

---

### Task 5: 全量验证和 `barcode-pics` 实际回归

**Files:**
- Modify: none unless verification reveals a concrete defect in files listed above.

**Interfaces:**
- Consumes: Tasks 1-4 的完整实现。
- Produces: build/test/static scope/实际预览证据。

- [ ] **Step 1: 检查变更范围和格式**

Run:

```powershell
git diff --check main...HEAD
git status --short --branch
git diff --name-only main...HEAD
```

Expected: 代码变更仅包含 File Map 中的 Virtual Camera 和测试文件以及本计划/规格提交；`NodeCraft.Flow/**`、执行控制器、真实相机和 mailbox 无代码 diff。

- [ ] **Step 2: 静态检查关键约束**

Run:

```powershell
rg -n "FrameRate|frameRate|WaitForFrameStartAsync|IncrementFrameIdChecked|PreloadedTemplate|CreateFrame" NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Vision/Nodes/VirtualCameraImageTemplate.cs NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs
rg -n "LatestFrameMailbox|Task.Run|ConcurrentQueue|Channel<" NodeCraft.Vision/Nodes -g "VirtualCamera*.cs"
```

Expected: 第一条显示完整配置/节拍/模板链路；第二条无输出，证明没有后台 producer 或队列。

- [ ] **Step 3: 构建 solution**

Run:

```powershell
dotnet build NodeCraft.sln --no-restore
```

Expected: build 成功，无编译错误；不得修改 framework 来消除无关 warning。

- [ ] **Step 4: 运行完整测试跑棒**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-build --no-restore
```

Expected: 所有 Virtual Camera 新旧测试 PASS，末尾输出 `ALL PASS`。

- [ ] **Step 5: 使用实际图片目录回归预览**

先确认目录仍存在并包含受支持图片：

```powershell
Get-ChildItem -LiteralPath D:\test\barcode-pics -File | Where-Object { $_.Extension -in '.jpg', '.png', '.bmp' } | Select-Object Name, Length
```

启动应用：

```powershell
dotnet run --project NodeCraft/NodeCraft.csproj --no-build --no-restore
```

在 UI 中执行以下固定步骤：

1. 创建 Virtual Camera 和 FlowImage Preview，并连接 `image`。
2. `SourcePath = D:\test\barcode-pics`、`LoadMode = Preload`、`Frame rate (FPS) = 18`。
3. 连续运行至少 5 秒，观察 `imagePath` 和 preview 按同一文件顺序持续变化。
4. 确认没有“路径一直变化但预览停住”的现象。
5. 停止运行，确认停止后没有补播或额外切换一帧。

Expected: 大图预览在运行期间稳定更新；严格顺序、不跳图；停止后状态保持最后一帧。

- [ ] **Step 6: 对照规格完成自审**

逐项确认：

- model/XML/workflow/editor 的 `double` FPS 和 18 默认值；
- missing runtime key fallback 与非法值拒绝；
- 首帧周期、快路径剩余等待、慢路径立即但不追赶；
- 严格 path 顺序、连续 FrameId、微秒 DeviceTimestamp、UTC CapturedAtUtc；
- wait/decode cancellation 不提交成功帧；
- restart 重置；
- Preload buffer 共享和 decoded-byte 限制；
- Dynamic 修改/坏图/全坏/异常回归；
- graph Preview/string/session outputs；
- framework 和真实相机零改动。

- [ ] **Step 7: 仅在验证产生修正时提交**

若 Step 1-6 发现具体缺陷，只修改 File Map 中相应文件，重新执行 Step 1-5 后提交：

```powershell
git add NodeCraft.Vision/Nodes NodeCraft.Vision/Views NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "test: verify virtual camera frame pacing"
```

若没有修正，保持 clean worktree，不创建空提交。

---

### Task 6: 将预加载内存上限的编辑器单位改为 MB

**Files:**
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs:8-55`：增加 UI 换算常量，不改变 `MaxPreloadedBytes` 类型、默认值或 workflow mapping。
- Modify: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml:26-34`：将字节上限标签改为 `Maximum preloaded memory (MB)`。
- Modify: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs:48-175`：初始化时 bytes→MB，输入时 MB→bytes，保持非法输入不通知。
- Test: `NodeCraft.Tests/VirtualCameraTests.cs:1480-1540`：验证默认显示、转换、边界和通知行为。

**Interfaces:**
- Consumes: `VirtualCameraNodeModel.MaxPreloadedBytes` (`long`) 和现有 `_maxPreloadedBytesEditor`。
- Produces: `VirtualCameraNodeModel.BytesPerMegabyte`（internal `long` 常量）以及 UI 的 MB 输入/显示行为；`maxPreloadedBytes` workflow key 和 executor 不变。

- [ ] **Step 1: 写编辑器 MB 转换的失败测试**

在 `virtual camera editor mutates all properties and notifies graph changes` 中，把有效字节上限输入改成 MB，并在初始化后记录显示值：

```csharp
var maxBytes = GetPrivateField<TextBox>(content, "_maxPreloadedBytesEditor");
var initialMaxBytesText = maxBytes.Text;

source.Text = "C:\\frames";
mode.SelectedItem = VirtualCameraLoadMode.Dynamic;
frameRate.Text = "29.97";
maxImages.Text = "7";
maxBytes.Text = "256";
skipErrors.IsChecked = true;

var changesAfterValidInput = graphChanges;
foreach (var invalid in new[]
{
    string.Empty,
    "not-an-int",
    "0",
    "-1",
    "8796093022208",
})
{
    maxBytes.Text = invalid;
}
```

将返回断言补为：

```csharp
&& initialMaxBytesText == "512"
&& node.MaxPreloadedBytes == 256L * 1024L * 1024L
&& changesAfterValidInput == 6
&& graphChanges == changesAfterValidInput
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: editor test fails because the current editor displays `536870912` and treats `256` as 256 bytes.

- [ ] **Step 3: 实现最小 bytes/MB 转换**

在 `VirtualCameraNodeModel` 增加：

```csharp
internal const long BytesPerMegabyte = 1024L * 1024L;
```

初始化编辑器时改为：

```csharp
_maxPreloadedBytesEditor.Text = (_node.MaxPreloadedBytes
    / VirtualCameraNodeModel.BytesPerMegabyte)
    .ToString(CultureInfo.InvariantCulture);
```

handler 使用 invariant culture 解析正整数 MB，checked 转换后才写回模型：

```csharp
if (_initializing
    || !long.TryParse(
        _maxPreloadedBytesEditor.Text,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var megabytes)
    || megabytes <= 0)
{
    return;
}

long bytes;
try
{
    bytes = checked(megabytes * VirtualCameraNodeModel.BytesPerMegabyte);
}
catch (OverflowException)
{
    return;
}

_node.MaxPreloadedBytes = bytes;
NotifyChanged();
```

- [ ] **Step 4: 运行 editor 和全量测试确认绿灯**

Run:

```powershell
dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore
```

Expected: MB 编辑器断言 PASS，所有测试末尾输出 `ALL PASS`。

- [ ] **Step 5: 检查契约未改变并提交**

确认 `VirtualCameraNodeModel.WriteWorkflowInputs` 仍写入 `MaxPreloadedBytes` 的原始 bytes 值，未修改 `VirtualCameraExecutor` 的 bytes 校验路径；然后提交：

```powershell
git diff --check
git add NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs NodeCraft.Vision/Views/VirtualCameraEditor.xaml NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: show virtual camera memory limit in MB"
```

## Spec Coverage Self-Review

| 规格要求 | 计划覆盖 |
| --- | --- |
| `double FrameRate`、默认 18、0.1-1000、finite validation | Tasks 1, 3 |
| XML round-trip、旧 XML default、workflow mapping | Task 1 |
| 编辑器以 MB 显示/输入、内部仍保存 bytes、溢出不通知 | Task 6 |
| missing runtime key fallback、错误类型/范围异常 | Task 3 |
| 编辑器 InvariantCulture、合法/非法通知 | Task 1 |
| 不修改 Flow/framework/真实相机，不增加后台线程或队列 | Global Constraints, Task 5 |
| 首帧一个周期、剩余等待、慢处理 rebase、不追赶 | Task 3 |
| 严格顺序、不跳帧 | Tasks 3, 4 |
| 连续 FrameId、微秒 DeviceTimestamp、UTC CapturedAtUtc | Task 3 |
| Preload/builtin 模板和逐帧零像素复制 | Task 2 |
| Dynamic 每帧重新加载、坏图删除和取消边界 | Tasks 2-4 |
| Stop/restart 重置和幂等清理 | Tasks 3, 4 |
| graph Preview/imagePath/imageDirectory 契约 | Task 4 |
| build、ALL PASS、`D:\test\barcode-pics` 实际回归 | Task 5 |

类型和签名一致性：Task 2 定义 `IVirtualCameraImageLoader.Load(string)` 和 `VirtualCameraImageTemplate.CreateFrame(ulong, ulong, DateTimeOffset)`；Tasks 3-4 只使用这两个签名。Task 1 定义的 `double FrameRate`、常量和校验函数由 Task 3 直接复用，不创建第二套范围常量。所有测试 timing 依赖都通过 internal constructor 注入，不进入 plugin 公共 API。
