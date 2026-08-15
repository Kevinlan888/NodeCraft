using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
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

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
