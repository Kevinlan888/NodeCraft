using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Plugin;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunBuiltInLogicNodeTestsAsync()
    {
        Run("BuiltIn Logic completes the exact eighteen-node plugin order", () =>
        {
            var plugin = new BuiltInPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);
            var expectedTypeKeys = new[]
            {
                "nodecraft.builtin.string-value",
                "nodecraft.builtin.append-text",
                "nodecraft.builtin.text-preview",
                "nodecraft.builtin.json-serialize",
                "nodecraft.builtin.integer-value",
                "nodecraft.builtin.float-value",
                "nodecraft.builtin.boolean-value",
                "nodecraft.builtin.add-number",
                "nodecraft.builtin.multiply-number",
                "nodecraft.builtin.subtract-number",
                "nodecraft.builtin.divide-number",
                "nodecraft.builtin.greater-than",
                "nodecraft.builtin.less-than",
                "nodecraft.builtin.equal",
                "nodecraft.builtin.boolean-and",
                "nodecraft.builtin.boolean-or",
                "nodecraft.builtin.boolean-not",
                "nodecraft.builtin.if",
            };

            return context.Registrations.Count == 18
                && context.Registrations.Select(item => item.Definition.TypeKey)
                    .SequenceEqual(expectedTypeKeys, StringComparer.Ordinal);
        });

        Run("BuiltIn Logic registrations preserve seven exact definitions and presentation contracts", () =>
        {
            var registrations = CreateBuiltInLogicRegistry(out _);
            var expected = new[]
            {
                new LogicContract("nodecraft.builtin.greater-than", "Greater Than", "A > B", typeof(GreaterThanNodeModel), typeof(GreaterThanExecutor), new[] { FlowDataType.Number, FlowDataType.Number }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.less-than", "Less Than", "A < B", typeof(LessThanNodeModel), typeof(LessThanExecutor), new[] { FlowDataType.Number, FlowDataType.Number }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.equal", "Equal", "A == B", typeof(EqualNodeModel), typeof(EqualExecutor), new[] { FlowDataType.Object, FlowDataType.Object }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.boolean-and", "Boolean And", "A && B", typeof(BooleanAndNodeModel), typeof(BooleanAndExecutor), new[] { FlowDataType.Boolean, FlowDataType.Boolean }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.boolean-or", "Boolean Or", "A || B", typeof(BooleanOrNodeModel), typeof(BooleanOrExecutor), new[] { FlowDataType.Boolean, FlowDataType.Boolean }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.boolean-not", "Boolean Not", "!A", typeof(BooleanNotNodeModel), typeof(BooleanNotExecutor), new[] { FlowDataType.Boolean }, new[] { new LogicPortContract("output", "Result", FlowDataType.Boolean, false) }),
                new LogicContract("nodecraft.builtin.if", "If", "condition ? true : false", typeof(IfNodeModel), typeof(IfExecutor), new[] { FlowDataType.Boolean }, new[]
                {
                    new LogicPortContract("true", "True", FlowDataType.Control, false),
                    new LogicPortContract("false", "False", FlowDataType.Control, false),
                }),
            };

            return registrations.Length == expected.Length
                && registrations.Zip(expected, LogicRegistrationMatches).All(value => value);
        });

        Run("BuiltIn Logic models own exact plugin ports and identities", () =>
        {
            var models = new NodeModel[]
            {
                new GreaterThanNodeModel(),
                new LessThanNodeModel(),
                new EqualNodeModel(),
                new BooleanAndNodeModel(),
                new BooleanOrNodeModel(),
                new BooleanNotNodeModel(),
                new IfNodeModel(),
            };
            var expected = new[]
            {
                ("nodecraft.builtin.greater-than", "Greater Than", new[] { "inputA", "inputB" }, new[] { "output" }),
                ("nodecraft.builtin.less-than", "Less Than", new[] { "inputA", "inputB" }, new[] { "output" }),
                ("nodecraft.builtin.equal", "Equal", new[] { "inputA", "inputB" }, new[] { "output" }),
                ("nodecraft.builtin.boolean-and", "Boolean And", new[] { "inputA", "inputB" }, new[] { "output" }),
                ("nodecraft.builtin.boolean-or", "Boolean Or", new[] { "inputA", "inputB" }, new[] { "output" }),
                ("nodecraft.builtin.boolean-not", "Boolean Not", new[] { "input" }, new[] { "output" }),
                ("nodecraft.builtin.if", "If", new[] { "condition" }, new[] { "true", "false" }),
            };

            return models.Zip(expected, (model, contract) =>
                model.ExecutorType == contract.Item1
                && model.Name == contract.Item2
                && model.InputParameters.Select(port => port.PortId)
                    .SequenceEqual(contract.Item3, StringComparer.Ordinal)
                && model.OutputParameters.Select(port => port.PortId)
                    .SequenceEqual(contract.Item4, StringComparer.Ordinal))
                .All(value => value);
        });

        Run("BuiltIn Logic factories return fresh exact models executors and XAML views", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInLogicRegistry(out var registry);
            var expectedViews = new[]
            {
                typeof(GreaterThanView),
                typeof(LessThanView),
                typeof(EqualView),
                typeof(BooleanAndView),
                typeof(BooleanOrView),
                typeof(BooleanNotView),
                typeof(IfView),
            };

            return registrations.Zip(expectedViews, (registration, viewType) =>
            {
                var firstNode = registration.NodeFactory();
                var secondNode = registration.NodeFactory();
                var firstExecutor = registration.ExecutorFactory();
                var secondExecutor = registration.ExecutorFactory();
                var canvas = CreateHeadlessCanvas();
                canvas.GraphModel.Nodes.Add(firstNode);
                var firstView = registry.BuildNodeContent(canvas, firstNode);
                var secondView = registry.BuildNodeContent(canvas, firstNode);
                return firstNode.GetType() == registration.NodeModelType
                    && secondNode.GetType() == registration.NodeModelType
                    && !ReferenceEquals(firstNode, secondNode)
                    && firstExecutor.GetType() == secondExecutor.GetType()
                    && !ReferenceEquals(firstExecutor, secondExecutor)
                    && firstView?.GetType() == viewType
                    && secondView?.GetType() == viewType
                    && !ReferenceEquals(firstView, secondView);
            }).All(value => value);
        }));

        Run("BuiltIn Logic factories strictly reject every other concrete model including subclasses", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInLogicRegistry(out var registry);
            var models = registrations.Select(item => item.NodeFactory()).ToArray();
            var canvas = CreateHeadlessCanvas();
            var registeredNode = registrations[0].NodeFactory();
            canvas.GraphModel.Nodes.Add(registeredNode);
            registry.BuildNodeContent(canvas, registeredNode);

            return registrations.All(registration => models
                .Where(model => model.GetType() != registration.NodeModelType)
                .All(model =>
                {
                    try
                    {
                        registration.ContentFactory(canvas, model);
                        return false;
                    }
                    catch (InvalidOperationException exception)
                    {
                        return exception.Message.Contains(
                            registration.NodeModelType.Name,
                            StringComparison.Ordinal);
                    }
                }));
        }));

        Run("BuiltIn Logic binary view reuses atomic slot swap behavior", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInLogicRegistry(out var registry);
            var sourceA = new IntegerValueNodeModel { Id = "logic-a", Name = "Left" };
            var sourceB = new IntegerValueNodeModel { Id = "logic-b", Name = "Right" };
            var target = new GreaterThanNodeModel { Id = "logic-target" };
            target.InputParameters.Reverse();
            var linkA = new GraphLink
            {
                Id = "logic-a-target",
                OriginNodeId = sourceA.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            var linkB = new GraphLink
            {
                Id = "logic-b-target",
                OriginNodeId = sourceB.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 2,
            };
            target.InputParameters.Single(port => port.PortId == "inputA").LinkId = linkA.Id;
            target.InputParameters.Single(port => port.PortId == "inputB").LinkId = linkB.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(sourceA);
            canvas.GraphModel.Nodes.Add(sourceB);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(linkA);
            canvas.GraphModel.Links.Add(linkB);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;

            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var summaries = FindLogicalDescendants<TextBlock>(view).Select(text => text.Text).ToArray();
            var swap = FindLogicalDescendants<Button>(view).Single();
            swap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            return registrations.Any(item => item.Definition.TypeKey == GreaterThanNodeModel.FlowNodeTypeKey)
                && summaries.Contains("Left · Value", StringComparer.Ordinal)
                && summaries.Contains("Right · Value", StringComparer.Ordinal)
                && Equals(swap.Content, "Swap A/B")
                && linkA.TargetSlot == 2
                && linkB.TargetSlot == 1
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == linkB.Id
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == linkA.Id
                && changes == 1;
        }));

        Run("BuiltIn Boolean Not view summarizes its unary source", () => RunOnSta(() =>
        {
            CreateBuiltInLogicRegistry(out var registry);
            var source = new BooleanValueNodeModel { Id = "boolean-source", Name = "Flag" };
            var target = new BooleanNotNodeModel { Id = "boolean-not" };
            var link = new GraphLink
            {
                Id = "boolean-source-not",
                OriginNodeId = source.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            target.InputParameters.Single().LinkId = link.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(source);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(link);

            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            return FindLogicalDescendants<TextBlock>(view)
                .Any(text => text.Text == "Flag · Value");
        }));

        Run("BuiltIn If XAML resolves localized branch labels and themed foregrounds", () => RunOnSta(() =>
        {
            CreateBuiltInLogicRegistry(out var registry);
            var canvas = CreateHeadlessCanvas();
            var node = new IfNodeModel { Id = "if-view" };
            canvas.GraphModel.Nodes.Add(node);
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, node);

            return RunWithThemedWindow(window =>
            {
                window.Content = view;
                window.UpdateLayout();
                var labels = FindLogicalDescendants<TextBlock>(view).ToArray();
                var trueLabel = labels.Single(item => item.Name == "TrueLabel");
                var falseLabel = labels.Single(item => item.Name == "FalseLabel");
                return labels.Any(item => item.Text == "IF" && item.Name == "IF")
                    && !string.IsNullOrWhiteSpace(trueLabel.Text)
                    && !string.IsNullOrWhiteSpace(falseLabel.Text)
                    && trueLabel.Foreground != null
                    && falseLabel.Foreground != null
                    && !ReferenceEquals(trueLabel.Foreground, falseLabel.Foreground);
            });
        }));

        Run("BuiltIn Logic views are seven independent embedded XAML resources with complete policy", () =>
        {
            var project = XDocument.Load(FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj"));
            var resources = typeof(GreaterThanView).Assembly.GetManifestResourceNames();
            var binary = new[]
            {
                new LogicViewContract("GreaterThanView", "A > B", "当 A 大于 B 时输出 true"),
                new LogicViewContract("LessThanView", "A &lt; B", "当 A 小于 B 时输出 true"),
                new LogicViewContract("EqualView", "A == B", "当 A 与 B 相等时输出 true"),
                new LogicViewContract("BooleanAndView", "A &amp;&amp; B", "仅当 A 与 B 都为 true 时输出 true"),
                new LogicViewContract("BooleanOrView", "A || B", "当 A 或 B 任一为 true 时输出 true"),
            };
            var allViews = binary.Select(item => item.ViewName)
                .Concat(new[] { "BooleanNotView", "IfView" })
                .ToArray();
            var forbidden = new[]
            {
                "new StackPanel", "new TextBlock", "new TextBox", "new Button",
                "new Border", "new Grid", "new SolidColorBrush",
            };

            return binary.All(view =>
                LogicViewIsEmbedded(project, resources, view.ViewName)
                && ReadBuiltInView(view.ViewName).Contains("Text=\"" + view.Formula + "\"", StringComparison.Ordinal)
                && ReadBuiltInView(view.ViewName).Contains("Text=\"" + view.Description + "\"", StringComparison.Ordinal)
                && ReadBuiltInView(view.ViewName).Contains("x:Name=\"InputAValue\"", StringComparison.Ordinal)
                && ReadBuiltInView(view.ViewName).Contains("x:Name=\"InputBValue\"", StringComparison.Ordinal)
                && ReadBuiltInView(view.ViewName).Contains("x:Name=\"SwapInputsButton\"", StringComparison.Ordinal))
                && LogicViewIsEmbedded(project, resources, "BooleanNotView")
                && ReadBuiltInView("BooleanNotView").Contains("Text=\"!A\"", StringComparison.Ordinal)
                && ReadBuiltInView("BooleanNotView").Contains("Text=\"反转布尔输入\"", StringComparison.Ordinal)
                && ReadBuiltInView("BooleanNotView").Contains("x:Name=\"InputValue\"", StringComparison.Ordinal)
                && LogicViewIsEmbedded(project, resources, "IfView")
                && ReadBuiltInView("IfView").Contains("x:Name=\"IF\"", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("x:Name=\"TrueLabel\"", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("x:Name=\"FalseLabel\"", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("{l:Loc FlowPort_true}", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("{l:Loc FlowPort_false}", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("{DynamicResource colorStatusSuccessForeground1}", StringComparison.Ordinal)
                && ReadBuiltInView("IfView").Contains("{DynamicResource colorStatusDangerForeground1}", StringComparison.Ordinal)
                && allViews.All(view =>
                {
                    var xaml = ReadBuiltInView(view);
                    var code = ReadBuiltInViewCode(view);
                    return xaml.Contains("TextWrapping=\"Wrap\"", StringComparison.Ordinal)
                        && xaml.Contains("Margin=", StringComparison.Ordinal)
                        && xaml.Contains("DynamicResource", StringComparison.Ordinal)
                        && !xaml.Contains("#", StringComparison.Ordinal)
                        && forbidden.All(text => !code.Contains(text, StringComparison.Ordinal));
                });
        });

        await RunAsync("BuiltIn Logic executors preserve conversions equality branches and cancellation", async () =>
        {
            var execution = new FlowExecutionContext();
            var workflowNode = new WorkflowNode();
            var definition = new FlowNodeDefinition();
            var greater = await new GreaterThanExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = "3.5", ["inputB"] = true }, CancellationToken.None);
            var less = await new LessThanExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = 2, ["inputB"] = "3.5" }, CancellationToken.None);
            var equal = await new EqualExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object>
            {
                ["inputA"] = new string(new[] { 'v', 'a', 'l', 'u', 'e' }),
                ["inputB"] = new string(new[] { 'v', 'a', 'l', 'u', 'e' }),
            }, CancellationToken.None);
            var unequal = await new EqualExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = "left", ["inputB"] = "right" }, CancellationToken.None);
            var and = await new BooleanAndExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = "true", ["inputB"] = 1 }, CancellationToken.None);
            var andFalse = await new BooleanAndExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = true, ["inputB"] = false }, CancellationToken.None);
            var or = await new BooleanOrExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["inputA"] = false, ["inputB"] = "true" }, CancellationToken.None);
            var not = await new BooleanNotExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["input"] = 0 }, CancellationToken.None);
            var ifTrue = await new IfExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["condition"] = "true" }, CancellationToken.None);
            var ifFalse = await new IfExecutor().ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object> { ["condition"] = 0 }, CancellationToken.None);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var executors = new IFlowNodeExecutor[]
            {
                new GreaterThanExecutor(), new LessThanExecutor(), new EqualExecutor(),
                new BooleanAndExecutor(), new BooleanOrExecutor(), new BooleanNotExecutor(), new IfExecutor(),
            };

            return Equals(greater["output"], true)
                && Equals(less["output"], true)
                && Equals(equal["output"], true)
                && Equals(unequal["output"], false)
                && Equals(and["output"], true)
                && Equals(andFalse["output"], false)
                && Equals(or["output"], true)
                && Equals(not["output"], true)
                && ifTrue.Count == 1 && Equals(ifTrue["true"], FlowControlSignal.Active)
                && ifFalse.Count == 1 && Equals(ifFalse["false"], FlowControlSignal.Active)
                && executors.All(executor => Throws<OperationCanceledException>(() =>
                    executor.ExecuteAsync(execution, workflowNode, definition, new Dictionary<string, object>(), cancelled.Token)
                        .GetAwaiter().GetResult()));
        });

        await RunAsync("BuiltIn Logic representative workflows execute through new keys and local registry", async () =>
        {
            CreateBuiltInLogicRegistry(out var registry);
            var workflow = new WorkflowDocument
            {
                Nodes = new List<WorkflowNode>
                {
                    LogicNode("greater", "nodecraft.builtin.greater-than", ("inputA", 9), ("inputB", 4)),
                    LogicNode("less", "nodecraft.builtin.less-than", ("inputA", 2), ("inputB", 3)),
                    LogicNode("equal", "nodecraft.builtin.equal", ("inputA", "same"), ("inputB", "same")),
                    LogicNode("and", "nodecraft.builtin.boolean-and", ("inputA", true), ("inputB", "true")),
                    LogicNode("or", "nodecraft.builtin.boolean-or", ("inputA", false), ("inputB", 1)),
                    LogicNode("not", "nodecraft.builtin.boolean-not", ("input", false)),
                },
            };
            var context = await new GraphExecutor(workflow, registry).ExecuteAsync();
            var ordinaryMatches = workflow.Nodes.All(node =>
                context.Statuses[node.Id] == FlowNodeExecutionStatus.Succeeded
                && context.TryGetPortValue(node.Id, 0, out var output)
                && Equals(output, true));
            var trueContext = await new GraphExecutor(BuildLogicIfWorkflow(true), registry).ExecuteAsync();
            var falseContext = await new GraphExecutor(BuildLogicIfWorkflow(false), registry).ExecuteAsync();

            return ordinaryMatches
                && trueContext.Statuses["if-true"] == FlowNodeExecutionStatus.Succeeded
                && trueContext.Statuses["true-true"] == FlowNodeExecutionStatus.Succeeded
                && trueContext.Statuses["false-true"] == FlowNodeExecutionStatus.Skipped
                && falseContext.Statuses["if-false"] == FlowNodeExecutionStatus.Succeeded
                && falseContext.Statuses["true-false"] == FlowNodeExecutionStatus.Skipped
                && falseContext.Statuses["false-false"] == FlowNodeExecutionStatus.Succeeded;
        });
    }

    private static FlowNodeRegistration[] CreateBuiltInLogicRegistry(out FlowNodeRegistry registry)
    {
        var plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
        plugin.Register(context);
        registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
        return context.Registrations
            .Where(item => item.Definition.Category == "Logic")
            .ToArray();
    }

    private static bool LogicRegistrationMatches(
        FlowNodeRegistration registration,
        LogicContract expected)
    {
        var dataInputs = registration.Definition.InputPorts
            .Where(port => !port.IsControlPort)
            .ToArray();
        return registration.Definition.TypeKey == expected.TypeKey
            && registration.Definition.DisplayName == expected.DisplayName
            && registration.Definition.Category == "Logic"
            && registration.PaletteDisplayName == expected.DisplayName
            && registration.PaletteDescription == expected.Description
            && registration.PaletteCategoryIconKind == "SourceBranch"
            && registration.PaletteIconKind == "SourceBranch"
            && registration.ShowInPalette
            && registration.IsPaletteCategoryExpanded
            && registration.NodeModelType == expected.ModelType
            && registration.ExecutorFactory().GetType() == expected.ExecutorType
            && registration.ContentFactory != null
            && dataInputs.Length == expected.InputTypes.Length
            && dataInputs.Select(input => input.Id).SequenceEqual(
                expected.InputTypes.Length == 1
                    ? new[] { expected.TypeKey.EndsWith(".if", StringComparison.Ordinal) ? "condition" : "input" }
                    : new[] { "inputA", "inputB" },
                StringComparer.Ordinal)
            && dataInputs.Select(input => input.DisplayName).SequenceEqual(
                expected.InputTypes.Length == 1
                    ? new[] { expected.TypeKey.EndsWith(".if", StringComparison.Ordinal) ? "Condition" : "Input" }
                    : new[] { "A", "B" },
                StringComparer.Ordinal)
            && dataInputs.Select(input => input.DataType).SequenceEqual(expected.InputTypes)
            && dataInputs.All(input => input.IOType == EIOType.Input
                && input.PreferredDirection == EPortDirection.Left
                && input.IsRequired)
            && registration.Definition.OutputPorts.Count == expected.Outputs.Length
            && registration.Definition.OutputPorts.Zip(expected.Outputs, PortMatches).All(value => value);
    }

    private static bool PortMatches(FlowPortDefinition port, LogicPortContract expected)
    {
        return port.Id == expected.Id
            && port.DisplayName == expected.DisplayName
            && port.DataType.Equals(expected.DataType)
            && port.IOType == EIOType.Output
            && port.PreferredDirection == EPortDirection.Right
            && port.IsRequired == expected.IsRequired;
    }

    private static bool LogicViewIsEmbedded(
        XDocument project,
        string[] resources,
        string viewName)
    {
        var relativePath = @"Views\" + viewName + ".xaml";
        return project.Descendants("Page").Any(item =>
                (string?)item.Attribute("Remove") == relativePath)
            && project.Descendants("EmbeddedResource").Any(item =>
                (string?)item.Attribute("Include") == relativePath)
            && resources.Contains(
                "NodeCraft.BuiltIn.Views." + viewName + ".xaml",
                StringComparer.Ordinal);
    }

    private static string ReadBuiltInView(string viewName)
    {
        return File.ReadAllText(FindRepositoryFile(
            "NodeCraft.BuiltIn", "Views", viewName + ".xaml"));
    }

    private static string ReadBuiltInViewCode(string viewName)
    {
        return File.ReadAllText(FindRepositoryFile(
            "NodeCraft.BuiltIn", "Views", viewName + ".xaml.cs"));
    }

    private static WorkflowNode LogicNode(
        string id,
        string typeKey,
        params (string PortId, object Value)[] inputs)
    {
        var node = new WorkflowNode { Id = id, TypeKey = typeKey, DisplayName = id };
        foreach (var input in inputs)
        {
            node.Inputs[input.PortId] = input.Value;
        }

        return node;
    }

    private static WorkflowDocument BuildLogicIfWorkflow(bool condition)
    {
        var suffix = condition ? "true" : "false";
        return new WorkflowDocument
        {
            Nodes = new List<WorkflowNode>
            {
                LogicNode("if-" + suffix, "nodecraft.builtin.if", ("condition", condition)),
                LogicNode(
                    "true-" + suffix,
                    "nodecraft.builtin.string-value",
                    ("value", "TRUE"),
                    ("flowIn", new LinkRef { SourceNodeId = "if-" + suffix, SourceSlot = 0 })),
                LogicNode(
                    "false-" + suffix,
                    "nodecraft.builtin.string-value",
                    ("value", "FALSE"),
                    ("flowIn", new LinkRef { SourceNodeId = "if-" + suffix, SourceSlot = 1 })),
            },
        };
    }

    private sealed record LogicContract(
        string TypeKey,
        string DisplayName,
        string Description,
        Type ModelType,
        Type ExecutorType,
        FlowDataType[] InputTypes,
        LogicPortContract[] Outputs);

    private sealed record LogicPortContract(
        string Id,
        string DisplayName,
        FlowDataType DataType,
        bool IsRequired);

    private sealed record LogicViewContract(
        string ViewName,
        string Formula,
        string Description);
}
