using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Node.Algorithm.Interop;
using Node.Algorithm.Nodes;
using Node.Algorithm.Plugin;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RunAlgorithmPluginTests()
    {
        Run("Node.Algorithm manifest has a stable plugin identity", () =>
        {
            var manifestPath = FindRepositoryFile("Node.Algorithm", "plugin.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            return root.GetProperty("id").GetString() == "nodecraft.algorithm"
                && root.GetProperty("entryAssembly").GetString() == "Node.Algorithm.dll"
                && root.GetProperty("entryType").GetString() == "Node.Algorithm.Plugin.AlgorithmPlugin"
                && root.GetProperty("apiVersion").GetString() == "1.0"
                && root.GetProperty("privateLibraryPath").GetString() == "lib";
        });

        Run("Waybill NodeModel exposes image, count, detections and annotated image", () =>
        {
            var node = new WaybillRecognizerNodeModel();
            return node.ExecutorType == WaybillRecognizerNodeModel.FlowNodeTypeKey
                && node.InputParameters.Single().PortId == "image"
                && node.InputParameters.Single().Parameter.ParameterType == FlowDataType.Image.Key
                && node.OutputParameters.Select(parameter => parameter.PortId)
                    .SequenceEqual(new[] { "count", "detections", "annotatedImage" })
                && node.OutputParameters[0].Parameter.ParameterType == FlowDataType.Number.Key
                && node.OutputParameters[1].Parameter.ParameterType == FlowDataType.Object.Key
                && node.OutputParameters[2].Parameter.ParameterType == FlowDataType.Image.Key;
        });

        Run("Algorithm plugin registers the Waybill Recognizer node", () =>
        {
            var plugin = AlgorithmPlugin.CreateForTesting(
                new RecordingWaybillSessionFactory(null!),
                Path.Combine(Path.GetTempPath(), "Node.Algorithm.dll"));
            var context = new PluginRegistrationContext(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                new Version(1, 0));
            plugin.Register(context);
            var registration = context.Registrations.Single();

            return plugin.Metadata.Id == "nodecraft.algorithm"
                && plugin.Metadata.DisplayName == "Algorithm"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && registration.Definition.TypeKey == WaybillRecognizerNodeModel.FlowNodeTypeKey
                && registration.Definition.Category == "Algorithm"
                && registration.Definition.InputPorts.Any(port =>
                    port.Id == "image"
                    && port.DataType == FlowDataType.Image
                    && port.IsRequired)
                && registration.Definition.OutputPorts.Select(port => port.Id)
                    .SequenceEqual(new[] { "count", "detections", "annotatedImage" })
                && registration.NodeModelType == typeof(WaybillRecognizerNodeModel)
                && registration.NodeFactory() is WaybillRecognizerNodeModel
                && registration.PaletteDisplayName == "Waybill Recognizer";
        });
    }
}
