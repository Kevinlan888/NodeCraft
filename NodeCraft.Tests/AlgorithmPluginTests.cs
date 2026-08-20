using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Node.Algorithm.Nodes;
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
    }
}
