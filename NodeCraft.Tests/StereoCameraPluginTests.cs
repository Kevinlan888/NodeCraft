using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Nodes;
using NodeCraft.Vision.StereoCamera.Plugin;

internal static partial class Program
{
    private static async Task RunStereoCameraPluginTestsAsync()
    {
        await RunAsync("stereo camera plugin registers stable Vision node metadata and ports", async () =>
        {
            var plugin = new StereoCameraPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);

            var camera = context.Registrations.Single(registration =>
                registration.Definition.TypeKey == StereoCameraNodeModel.FlowNodeTypeKey);
            var preview = context.Registrations.Single(registration =>
                registration.Definition.TypeKey == FlowImagePreviewNodeModel.FlowNodeTypeKey);
            var cameraInputIds = camera.Definition.InputPorts.Select(port => port.Id).ToArray();
            var outputIds = camera.Definition.OutputPorts.Select(port => port.Id).ToArray();
            var outputTypes = camera.Definition.OutputPorts.Select(port => port.DataType).ToArray();
            var previewInput = preview.Definition.InputPorts.Single(port => port.Id == "image");
            var previewOutput = preview.Definition.OutputPorts.Single(port => port.Id == "image");
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);

            return plugin.Metadata.Id == "nodecraft.vision.stereo-camera"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && cameraInputIds.Length == 0
                && !cameraInputIds.Contains("ipAddress", StringComparer.Ordinal)
                && outputIds.SequenceEqual(new[] { "colorImage", "depthImage", "colorCalibration", "depthCalibration" })
                && outputTypes.SequenceEqual(new[]
                {
                    FlowDataType.Image,
                    FlowDataType.Image,
                    FlowDataType.CameraCalibration,
                    FlowDataType.CameraCalibration,
                })
                && previewInput.DataType == FlowDataType.Image
                && previewOutput.DataType == FlowDataType.Image
                && previewInput.IsRequired
                && registry.Contains(StereoCameraNodeModel.FlowNodeTypeKey)
                && registry.Contains(FlowImagePreviewNodeModel.FlowNodeTypeKey);
        });

        await RunAsync("stereo camera model persists only its IP address", async () =>
        {
            var node = new StereoCameraNodeModel { IpAddress = "192.168.1.10" };
            var workflow = new WorkflowDocument();
            var workflowNode = workflow.Nodes.AddAndReturn(new WorkflowNode
            {
                Id = node.Id,
                TypeKey = node.ExecutorType,
            });
            node.WriteWorkflowInputs(workflowNode);

            var path = Path.Combine(Path.GetTempPath(), "nodecraft-stereo-node-" + Guid.NewGuid().ToString("N") + ".flow.xml");
            try
            {
                GraphModelXmlSerializer.Save(
                    new GraphModel
                    {
                        Nodes = new List<NodeModel> { node },
                        Links = new List<GraphLink>(),
                    },
                    path);
                var xml = File.ReadAllText(path);
                return (node.InputParameters == null || node.InputParameters.Count == 0)
                    && workflowNode.Inputs.TryGetValue("ipAddress", out var ipAddress)
                    && Equals(ipAddress, "192.168.1.10")
                    && xml.Contains("Name=\"IpAddress\"", StringComparison.Ordinal)
                    && xml.Contains("192.168.1.10", StringComparison.Ordinal)
                    && !xml.Contains("CurrentImage", StringComparison.Ordinal)
                    && !xml.Contains("BitmapSource", StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        });

        await RunAsync("image preview runtime state is omitted from graph XML", async () =>
        {
            var node = new FlowImagePreviewNodeModel();
            node.SetStatusText("Depth16 2x1");
            node.SetCurrentImage(FlowImage.CopyFrom(
                2,
                1,
                4,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                new byte[] { 1, 0, 2, 0 },
                1,
                2,
                DateTimeOffset.UtcNow));
            var path = Path.Combine(Path.GetTempPath(), "nodecraft-preview-node-" + Guid.NewGuid().ToString("N") + ".flow.xml");
            try
            {
                GraphModelXmlSerializer.Save(
                    new GraphModel
                    {
                        Nodes = new List<NodeModel> { node },
                        Links = new List<GraphLink>(),
                    },
                    path);
                var xml = File.ReadAllText(path);
                return !xml.Contains("CurrentImage", StringComparison.Ordinal)
                    && !xml.Contains("StatusText", StringComparison.Ordinal)
                    && !xml.Contains("BitmapSource", StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(path);
            }
        });

        await RunAsync("image preview executor preserves FlowImage object identity", async () =>
        {
            var image = FlowImage.CopyFrom(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { 1, 2, 3 },
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = await new FlowImagePreviewExecutor().ExecuteAsync(
                new FlowExecutionContext(),
                new WorkflowNode { Id = "preview" },
                new FlowNodeDefinition(),
                new Dictionary<string, object> { ["image"] = image },
                CancellationToken.None);
            return ReferenceEquals(image, output["image"]);
        });

        await RunAsync("image preview registration preserves content and applies the image result", async () =>
        {
            var plugin = new StereoCameraPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);
            var preview = context.Registrations.Single(registration =>
                registration.Definition.TypeKey == FlowImagePreviewNodeModel.FlowNodeTypeKey);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
            var node = new FlowImagePreviewNodeModel();
            var image = FlowImage.CopyFrom(
                1,
                1,
                1,
                FlowPixelFormat.Mono8,
                FlowImageKind.Color,
                new byte[] { 3 },
                1,
                2,
                DateTimeOffset.UtcNow);
            var execution = new FlowExecutionContext();
            execution.SetPortValue(node.Id, 0, image);
            registry.ApplyExecutionResults(new[] { node }, execution);
            return !registry.ShouldRefreshContentAfterExecution(node)
                && ReferenceEquals(node.CurrentImage, image)
                && preview.ExecutionResultHandler != null;
        });
    }

}

internal static class WorkflowNodeListExtensions
{
    internal static T AddAndReturn<T>(this IList<T> list, T item)
    {
        list.Add(item);
        return item;
    }
}
