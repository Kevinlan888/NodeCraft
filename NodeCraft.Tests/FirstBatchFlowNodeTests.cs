using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Plugin;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunFirstBatchFlowNodeTestsAsync()
    {
        Run("First batch registrations expose stable keys and dynamic port contracts", () =>
        {
            var registrations = StageBuiltInPlugin(out _);
            var firstBatch = registrations
                .Where(registration =>
                    registration.Definition.TypeKey == ToStringNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == StringConcatNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == NotEqualNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == GreaterThanOrEqualNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == LessThanOrEqualNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == SelectNodeModel.FlowNodeTypeKey
                    || registration.Definition.TypeKey == MergeFlowNodeModel.FlowNodeTypeKey)
                .ToArray();
            var concat = firstBatch.Single(item => item.Definition.TypeKey == StringConcatNodeModel.FlowNodeTypeKey);
            var merge = firstBatch.Single(item => item.Definition.TypeKey == MergeFlowNodeModel.FlowNodeTypeKey);
            var concatTemplate = concat.Definition.DynamicInputTemplate;
            var mergeTemplate = merge.Definition.DynamicInputTemplate;

            return firstBatch.Length == 7
                && firstBatch.Select(item => item.Definition.TypeKey).Distinct(StringComparer.Ordinal).Count() == 7
                && concat.Definition.Category == "Preview"
                && concatTemplate != null
                && concatTemplate.PortIdPrefix == "input"
                && concatTemplate.DataType.Equals(FlowDataType.String)
                && concatTemplate.IsRequired
                && concatTemplate.MinCount == 2
                && concatTemplate.InitialCount == 2
                && concatTemplate.MaxCount == null
                && merge.Definition.Category == "Logic"
                && merge.Definition.OutputPorts.Count == 1
                && merge.Definition.OutputPorts[0].Id == BuiltInPortIds.FlowOut
                && merge.Definition.OutputPorts[0].DataType.Equals(FlowDataType.Control)
                && mergeTemplate != null
                && mergeTemplate.PortIdPrefix == "branch"
                && mergeTemplate.DataType.Equals(FlowDataType.Control)
                && !mergeTemplate.IsRequired
                && mergeTemplate.MinCount == 2
                && mergeTemplate.InitialCount == 2
                && mergeTemplate.MaxCount == null;
        });

        await RunAsync("First batch executors preserve conversion, comparison, selection, and cancellation semantics", async () =>
        {
            var context = new FlowExecutionContext();
            var definition = new FlowNodeDefinition();
            var workflowNode = new WorkflowNode { Id = "first-batch" };
            var probe = new ToStringProbe();
            var toString = new ToStringExecutor();
            var nullText = await toString.ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None);
            var stringText = await toString.ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object> { [BuiltInPortIds.Input] = "hello" },
                CancellationToken.None);
            var numberText = await toString.ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object> { [BuiltInPortIds.Input] = 1.5d },
                CancellationToken.None);
            var objectText = await toString.ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object> { [BuiltInPortIds.Input] = probe },
                CancellationToken.None);

            var concatDefinition = FirstBatchDynamicDefinition(
                "input",
                FlowDataType.String,
                isRequired: true,
                "input_1",
                "input_2",
                "input_3");
            var concatNode = new WorkflowNode
            {
                Id = "concat",
                Inputs =
                {
                    [BuiltInPortIds.Separator] = "|",
                },
            };
            var concat = await new StringConcatExecutor().ExecuteAsync(
                context,
                concatNode,
                concatDefinition,
                new Dictionary<string, object>
                {
                    ["input_1"] = "A",
                    ["input_2"] = "B",
                    ["input_3"] = "C",
                },
                CancellationToken.None);

            var notEqual = await new NotEqualExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.InputA] = "left",
                    [BuiltInPortIds.InputB] = "right",
                },
                CancellationToken.None);
            var notEqualSame = await new NotEqualExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.InputA] = "same",
                    [BuiltInPortIds.InputB] = "same",
                },
                CancellationToken.None);
            var greaterOrEqual = await new GreaterThanOrEqualExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.InputA] = "3.5",
                    [BuiltInPortIds.InputB] = 3.5d,
                },
                CancellationToken.None);
            var lessOrEqual = await new LessThanOrEqualExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.InputA] = 2,
                    [BuiltInPortIds.InputB] = 3,
                },
                CancellationToken.None);
            var missingNumber = await new GreaterThanOrEqualExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None);

            var selectedNull = await new SelectExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.Condition] = true,
                    [BuiltInPortIds.TrueValue] = null!,
                    [BuiltInPortIds.FalseValue] = "fallback",
                },
                CancellationToken.None);
            var selectedFalse = await new SelectExecutor().ExecuteAsync(
                context,
                workflowNode,
                definition,
                new Dictionary<string, object>
                {
                    [BuiltInPortIds.Condition] = false,
                    [BuiltInPortIds.TrueValue] = "true",
                    [BuiltInPortIds.FalseValue] = 0,
                },
                CancellationToken.None);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancellationObserved = Throws<OperationCanceledException>(() =>
                new ToStringExecutor().ExecuteAsync(
                    context,
                    workflowNode,
                    definition,
                    new Dictionary<string, object>(),
                    cancelled.Token).GetAwaiter().GetResult());

            return Equals(nullText[BuiltInPortIds.Output], string.Empty)
                && Equals(stringText[BuiltInPortIds.Output], "hello")
                && Equals(numberText[BuiltInPortIds.Output], "1.5")
                && Equals(objectText[BuiltInPortIds.Output], "probe")
                && Equals(concat[BuiltInPortIds.Output], "A|B|C")
                && Equals(notEqual[BuiltInPortIds.Output], true)
                && Equals(notEqualSame[BuiltInPortIds.Output], false)
                && Equals(greaterOrEqual[BuiltInPortIds.Output], true)
                && Equals(lessOrEqual[BuiltInPortIds.Output], true)
                && Equals(missingNumber[BuiltInPortIds.Output], true)
                && selectedNull.ContainsKey(BuiltInPortIds.Output)
                && selectedNull[BuiltInPortIds.Output] == null
                && Equals(selectedFalse[BuiltInPortIds.Output], 0)
                && cancellationObserved;
        });

        await RunAsync("Merge Flow forwards one active branch and suppresses empty flow", async () =>
        {
            var context = new FlowExecutionContext();
            var definition = FirstBatchDynamicDefinition(
                "branch",
                FlowDataType.Control,
                isRequired: false,
                "branch_1",
                "branch_2",
                "branch_3");
            var node = new WorkflowNode { Id = "merge" };
            var active = await new MergeFlowExecutor().ExecuteAsync(
                context,
                node,
                definition,
                new Dictionary<string, object>
                {
                    ["branch_1"] = FlowControlSignal.Active,
                    ["branch_2"] = FlowControlSignal.Active,
                },
                CancellationToken.None);
            var empty = await new MergeFlowExecutor().ExecuteAsync(
                context,
                node,
                definition,
                new Dictionary<string, object>
                {
                },
                CancellationToken.None);

            return active.Count == 1
                && Equals(active[BuiltInPortIds.FlowOut], FlowControlSignal.Active)
                && empty.Count == 0;
        });

        Run("String Concat editor persists the separator and notifies the graph", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out var registry);
            var node = new StringConcatNodeModel { Id = "concat-editor" };
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(node);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var separator = FindLogicalDescendants<TextBox>(view)
                .Single(textBox => textBox.Name == "SeparatorEditor");
            separator.Text = ",";

            return registrations.Any(item => item.Definition.TypeKey == StringConcatNodeModel.FlowNodeTypeKey)
                && node.Separator == ","
                && changes == 1;
        }));

        await RunAsync("Merge Flow gates a downstream node by active control branches", async () =>
        {
            var registrations = StageBuiltInPlugin(out var plugin);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, registrations);
            var activeContext = await new GraphExecutor(
                CreateMergeFlowWorkflow(firstCondition: true, secondCondition: false),
                registry).ExecuteAsync();
            var emptyContext = await new GraphExecutor(
                CreateMergeFlowWorkflow(firstCondition: false, secondCondition: false),
                registry).ExecuteAsync();

            return activeContext.Statuses["merge"] == FlowNodeExecutionStatus.Succeeded
                && activeContext.Statuses["downstream"] == FlowNodeExecutionStatus.Succeeded
                && emptyContext.Statuses["merge"] == FlowNodeExecutionStatus.Skipped
                && emptyContext.Statuses["downstream"] == FlowNodeExecutionStatus.Skipped;
        });
    }

    private static FlowNodeDefinition FirstBatchDynamicDefinition(
        string prefix,
        FlowDataType dataType,
        bool isRequired,
        params string[] portIds)
    {
        var definition = new FlowNodeDefinition();
        foreach (var portId in portIds)
        {
            definition.InputPorts.Add(new FlowPortDefinition
            {
                Id = portId,
                DisplayName = portId,
                IOType = EIOType.Input,
                DataType = dataType,
                PreferredDirection = EPortDirection.Left,
                IsRequired = isRequired,
                IsDynamic = true,
            });
        }

        return definition;
    }

    private static WorkflowDocument CreateMergeFlowWorkflow(
        bool firstCondition,
        bool secondCondition)
    {
        var first = FirstBatchWorkflowNode(
            "first-if",
            "nodecraft.builtin.if",
            ("condition", firstCondition));
        var second = FirstBatchWorkflowNode(
            "second-if",
            "nodecraft.builtin.if",
            ("condition", secondCondition));
        var merge = FirstBatchWorkflowNode(
            "merge",
            MergeFlowNodeModel.FlowNodeTypeKey,
            ("branch_1", new LinkRef { SourceNodeId = first.Id, SourceSlot = 0 }),
            ("branch_2", new LinkRef { SourceNodeId = second.Id, SourceSlot = 0 }));
        merge.DynamicInputPortIds.Add("branch_1");
        merge.DynamicInputPortIds.Add("branch_2");
        var downstream = FirstBatchWorkflowNode(
            "downstream",
            "nodecraft.builtin.string-value",
            ("value", "done"),
            (FlowPorts.FlowIn, new LinkRef { SourceNodeId = merge.Id, SourceSlot = 0 }));

        return new WorkflowDocument
        {
            Nodes = new List<WorkflowNode> { first, second, merge, downstream },
        };
    }

    private static WorkflowNode FirstBatchWorkflowNode(
        string id,
        string typeKey,
        params (string PortId, object Value)[] inputs)
    {
        var node = new WorkflowNode
        {
            Id = id,
            TypeKey = typeKey,
            DisplayName = id,
        };
        foreach (var input in inputs)
        {
            node.Inputs[input.PortId] = input.Value;
        }

        return node;
    }

    private sealed class ToStringProbe
    {
        public override string ToString()
        {
            return "probe";
        }
    }
}
