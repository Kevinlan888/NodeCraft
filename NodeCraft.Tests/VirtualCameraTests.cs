using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;
using NodeCraft.Vision.Nodes;
using NodeCraft.Vision.Plugin;
using NodeCraft.Vision.Views;

internal static partial class Program
{
    private static async Task RunVirtualCameraTestsAsync()
    {
        await RunAsync("virtual camera model persists configuration and maps workflow inputs", async () =>
        {
            var node = new VirtualCameraNodeModel
            {
                SourcePath = Path.Combine(Path.GetTempPath(), "frames"),
                LoadMode = VirtualCameraLoadMode.Dynamic,
                FrameRate = 29.97,
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
                    && workflowNode.Inputs["frameRate"] is double frameRate
                    && frameRate == 29.97
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
                    && xml.Contains("Name=\"FrameRate\"", StringComparison.Ordinal)
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
                    && restored.FrameRate == 29.97
                    && restored.MaxPreloadedImages == node.MaxPreloadedImages
                    && restored.MaxPreloadedBytes == node.MaxPreloadedBytes
                    && restored.SkipErrorImages == node.SkipErrorImages
                    && legacyDefaults.SourcePath == "builtin://vision/sample-set"
                    && legacyDefaults.LoadMode == VirtualCameraLoadMode.Preload
                    && legacyDefaults.FrameRate == 18.0
                    && legacyDefaults.MaxPreloadedImages == 100
                    && legacyDefaults.MaxPreloadedBytes == 536870912L
                    && !legacyDefaults.SkipErrorImages;
            }
            finally
            {
                File.Delete(xmlPath);
            }
        });

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

        await RunAsync("virtual camera resolves a single absolute image and its directory", async () =>
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
                && source.Entries[0].PreloadedTemplate == null;
        });

        await RunAsync("virtual camera sorts supported folder images with ordinal tie break", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            fixture.WriteImage("A.jpg", new byte[] { 1, 2, 3 });
            fixture.WriteImage("a.PNG", new byte[] { 4, 5, 6 });
            fixture.WriteImage("b.bmp", new byte[] { 7, 8, 9 });
            var nestedDirectory = Directory.CreateDirectory(
                Path.Combine(fixture.DirectoryPath, "nested"));
            File.WriteAllBytes(
                Path.Combine(nestedDirectory.FullName, "nested.png"),
                new byte[] { 8, 8, 8 });
            File.WriteAllText(Path.Combine(fixture.DirectoryPath, "ignored.txt"), "ignored");
            var source = VirtualCameraSourceResolver.Resolve(fixture.DirectoryPath);
            var names = source.Entries.Select(entry => Path.GetFileName(entry.Path)).ToArray();
            return names.SequenceEqual(new[] { "A.jpg", "a.PNG", "b.bmp" })
                && !names.Contains("nested.png", StringComparer.Ordinal)
                && source.Entries.Select(entry => entry.Ordinal).SequenceEqual(new[] { 0, 1, 2 });
        });

        await RunAsync("virtual camera resolves builtin collection and single asset", async () =>
        {
            var collection = VirtualCameraSourceResolver.Resolve("builtin://vision/sample-set");
            var single = VirtualCameraSourceResolver.Resolve("builtin://vision/sample-set/checkerboard");
            var uppercase = VirtualCameraSourceResolver.Resolve(
                "BUILTIN://VISION/SAMPLE-SET/CHECKERBOARD");
            var checkerboard = collection.Entries[0].PreloadedTemplate.CreateFrame(
                0,
                0,
                DateTimeOffset.UnixEpoch);
            var colorBars = collection.Entries[1].PreloadedTemplate.CreateFrame(
                1,
                0,
                DateTimeOffset.UnixEpoch);
            var singleCheckerboard = single.Entries[0].PreloadedTemplate.CreateFrame(
                0,
                0,
                DateTimeOffset.UnixEpoch);
            return collection.IsBuiltin
                && collection.ImageDirectory == "builtin://vision/sample-set"
                && collection.Entries.Count == 2
                && collection.Entries[0].Path == "builtin://vision/sample-set/checkerboard"
                && collection.Entries[1].Path == "builtin://vision/sample-set/color-bars"
                && checkerboard.Width == 2
                && checkerboard.Height == 2
                && checkerboard.Stride == 6
                && checkerboard.PixelFormat == FlowPixelFormat.Bgr24
                && checkerboard.Buffer.Span.SequenceEqual(new byte[]
                {
                    255, 255, 255, 0, 0, 0,
                    0, 0, 0, 255, 255, 255,
                })
                && colorBars.Width == 3
                && colorBars.Height == 1
                && colorBars.Stride == 9
                && colorBars.PixelFormat == FlowPixelFormat.Bgr24
                && colorBars.Buffer.Span.SequenceEqual(new byte[]
                {
                    255, 0, 0, 0, 255, 0, 0, 0, 255,
                })
                && single.Entries.Count == 1
                && single.ImageDirectory == "builtin://vision/sample-set"
                && single.Entries[0].Path == "builtin://vision/sample-set/checkerboard"
                && singleCheckerboard.Width == 2
                && uppercase.ImageDirectory == "builtin://vision/sample-set"
                && uppercase.Entries[0].Path == "builtin://vision/sample-set/checkerboard";
        });

        await RunAsync("virtual camera rejects invalid source kinds and empty folders", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            var cases = new[]
            {
                string.Empty,
                Path.Combine(fixture.DirectoryPath, "missing.png"),
                Path.Combine(fixture.DirectoryPath, "unsupported.gif"),
                "builtin://vision/unknown",
                "builtin://vision/sample-set?query=1",
                "builtin://vision/sample-set/checkerboard/extra",
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

        await RunAsync("virtual camera decodes gray8 as mono8 and color as bgr24", () =>
            Task.FromResult(RunOnSta(() =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var monoPath = fixture.WriteBitmap(
                    "mono.png", PixelFormats.Gray8, 2, 1, new byte[] { 9, 10 }, 2);
                var colorPath = fixture.WriteBitmap(
                    "color.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var capturedAt = new DateTimeOffset(
                    2026,
                    8,
                    16,
                    1,
                    2,
                    3,
                    TimeSpan.Zero);
                var monoTemplate = new VirtualCameraImageLoader().Load(monoPath);
                var colorTemplate = new VirtualCameraImageLoader().Load(colorPath);
                var mono = monoTemplate.CreateFrame(4, 1234, capturedAt);
                var color = colorTemplate.CreateFrame(5, 5678, capturedAt);
                var monoAgain = monoTemplate.CreateFrame(
                    6,
                    6789,
                    capturedAt.AddSeconds(1));

                var firstArrayFound = MemoryMarshal.TryGetArray(
                    mono.Buffer,
                    out var firstArray);
                var secondArrayFound = MemoryMarshal.TryGetArray(
                    monoAgain.Buffer,
                    out var secondArray);
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
            })));

        await RunAsync("virtual camera wraps only expected image load failures", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            var missingPath = Path.Combine(fixture.DirectoryPath, "missing.png");
            var corruptPath = fixture.WriteImage("corrupt.png", new byte[] { 1, 2, 3 });
            var loader = new VirtualCameraImageLoader();
            try
            {
                loader.Load(missingPath);
                return false;
            }
            catch (VirtualCameraImageLoadException exception)
            {
                if (exception.Path != missingPath
                    || !exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                    || !VirtualCameraImageLoader.IsSkippableImageLoadError(exception)
                    || VirtualCameraImageLoader.IsSkippableImageLoadError(new InvalidOperationException()))
                {
                    return false;
                }
            }

            try
            {
                loader.Load(corruptPath);
                return false;
            }
            catch (VirtualCameraImageLoadException exception)
            {
                return exception.Path == corruptPath
                    && exception.Message.Contains(corruptPath, StringComparison.Ordinal);
            }
        });

        await RunAsync("virtual camera decodes JPEG and BMP on a worker and releases file handles", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            var paths = await RunOnStaValueAsync(() =>
            {
                var jpg = fixture.WriteBitmap(
                    "sample.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var bmp = fixture.WriteBitmap(
                    "sample.bmp", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
                return (jpg, bmp);
            });
            var workerApartment = ApartmentState.Unknown;
            var decoded = await Task.Run(() =>
            {
                workerApartment = Thread.CurrentThread.GetApartmentState();
                var loader = new VirtualCameraImageLoader();
                var capturedAt = DateTimeOffset.UnixEpoch;
                return (
                    loader.Load(paths.jpg).CreateFrame(6, 0, capturedAt),
                    loader.Load(paths.bmp).CreateFrame(7, 0, capturedAt));
            });
            var movedJpg = paths.jpg + ".moved";
            var movedBmp = paths.bmp + ".moved";
            File.Move(paths.jpg, movedJpg);
            File.Move(paths.bmp, movedBmp);
            return decoded.Item1.PixelFormat == FlowPixelFormat.Bgr24
                && decoded.Item2.PixelFormat == FlowPixelFormat.Bgr24
                && decoded.Item1.Width == 1
                && decoded.Item2.Width == 1
                && decoded.Item1.FrameId == 6
                && decoded.Item2.FrameId == 7
                && workerApartment == ApartmentState.MTA
                && File.Exists(movedJpg)
                && File.Exists(movedBmp);
        });

        await RunAsync("virtual camera preload starts at ordinal zero and exposes session directory", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                fixture.WriteBitmap("b.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
                var executor = CreateVirtualCameraExecutor();
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    maxImages: 10,
                    maxBytes: 100,
                    skipErrors: false,
                    out var node,
                    out var definition);

                await executor.StartSessionAsync(context, CancellationToken.None);
                await executor.StartSessionAsync(context, CancellationToken.None);
                var sessionOutputs = await executor.InitializeSessionAsync(
                    context,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var first = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var second = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var wrapped = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.StopSessionAsync(context, CancellationToken.None);
                await executor.StopSessionAsync(context, CancellationToken.None);

                var firstImage = (FlowImage)first["image"];
                var wrappedImage = (FlowImage)wrapped["image"];
                var firstArrayFound = MemoryMarshal.TryGetArray(
                    firstImage.Buffer,
                    out var firstArray);
                var wrappedArrayFound = MemoryMarshal.TryGetArray(
                    wrappedImage.Buffer,
                    out var wrappedArray);
                return (string)sessionOutputs["imageDirectory"] == Path.GetFullPath(fixture.DirectoryPath)
                    && firstImage.FrameId == 0
                    && (string)first["imagePath"] == Path.Combine(fixture.DirectoryPath, "a.png")
                    && ((FlowImage)second["image"]).FrameId == 1
                    && wrappedImage.FrameId == 2
                    && !ReferenceEquals(firstImage, wrappedImage)
                    && firstArrayFound
                    && wrappedArrayFound
                    && ReferenceEquals(firstArray.Array, wrappedArray.Array);
            }));

        await RunAsync("virtual camera preload enforces positive count and checked decoded bytes", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var aPath = fixture.WriteBitmap(
                    "a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var bPath = fixture.WriteBitmap(
                    "b.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
                var compressedSourceBytes = new FileInfo(aPath).Length + new FileInfo(bPath).Length;

                await StartVirtualCameraAsync(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    6,
                    false);
                var invalidCount = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        0,
                        100,
                        false));
                var invalidBytes = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        10,
                        0,
                        false));
                var tooSmall = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        10,
                        2,
                        false));
                var tooMany = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        1,
                        100,
                        false));
                var invalidMode = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        (VirtualCameraLoadMode)123,
                        10,
                        100,
                        false));
                return compressedSourceBytes > 6
                    && invalidCount
                    && invalidBytes
                    && tooSmall
                    && tooMany
                    && invalidMode;
            }));

        await RunAsync("virtual camera rejects missing and wrongly typed runtime inputs", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            var mutations = new (string Key, Action<WorkflowNode> Mutate)[]
            {
                ("sourcePath", node => node.Inputs.Remove("sourcePath")),
                ("sourcePath", node => node.Inputs["sourcePath"] = 123),
                ("loadMode", node => node.Inputs["loadMode"] = "Preload"),
                ("frameRate", node => node.Inputs["frameRate"] = "18"),
                ("maxPreloadedImages", node => node.Inputs["maxPreloadedImages"] = 10L),
                ("maxPreloadedBytes", node => node.Inputs["maxPreloadedBytes"] = 10),
                ("skipErrorImages", node => node.Inputs["skipErrorImages"] = "false"),
            };
            var allRejected = true;
            foreach (var mutation in mutations)
            {
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    100,
                    false,
                    out var node,
                    out _);
                mutation.Mutate(node);
                var executor = CreateVirtualCameraExecutor();
                try
                {
                    await executor.StartSessionAsync(context, CancellationToken.None);
                    allRejected = false;
                }
                catch (InvalidOperationException exception)
                {
                    allRejected = allRejected
                        && exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                        && exception.Message.Contains(mutation.Key, StringComparison.Ordinal);
                }
                finally
                {
                    await executor.StopSessionAsync(context, CancellationToken.None);
                }
            }

            return allRejected;
        });

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
                    .SequenceEqual(new[]
                    {
                        "checkerboard",
                        "color-bars",
                        "checkerboard",
                        "color-bars",
                    })
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

        await RunAsync("virtual camera wraps decoded byte accounting overflow", async () =>
        {
            try
            {
                VirtualCameraExecutor.AddPreloadedBytesChecked(
                    long.MaxValue,
                    1,
                    "C:\\frames",
                    "C:\\frames\\overflow.png");
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                    && exception.Message.Contains("C:\\frames", StringComparison.Ordinal)
                    && exception.InnerException is OverflowException;
            }
        });

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

        await RunAsync("virtual camera wraps deadline overflow before committing frame", async () =>
        {
            const double frameRate = 20.0;
            var framePeriod = TimeSpan.FromSeconds(1.0 / frameRate);
            var clock = new VirtualCameraTestClock(TimeSpan.MaxValue - framePeriod);
            Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.Advance(duration);
                return Task.CompletedTask;
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
                out _,
                out _,
                frameRate);
            await executor.StartSessionAsync(context, CancellationToken.None);
            Exception? observed = null;
            try
            {
                await executor.PrepareIterationAsync(context, CancellationToken.None);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var executorType = typeof(VirtualCameraExecutor);
            var index = (int)executorType.GetField("_index", fields)!.GetValue(executor)!;
            var nextFrameId = (ulong)executorType
                .GetField("_nextFrameId", fields)!
                .GetValue(executor)!;
            var current = executorType.GetField("_current", fields)!.GetValue(executor);
            await executor.StopSessionAsync(context, CancellationToken.None);

            return observed is InvalidOperationException invalidOperation
                && invalidOperation.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                && invalidOperation.Message.Contains(
                    "builtin://vision/sample-set", StringComparison.Ordinal)
                && invalidOperation.InnerException is OverflowException
                && index == -1
                && nextFrameId == 0
                && current == null;
        });

        await RunAsync("virtual camera reports distinct prepare and execute errors after stop", async () =>
        {
            var executor = CreateVirtualCameraExecutor();
            var context = CreateVirtualCameraContext(
                "builtin://vision/sample-set",
                VirtualCameraLoadMode.Preload,
                10,
                100,
                false,
                out var node,
                out var definition);
            await executor.StopSessionAsync(context, CancellationToken.None);
            var prepareMessage = string.Empty;
            var initializeMessage = string.Empty;
            var executeMessage = string.Empty;
            try
            {
                await executor.InitializeSessionAsync(
                    context,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                initializeMessage = exception.Message;
            }
            try
            {
                await executor.PrepareIterationAsync(context, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                prepareMessage = exception.Message;
            }
            try
            {
                await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                executeMessage = exception.Message;
            }

            return initializeMessage.Contains("session is not started", StringComparison.Ordinal)
                && prepareMessage.Contains("session is not started", StringComparison.Ordinal)
                && executeMessage.Contains("no prepared image", StringComparison.Ordinal)
                && initializeMessage.Contains("VirtualCamera", StringComparison.Ordinal)
                && prepareMessage.Contains("VirtualCamera", StringComparison.Ordinal)
                && executeMessage.Contains("VirtualCamera", StringComparison.Ordinal)
                && prepareMessage.Contains("builtin://vision/sample-set", StringComparison.Ordinal)
                && executeMessage.Contains("builtin://vision/sample-set", StringComparison.Ordinal);
        });

        await RunAsync("virtual camera failed start can be stopped and restarted cleanly", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var executor = CreateVirtualCameraExecutor();
                var failedContext = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    0,
                    false,
                    out _,
                    out _);
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
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    100,
                    false,
                    out var node,
                    out var definition);
                await executor.StartSessionAsync(validContext, CancellationToken.None);
                await executor.PrepareIterationAsync(validContext, CancellationToken.None);
                var output = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.StopSessionAsync(validContext, CancellationToken.None);

                return primaryFailurePreserved
                    && ((FlowImage)output["image"]).FrameId == 0;
            }));

        await RunAsync("virtual camera propagates cancellation and leaves the executor stopped", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteBitmap("a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var executor = CreateVirtualCameraExecutor();
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    100,
                    false,
                    out var node,
                    out var definition);

                using var startCancellation = new CancellationTokenSource();
                startCancellation.Cancel();
                var startCanceled = false;
                try
                {
                    await executor.StartSessionAsync(context, startCancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    startCanceled = exception.CancellationToken == startCancellation.Token;
                }
                await executor.StopSessionAsync(context, CancellationToken.None);

                await executor.StartSessionAsync(context, CancellationToken.None);
                using var prepareCancellation = new CancellationTokenSource();
                prepareCancellation.Cancel();
                var prepareCanceled = false;
                try
                {
                    await executor.PrepareIterationAsync(context, prepareCancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    prepareCanceled = exception.CancellationToken == prepareCancellation.Token;
                }

                using var executeCancellation = new CancellationTokenSource();
                executeCancellation.Cancel();
                var executeCanceled = false;
                try
                {
                    await executor.ExecuteAsync(
                        new FlowExecutionContext(),
                        node,
                        definition,
                        new Dictionary<string, object>(),
                        executeCancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    executeCanceled = exception.CancellationToken == executeCancellation.Token;
                }
                await executor.StopSessionAsync(context, CancellationToken.None);

                return startCanceled && prepareCanceled && executeCanceled;
            }));

        await RunAsync("virtual camera observes cancellation during the final preload decode", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteImage("a.png", new byte[] { 1 });
                fixture.WriteImage("b.png", new byte[] { 2 });
                using var cancellation = new CancellationTokenSource();
                var loader = new CancelOnLoadVirtualCameraImageLoader(
                    cancellation,
                    cancelOnLoad: 2);
                var executor = CreateVirtualCameraExecutor(loader);
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    10,
                    100,
                    false,
                    out _,
                    out _);

                var canceled = false;
                try
                {
                    await executor.StartSessionAsync(context, cancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    canceled = exception.CancellationToken == cancellation.Token;
                }

                var stayedStopped = false;
                try
                {
                    await executor.PrepareIterationAsync(context, CancellationToken.None);
                }
                catch (InvalidOperationException exception)
                {
                    stayedStopped = exception.Message.Contains(
                        "session is not started", StringComparison.Ordinal);
                }

                await executor.StopSessionAsync(context, CancellationToken.None);
                return canceled && stayedStopped && loader.LoadCount == 2;
            }));

        await RunAsync("virtual camera preload limits count and bytes only after successful decode", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteBitmap(
                    "A.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                fixture.WriteImage("Bad.png", new byte[] { 0, 1, 2, 3 });
                fixture.WriteBitmap(
                    "C.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);
                var executor = CreateVirtualCameraExecutor();
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Preload,
                    2,
                    6,
                    true,
                    out var node,
                    out var definition);

                await executor.StartSessionAsync(context, CancellationToken.None);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var first = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var second = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.StopSessionAsync(context, CancellationToken.None);

                return (string)first["imagePath"] == Path.Combine(fixture.DirectoryPath, "A.png")
                    && (string)second["imagePath"] == Path.Combine(fixture.DirectoryPath, "C.png");
            }));

        await RunAsync("virtual camera preload rejects a sequence with no readable images", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteImage("A.png", new byte[] { 0, 1, 2, 3 });
                fixture.WriteImage("B.png", new byte[] { 4, 5, 6, 7 });
                var withoutSkip = await ThrowsVirtualCameraAsync<VirtualCameraImageLoadException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        10,
                        100,
                        false));
                var withSkip = await ThrowsVirtualCameraAsync<InvalidOperationException>(
                    fixture.DirectoryPath,
                    () => StartVirtualCameraAsync(
                        fixture.DirectoryPath,
                        VirtualCameraLoadMode.Preload,
                        10,
                        100,
                        true));
                return withoutSkip && withSkip;
            }));

        await RunAsync("virtual camera rejects builtin dynamic before materializing images", async () =>
        {
            var loader = new RecordingVirtualCameraImageLoader();
            var executor = CreateVirtualCameraExecutor(loader);
            var context = CreateVirtualCameraContext(
                "builtin://vision/sample-set",
                VirtualCameraLoadMode.Dynamic,
                10,
                100,
                false,
                out _,
                out _);
            var rejected = false;
            try
            {
                await executor.StartSessionAsync(context, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                rejected = exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                    && exception.Message.Contains("Dynamic", StringComparison.Ordinal);
            }
            await executor.StopSessionAsync(context, CancellationToken.None);
            return rejected && loader.LoadCount == 0;
        });

        await RunAsync("virtual camera dynamic loads only during prepare and observes file changes", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var path = fixture.WriteBitmap(
                    "a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var loader = new RecordingVirtualCameraImageLoader();
                var executor = CreateVirtualCameraExecutor(loader);
                var context = CreateVirtualCameraContext(
                    path,
                    VirtualCameraLoadMode.Dynamic,
                    0,
                    0,
                    false,
                    out var node,
                    out var definition);

                await executor.StartSessionAsync(context, CancellationToken.None);
                var startedWithoutLoad = loader.Loads.Count == 0;
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var first = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                fixture.WriteBitmap(
                    "a.png", PixelFormats.Bgr24, 1, 1, new byte[] { 9, 8, 7 }, 3);
                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var second = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.StopSessionAsync(context, CancellationToken.None);

                return startedWithoutLoad
                    && loader.Loads.Count == 2
                    && (string)first["imagePath"] == Path.GetFullPath(path)
                    && (string)second["imagePath"] == Path.GetFullPath(path)
                    && ((FlowImage)first["image"]).FrameId == 0
                    && ((FlowImage)second["image"]).FrameId == 1
                    && !((FlowImage)first["image"]).Buffer.Span.SequenceEqual(
                        ((FlowImage)second["image"]).Buffer.Span)
                    && !ReferenceEquals(first["image"], second["image"]);
            }));

        await RunAsync("virtual camera dynamic skip removes bad entry without skipping next", () =>
            RunOnStaAsync(async () =>
            {
                var bad = string.Empty;
                using var fixture = new TemporaryVirtualCameraFiles();
                var a = fixture.WriteBitmap(
                    "A.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 1, 1 }, 3);
                bad = fixture.WriteBitmap(
                    "Bad.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 2, 2, 2 }, 3);
                var c = fixture.WriteBitmap(
                    "C.jpg", PixelFormats.Bgr24, 1, 1, new byte[] { 3, 3, 3 }, 3);
                var loader = new SelectiveVirtualCameraImageLoader(bad);
                var executor = CreateVirtualCameraExecutor(loader);
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Dynamic,
                    0,
                    0,
                    true,
                    out var node,
                    out var definition);
                await executor.StartSessionAsync(context, CancellationToken.None);
                var paths = new List<string>();
                var frames = new List<ulong>();
                for (var i = 0; i < 3; i++)
                {
                    await executor.PrepareIterationAsync(context, CancellationToken.None);
                    var output = await executor.ExecuteAsync(
                        new FlowExecutionContext(),
                        node,
                        definition,
                        new Dictionary<string, object>(),
                        CancellationToken.None);
                    paths.Add((string)output["imagePath"]);
                    frames.Add(((FlowImage)output["image"]).FrameId);
                }
                await executor.StopSessionAsync(context, CancellationToken.None);

                return paths.SequenceEqual(new[]
                    { Path.GetFullPath(a), Path.GetFullPath(c), Path.GetFullPath(a) })
                    && frames.SequenceEqual(new[] { 0UL, 1UL, 2UL })
                    && loader.Loads.Select(Path.GetFileName).SequenceEqual(
                        new[] { "A.jpg", "Bad.jpg", "C.jpg", "A.jpg" });
            }));

        await RunAsync("virtual camera dynamic cancellation does not commit cursor", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteImage("A.png", new byte[] { 1 });
                fixture.WriteImage("B.png", new byte[] { 2 });
                using var cancellation = new CancellationTokenSource();
                var loader = new CancelOnLoadVirtualCameraImageLoader(
                    cancellation,
                    cancelOnLoad: 1);
                var executor = CreateVirtualCameraExecutor(loader);
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Dynamic,
                    0,
                    0,
                    false,
                    out var node,
                    out var definition);
                await executor.StartSessionAsync(context, CancellationToken.None);

                var canceled = false;
                try
                {
                    await executor.PrepareIterationAsync(context, cancellation.Token);
                }
                catch (OperationCanceledException exception)
                {
                    canceled = exception.CancellationToken == cancellation.Token;
                }

                await executor.PrepareIterationAsync(context, CancellationToken.None);
                var output = await executor.ExecuteAsync(
                    new FlowExecutionContext(),
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                await executor.StopSessionAsync(context, CancellationToken.None);

                return canceled
                    && loader.LoadCount == 2
                    && (string)output["imagePath"] == Path.Combine(fixture.DirectoryPath, "A.png")
                    && ((FlowImage)output["image"]).FrameId == 0;
            }));

        await RunAsync("virtual camera dynamic empty sequence fails every prepare", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var bad = fixture.WriteImage("Bad.png", new byte[] { 1 });
                var loader = new SelectiveVirtualCameraImageLoader(bad);
                var executor = CreateVirtualCameraExecutor(loader);
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Dynamic,
                    0,
                    0,
                    true,
                    out _,
                    out _);
                await executor.StartSessionAsync(context, CancellationToken.None);

                var failures = 0;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        await executor.PrepareIterationAsync(context, CancellationToken.None);
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                            && exception.Message.Contains(
                                fixture.DirectoryPath, StringComparison.Ordinal))
                        {
                            failures++;
                        }
                    }
                }

                await executor.StopSessionAsync(context, CancellationToken.None);
                return failures == 2 && loader.Loads.Count == 1;
            }));

        await RunAsync("virtual camera dynamic propagates a skippable error when skipping is disabled", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var bad = fixture.WriteImage("Bad.png", new byte[] { 1 });
                var executor = CreateVirtualCameraExecutor(
                    new SelectiveVirtualCameraImageLoader(bad));
                var context = CreateVirtualCameraContext(
                    fixture.DirectoryPath,
                    VirtualCameraLoadMode.Dynamic,
                    0,
                    0,
                    false,
                    out _,
                    out _);
                await executor.StartSessionAsync(context, CancellationToken.None);
                var propagated = false;
                try
                {
                    await executor.PrepareIterationAsync(context, CancellationToken.None);
                }
                catch (VirtualCameraImageLoadException exception)
                {
                    propagated = exception.Message.Contains(
                        "VirtualCamera", StringComparison.Ordinal);
                }
                await executor.StopSessionAsync(context, CancellationToken.None);
                return propagated;
            }));

        await RunAsync("virtual camera never skips non-image load exceptions", async () =>
        {
            var exceptions = new Exception[]
            {
                new OperationCanceledException(),
                new OutOfMemoryException(),
                new InvalidOperationException("logic failure"),
            };
            var allPropagated = true;
            foreach (var exception in exceptions)
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                fixture.WriteImage("a.png", new byte[] { 1 });
                foreach (var loadMode in new[]
                {
                    VirtualCameraLoadMode.Preload,
                    VirtualCameraLoadMode.Dynamic,
                })
                {
                    var executor = CreateVirtualCameraExecutor(
                        new ThrowingVirtualCameraImageLoader(exception));
                    var context = CreateVirtualCameraContext(
                        fixture.DirectoryPath,
                        loadMode,
                        10,
                        100,
                        true,
                        out _,
                        out _);
                    try
                    {
                        await executor.StartSessionAsync(context, CancellationToken.None);
                        if (loadMode == VirtualCameraLoadMode.Dynamic)
                        {
                            await executor.PrepareIterationAsync(context, CancellationToken.None);
                        }
                        allPropagated = false;
                    }
                    catch (Exception actual)
                    {
                        allPropagated = allPropagated
                            && actual.GetType() == exception.GetType();
                    }
                    finally
                    {
                        await executor.StopSessionAsync(context, CancellationToken.None);
                    }
                }
            }

            return allPropagated;
        });

        await RunAsync("virtual camera registration exposes image, path and session directory", async () =>
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

        await RunAsync("virtual camera editor mutates all properties and notifies graph changes", () =>
            Task.FromResult(RunOnSta(() =>
            {
                var canvas = new FlowCanvas();
                var node = new VirtualCameraNodeModel();
                var graphChanges = 0;
                canvas.GraphChanged += (_, __) => graphChanges++;
                var content = VirtualCameraEditor.CreateContent(canvas, node);
                var initializedWithoutChange = graphChanges == 0;

                var source = GetPrivateField<TextBox>(content, "_sourcePathEditor");
                var mode = GetPrivateField<ComboBox>(content, "_loadModeEditor");
                var frameRate = GetPrivateField<TextBox>(content, "_frameRateEditor");
                var maxImages = GetPrivateField<TextBox>(content, "_maxPreloadedImagesEditor");
                var maxBytes = GetPrivateField<TextBox>(content, "_maxPreloadedBytesEditor");
                var skipErrors = GetPrivateField<CheckBox>(content, "_skipErrorImagesEditor");
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

                return content is FrameworkElement
                    && initializedWithoutChange
                    && node.SourcePath == "C:\\frames"
                    && node.LoadMode == VirtualCameraLoadMode.Dynamic
                    && node.FrameRate == 29.97
                    && node.MaxPreloadedImages == 7
                    && initialMaxBytesText == "512"
                    && node.MaxPreloadedBytes == 256L * 1024L * 1024L
                    && node.SkipErrorImages
                    && changesAfterValidInput == 6
                    && graphChanges == changesAfterValidInput;
            })));

        await RunAsync("virtual camera graph links preview, path and session directory", () =>
            RunOnStaAsync(async () =>
            {
                using var fixture = new TemporaryVirtualCameraFiles();
                var firstPath = fixture.WriteBitmap(
                    "A.png", PixelFormats.Bgr24, 1, 1, new byte[] { 1, 2, 3 }, 3);
                var secondPath = fixture.WriteBitmap(
                    "B.png", PixelFormats.Bgr24, 1, 1, new byte[] { 4, 5, 6 }, 3);

                var plugin = new VisionPlugin();
                var pluginContext = new PluginRegistrationContext(
                    NullLogger.Instance,
                    new Version(1, 0));
                plugin.Register(pluginContext);
                var registry = new FlowNodeRegistry();
                registry.RegisterPlugin(plugin.Metadata.Id, pluginContext.Registrations);
                registry.Register(new FlowNodeRegistration(
                    CreateSessionObservationDefinition(),
                    () => new SessionObservationExecutor())
                {
                    ShowInPalette = false,
                });

                var workflow = new WorkflowDocument();
                workflow.Nodes.Add(new WorkflowNode
                {
                    Id = "virtual-camera",
                    TypeKey = VirtualCameraNodeModel.FlowNodeTypeKey,
                    Inputs =
                    {
                        ["sourcePath"] = fixture.DirectoryPath,
                        ["loadMode"] = VirtualCameraLoadMode.Preload,
                        ["frameRate"] = 1000.0,
                        ["maxPreloadedImages"] = 10,
                        ["maxPreloadedBytes"] = 100L,
                        ["skipErrorImages"] = false,
                    },
                });
                workflow.Nodes.Add(new WorkflowNode
                {
                    Id = "preview",
                    TypeKey = FlowImagePreviewNodeModel.FlowNodeTypeKey,
                    Inputs =
                    {
                        ["image"] = new LinkRef
                        {
                            SourceNodeId = "virtual-camera",
                            SourceSlot = 0,
                        },
                    },
                });
                workflow.Nodes.Add(new WorkflowNode
                {
                    Id = "observation",
                    TypeKey = SessionObservationExecutor.FlowNodeTypeKey,
                    Inputs =
                    {
                        ["image"] = new LinkRef
                        {
                            SourceNodeId = "virtual-camera",
                            SourceSlot = 0,
                        },
                        ["imagePath"] = new LinkRef
                        {
                            SourceNodeId = "virtual-camera",
                            SourceSlot = 1,
                        },
                        ["imageDirectory"] = new LinkRef
                        {
                            SourceNodeId = "virtual-camera",
                            SourceSlot = 2,
                        },
                    },
                });

                var graphExecutor = new GraphExecutor(workflow, registry);
                var validation = graphExecutor.Validate();
                if (!validation.IsValid)
                {
                    return false;
                }

                await using var session = graphExecutor.CreateSession();
                await session.StartAsync(CancellationToken.None);
                try
                {
                    var firstContext = await session.ExecuteIterationAsync(
                        CancellationToken.None);
                    var secondContext = await session.ExecuteIterationAsync(
                        CancellationToken.None);

                    var hasFirstObservation = firstContext.TryGetPortValue(
                        "observation", 0, out var firstObservationValue);
                    var hasSecondObservation = secondContext.TryGetPortValue(
                        "observation", 0, out var secondObservationValue);
                    var hasFirstPreview = firstContext.TryGetPortValue(
                        "preview", 0, out var firstPreviewValue);
                    var hasSecondPreview = secondContext.TryGetPortValue(
                        "preview", 0, out var secondPreviewValue);
                    var firstObservation = firstObservationValue as SessionObservation;
                    var secondObservation = secondObservationValue as SessionObservation;

                    return hasFirstObservation
                        && hasSecondObservation
                        && hasFirstPreview
                        && hasSecondPreview
                        && firstObservation != null
                        && secondObservation != null
                        && firstObservation.ImagePath == Path.GetFullPath(firstPath)
                        && secondObservation.ImagePath == Path.GetFullPath(secondPath)
                        && firstObservation.ImageDirectory
                            == Path.GetFullPath(fixture.DirectoryPath)
                        && secondObservation.ImageDirectory
                            == firstObservation.ImageDirectory
                        && firstObservation.Image.FrameId == 0
                        && secondObservation.Image.FrameId == 1
                        && ReferenceEquals(firstObservation.Image, firstPreviewValue)
                        && ReferenceEquals(secondObservation.Image, secondPreviewValue);
                }
                finally
                {
                    await session.StopAsync();
                }
            }));
    }

    private static bool ThrowsVirtualCamera<TException>(string sourcePath, Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException exception)
        {
            return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(sourcePath)
                    || exception.Message.Contains(sourcePath, StringComparison.Ordinal));
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
            return exception.Message.Contains("VirtualCamera", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(sourcePath)
                    || exception.Message.Contains(sourcePath, StringComparison.Ordinal));
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
        where T : class
    {
        return instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance) as T
            ?? throw new InvalidOperationException(
                $"Missing editor field '{fieldName}'.");
    }

    private static FlowNodeDefinition CreateSessionObservationDefinition()
    {
        return new FlowNodeDefinition
        {
            TypeKey = SessionObservationExecutor.FlowNodeTypeKey,
            DisplayName = "Session Observation",
            InputPorts =
            {
                new FlowPortDefinition
                {
                    Id = "image",
                    DisplayName = "Image",
                    IOType = EIOType.Input,
                    DataType = FlowDataType.Image,
                    PreferredDirection = EPortDirection.Left,
                    IsRequired = true,
                    Availability = FlowPortAvailability.Iteration,
                },
                new FlowPortDefinition
                {
                    Id = "imagePath",
                    DisplayName = "Image Path",
                    IOType = EIOType.Input,
                    DataType = FlowDataType.String,
                    PreferredDirection = EPortDirection.Left,
                    IsRequired = true,
                    Availability = FlowPortAvailability.Iteration,
                },
                new FlowPortDefinition
                {
                    Id = "imageDirectory",
                    DisplayName = "Image Directory",
                    IOType = EIOType.Input,
                    DataType = FlowDataType.String,
                    PreferredDirection = EPortDirection.Left,
                    IsRequired = true,
                    Availability = FlowPortAvailability.Session,
                },
            },
            OutputPorts =
            {
                new FlowPortDefinition
                {
                    Id = "observation",
                    DisplayName = "Observation",
                    IOType = EIOType.Output,
                    DataType = FlowDataType.Object,
                    PreferredDirection = EPortDirection.Right,
                    Availability = FlowPortAvailability.Iteration,
                },
            },
        };
    }

    private sealed class SessionObservation
    {
        internal SessionObservation(
            FlowImage image,
            string imagePath,
            string imageDirectory)
        {
            Image = image;
            ImagePath = imagePath;
            ImageDirectory = imageDirectory;
        }

        public FlowImage Image { get; }

        public string ImagePath { get; }

        public string ImageDirectory { get; }
    }

    private sealed class SessionObservationExecutor : IFlowNodeExecutor
    {
        internal const string FlowNodeTypeKey = "test.session-observation";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputs.TryGetValue("image", out var imageValue)
                || !(imageValue is FlowImage image)
                || !inputs.TryGetValue("imagePath", out var pathValue)
                || !(pathValue is string imagePath)
                || !inputs.TryGetValue("imageDirectory", out var directoryValue)
                || !(directoryValue is string imageDirectory))
            {
                throw new InvalidOperationException(
                    "SessionObservation requires image, imagePath, and imageDirectory inputs.");
            }

            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    ["observation"] = new SessionObservation(
                        image,
                        imagePath,
                        imageDirectory),
                });
        }
    }

    private sealed class VirtualCameraTestClock : IMonotonicClock
    {
        internal VirtualCameraTestClock(TimeSpan now = default)
        {
            Now = now;
        }

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
        IVirtualCameraImageLoader imageLoader = null!)
    {
        var timing = new VirtualCameraTestTiming();
        return new VirtualCameraExecutor(
            imageLoader,
            timing.Clock,
            timing.DelayAsync,
            () => DateTimeOffset.UnixEpoch);
    }

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

    private static FlowNodeSessionContext CreateVirtualCameraContext(
        string sourcePath,
        VirtualCameraLoadMode loadMode,
        int maxImages,
        long maxBytes,
        bool skipErrors,
        out WorkflowNode node,
        out FlowNodeDefinition definition,
        double? frameRate = null)
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
        if (frameRate.HasValue)
        {
            node.Inputs["frameRate"] = frameRate.Value;
        }

        definition = new FlowNodeDefinition
        {
            TypeKey = VirtualCameraNodeModel.FlowNodeTypeKey,
            DisplayName = "Virtual Camera",
            OutputPorts =
            {
                new FlowPortDefinition
                {
                    Id = "image",
                    DisplayName = "Image",
                    IOType = EIOType.Output,
                    DataType = FlowDataType.Image,
                    Availability = FlowPortAvailability.Iteration,
                },
                new FlowPortDefinition
                {
                    Id = "imagePath",
                    DisplayName = "Image Path",
                    IOType = EIOType.Output,
                    DataType = FlowDataType.String,
                    Availability = FlowPortAvailability.Iteration,
                },
                new FlowPortDefinition
                {
                    Id = "imageDirectory",
                    DisplayName = "Image Directory",
                    IOType = EIOType.Output,
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
        var executor = CreateVirtualCameraExecutor();
        var context = CreateVirtualCameraContext(
            sourcePath,
            loadMode,
            maxImages,
            maxBytes,
            skipErrors,
            out _,
            out _);
        try
        {
            await executor.StartSessionAsync(context, CancellationToken.None);
        }
        finally
        {
            await executor.StopSessionAsync(context, CancellationToken.None);
        }
    }

    private sealed class CancelOnLoadVirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly int _cancelOnLoad;

        internal CancelOnLoadVirtualCameraImageLoader(
            CancellationTokenSource cancellation,
            int cancelOnLoad)
        {
            _cancellation = cancellation;
            _cancelOnLoad = cancelOnLoad;
        }

        internal int LoadCount { get; private set; }

        public VirtualCameraImageTemplate Load(string path)
        {
            LoadCount++;
            if (LoadCount == _cancelOnLoad)
            {
                _cancellation.Cancel();
            }

            return new VirtualCameraImageTemplate(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3 });
        }
    }

    private sealed class RecordingVirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        private readonly VirtualCameraImageLoader _inner = new VirtualCameraImageLoader();

        internal List<string> Loads { get; } = new List<string>();

        internal int LoadCount => Loads.Count;

        public VirtualCameraImageTemplate Load(string path)
        {
            Loads.Add(path);
            return _inner.Load(path);
        }
    }

    private sealed class SelectiveVirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        private readonly string _badPath;

        internal SelectiveVirtualCameraImageLoader(string badPath)
        {
            _badPath = Path.GetFullPath(badPath);
        }

        internal List<string> Loads { get; } = new List<string>();

        public VirtualCameraImageTemplate Load(string path)
        {
            Loads.Add(path);
            if (string.Equals(Path.GetFullPath(path), _badPath, StringComparison.Ordinal))
            {
                throw new VirtualCameraImageLoadException(
                    path,
                    new InvalidDataException("bad image"));
            }

            return new VirtualCameraImageTemplate(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3 });
        }
    }

    private sealed class ThrowingVirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        private readonly Exception _exception;

        internal ThrowingVirtualCameraImageLoader(Exception exception)
        {
            _exception = exception;
        }

        public VirtualCameraImageTemplate Load(string path)
        {
            throw _exception;
        }
    }

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
                width,
                height,
                96,
                96,
                pixelFormat,
                null,
                pixels,
                stride);
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

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private static Task<T> RunOnStaValueAsync<T>(Func<T> action)
    {
        return RunOnStaAsync(() => Task.FromResult(action()));
    }

    private static Task<T> RunOnStaAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(async () =>
                {
                    try
                    {
                        completion.TrySetResult(await action());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                }));
            Dispatcher.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
