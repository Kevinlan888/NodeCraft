# Vision Virtual Camera Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `NodeCraft.Vision` 中实现一个可从本地 JPG/PNG/BMP、文件夹或固定 builtin URI 读取图片，并按 session 循环输出 `FlowImage`、图片路径和 session 目录的 Virtual Camera 节点。

**Architecture:** 将功能拆成模型/配置、来源解析、WPF 解码和生命周期执行器四个边界。来源解析在 session 启动时建立带稳定 ordinal 的 entry 列表；Preload 在启动时缓存最终 `FlowImage`，Dynamic 只保存路径并在每次 `PrepareIterationAsync` 解码。执行器通过现有 `IFlowNodeSessionLifecycle`、`IFlowNodeSessionInitializer` 和 `IFlowIterationSource` 接入 Flow runtime，插件注册提供输出契约和编辑器。

**Tech Stack:** .NET 8 Windows x64、WPF `BitmapDecoder`/`FormatConvertedBitmap`、现有 `FlowImage`/Flow session contracts、现有 `NodeCraft.Tests` 控制台测试跑棒；不增加第三方图片库或测试框架。

## Global Constraints

- 生产代码只放在 `NodeCraft.Vision`；不在 `NodeCraft.Flow` 增加图片解码依赖或通用图片序列 API。
- 只接受 `.jpg`、`.png`、`.bmp`，扩展名比较使用 `OrdinalIgnoreCase`；不支持 GIF、TIFF、WebP 或其他格式。
- 本地文件夹只枚举直接子文件；排序必须是 `OrderBy(fileName, StringComparer.OrdinalIgnoreCase).ThenBy(fileName, StringComparer.Ordinal)`。
- builtin 前缀为 `builtin://vision/`，builtin 只允许 `Preload`，不得自动切换或回退。
- 默认配置为 `SourcePath="builtin://vision/sample-set"`、`LoadMode=Preload`、`MaxPreloadedImages=100`、`MaxPreloadedBytes=536870912`、`SkipErrorImages=false`。
- runtime `loadMode` 不仅必须是 `VirtualCameraLoadMode` 类型，还必须通过 `Enum.IsDefined(typeof(VirtualCameraLoadMode), value)`；只允许 `Preload` 和 `Dynamic`，数值 cast 得到的未定义 enum 值属于配置错误。
- `Preload` 要求两个上限都大于 0；字节上限按最终存入 `FlowImage` 的托管像素 buffer 实际字节数累计，并使用 `checked`；`OverflowException` 必须转换成带 source 上下文的启动异常；Dynamic 忽略两个上限。
- 第一次 iteration 必须选择 ordinal 0；`FrameId` 使用 entry ordinal，不使用可变列表 index 或 iteration 序号。
- 外部环境或配置导致的预期失败（非法/无法规范化/无法访问 source、目录枚举 I/O、无效扩展名或空目录、无效 preload 上限、decoded byte 超限以及 checked overflow）必须包装成 `InvalidOperationException`（必要时保留原异常为 `InnerException`），消息同时包含 `VirtualCamera` 和相关 source path/URI；图片读取/解码失败使用 `VirtualCameraImageLoadException`，消息还必须包含图片绝对路径。
- 坏图只允许通过 `VirtualCameraImageLoadException` 被 Skip；`OperationCanceledException`、`OutOfMemoryException` 和底层/程序自身的 `InvalidOperationException` 不得被包装或吞掉。上一条只约束 Virtual Camera 自己创建的预期失败；取消、OOM 和程序 bug 即使原始消息没有 `VirtualCamera` 也必须原样传播。
- 输出顺序和 key 固定为 `image`（Iteration）、`imagePath`（Iteration）、`imageDirectory`（Session）；session 输出只由 `InitializeSessionAsync` 返回。
- `VirtualCameraExecutor.StopSessionAsync` 必须是幂等清理操作：未启动、启动中途失败、已启动或已停止都只清空当前 session 状态并返回，不得因“未启动/已停止”抛异常；清理不能覆盖 `StartSessionAsync` 的原始异常。
- 所有 Virtual Camera 自己创建或包装的异常消息都必须包含 `VirtualCamera` 和相关来源路径/URI；本地图片错误还必须包含该图片的绝对路径。
- 每个任务完成后运行该任务的测试并提交一个小 commit；实现阶段在本计划执行时使用 TDD。

## File Map

**Create:**

- `NodeCraft.Vision/Nodes/VirtualCameraLoadMode.cs`：`Preload`/`Dynamic` 枚举。
- `NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs`：五个可持久化配置属性、三个 model output ports 和 `WriteWorkflowInputs`。
- `NodeCraft.Vision/Nodes/VirtualCameraSource.cs`：`VirtualCameraEntry`、本地/builtin 来源解析、确定性排序、固定 builtin sample-set 和 source 环境异常包装。
- `NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs`：可注入的 loader 接口、WPF 解码实现、窄异常包装和像素格式转换。
- `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs`：session 生命周期、session 初始化输出、Preload/Dynamic 游标和 iteration 输出。
- `NodeCraft.Vision/Views/VirtualCameraEditor.xaml`：路径和加载配置编辑器布局。
- `NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs`：编辑器 content factory 和属性更新。
- `NodeCraft.Tests/VirtualCameraTests.cs`：模型、来源、解码、执行器、注册和图级集成测试；包含测试图片及 fake loader 辅助代码。

**Modify:**

- `NodeCraft.Vision/Plugin/VisionPlugin.cs`：注册 Virtual Camera、输出定义、executor factory、palette 和 editor；扩展输出 port helper 以接受 availability。
- `NodeCraft.Vision/NodeCraft.Vision.csproj`：嵌入 `VirtualCameraEditor.xaml`。
- `NodeCraft.Tests/Program.cs`：调用 `RunVirtualCameraTestsAsync`。

不修改 `GraphExecutionSession`、`FlowGraphIterationRunner` 或 XML serializer；这些基础能力已经支持 session initializer、全节点 required session input 检查和公开属性 round-trip。

---

### Task 1: 建立节点模型、枚举和配置持久化契约

**Files:**
- Create: `NodeCraft.Vision/Nodes/VirtualCameraLoadMode.cs`
- Create: `NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs`
- Create: `NodeCraft.Tests/VirtualCameraTests.cs`
- Modify: `NodeCraft.Tests/Program.cs:63-65`，在 Vision plugin/preview 测试之后调用 `RunVirtualCameraTestsAsync()`

**Interfaces:**
- Produces `VirtualCameraLoadMode`, `VirtualCameraNodeModel.FlowNodeTypeKey`、五个 public 属性和 `WriteWorkflowInputs(WorkflowNode node)`，后续 executor 和 plugin 直接使用这些名字。

- [ ] **Step 1: 写模型配置的失败测试**

在 `VirtualCameraTests.cs` 建立 `partial class Program`，先添加以下测试结构；测试覆盖默认值、model output IDs/types、runtime input key/type，以及通用 XML serializer 保存五个属性：

```csharp
private static async Task RunVirtualCameraTestsAsync()
{
    await RunAsync("virtual camera model persists configuration and maps workflow inputs", () =>
    {
        var node = new VirtualCameraNodeModel
        {
            SourcePath = Path.Combine(Path.GetTempPath(), "frames"),
            LoadMode = VirtualCameraLoadMode.Dynamic,
            MaxPreloadedImages = 7,
            MaxPreloadedBytes = 123456L,
            SkipErrorImages = true,
        };
        var workflowNode = new WorkflowNode
        {
            Id = node.Id,
            TypeKey = node.ExecutorType,
        };

        node.WriteWorkflowInputs(workflowNode);
        var xmlPath = Path.Combine(
            Path.GetTempPath(),
            "nodecraft-virtual-camera-model-" + Guid.NewGuid().ToString("N") + ".flow.xml");
        try
        {
            GraphModelXmlSerializer.Save(
                new GraphModel
                {
                    Nodes = new List<NodeModel> { node },
                    Links = new List<GraphLink>(),
                },
                xmlPath);
            var xml = File.ReadAllText(xmlPath);
            var modelAssertions = node.ExecutorType == VirtualCameraNodeModel.FlowNodeTypeKey
                && node.SourcePath == (string)workflowNode.Inputs["sourcePath"]
                && workflowNode.Inputs["loadMode"] is VirtualCameraLoadMode mode
                && mode == VirtualCameraLoadMode.Dynamic
                && workflowNode.Inputs["maxPreloadedImages"] is int imageLimit
                && imageLimit == 7
                && workflowNode.Inputs["maxPreloadedBytes"] is long byteLimit
                && byteLimit == 123456L
                && workflowNode.Inputs["skipErrorImages"] is bool skip
                && skip
                && node.OutputParameters.Select(port => port.PortId).SequenceEqual(
                    new[] { "image", "imagePath", "imageDirectory" })
                && node.OutputParameters.Select(port => port.Parameter.ParameterType).SequenceEqual(
                    new[] { FlowDataType.Image.Key, FlowDataType.String.Key, FlowDataType.String.Key })
                && xml.Contains("Name=\"SourcePath\"", StringComparison.Ordinal)
                && xml.Contains("Name=\"LoadMode\"", StringComparison.Ordinal)
                && xml.Contains("Name=\"MaxPreloadedImages\"", StringComparison.Ordinal)
                && xml.Contains("Name=\"MaxPreloadedBytes\"", StringComparison.Ordinal)
                && xml.Contains("Name=\"SkipErrorImages\"", StringComparison.Ordinal);
            var restored = (VirtualCameraNodeModel)GraphModelXmlSerializer.Load(xmlPath)
                .Nodes.Single();
            var legacyDocument = XDocument.Load(xmlPath);
            legacyDocument.Descendants("Property").Remove();
            legacyDocument.Save(xmlPath);
            var legacyDefaults = (VirtualCameraNodeModel)GraphModelXmlSerializer.Load(xmlPath)
                .Nodes.Single();
            return modelAssertions
                && restored.SourcePath == node.SourcePath
                && restored.LoadMode == node.LoadMode
                && restored.MaxPreloadedImages == node.MaxPreloadedImages
                && restored.MaxPreloadedBytes == node.MaxPreloadedBytes
                && restored.SkipErrorImages == node.SkipErrorImages
                && legacyDefaults.SourcePath == "builtin://vision/sample-set"
                && legacyDefaults.LoadMode == VirtualCameraLoadMode.Preload
                && legacyDefaults.MaxPreloadedImages == 100
                && legacyDefaults.MaxPreloadedBytes == 536870912L
                && !legacyDefaults.SkipErrorImages;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    });

    await RunAsync("virtual camera model defaults match builtin preload", () =>
    {
        var node = new VirtualCameraNodeModel();
        return node.SourcePath == "builtin://vision/sample-set"
            && node.LoadMode == VirtualCameraLoadMode.Preload
            && node.MaxPreloadedImages == 100
            && node.MaxPreloadedBytes == 536870912L
            && !node.SkipErrorImages;
    });
}
```

添加所需的 `System.Collections.Generic`, `System.IO`, `System.Linq`, `NodeCraft.Flow` 和 `NodeCraft.Vision.Nodes` using，并在 `Program.cs` 调用测试入口。

该断言需要 `System.Xml.Linq` using；它已经并入上面的同一个 `try`，不要创建第二个持久化格式或修改 `GraphModelXmlSerializer`。

- [ ] **Step 2: 运行测试确认当前缺少实现**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 编译失败，提示 `VirtualCameraNodeModel` 或 `VirtualCameraLoadMode` 尚未定义；这是本任务的红灯状态。

- [ ] **Step 3: 实现枚举和 NodeModel**

在 `VirtualCameraLoadMode.cs` 写：

```csharp
namespace NodeCraft.Vision.Nodes
{
    public enum VirtualCameraLoadMode
    {
        Preload,
        Dynamic,
    }
}
```

在 `VirtualCameraNodeModel.cs` 实现 `NodeModel, IWorkflowNodeValueProvider`。构造函数设置 `ExecutorType`、`Name = "Virtual Camera"`、空输入列表，并按 `image`、`imagePath`、`imageDirectory` 顺序建立 output parameters。`WriteWorkflowInputs` 必须精确写入：

```csharp
node.Inputs["sourcePath"] = SourcePath ?? string.Empty;
node.Inputs["loadMode"] = LoadMode;
node.Inputs["maxPreloadedImages"] = MaxPreloadedImages;
node.Inputs["maxPreloadedBytes"] = MaxPreloadedBytes;
node.Inputs["skipErrorImages"] = SkipErrorImages;
```

属性默认值必须与全局约束完全一致；不增加 runtime index、FlowImage、BitmapSource 或其他状态属性。

- [ ] **Step 4: 运行模型测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 两个 Virtual Camera model 测试 PASS；后续尚未实现的测试不存在，因此本阶段至少不应出现模型测试失败。

- [ ] **Step 5: 提交模型契约**

```bash
git add NodeCraft.Vision/Nodes/VirtualCameraLoadMode.cs NodeCraft.Vision/Nodes/VirtualCameraNodeModel.cs NodeCraft.Tests/VirtualCameraTests.cs NodeCraft.Tests/Program.cs
git commit -m "feat: add virtual camera node model"
```

### Task 2: 实现本地和 builtin 来源解析

**Files:**
- Create: `NodeCraft.Vision/Nodes/VirtualCameraSource.cs`
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Consumes `VirtualCameraLoadMode` only for tests/validation context; source resolution itself receives a non-empty `string sourcePath`。
- Produces `VirtualCameraEntry`、`VirtualCameraSource` 和 `VirtualCameraSourceResolver.Resolve(string sourcePath)`；executor 使用 `ImageDirectory`, `IsBuiltin` 和 `Entries`。

- [ ] **Step 1: 写来源解析失败测试**

向 `RunVirtualCameraTestsAsync` 增加以下场景：

```csharp
await RunAsync("virtual camera resolves a single absolute image and its directory", () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    var imagePath = fixture.WriteImage("single.jpg", new byte[] { 1, 2, 3 });
    var relative = Path.GetRelativePath(Environment.CurrentDirectory, imagePath);
    var source = VirtualCameraSourceResolver.Resolve(relative);
    return !source.IsBuiltin
        && source.ImageDirectory == Path.GetDirectoryName(Path.GetFullPath(imagePath))
        && source.Entries.Count == 1
        && source.Entries[0].Ordinal == 0
        && source.Entries[0].Path == Path.GetFullPath(imagePath)
        && source.Entries[0].PreloadedImage == null;
});

await RunAsync("virtual camera sorts supported folder images with ordinal tie break", () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    fixture.WriteImage("A.jpg", new byte[] { 1, 2, 3 });
    fixture.WriteImage("a.PNG", new byte[] { 4, 5, 6 });
    fixture.WriteImage("b.bmp", new byte[] { 7, 8, 9 });
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "ignored.txt"), "ignored");
    var source = VirtualCameraSourceResolver.Resolve(fixture.DirectoryPath);
    var names = source.Entries.Select(entry => Path.GetFileName(entry.Path)).ToArray();
    return names.SequenceEqual(new[] { "A.jpg", "a.PNG", "b.bmp" })
        && source.Entries.Select(entry => entry.Ordinal).SequenceEqual(new[] { 0, 1, 2 });
});

await RunAsync("virtual camera resolves builtin collection and single asset", () =>
{
    var collection = VirtualCameraSourceResolver.Resolve("builtin://vision/sample-set");
    var single = VirtualCameraSourceResolver.Resolve("builtin://vision/sample-set/checkerboard");
    return collection.IsBuiltin
        && collection.ImageDirectory == "builtin://vision/sample-set"
        && collection.Entries.Count >= 2
        && collection.Entries[0].PreloadedImage != null
        && collection.Entries[0].Path.StartsWith("builtin://vision/sample-set/", StringComparison.Ordinal)
        && single.Entries.Count == 1
        && single.ImageDirectory == "builtin://vision/sample-set"
        && single.Entries[0].Path == "builtin://vision/sample-set/checkerboard";
});

await RunAsync("virtual camera rejects invalid source kinds and empty folders", () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    var cases = new[]
    {
        string.Empty,
        Path.Combine(fixture.DirectoryPath, "missing.png"),
        Path.Combine(fixture.DirectoryPath, "unsupported.gif"),
        "builtin://vision/unknown",
    };
    File.WriteAllBytes(cases[2], new byte[] { 1, 2, 3 });
    var allRejected = cases.All(path => ThrowsVirtualCamera<InvalidOperationException>(
        path,
        () => VirtualCameraSourceResolver.Resolve(path)));
    var emptyFolderRejected = ThrowsVirtualCamera<InvalidOperationException>(
        fixture.DirectoryPath,
        () => VirtualCameraSourceResolver.Resolve(fixture.DirectoryPath));
    var invalidPath = "\0invalid";
    var invalidPathWrapped = false;
    try
    {
        VirtualCameraSourceResolver.Resolve(invalidPath);
    }
    catch (InvalidOperationException exception)
    {
        invalidPathWrapped = exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
            && exception.Message.Contains(invalidPath, StringComparison.Ordinal)
            && exception.InnerException is ArgumentException;
    }
    return allRejected && emptyFolderRejected && invalidPathWrapped;
});
```

`TemporaryVirtualCameraFiles` 在本任务中就提供空临时目录和 `WriteImage(string fileName, byte[] bytes)`；`WriteImage` 使用 `File.WriteAllBytes` 写入明确的临时路径，来源解析测试只验证存在性和排序，不在此处解码。Task 3 再在同一 helper 增加 `WriteBitmap`，最终所有测试仍只操作 helper 创建的临时目录。

- [ ] **Step 2: 运行来源测试确认失败**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 编译失败，提示 `VirtualCameraSourceResolver`、`VirtualCameraEntry` 或 `TemporaryVirtualCameraFiles` 未定义。

- [ ] **Step 3: 写来源和值对象实现**

在 `VirtualCameraSource.cs` 定义以下稳定接口：

```csharp
internal sealed class VirtualCameraEntry
{
    internal VirtualCameraEntry(int ordinal, string path, FlowImage preloadedImage)
    {
        Ordinal = ordinal;
        Path = path;
        PreloadedImage = preloadedImage;
    }

    public int Ordinal { get; }
    public string Path { get; }
    public FlowImage PreloadedImage { get; }
}

internal sealed class VirtualCameraSource
{
    internal VirtualCameraSource(
        string imageDirectory,
        bool isBuiltin,
        IReadOnlyList<VirtualCameraEntry> entries)
    {
        ImageDirectory = imageDirectory;
        IsBuiltin = isBuiltin;
        Entries = entries;
    }

    public string ImageDirectory { get; }
    public bool IsBuiltin { get; }
    public IReadOnlyList<VirtualCameraEntry> Entries { get; }
}
```

`VirtualCameraSourceResolver.Resolve` 的实现顺序固定为：

1. 空白 source 直接抛带 `VirtualCamera` 和 `<empty>` 上下文的 `InvalidOperationException`；非 builtin 的非法本地 source 先进入受控路径规范化流程。
2. `StartsWith("builtin://vision/", StringComparison.OrdinalIgnoreCase)` 时只接受 `sample-set` 或其 `checkerboard`/`color-bars` 资产；集合固定按 `checkerboard`, `color-bars` 顺序生成。每个 builtin entry 立即带独立托管 buffer 的 `FlowImage`，目录固定为 `builtin://vision/sample-set`。
3. 本地来源使用 `Path.GetFullPath`。`Path.GetFullPath` 以及后续目录枚举/排序只捕获预期的外部环境异常：`ArgumentException`、`NotSupportedException`、`PathTooLongException`、`UnauthorizedAccessException`、`SecurityException` 和 `IOException`；统一包装为包含 `VirtualCamera` 与 source path 的 `InvalidOperationException`，并保留原异常。不得捕获 `OperationCanceledException`、`OutOfMemoryException` 或底层/程序自身的 `InvalidOperationException`。
4. 既不是现有文件也不是现有目录时抛包含 `VirtualCamera` 和规范化绝对路径的明确异常。单文件仅允许 `.jpg`/`.png`/`.bmp`；目录使用 `Directory.EnumerateFiles(directory)` 只取直接子文件，且必须在同一个 `try` 内完成 materialize、过滤和排序，以便迭代器实际抛出的 I/O 异常也被包装：

```csharp
files
    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
    .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
```

5. 所有 entry 用当前排序位置分配 ordinal，从 0 开始；本地 entry 的 `PreloadedImage` 为 null。目录为空或没有支持扩展名时抛包含 `VirtualCamera` 和规范化 source path 的明确异常。

使用以下局部规则实现包装边界，禁止用裸 `catch (Exception)`：

```csharp
private static bool IsExpectedSourceResolutionFailure(Exception exception) =>
    exception is ArgumentException
    || exception is NotSupportedException
    || exception is PathTooLongException
    || exception is UnauthorizedAccessException
    || exception is SecurityException
    || exception is IOException;

private static InvalidOperationException WrapSourceFailure(
    string sourceLabel,
    Exception exception) =>
    new InvalidOperationException(
        $"VirtualCamera source '{sourceLabel}' could not be resolved.",
        exception);
```

`Path.GetFullPath(sourcePath)` 放在 `try/catch when (IsExpectedSourceResolutionFailure(exception))` 中；`Directory.EnumerateFiles(...).Where(...).OrderBy(...).ThenBy(...).ToArray()` 的整个 materialize 过程也放在同样的受控边界中。source 为空时使用 `<empty>` 作为消息上下文；已规范化的本地路径和 builtin URI分别使用其绝对路径/URI，保证每个 resolver 自己产生的异常都带来源上下文。

builtin 的固定 pixel buffer 直接用 `FlowImage.FromOwnedBuffer` 构造，`DeviceTimestamp=0`、`CapturedAtUtc` 使用构造时 UTC、`FrameId` 使用其 ordinal；不要使用字典枚举顺序。

- [ ] **Step 4: 完成临时文件 helper 并运行来源测试**

在 `VirtualCameraTests.cs` 添加 `IDisposable TemporaryVirtualCameraFiles`，构造唯一临时目录但不创建任何文件，`Dispose` 只删除该目录；提供 `DirectoryPath` 和 `WriteImage(string fileName, byte[] bgrPixel)`。使用 `File.Delete`/`Directory.Delete` 前只操作该 helper 创建的明确目录。

其构造和清理接口固定为：

```csharp
private sealed class TemporaryVirtualCameraFiles : IDisposable
{
    internal TemporaryVirtualCameraFiles()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "nodecraft-virtual-camera-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    internal string DirectoryPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
```

helper 的图片写入方法使用以下签名，`WriteBitmap` 负责真正生成可被 WPF decoder 读取的文件：

```csharp
internal string WriteImage(string fileName, byte[] bytes)
{
    var path = Path.Combine(DirectoryPath, fileName);
    File.WriteAllBytes(path, bytes);
    return path;
}

internal string WriteBitmap(
    string fileName,
    PixelFormat pixelFormat,
    int width,
    int height,
    byte[] pixels,
    int stride)
{
    var path = Path.Combine(DirectoryPath, fileName);
    var bitmap = BitmapSource.Create(
        width, height, 96, 96, pixelFormat, null, pixels, stride);
    BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" => new JpegBitmapEncoder(),
        ".png" => new PngBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        _ => throw new InvalidOperationException("Test bitmap extension is unsupported."),
    };
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        encoder.Save(stream);
    }
    return path;
}
```

`WriteBitmap` 的调用方必须显式传入 width、height、stride，并保证 `pixels.Length == stride * height`；Gray8 的测试使用 width 2、height 1、stride 2，彩色 1x1 测试使用 stride 3。

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 来源解析、确定性排序、builtin URI、空来源和非法来源测试 PASS。

- [ ] **Step 5: 提交来源解析**

```bash
git add NodeCraft.Vision/Nodes/VirtualCameraSource.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: resolve virtual camera image sources"
```

### Task 3: 实现 WPF 图片加载和窄异常边界

**Files:**
- Create: `NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs`
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Produces `IVirtualCameraImageLoader.Load(string path, ulong frameId)`、`VirtualCameraImageLoader`、`VirtualCameraImageLoadException` 和 `VirtualCameraImageLoader.IsSkippableImageLoadError(Exception)`。
- `VirtualCameraExecutor` 只依赖 `IVirtualCameraImageLoader`；测试可以注入 fake loader。

- [ ] **Step 1: 写解码和异常分类的失败测试**

使用现有 `RunOnSta` 和 WPF encoders 增加测试：

```csharp
await RunAsync("virtual camera decodes gray8 as mono8 and color as bgr24", () =>
    RunOnSta(() =>
    {
        using var fixture = new TemporaryVirtualCameraFiles();
        var monoPath = fixture.WriteBitmap(
            "mono.png", PixelFormats.Gray8, 2, 1, new byte[] { 9, 10 }, 2);
        var colorPath = fixture.WriteBitmap(
            "color.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
        var mono = new VirtualCameraImageLoader().Load(monoPath, 4);
        var color = new VirtualCameraImageLoader().Load(colorPath, 5);
        return mono.PixelFormat == FlowPixelFormat.Mono8
            && mono.Stride == 2
            && mono.Buffer.Span.SequenceEqual(new byte[] { 9, 10 })
            && mono.FrameId == 4
            && mono.DeviceTimestamp == 0
            && color.PixelFormat == FlowPixelFormat.Bgr24
            && color.Stride == 3
            && color.Buffer.Span.SequenceEqual(new byte[] { 1, 2, 3 })
            && color.FrameId == 5;
    }));

await RunAsync("virtual camera wraps only expected image load failures", () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    var missingPath = Path.Combine(fixture.DirectoryPath, "missing.png");
    try
    {
        new VirtualCameraImageLoader().Load(missingPath, 0);
        return false;
    }
    catch (VirtualCameraImageLoadException exception)
    {
        return exception.Path == missingPath
            && exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
            && VirtualCameraImageLoader.IsSkippableImageLoadError(exception)
            && !VirtualCameraImageLoader.IsSkippableImageLoadError(new InvalidOperationException());
    }
});
```

`TemporaryVirtualCameraFiles.WriteBitmap` 使用 `BitmapSource.Create` 和根据扩展名选择的 `PngBitmapEncoder`/`JpegBitmapEncoder`/`BmpBitmapEncoder`，写入后关闭文件流。另加损坏文件测试：写入任意非图片 bytes，要求得到包含绝对路径的 `VirtualCameraImageLoadException`。

- [ ] **Step 2: 运行解码测试确认缺少 loader**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 编译失败，提示 loader 接口/异常/实现尚不存在。

- [ ] **Step 3: 实现 loader 和专用异常**

实现以下接口和异常边界：

```csharp
internal interface IVirtualCameraImageLoader
{
    FlowImage Load(string path, ulong frameId);
}

internal sealed class VirtualCameraImageLoadException : Exception
{
    internal VirtualCameraImageLoadException(string path, Exception innerException)
        : base($"VirtualCamera image '{path}' could not be loaded.", innerException)
    {
        Path = path;
    }

    public string Path { get; }
}
```

`VirtualCameraImageLoader.Load` 必须执行：

1. `FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`，用 `BitmapDecoder.Create(..., BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)`，不保留 stream/decoder/frame 到返回值。
2. 解码首帧；首帧缺失、非正尺寸或 stride/buffer 长度不合法时构造带 `InvalidDataException` inner exception 的专用 wrapper。
3. 原始 `PixelFormats.Gray8` 直接复制；其他格式用 `FormatConvertedBitmap` 转 `PixelFormats.Bgr24`，然后 `Freeze` 并 `CopyPixels` 到独立 byte[]。
4. 以 `FlowImage.FromOwnedBuffer(width, height, stride, pixelFormat, FlowImageKind.Color, buffer, frameId, 0, DateTimeOffset.UtcNow)` 返回。

文件打开、decoder 创建和 pixel copy 必须分别使用窄过滤 catch，把 `IOException`、`UnauthorizedAccessException`、`InvalidDataException`、WPF `FileFormatException`、`NotSupportedException` 以及明确的 `ArgumentException` 像素布局失败包装成 `VirtualCameraImageLoadException`。不要在整个 `Load` 外围使用 `catch (Exception)`；不要捕获或包装 `OperationCanceledException`、`OutOfMemoryException`、`InvalidOperationException`。

`IsSkippableImageLoadError` 的实现必须仅为 `exception is VirtualCameraImageLoadException`，不能根据 `_skipErrorImages` 或异常消息猜测。

- [ ] **Step 4: 运行 WPF loader 测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: Gray8 -> Mono8、彩色 -> Bgr24、metadata、文件流关闭和损坏/缺失图片异常测试 PASS。

- [ ] **Step 5: 提交图片 loader**

```bash
git add NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: add virtual camera image loader"
```

### Task 4: 实现 VirtualCameraExecutor 的 Preload 生命周期和输出

**Files:**
- Create: `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs`
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Consumes `VirtualCameraSourceResolver`, `IVirtualCameraImageLoader` 和 `VirtualCameraLoadMode`。
- Produces `VirtualCameraExecutor : IFlowNodeExecutor, IFlowNodeSessionLifecycle, IFlowNodeSessionInitializer, IFlowIterationSource`，构造函数为 `internal VirtualCameraExecutor(IVirtualCameraImageLoader imageLoader = null)`。

- [ ] **Step 1: 写 Preload 生命周期的失败测试**

建立 `CreateVirtualCameraContext` helper，使用 `WorkflowNode.Inputs` 写入五个 runtime 配置和只包含三个 output ports 的 definition。增加以下断言：

```csharp
await RunAsync("virtual camera preload starts at ordinal zero and exposes session directory", async () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
    fixture.WriteBitmap("b.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
    var executor = new VirtualCameraExecutor();
    var context = CreateVirtualCameraContext(
        fixture.DirectoryPath,
        VirtualCameraLoadMode.Preload,
        maxImages: 10,
        maxBytes: 100,
        skipErrors: false,
        out var node,
        out var definition);

    await executor.StartSessionAsync(context, CancellationToken.None);
    var sessionOutputs = await executor.InitializeSessionAsync(
        context,
        new Dictionary<string, object>(),
        CancellationToken.None);
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    var first = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    var second = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    var wrapped = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);

    return (string)sessionOutputs["imageDirectory"] == Path.GetFullPath(fixture.DirectoryPath)
        && ((FlowImage)first["image"]).FrameId == 0
        && (string)first["imagePath"] == Path.Combine(fixture.DirectoryPath, "a.png")
        && ((FlowImage)second["image"]).FrameId == 1
        && ((FlowImage)wrapped["image"]).FrameId == 0
        && ReferenceEquals(first["image"], wrapped["image"]);
});

await RunAsync("virtual camera preload enforces positive count and checked decoded bytes", async () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
    fixture.WriteBitmap("b.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
    var invalidCount = await ThrowsVirtualCameraAsync<InvalidOperationException>(
        fixture.DirectoryPath,
        () => StartVirtualCameraAsync(
            fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 0, 100, false));
    var invalidBytes = await ThrowsVirtualCameraAsync<InvalidOperationException>(
        fixture.DirectoryPath,
        () => StartVirtualCameraAsync(
            fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 10, 0, false));
    var tooSmall = await ThrowsVirtualCameraAsync<InvalidOperationException>(
        fixture.DirectoryPath,
        () => StartVirtualCameraAsync(
            fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 10, 2, false));
    var tooMany = await ThrowsVirtualCameraAsync<InvalidOperationException>(
        fixture.DirectoryPath,
        () => StartVirtualCameraAsync(
            fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 1, 100, false));
    var invalidMode = await ThrowsVirtualCameraAsync<InvalidOperationException>(
        fixture.DirectoryPath,
        () => StartVirtualCameraAsync(
            fixture.DirectoryPath, (VirtualCameraLoadMode)123, 10, 100, false));
    return invalidCount && invalidBytes && tooSmall && tooMany && invalidMode;
});

await RunAsync("virtual camera failed start can be stopped and restarted cleanly", async () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
    var executor = new VirtualCameraExecutor();
    var failedContext = CreateVirtualCameraContext(
        fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 10, 0, false,
        out _, out _);
    await executor.StopSessionAsync(failedContext, CancellationToken.None);
    var primaryFailurePreserved = false;
    try
    {
        await executor.StartSessionAsync(failedContext, CancellationToken.None);
    }
    catch (InvalidOperationException exception)
    {
        primaryFailurePreserved = exception.Message.Contains(
            "VirtualCamera", StringComparison.Ordinal)
            && exception.Message.Contains(
                fixture.DirectoryPath, StringComparison.Ordinal);
    }

    await executor.StopSessionAsync(failedContext, CancellationToken.None);
    await executor.StopSessionAsync(failedContext, CancellationToken.None);

    var validContext = CreateVirtualCameraContext(
        fixture.DirectoryPath, VirtualCameraLoadMode.Preload, 10, 100, false,
        out var node, out var definition);
    await executor.StartSessionAsync(validContext, CancellationToken.None);
    await executor.PrepareIterationAsync(validContext, CancellationToken.None);
    var output = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    await executor.StopSessionAsync(validContext, CancellationToken.None);

    return primaryFailurePreserved
        && ((FlowImage)output["image"]).FrameId == 0;
});
```

另加 builtin 行为断言：`StartVirtualCameraAsync("builtin://vision/sample-set", VirtualCameraLoadMode.Preload, 100, 536870912L, false)` 成功；同一 source 配置为 `Dynamic` 必须在启动阶段抛包含 `VirtualCamera` 的配置异常，且不发生自动模式切换。

再用 `A.jpg`、损坏的 `Bad.jpg`、`C.jpg` 建立 Preload folder：`SkipErrorImages=false` 的 Start 必须传播 `VirtualCameraImageLoadException`；`true` 必须成功启动，并在三次 Prepare/Execute 中输出 A、C、A，且 C 的 ordinal 仍为 2。这样 Preload 和 Dynamic 共用的异常边界都有直接测试。

同时添加 `ExecuteAsync` 在未 Start、未 Prepare、Stop 后都抛 `InvalidOperationException` 且消息含 `VirtualCamera` 的测试；添加 `InitializeSessionAsync` 在未 Start 时失败的测试。

- [ ] **Step 2: 运行 executor 测试确认缺少实现**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 编译失败，提示 `VirtualCameraExecutor` 和测试 helper 尚不存在。

- [ ] **Step 3: 实现 executor 的配置读取和启动**

`StartSessionAsync` 从 `context.Node.Inputs` 读取且只接受以下类型：`sourcePath:string`、`loadMode:VirtualCameraLoadMode`、`maxPreloadedImages:int`、`maxPreloadedBytes:long`、`skipErrorImages:bool`。缺失或错误类型直接抛带 `VirtualCamera` 的 `InvalidOperationException`；source 尚不可用时使用输入值或 `<empty>` 作为上下文，source 已解析后统一使用 `_imageDirectory`。`loadMode` 在类型检查之后必须再执行 `Enum.IsDefined(typeof(VirtualCameraLoadMode), loadMode)`，拒绝 `(VirtualCameraLoadMode)123` 这类底层整数未定义值，不得按默认值或任意分支继续执行。

值域校验必须在 builtin/Dynamic 分支和 Preload 上限校验之前完成，具体边界为：

```csharp
var sourceLabel = string.IsNullOrWhiteSpace(sourcePath) ? "<empty>" : sourcePath;
if (!Enum.IsDefined(typeof(VirtualCameraLoadMode), loadMode))
{
    throw new InvalidOperationException(
        $"VirtualCamera source '{sourceLabel}' has unsupported load mode value '{(int)loadMode}'.");
}
```

调用 `VirtualCameraSourceResolver.Resolve(sourcePath)` 后：

- builtin + Dynamic 直接失败；不自动变成 Preload。
- Dynamic 不验证两个 preload 上限。
- Preload 验证两个上限大于 0，并按 entry 顺序加载；所有配置/容量失败都包装成包含 `VirtualCamera` 和 `_imageDirectory` 的启动 `InvalidOperationException`。
- 启动失败时清空所有字段，不能让半成品 sequence 留到下一次启动；清理路径必须重新使用幂等的 `StopSessionAsync` 或等价的无条件字段清空，然后原样重新抛出 primary exception，不能让 cleanup exception 覆盖启动错误。

executor 的字段至少包含 `_entries`, `_imageDirectory`, `_index`, `_current`, `_skipErrorImages`, `_loadMode`；`_index` 初始为 `-1`，`_current` 初始为 null。

- [ ] **Step 4: 实现 Preload 加载、limit 和 session/iteration 输出**

Preload 循环使用 `validEntries`，对本地 entry 调 `_imageLoader.Load(entry.Path, (ulong)entry.Ordinal)`；builtin entry 复用 `entry.PreloadedImage`。成功后检查数量和实际 `image.Buffer.Length`，累计必须使用：

```csharp
long totalBytes = 0;
try
{
    checked
    {
        totalBytes += image.Buffer.Length;
    }
}
catch (OverflowException exception)
{
    throw new InvalidOperationException(
        $"VirtualCamera source '{_imageDirectory}' overflowed decoded byte accounting near '{entry.Path}'.",
        exception);
}
```

超过 `MaxPreloadedBytes` 或 `MaxPreloadedImages` 时也抛包含 `VirtualCamera`、`_imageDirectory` 和相关 entry path 的启动异常，不能静默截断；不要捕获其它异常。成功的 entry 用同一 ordinal/path 和 decoded image 创建新的 cached entry。

`InitializeSessionAsync` 做 cancellation check，要求 session 已启动，然后返回只有 `imageDirectory` 的 dictionary。`PrepareIterationAsync` 先清 `_current` 和 `_currentEntry`，执行 `_index = (_index + 1) % _entries.Count` 并取 entry，设置 `_currentEntry = entry`，Preload 下 `_current = entry.PreloadedImage`。`ExecuteAsync` 做 cancellation check，要求 `_current != null` 且 `_currentEntry != null`，返回：

```csharp
new Dictionary<string, object>
{
    ["image"] = _current,
    ["imagePath"] = _currentEntry.Path,
}
```

`StopSessionAsync` 是无外部资源的幂等清理操作：无论 `_entries`/`_imageDirectory` 是否已初始化，都清空 current、current entry、entries、directory、index、load mode 和其它 session flags，然后直接返回 `Task.CompletedTask`；不得先检查“session 已启动”，也不因重复 Stop 抛异常，也不得在清理前因 `cancellationToken` 已取消而抛出。启动失败、未启动、已停止和正常停止都走这条路径，任何 stale image 不得从下一 session 泄漏。

- [ ] **Step 5: 运行 Preload 测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 首图 ordinal 0、循环复用同一 cached FlowImage、session directory、positive limit、actual decoded bytes、正常 Stop、重复 Stop、失败启动后的清理和成功重启测试 PASS。

- [ ] **Step 6: 提交 Preload executor**

```bash
git add NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: implement virtual camera preload execution"
```

### Task 5: 实现 Dynamic、稳定 ordinal 和窄 Skip 错误规则

**Files:**
- Modify: `NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs`
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Consumes `VirtualCameraImageLoader.IsSkippableImageLoadError` 和 immutable `VirtualCameraEntry.Ordinal`。
- Produces Dynamic 每轮重新加载、坏图删除游标规则和异常传播行为。

- [ ] **Step 1: 写 Dynamic 和异常过滤的失败测试**

增加一个记录 path/frameId 的 fake loader，并覆盖：

```csharp
await RunAsync("virtual camera dynamic loads only during prepare and observes file changes", async () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    var path = fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
    var loader = new RecordingVirtualCameraImageLoader();
    var executor = new VirtualCameraExecutor(loader);
    var context = CreateVirtualCameraContext(
        path, VirtualCameraLoadMode.Dynamic, 0, 0, false,
        out var node, out var definition);

    await executor.StartSessionAsync(context, CancellationToken.None);
    var startedWithoutLoad = loader.Loads.Count == 0;
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    var first = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 9, 8, 7 }, 3);
    await executor.PrepareIterationAsync(context, CancellationToken.None);
    var second = await executor.ExecuteAsync(
        new FlowExecutionContext(), node, definition,
        new Dictionary<string, object>(), CancellationToken.None);
    await executor.StopSessionAsync(context, CancellationToken.None);

    return startedWithoutLoad
        && loader.Loads.Count == 2
        && (string)first["imagePath"] == Path.GetFullPath(path)
        && (string)second["imagePath"] == Path.GetFullPath(path)
        && ((FlowImage)first["image"]).FrameId == 0
        && ((FlowImage)second["image"]).FrameId == 0
        && !((FlowImage)first["image"]).Buffer.Span.SequenceEqual(
            ((FlowImage)second["image"]).Buffer.Span)
        && !ReferenceEquals(first["image"], second["image"]);
});

await RunAsync("virtual camera dynamic skip removes bad entry without skipping next", async () =>
{
    using var fixture = new TemporaryVirtualCameraFiles();
    var a = fixture.WriteBitmap("A.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 1, 1 }, 3);
    var bad = fixture.WriteBitmap("Bad.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 2, 2, 2 }, 3);
    var c = fixture.WriteBitmap("C.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 3, 3, 3 }, 3);
    var loader = new SelectiveVirtualCameraImageLoader(bad);
    var executor = new VirtualCameraExecutor(loader);
    var context = CreateVirtualCameraContext(
        fixture.DirectoryPath, VirtualCameraLoadMode.Dynamic, 0, 0, true,
        out var node, out var definition);
    await executor.StartSessionAsync(context, CancellationToken.None);
    var paths = new List<string>();
    var frames = new List<ulong>();
    for (var i = 0; i < 3; i++)
    {
        await executor.PrepareIterationAsync(context, CancellationToken.None);
        var output = await executor.ExecuteAsync(
            new FlowExecutionContext(), node, definition,
            new Dictionary<string, object>(), CancellationToken.None);
        paths.Add((string)output["imagePath"]);
        frames.Add(((FlowImage)output["image"]).FrameId);
    }
    return paths.SequenceEqual(new[] { Path.GetFullPath(a), Path.GetFullPath(c), Path.GetFullPath(a) })
        && frames.SequenceEqual(new[] { 0UL, 2UL, 0UL });
});
```

`SelectiveVirtualCameraImageLoader` 对 bad path 抛 `new VirtualCameraImageLoadException(path, new InvalidDataException("bad image"))`，对其他路径返回新的 1x1 FlowImage；它必须记录每次 load，测试据此验证 Dynamic 没有启动解码或历史缓存。

Task 4 的测试 helper 必须在 `VirtualCameraTests.cs` 提供这些签名：

```csharp
private static FlowNodeSessionContext CreateVirtualCameraContext(
    string sourcePath,
    VirtualCameraLoadMode loadMode,
    int maxImages,
    long maxBytes,
    bool skipErrors,
    out WorkflowNode node,
    out FlowNodeDefinition definition);

private static async Task StartVirtualCameraAsync(
    string sourcePath,
    VirtualCameraLoadMode loadMode,
    int maxImages,
    long maxBytes,
    bool skipErrors);

private static async Task<bool> ThrowsVirtualCameraAsync<TException>(
    string sourcePath,
    Func<Task> action)
    where TException : Exception;

private static bool ThrowsVirtualCamera<TException>(
    string sourcePath,
    Action action)
    where TException : Exception;
```

`CreateVirtualCameraContext` 的 definition output ports 必须按 `image`/`imagePath`/`imageDirectory` 顺序设置对应 data type 和 availability；`node.Inputs` 必须写五个 runtime key。`StartVirtualCameraAsync` 保持在 `finally` 中调用 `StopSessionAsync`，因为该 Stop 是幂等清理，失败启动也必须清字段且不抛“未启动”错误，从而不会覆盖 `StartSessionAsync` 的 primary exception。另加一个直接复用 executor 的失败启动后 Stop/重复 Stop/成功重启测试，验证半成品状态已清空。两个 `ThrowsVirtualCamera` helper 只在捕获到 `TException` 且消息同时包含 `VirtualCamera` 与相关 source path/URI 时返回 true，其他异常重新抛出。

helper 的具体实现契约如下，后续测试直接复用，不再创建第二套上下文约定：

```csharp
private static FlowNodeSessionContext CreateVirtualCameraContext(
    string sourcePath,
    VirtualCameraLoadMode loadMode,
    int maxImages,
    long maxBytes,
    bool skipErrors,
    out WorkflowNode node,
    out FlowNodeDefinition definition)
{
    node = new WorkflowNode
    {
        Id = "virtual-camera",
        TypeKey = VirtualCameraNodeModel.FlowNodeTypeKey,
        Inputs =
        {
            ["sourcePath"] = sourcePath,
            ["loadMode"] = loadMode,
            ["maxPreloadedImages"] = maxImages,
            ["maxPreloadedBytes"] = maxBytes,
            ["skipErrorImages"] = skipErrors,
        },
    };
    definition = new FlowNodeDefinition
    {
        TypeKey = VirtualCameraNodeModel.FlowNodeTypeKey,
        OutputPorts =
        {
            new FlowPortDefinition { Id = "image", DataType = FlowDataType.Image },
            new FlowPortDefinition { Id = "imagePath", DataType = FlowDataType.String },
            new FlowPortDefinition
            {
                Id = "imageDirectory",
                DataType = FlowDataType.String,
                Availability = FlowPortAvailability.Session,
            },
        },
    };
    return new FlowNodeSessionContext(node, definition, NullLogger.Instance);
}

private static async Task StartVirtualCameraAsync(
    string sourcePath,
    VirtualCameraLoadMode loadMode,
    int maxImages,
    long maxBytes,
    bool skipErrors)
{
    var executor = new VirtualCameraExecutor();
    var context = CreateVirtualCameraContext(
        sourcePath, loadMode, maxImages, maxBytes, skipErrors,
        out _, out _);
    try
    {
        await executor.StartSessionAsync(context, CancellationToken.None);
    }
    finally
    {
        await executor.StopSessionAsync(context, CancellationToken.None);
    }
}

private static async Task<bool> ThrowsVirtualCameraAsync<TException>(
    string sourcePath,
    Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
        return false;
    }
    catch (TException exception)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(sourcePath) ? "<empty>" : sourcePath;
        return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
            && exception.Message.Contains(sourceLabel, StringComparison.Ordinal);
    }
}

private static bool ThrowsVirtualCamera<TException>(
    string sourcePath,
    Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException exception)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(sourcePath) ? "<empty>" : sourcePath;
        return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
            && exception.Message.Contains(sourceLabel, StringComparison.Ordinal);
    }
}
```

两个 fake loader 的最小实现如下：

```csharp
private sealed class RecordingVirtualCameraImageLoader : IVirtualCameraImageLoader
{
    private readonly VirtualCameraImageLoader _inner = new VirtualCameraImageLoader();

    public List<string> Loads { get; } = new List<string>();

    public FlowImage Load(string path, ulong frameId)
    {
        Loads.Add(path);
        return _inner.Load(path, frameId);
    }
}

private sealed class SelectiveVirtualCameraImageLoader : IVirtualCameraImageLoader
{
    private readonly string _badPath;

    internal SelectiveVirtualCameraImageLoader(string badPath)
    {
        _badPath = Path.GetFullPath(badPath);
    }

    public FlowImage Load(string path, ulong frameId)
    {
        if (string.Equals(Path.GetFullPath(path), _badPath, StringComparison.Ordinal))
        {
            throw new VirtualCameraImageLoadException(
                path,
                new InvalidDataException("bad image"));
        }

        return FlowImage.CopyFrom(
            1, 1, 3, FlowPixelFormat.Bgr24, FlowImageKind.Color,
            new byte[] { (byte)frameId, 2, 3 },
            frameId, 0, DateTimeOffset.UtcNow);
    }
}
```

`ThrowingVirtualCameraImageLoader` 取一个 `Exception` 构造参数并在 `Load` 中原样抛出；用它分别创建 Dynamic context 和 Preload context，确保专用 wrapper 才会被过滤，三类非可 Skip 异常在两种模式都向上传播：

```csharp
private sealed class ThrowingVirtualCameraImageLoader : IVirtualCameraImageLoader
{
    private readonly Exception _exception;

    internal ThrowingVirtualCameraImageLoader(Exception exception)
    {
        _exception = exception;
    }

    public FlowImage Load(string path, ulong frameId)
    {
        throw _exception;
    }
}
```

再添加四个 fake loader 测试：

- `SkipErrorImages=false` 遇专用 wrapper 立即传播。
- `SkipErrorImages=true` 遇专用 wrapper 才删除并继续。
- `OperationCanceledException` 在 true 时仍传播。
- `OutOfMemoryException` 和 `InvalidOperationException` 在 true 时仍传播。

- [ ] **Step 2: 运行 Dynamic 测试确认失败**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: Dynamic 行为测试失败或 executor 仍未实现动态分支；该红灯确认测试确实约束了每轮加载和 cursor 规则。

- [ ] **Step 3: 实现 Dynamic 的每轮加载**

Dynamic `StartSessionAsync` 只保留 resolver 返回的本地路径 entry，不调用 loader、不填充 `PreloadedImage`，并继续使用 `_index = -1`。`PrepareIterationAsync` 采用以下精确结构，禁止再次写成裸 `catch when (_skipErrorImages)`：

```csharp
_current = null;
while (_entries.Count > 0)
{
    cancellationToken.ThrowIfCancellationRequested();
    var nextIndex = (_index + 1) % _entries.Count;
    var entry = _entries[nextIndex];
    try
    {
        _current = _imageLoader.Load(entry.Path, (ulong)entry.Ordinal);
        _currentEntry = entry;
        _index = nextIndex;
        cancellationToken.ThrowIfCancellationRequested();
        return;
    }
    catch (Exception exception) when (
        _skipErrorImages
        && VirtualCameraImageLoader.IsSkippableImageLoadError(exception))
    {
        _entries.RemoveAt(nextIndex);
        if (_entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"VirtualCamera has no readable images after '{entry.Path}'.", exception);
        }

        _index = nextIndex - 1;
    }
}
```

删除 bad entry 后不重新分配 ordinal；`nextIndex - 1` 让原 nextIndex 位置的下一候选项在下一循环立即尝试。捕获条件只匹配专用 wrapper，因此取消、OOM 和逻辑异常直接向上冒泡。

- [ ] **Step 4: 运行 Dynamic/error 测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: dynamic 首次加载、文件变化、A/Bad/C -> A/C/A、FrameId 0/2/0、专用错误可 Skip 以及三类非可 Skip 异常传播测试 PASS。

- [ ] **Step 5: 提交 Dynamic executor**

```bash
git add NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: add virtual camera dynamic loading"
```

### Task 6: 注册节点并增加可持久化配置编辑器

**Files:**
- Create: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml`
- Create: `NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs`
- Modify: `NodeCraft.Vision/Plugin/VisionPlugin.cs`
- Modify: `NodeCraft.Vision/NodeCraft.Vision.csproj`
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Consumes `VirtualCameraNodeModel`, `VirtualCameraExecutor` 和现有 `FlowNodeRegistration`/`FlowCanvas` editor pattern。
- Produces registered type key `nodecraft.vision.virtual-camera` with exact output definition and a content factory。

- [ ] **Step 1: 写插件注册和 editor content 的失败测试**

增加注册断言：

```csharp
await RunAsync("virtual camera registration exposes image, path and session directory", () =>
{
    var plugin = new VisionPlugin();
    var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
    plugin.Register(context);
    var registration = context.Registrations.Single(item =>
        item.Definition.TypeKey == VirtualCameraNodeModel.FlowNodeTypeKey);
    var ports = registration.Definition.OutputPorts;
    return ports.Select(port => port.Id).SequenceEqual(
            new[] { "image", "imagePath", "imageDirectory" })
        && ports.Select(port => port.DataType).SequenceEqual(
            new[] { FlowDataType.Image, FlowDataType.String, FlowDataType.String })
        && ports.Select(port => port.Availability).SequenceEqual(
            new[]
            {
                FlowPortAvailability.Iteration,
                FlowPortAvailability.Iteration,
                FlowPortAvailability.Session,
            })
        && registration.NodeModelType == typeof(VirtualCameraNodeModel)
        && registration.NodeFactory != null
        && registration.ExecutorFactory != null
        && registration.ContentFactory != null
        && registration.PaletteDescription.Contains("FlowImage", StringComparison.Ordinal);
});

await RunAsync("virtual camera editor has an embedded content factory", () =>
    RunOnSta(() =>
    {
        var content = VirtualCameraEditor.CreateContent(
            new FlowCanvas(),
            new VirtualCameraNodeModel());
        return content is FrameworkElement;
    }));
```

- [ ] **Step 2: 运行注册/UI 测试确认失败**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: registration 找不到 Virtual Camera 或 editor resource/type 尚不存在。

- [ ] **Step 3: 在 VisionPlugin 中注册 Virtual Camera**

在现有 `Register` 中保留 Vision Camera、Stereo Camera、FlowImage Preview，并新增 `CreateVirtualCameraRegistration()`。定义必须包含：

```csharp
TypeKey = VirtualCameraNodeModel.FlowNodeTypeKey,
DisplayName = "Virtual Camera",
Category = "Vision",
OutputPorts =
{
    CreateOutputPort("image", "Image", FlowDataType.Image, FlowPortAvailability.Iteration),
    CreateOutputPort("imagePath", "Image Path", FlowDataType.String, FlowPortAvailability.Iteration),
    CreateOutputPort("imageDirectory", "Image Directory", FlowDataType.String, FlowPortAvailability.Session),
},
```

扩展 `CreateOutputPort` 增加 `FlowPortAvailability availability` 参数并设置 `Availability = availability`；更新现有 Vision Camera 调用传 `Iteration`。executor factory 返回 `new VirtualCameraExecutor()`，设置 `NodeModelType`、`NodeFactory`、`PaletteDisplayName = "Virtual Camera"`、明确说明循环输出 FlowImage 的 `PaletteDescription` 和 `ContentFactory = VirtualCameraEditor.CreateContent`。

- [ ] **Step 4: 实现编辑器和资源嵌入**

XAML 使用现有 DynamicResource 主题 key，包含命名控件 `SourcePathEditor`, `LoadModeEditor`, `MaxPreloadedImagesEditor`, `MaxPreloadedBytesEditor`, `SkipErrorImagesEditor`。code-behind 按现有 `VisionCameraEditor` 的 resource parse pattern：解析 embedded XAML、拆出 root content、订阅事件、初始化五个控件并调用 `_canvas.NotifyGraphChanged(refreshNodeContents: false)`。

事件规则固定为：路径文本直接更新 `SourcePath`；ComboBox 只接受 `VirtualCameraLoadMode` enum；数量/bytes 文本只有 `int.TryParse`/`long.TryParse` 成功时更新对应属性；CheckBox 更新 bool。UI 不扫描文件、不解码、不维护 index，也不把 builtin Dynamic 自动改成 Preload；运行时 executor 负责拒绝该配置。

在 `.csproj` 的 WPF ItemGroup 中 `Page Remove="Views\\VirtualCameraEditor.xaml"` 并 `EmbeddedResource Include="Views\\VirtualCameraEditor.xaml"`，保证 resource name 为 `NodeCraft.Vision.Views.VirtualCameraEditor.xaml`。

- [ ] **Step 5: 运行注册/UI 测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 注册 port contract、palette、executor factory、content factory 和 embedded editor 测试 PASS；现有 Vision/Preview 注册测试仍 PASS。

- [ ] **Step 6: 提交注册和编辑器**

```bash
git add NodeCraft.Vision/Plugin/VisionPlugin.cs NodeCraft.Vision/NodeCraft.Vision.csproj NodeCraft.Vision/Views/VirtualCameraEditor.xaml NodeCraft.Vision/Views/VirtualCameraEditor.xaml.cs NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "feat: register virtual camera node"
```

### Task 7: 增加图级执行集成和 session 输出验证

**Files:**
- Modify: `NodeCraft.Tests/VirtualCameraTests.cs`

**Interfaces:**
- Consumes registered Virtual Camera and existing FlowImage Preview registration。
- Verifies `GraphExecutor` session startup, `image` iteration link、`imagePath` string link、`imageDirectory` session link，以及不实现 initializer 的后续节点仍能读取 required session input。

- [ ] **Step 1: 写 graph integration 的失败测试**

在测试文件中定义 test-only `SessionObservationExecutor`，它只实现 `IFlowNodeExecutor`，输入 ports 为：`image` (`Image`, required, Iteration)、`imagePath` (`String`, required, Iteration)、`imageDirectory` (`String`, required, Session)，输出 `observation` (`Object`, Iteration)，并将三项 inputs 放进一个 `SessionObservation` 对象返回。它故意不实现 `IFlowNodeSessionInitializer`，用来验证 session required input 检查和 session store。`SessionObservation` 必须有 `FlowImage Image`、`string ImagePath`、`string ImageDirectory` 三个只读属性。

构造 workflow：Virtual Camera 节点链接到现有 FlowImage Preview 的 `image` 输入，同时链接到 observation 节点的三个输入。用一个真实临时 folder，执行两次 `GraphExecutionSession.ExecuteIterationAsync`，断言：

```csharp
var validation = new GraphExecutor(workflow, registry).Validate();
if (!validation.IsValid) return false;

await using var session = new GraphExecutor(workflow, registry).CreateSession();
await session.StartAsync(CancellationToken.None);
var firstContext = await session.ExecuteIterationAsync(CancellationToken.None);
var secondContext = await session.ExecuteIterationAsync(CancellationToken.None);
var firstObservation = (SessionObservation)firstContext.Values
    .Single(pair => pair.Key.Item1 == "observation").Value;
var secondObservation = (SessionObservation)secondContext.Values
    .Single(pair => pair.Key.Item1 == "observation").Value;
var previewOutput = firstContext.Values
    .Single(pair => pair.Key.Item1 == "preview").Value;
await session.StopAsync();

return firstObservation.Image is FlowImage
    && firstObservation.ImagePath == Path.GetFullPath(firstPath)
    && firstObservation.ImageDirectory == Path.GetFullPath(folder)
    && secondObservation.ImagePath != firstObservation.ImagePath
    && previewOutput is FlowImage
    && firstObservation.Image.FrameId == 0;
```

同时使用 `imagePath` 链接到 `FlowDataType.String` 输入的 observation port，确认不会把字符串当作 FlowImage 或走 Preview 的错误路径。

- [ ] **Step 2: 运行集成测试确认失败**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 在注册/图构造或 executor 尚未完成时失败；记录具体失败消息后继续实现，不修改测试语义来绕开 link/availability 验证。

- [ ] **Step 3: 完成 test-only registry 和 observation executor**

使用 `VisionPlugin.Register` 把 Vision registrations 放入本地 `FlowNodeRegistry`，再注册 `SessionObservationExecutor` 的 test registration；不要修改生产 Flow registry。`SessionObservationExecutor.ExecuteAsync` 必须对三项输入做类型断言并返回 `observation`，缺任何 required input 就抛错，这会直接暴露 session output 未写入的问题。

- [ ] **Step 4: 运行图级集成测试确认通过**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: Virtual Camera -> FlowImage Preview、`imagePath` -> string input、`imageDirectory` session input 和两次 iteration 的稳定目录/循环图片测试 PASS；session stop 后 executor cleanup 测试 PASS。

- [ ] **Step 5: 提交图级集成**

```bash
git add NodeCraft.Tests/VirtualCameraTests.cs
git commit -m "test: cover virtual camera graph integration"
```

### Task 8: 全量验证和交付前审查

**Files:**
- Modify: none unless verification reveals a concrete failure in the files above.

- [ ] **Step 1: 检查变更和格式**

Run:

```bash
git diff --check
git status --short --branch
rg -n "catch when \\(_skipErrorImages\\)|catch \\(Exception|Path.GetFullPath|Directory.EnumerateFiles" NodeCraft.Vision/Nodes/VirtualCameraExecutor.cs NodeCraft.Vision/Nodes/VirtualCameraImageLoader.cs NodeCraft.Vision/Nodes/VirtualCameraSource.cs
```

Expected: `git diff --check` 无错误；executor 中不存在裸 `catch when (_skipErrorImages)`；loader 只有明确 scope 的异常过滤，不存在覆盖整个 `Load` 的宽泛 catch；source resolver 的路径规范化和目录 materialize 只使用预期异常过滤，并将外部环境错误包装成带 source 上下文的 `InvalidOperationException`。

- [ ] **Step 2: 构建解决方案**

Run: `dotnet build NodeCraft.sln --no-restore`

Expected: `NodeCraft.Vision`、`NodeCraft.Tests` 和 solution 全部 build 成功；允许保留仓库已有 nullable warnings，不得新增编译错误。

- [ ] **Step 3: 运行完整测试跑棒**

Run: `dotnet run --project NodeCraft.Tests/NodeCraft.Tests.csproj --no-restore`

Expected: 输出末尾为 `ALL PASS`，且新增 Virtual Camera 测试全部出现 `PASS`。

- [ ] **Step 4: 对照规格逐项审查**

确认以下事实均有测试或实现证据：五个属性 XML round-trip、runtime key/type 和 `LoadMode` enum 值域校验、三个 output availability、默认 builtin、local absolute path、folder direct-child deterministic sort、source resolver 的路径/目录环境异常包装、首次 ordinal 0、Preload decoded-byte checked limit 与 overflow context wrapping、Dynamic 每轮加载、builtin Dynamic fail、Gray8/Bgr24、窄 Skip filter、异常消息、幂等 stop/失败启动清理、Preview 消费和 session directory 链接。

- [ ] **Step 5: 提交最终验证修正并报告**

如果前面没有未提交修正，保持 clean worktree；如验证发现实际缺陷，只在对应实现/测试文件中修复，重新运行 Step 1-3 后提交：

```bash
git add NodeCraft.Vision NodeCraft.Tests
git commit -m "test: verify virtual camera implementation"
```

最终报告包含测试命令和 `ALL PASS` 证据，不把“已写计划”描述成“功能已实现”。

## Spec Coverage Self-Review

| 规格要求 | 计划覆盖 |
| --- | --- |
| 节点身份、五个配置属性、默认值 | Task 1 |
| XML `<Properties>` 持久化和 runtime input mapping | Task 1 |
| 三个输出及 availability | Tasks 1, 6 |
| `LoadMode` enum 定义和值域校验 | Tasks 1, 4 |
| 本地单文件/文件夹、绝对路径、扩展名和 deterministic tie-break | Task 2 |
| source path normalization/目录枚举的外部异常包装和来源上下文 | Task 2 |
| builtin sample-set、单图 URI、目录语义、builtin 禁止 Dynamic | Tasks 2, 4 |
| WPF OnLoad 解码、Gray8/Mono8、Bgr24、FlowImage metadata | Task 3 |
| Preload 全量缓存、数量/decoded bytes/checked/overflow context wrapping/正数校验 | Task 4 |
| Dynamic 每轮解码、文件修改可见、无历史缓存 | Task 5 |
| ordinal、首图不跳过、坏图删除游标 | Tasks 4, 5 |
| 专用可 Skip 异常和取消/OOM/逻辑异常传播 | Tasks 3, 5 |
| session initializer、停止清理、未准备状态错误 | Task 4 |
| StopSessionAsync 幂等、失败启动后可清理并重启 | Task 4 |
| palette、editor、embedded resource | Task 6 |
| FlowImage Preview、string input、session directory integration | Task 7 |
| 全量测试和完成标准 | Task 8 |

计划中的接口名称在前置任务定义后由后续任务复用：`VirtualCameraEntry`、`VirtualCameraSourceResolver.Resolve`、`IVirtualCameraImageLoader.Load`、`VirtualCameraImageLoader.IsSkippableImageLoadError` 和 `VirtualCameraExecutor`。实现阶段不得以另一个同义名称替换这些跨任务接口而不同时更新后续步骤。
