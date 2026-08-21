using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
    private static void RunBuiltInValueNodeTests()
    {
        Run("BuiltIn Value plugin appends the three node contracts after Preview", () =>
        {
            var plugin = new BuiltInPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);
            var registrations = context.Registrations;
            var expected = new[]
            {
                new BuiltInValueContract(
                    "nodecraft.builtin.integer-value",
                    "Integer Value",
                    "固定整数输出",
                    "Numeric",
                    typeof(IntegerValueNodeModel),
                    typeof(IntegerValueExecutor),
                    FlowDataType.Number),
                new BuiltInValueContract(
                    "nodecraft.builtin.float-value",
                    "Float Value",
                    "固定浮点数输出",
                    "Numeric",
                    typeof(FloatValueNodeModel),
                    typeof(FloatValueExecutor),
                    FlowDataType.Number),
                new BuiltInValueContract(
                    "nodecraft.builtin.boolean-value",
                    "Boolean Value",
                    "固定布尔输出",
                    "ToggleSwitchOutline",
                    typeof(BooleanValueNodeModel),
                    typeof(BooleanValueExecutor),
                    FlowDataType.Boolean),
            };

            return registrations.Count == 7
                && registrations.Take(4).All(item => item.Definition.Category == "Preview")
                && registrations.Skip(4).Select(item => item.Definition.TypeKey)
                    .SequenceEqual(expected.Select(item => item.TypeKey), StringComparer.Ordinal)
                && registrations.Skip(4).Zip(expected, BuiltInValueRegistrationMatches).All(value => value);
        });

        Run("BuiltIn Value models preserve defaults and plugin-local output contracts", () =>
        {
            var integer = new IntegerValueNodeModel();
            var floating = new FloatValueNodeModel();
            var boolean = new BooleanValueNodeModel();
            var integerWorkflow = new WorkflowNode();
            var floatWorkflow = new WorkflowNode();
            var booleanWorkflow = new WorkflowNode();
            integer.WriteWorkflowInputs(integerWorkflow);
            floating.WriteWorkflowInputs(floatWorkflow);
            boolean.WriteWorkflowInputs(booleanWorkflow);

            return integer.IntegerValue == 42
                && floating.FloatValue.Equals(3.14d)
                && boolean.BooleanValue
                && integer.ExecutorType == "nodecraft.builtin.integer-value"
                && floating.ExecutorType == "nodecraft.builtin.float-value"
                && boolean.ExecutorType == "nodecraft.builtin.boolean-value"
                && integer.OutputParameters.Single().PortId == "output"
                && floating.OutputParameters.Single().PortId == "output"
                && boolean.OutputParameters.Single().PortId == "output"
                && integer.OutputParameters.Single().Parameter.ParameterType == FlowDataType.Number.Key
                && floating.OutputParameters.Single().Parameter.ParameterType == FlowDataType.Number.Key
                && boolean.OutputParameters.Single().Parameter.ParameterType == FlowDataType.Boolean.Key
                && Equals(integerWorkflow.Inputs["value"], 42)
                && Equals(floatWorkflow.Inputs["value"], 3.14d)
                && Equals(booleanWorkflow.Inputs["value"], true);
        });

        Run("BuiltIn Value factories return fresh matching models executors and XAML views", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInValueRegistry(out var registry).Skip(4).ToArray();
            var expectedViews = new[]
            {
                typeof(IntegerValueEditor),
                typeof(FloatValueEditor),
                typeof(BooleanValueEditor),
            };

            return registrations.Zip(expectedViews, (registration, viewType) =>
            {
                var firstNode = registration.NodeFactory?.Invoke();
                var secondNode = registration.NodeFactory?.Invoke();
                var firstExecutor = registration.ExecutorFactory();
                var secondExecutor = registration.ExecutorFactory();
                if (firstNode == null || secondNode == null)
                {
                    return false;
                }

                var canvas = CreateHeadlessCanvas();
                canvas.GraphModel.Nodes.Add(firstNode);
                var firstView = registry.BuildNodeContent(canvas, firstNode);
                var secondView = registry.BuildNodeContent(canvas, firstNode);
                return firstNode.GetType() == registration.NodeModelType
                    && secondNode.GetType() == registration.NodeModelType
                    && !ReferenceEquals(firstNode, secondNode)
                    && firstNode.ExecutorType == registration.Definition.TypeKey
                    && secondNode.ExecutorType == registration.Definition.TypeKey
                    && firstExecutor.GetType() == secondExecutor.GetType()
                    && !ReferenceEquals(firstExecutor, secondExecutor)
                    && firstView?.GetType() == viewType
                    && secondView?.GetType() == viewType
                    && !ReferenceEquals(firstView, secondView);
            }).All(value => value);
        }));

        Run("BuiltIn Value executors preserve integer double and boolean output semantics", () =>
        {
            var context = new FlowExecutionContext();
            var definition = new FlowNodeDefinition();
            var integer = new WorkflowNode { Inputs = { ["value"] = 17.6d } };
            var floating = new WorkflowNode { Inputs = { ["value"] = "3.5" } };
            var boolean = new WorkflowNode { Inputs = { ["value"] = 1 } };
            var integerOutput = new IntegerValueExecutor().ExecuteAsync(
                context,
                integer,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None).GetAwaiter().GetResult();
            var floatOutput = new FloatValueExecutor().ExecuteAsync(
                context,
                floating,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None).GetAwaiter().GetResult();
            var booleanOutput = new BooleanValueExecutor().ExecuteAsync(
                context,
                boolean,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None).GetAwaiter().GetResult();

            return integerOutput["output"] is int integerValue
                && integerValue == 18
                && floatOutput["output"] is double floatValue
                && floatValue.Equals(3.5d)
                && booleanOutput["output"] is bool booleanValue
                && booleanValue;
        });

        Run("BuiltIn Value integer editor accepts invariant integers and rejects invalid text", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInValueRegistry(out var registry);
            var registration = registrations.Single(item =>
                item.Definition.TypeKey == "nodecraft.builtin.integer-value");
            var node = new IntegerValueNodeModel();
            var canvas = CreateHeadlessCanvas();
            var graphChanges = 0;
            canvas.GraphChanged += (_, _) => graphChanges++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var editor = FindLogicalDescendants<TextBox>(view).Single();

            var initialText = editor.Text;
            editor.Text = "17";
            var firstChange = node.IntegerValue == 17 && graphChanges == 1;
            editor.Text = "-2";
            var secondChange = node.IntegerValue == -2 && graphChanges == 2;
            editor.Text = "NaN";
            editor.Text = "not an integer";

            return initialText == "42"
                && firstChange
                && secondChange
                && node.IntegerValue == -2
                && graphChanges == 2
                && registration.ContentFactory != null;
        }));

        Run("BuiltIn Value float editor accepts invariant finite values and rejects non-finite text", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInValueRegistry(out var registry);
            var registration = registrations.Single(item =>
                item.Definition.TypeKey == "nodecraft.builtin.float-value");
            var node = new FloatValueNodeModel();
            var canvas = CreateHeadlessCanvas();
            var graphChanges = 0;
            canvas.GraphChanged += (_, _) => graphChanges++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var editor = FindLogicalDescendants<TextBox>(view).Single();

            var initialText = editor.Text;
            editor.Text = "3.5";
            var validChange = node.FloatValue.Equals(3.5d) && graphChanges == 1;
            editor.Text = "NaN";
            editor.Text = "Infinity";
            editor.Text = "not a float";

            return initialText == "3.140"
                && validChange
                && node.FloatValue.Equals(3.5d)
                && graphChanges == 1
                && registration.ContentFactory != null;
        }));

        Run("BuiltIn Value boolean editor synchronizes True False content and notifies once per toggle", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInValueRegistry(out var registry);
            var registration = registrations.Single(item =>
                item.Definition.TypeKey == "nodecraft.builtin.boolean-value");
            var node = new BooleanValueNodeModel();
            var canvas = CreateHeadlessCanvas();
            var graphChanges = 0;
            canvas.GraphChanged += (_, _) => graphChanges++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var editor = FindLogicalDescendants<CheckBox>(view).Single();

            var initialState = editor.IsChecked == true
                && Equals(editor.Content, "True")
                && graphChanges == 0;
            editor.IsChecked = false;
            var falseState = !node.BooleanValue
                && Equals(editor.Content, "False")
                && graphChanges == 1;
            editor.IsChecked = true;

            return initialState
                && falseState
                && node.BooleanValue
                && Equals(editor.Content, "True")
                && graphChanges == 2
                && registration.ContentFactory != null;
        }));

        Run("BuiltIn Value views keep labels styles and named editors in embedded XAML", () =>
        {
            var projectPath = FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj");
            var project = XDocument.Load(projectPath);
            var resources = typeof(IntegerValueEditor).Assembly.GetManifestResourceNames();
            var views = new[]
            {
                new BuiltInValueViewContract("IntegerValueEditor", "IntegerEditor", "Integer"),
                new BuiltInValueViewContract("FloatValueEditor", "FloatEditor", "Float"),
                new BuiltInValueViewContract("BooleanValueEditor", "BooleanEditor", "Enabled"),
            };
            var forbidden = new[]
            {
                "new StackPanel",
                "new TextBlock",
                "new TextBox",
                "new CheckBox",
                "new Border",
            };

            return views.All(view =>
            {
                var relativePath = @"Views\" + view.ViewName + ".xaml";
                var xaml = File.ReadAllText(FindRepositoryFile(
                    "NodeCraft.BuiltIn",
                    "Views",
                    view.ViewName + ".xaml"));
                var codeBehind = File.ReadAllText(FindRepositoryFile(
                    "NodeCraft.BuiltIn",
                    "Views",
                    view.ViewName + ".xaml.cs"));
                return project.Descendants("Page").Any(item =>
                        (string?)item.Attribute("Remove") == relativePath)
                    && project.Descendants("EmbeddedResource").Any(item =>
                        (string?)item.Attribute("Include") == relativePath)
                    && resources.Contains(
                        "NodeCraft.BuiltIn.Views." + view.ViewName + ".xaml",
                        StringComparer.Ordinal)
                    && xaml.Contains("x:Name=\"" + view.EditorName + "\"", StringComparison.Ordinal)
                    && xaml.Contains("Text=\"" + view.Label + "\"", StringComparison.Ordinal)
                    && xaml.Contains("Margin=", StringComparison.Ordinal)
                    && xaml.Contains("DynamicResource", StringComparison.Ordinal)
                    && !xaml.Contains("#", StringComparison.Ordinal)
                    && forbidden.All(text => !codeBehind.Contains(text, StringComparison.Ordinal));
            });
        });
    }

    private static IReadOnlyList<FlowNodeRegistration> CreateBuiltInValueRegistry(
        out FlowNodeRegistry registry)
    {
        var plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
        plugin.Register(context);
        registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
        return context.Registrations;
    }

    private static bool BuiltInValueRegistrationMatches(
        FlowNodeRegistration registration,
        BuiltInValueContract expected)
    {
        var output = registration.Definition.OutputPorts.SingleOrDefault();
        return registration.Definition.TypeKey == expected.TypeKey
            && registration.Definition.DisplayName == expected.DisplayName
            && registration.Definition.Category == "Value"
            && registration.PaletteDisplayName == expected.DisplayName
            && registration.PaletteDescription == expected.Description
            && registration.PaletteCategoryIconKind == "FormatListNumbered"
            && registration.PaletteIconKind == expected.Icon
            && registration.ShowInPalette
            && registration.IsPaletteCategoryExpanded
            && registration.NodeModelType == expected.ModelType
            && registration.ExecutorFactory().GetType() == expected.ExecutorType
            && registration.ContentFactory != null
            && registration.Definition.InputPorts.Count == 0
            && output?.Id == "output"
            && output.DisplayName == "Value"
            && output.IOType == EIOType.Output
            && output.DataType.Equals(expected.OutputType)
            && output.PreferredDirection == EPortDirection.Right
            && !output.IsRequired;
    }

    private sealed record BuiltInValueContract(
        string TypeKey,
        string DisplayName,
        string Description,
        string Icon,
        Type ModelType,
        Type ExecutorType,
        FlowDataType OutputType);

    private sealed record BuiltInValueViewContract(
        string ViewName,
        string EditorName,
        string Label);
}
