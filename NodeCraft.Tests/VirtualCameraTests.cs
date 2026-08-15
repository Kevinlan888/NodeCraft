using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Vision.Nodes;

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

        await RunAsync("virtual camera model defaults match builtin preload", async () =>
        {
            var node = new VirtualCameraNodeModel();
            return node.SourcePath == "builtin://vision/sample-set"
                && node.LoadMode == VirtualCameraLoadMode.Preload
                && node.MaxPreloadedImages == 100
                && node.MaxPreloadedBytes == 536870912L
                && !node.SkipErrorImages;
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
                && source.Entries[0].PreloadedImage == null;
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
            var checkerboard = collection.Entries[0].PreloadedImage;
            var colorBars = collection.Entries[1].PreloadedImage;
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
                && single.Entries[0].PreloadedImage.Width == 2
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
            })));

        await RunAsync("virtual camera wraps only expected image load failures", async () =>
        {
            using var fixture = new TemporaryVirtualCameraFiles();
            var missingPath = Path.Combine(fixture.DirectoryPath, "missing.png");
            var corruptPath = fixture.WriteImage("corrupt.png", new byte[] { 1, 2, 3 });
            var loader = new VirtualCameraImageLoader();
            try
            {
                loader.Load(missingPath, 0);
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
                loader.Load(corruptPath, 1);
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
                return (loader.Load(paths.jpg, 6), loader.Load(paths.bmp, 7));
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

                return (string)sessionOutputs["imageDirectory"] == Path.GetFullPath(fixture.DirectoryPath)
                    && ((FlowImage)first["image"]).FrameId == 0
                    && (string)first["imagePath"] == Path.Combine(fixture.DirectoryPath, "a.png")
                    && ((FlowImage)second["image"]).FrameId == 1
                    && ((FlowImage)wrapped["image"]).FrameId == 0
                    && ReferenceEquals(first["image"], wrapped["image"]);
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
                var executor = new VirtualCameraExecutor();
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

        await RunAsync("virtual camera reports distinct prepare and execute errors after stop", async () =>
        {
            var executor = new VirtualCameraExecutor();
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
                var executor = new VirtualCameraExecutor();
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
                var executor = new VirtualCameraExecutor();
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
                var executor = new VirtualCameraExecutor(loader);
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
                var executor = new VirtualCameraExecutor();
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
            var executor = new VirtualCameraExecutor(loader);
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
        var executor = new VirtualCameraExecutor();
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

        public FlowImage Load(string path, ulong frameId)
        {
            LoadCount++;
            if (LoadCount == _cancelOnLoad)
            {
                _cancellation.Cancel();
            }

            return FlowImage.CopyFrom(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3 },
                frameId,
                0,
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class RecordingVirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        internal int LoadCount { get; private set; }

        public FlowImage Load(string path, ulong frameId)
        {
            LoadCount++;
            return FlowImage.CopyFrom(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3 },
                frameId,
                0,
                DateTimeOffset.UtcNow);
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
