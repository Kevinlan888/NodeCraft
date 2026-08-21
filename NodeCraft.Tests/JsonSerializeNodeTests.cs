using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunJsonSerializeNodeTestsAsync()
    {
        Run("JSON Serialize exposes an object-to-string palette node", () =>
        {
            const string typeKey = "node.json-serialize";
            if (!NodeExecutorFactory.Registry.TryResolve(typeKey, out var registration)
                || !NodeExecutorFactory.Registry.TryCreateNodeByTypeKey(typeKey, out var node))
            {
                return false;
            }

            var input = registration.Definition.InputPorts.SingleOrDefault(port => port.Id == "input");
            var output = registration.Definition.OutputPorts.SingleOrDefault(port => port.Id == "output");
            var modelInput = node.InputParameters.SingleOrDefault(port => port.PortId == "input");
            var modelOutput = node.OutputParameters.SingleOrDefault(port => port.PortId == "output");

            return registration.Definition.DisplayName == "JSON Serialize"
                && registration.Definition.Category == "Preview"
                && registration.ShowInPalette
                && input != null
                && input.IsRequired
                && input.DataType.Equals(FlowDataType.Object)
                && output != null
                && output.DataType.Equals(FlowDataType.String)
                && node.ExecutorType == typeKey
                && modelInput?.Parameter?.ParameterType == FlowDataType.Object.Key
                && modelOutput?.Parameter?.ParameterType == FlowDataType.String.Key;
        });

        Run("JSON Serialize renders an operation summary instead of output-node text", () =>
            RunOnSta(() =>
            {
                var canvas = CreateHeadlessCanvas();
                if (!NodeExecutorFactory.Registry.TryCreateNodeByTypeKey(
                    "node.json-serialize",
                    out var node))
                {
                    return false;
                }

                canvas.GraphModel.Nodes.Add(node);
                var content = (System.Windows.FrameworkElement)NodeExecutorFactory.Registry
                    .BuildNodeContent(canvas, node);
                var labels = FindLogicalDescendants<System.Windows.Controls.TextBlock>(content)
                    .Select(textBlock => textBlock.Text)
                    .ToList();

                return labels.Contains("JSON", StringComparer.Ordinal)
                    && labels.Contains("将任意输入格式化为多行 JSON", StringComparer.Ordinal)
                    && !labels.Contains("Output node", StringComparer.Ordinal);
            }));

        await RunAsync("JSON Serialize feeds indented runtime JSON into Text Preview", async () =>
        {
            const string typeKey = "node.json-serialize";
            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "json",
                TypeKey = typeKey,
                Inputs =
                {
                    ["input"] = new Dictionary<string, object>
                    {
                        ["name"] = "NodeCraft",
                        ["values"] = new object?[] { 1, true, null },
                    },
                },
            });
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "preview",
                TypeKey = "node.text-preview",
                Inputs =
                {
                    ["input"] = new LinkRef
                    {
                        SourceNodeId = "json",
                        SourceSlot = 0,
                    },
                },
            });
            var newline = Environment.NewLine;
            var expected = "{" + newline
                + "  \"name\": \"NodeCraft\"," + newline
                + "  \"values\": [" + newline
                + "    1," + newline
                + "    true," + newline
                + "    null" + newline
                + "  ]" + newline
                + "}";

            var executor = new GraphExecutor(workflow);
            if (!executor.Validate().IsValid)
            {
                throw new InvalidOperationException("JSON Serialize graph did not validate.");
            }

            var context = await executor.ExecuteAsync();

            if (!context.TryGetPortValue("json", 0, out var value) || value is not string json)
            {
                throw new InvalidOperationException("JSON Serialize did not return a string output.");
            }

            if (json != expected)
            {
                var visibleJson = json.Replace("\r", "\\r").Replace("\n", "\\n");
                throw new InvalidOperationException("Unexpected JSON output: " + visibleJson);
            }

            return context.TryGetPortValue("preview", 0, out var previewValue)
                && Equals(previewValue, expected);
        });
    }
}
