using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
    private static async Task RunBuiltInMathNodeTestsAsync()
    {
        Run("BuiltIn Math appends four exact contracts after Preview and Value", () =>
        {
            var plugin = new BuiltInPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);
            var registrations = context.Registrations
                .Where(item => item.Definition.Category == "Preview"
                    || item.Definition.Category == "Value"
                    || item.Definition.Category == "Math")
                .ToArray();
            var expectedTypeKeys = new[]
            {
                "nodecraft.builtin.string-value",
                "nodecraft.builtin.append-text",
                "nodecraft.builtin.text-preview",
                "nodecraft.builtin.json-serialize",
                "nodecraft.builtin.to-string",
                "nodecraft.builtin.string-concat",
                "nodecraft.builtin.integer-value",
                "nodecraft.builtin.float-value",
                "nodecraft.builtin.boolean-value",
                "nodecraft.builtin.add-number",
                "nodecraft.builtin.multiply-number",
                "nodecraft.builtin.subtract-number",
                "nodecraft.builtin.divide-number",
            };
            var expectedMath = new[]
            {
                new BuiltInMathContract("nodecraft.builtin.add-number", "Add", "A + B", "Plus", "Sum", typeof(AddNumberNodeModel), typeof(AddNumberExecutor)),
                new BuiltInMathContract("nodecraft.builtin.multiply-number", "Multiply", "A * B", "Close", "Product", typeof(MultiplyNumberNodeModel), typeof(MultiplyNumberExecutor)),
                new BuiltInMathContract("nodecraft.builtin.subtract-number", "Subtract", "A - B", "Minus", "Difference", typeof(SubtractNumberNodeModel), typeof(SubtractNumberExecutor)),
                new BuiltInMathContract("nodecraft.builtin.divide-number", "Divide", "A / B", "DivisionBox", "Quotient", typeof(DivideNumberNodeModel), typeof(DivideNumberExecutor)),
            };

            var math = registrations.Where(item => item.Definition.Category == "Math").ToArray();
            return registrations.Length == 13
                && registrations.Select(item => item.Definition.TypeKey)
                    .SequenceEqual(expectedTypeKeys, StringComparer.Ordinal)
                && math.Length == expectedMath.Length
                && math.Zip(expectedMath, BuiltInMathRegistrationMatches).All(value => value);
        });

        Run("BuiltIn Math models expose plugin-local binary number ports", () =>
        {
            var models = new NodeModel[]
            {
                new AddNumberNodeModel(),
                new MultiplyNumberNodeModel(),
                new SubtractNumberNodeModel(),
                new DivideNumberNodeModel(),
            };
            var expected = new[]
            {
                ("nodecraft.builtin.add-number", "Add"),
                ("nodecraft.builtin.multiply-number", "Multiply"),
                ("nodecraft.builtin.subtract-number", "Subtract"),
                ("nodecraft.builtin.divide-number", "Divide"),
            };

            return models.Zip(expected, (model, contract) =>
                model.ExecutorType == contract.Item1
                && model.Name == contract.Item2
                && model.InputParameters.Select(port => port.PortId)
                    .SequenceEqual(new[] { "inputA", "inputB" }, StringComparer.Ordinal)
                && model.InputParameters.All(port =>
                    port.Parameter.ParameterType == FlowDataType.Number.Key)
                && model.OutputParameters.Single().PortId == "output"
                && model.OutputParameters.Single().Parameter.ParameterType == FlowDataType.Number.Key)
                .All(value => value);
        });

        await RunAsync("BuiltIn Math executors preserve conversion arithmetic zero division and cancellation", async () =>
        {
            var context = new FlowExecutionContext();
            var node = new WorkflowNode();
            var definition = new FlowNodeDefinition();
            var contracts = new (IFlowNodeExecutor Executor, double OnlyA, double OnlyB)[]
            {
                (new AddNumberExecutor(), 6d, 2d),
                (new MultiplyNumberExecutor(), 0d, 0d),
                (new SubtractNumberExecutor(), 6d, -2d),
                (new DivideNumberExecutor(), 0d, 0d),
            };
            var add = await new AddNumberExecutor().ExecuteAsync(
                context, node, definition,
                new Dictionary<string, object> { ["inputA"] = "3.5", ["inputB"] = true },
                CancellationToken.None);
            var multiply = await new MultiplyNumberExecutor().ExecuteAsync(
                context, node, definition,
                new Dictionary<string, object> { ["inputA"] = 4, ["inputB"] = 2.5d },
                CancellationToken.None);
            var subtract = await new SubtractNumberExecutor().ExecuteAsync(
                context, node, definition,
                new Dictionary<string, object> { ["inputA"] = 10L, ["inputB"] = "3.5" },
                CancellationToken.None);
            var divide = await new DivideNumberExecutor().ExecuteAsync(
                context, node, definition,
                new Dictionary<string, object> { ["inputA"] = 9, ["inputB"] = 2 },
                CancellationToken.None);
            var zeroDivide = await new DivideNumberExecutor().ExecuteAsync(
                context, node, definition,
                new Dictionary<string, object> { ["inputA"] = 9, ["inputB"] = 0 },
                CancellationToken.None);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            var missingInputsMatch = true;
            foreach (var contract in contracts)
            {
                var neither = await contract.Executor.ExecuteAsync(
                    context,
                    node,
                    definition,
                    new Dictionary<string, object>(),
                    CancellationToken.None);
                var onlyA = await contract.Executor.ExecuteAsync(
                    context,
                    node,
                    definition,
                    new Dictionary<string, object> { ["inputA"] = 6 },
                    CancellationToken.None);
                var onlyB = await contract.Executor.ExecuteAsync(
                    context,
                    node,
                    definition,
                    new Dictionary<string, object> { ["inputB"] = 2 },
                    CancellationToken.None);
                missingInputsMatch &= Equals(neither["output"], 0d)
                    && Equals(onlyA["output"], contract.OnlyA)
                    && Equals(onlyB["output"], contract.OnlyB);
            }

            return Equals(add["output"], 4.5d)
                && Equals(multiply["output"], 10d)
                && Equals(subtract["output"], 6.5d)
                && Equals(divide["output"], 4.5d)
                && Equals(zeroDivide["output"], 0d)
                && missingInputsMatch
                && contracts.All(contract => Throws<OperationCanceledException>(() =>
                    contract.Executor.ExecuteAsync(
                        context,
                        node,
                        definition,
                        new Dictionary<string, object>(),
                        cancelled.Token).GetAwaiter().GetResult()));
        });

        Run("BuiltIn Math factories return fresh typed XAML views", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInMathRegistry(out var registry);
            var expectedViews = new[]
            {
                typeof(AddNumberView),
                typeof(MultiplyNumberView),
                typeof(SubtractNumberView),
                typeof(DivideNumberView),
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
                    && firstExecutor.GetType() == secondExecutor.GetType()
                    && !ReferenceEquals(firstExecutor, secondExecutor)
                    && firstView?.GetType() == viewType
                    && secondView?.GetType() == viewType
                    && !ReferenceEquals(firstView, secondView);
            }).All(value => value);
        }));

        Run("BuiltIn Math content factories reject every mismatched concrete model", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInMathRegistry(out var registry);
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

        Run("BuiltIn Math Add view summarizes and repeatedly swaps actual injected slots", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInMathRegistry(out var registry);
            var addRegistration = registrations.Single(item =>
                item.Definition.TypeKey == "nodecraft.builtin.add-number");
            var first = new IntegerValueNodeModel { Id = "first", Name = "First" };
            var second = new IntegerValueNodeModel { Id = "second", Name = "Second" };
            var target = new AddNumberNodeModel { Id = "add" };
            target.InputParameters.Reverse();
            var firstLink = new GraphLink
            {
                Id = "first-add",
                OriginNodeId = first.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            var secondLink = new GraphLink
            {
                Id = "second-add",
                OriginNodeId = second.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 2,
            };
            target.InputParameters.Single(port => port.PortId == "inputA").LinkId = firstLink.Id;
            target.InputParameters.Single(port => port.PortId == "inputB").LinkId = secondLink.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(first);
            canvas.GraphModel.Nodes.Add(second);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(firstLink);
            canvas.GraphModel.Links.Add(secondLink);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;

            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var texts = FindLogicalDescendants<TextBlock>(view).Select(item => item.Text).ToArray();
            var button = FindLogicalDescendants<Button>(view).Single();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var firstSwap = firstLink.TargetSlot == 2
                && secondLink.TargetSlot == 1
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == secondLink.Id
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == firstLink.Id
                && changes == 1;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            return addRegistration.Definition.InputPorts.Count == 3
                && addRegistration.Definition.InputPorts[0].Id == FlowPorts.FlowIn
                && addRegistration.Definition.InputPorts[0].IsControlPort
                && addRegistration.Definition.InputPorts.Count(port => !port.IsControlPort) == 2
                && texts.Contains("First · Value", StringComparer.Ordinal)
                && texts.Contains("Second · Value", StringComparer.Ordinal)
                && Equals(button.Content, "Swap A/B")
                && button.IsEnabled
                && firstSwap
                && firstLink.TargetSlot == 1
                && secondLink.TargetSlot == 2
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == firstLink.Id
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == secondLink.Id
                && changes == 2;
        }));

        Run("BuiltIn Math swap button labels one-sided moves and disables empty input", () => RunOnSta(() =>
        {
            CreateBuiltInMathRegistry(out var registry);
            var source = new IntegerValueNodeModel { Id = "source", Name = "Only" };
            var target = new AddNumberNodeModel { Id = "add" };
            var link = new GraphLink
            {
                Id = "only-add",
                OriginNodeId = source.Id,
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            target.InputParameters.Single(port => port.PortId == "inputA").LinkId = link.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(source);
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(link);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var button = FindLogicalDescendants<Button>(view).Single();
            var initial = Equals(button.Content, "Move A -> B") && button.IsEnabled;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var movedView = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var movedButton = FindLogicalDescendants<Button>(movedView).Single();
            var moved = link.TargetSlot == 2
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == null
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == link.Id
                && changes == 1;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var empty = new AddNumberNodeModel { Id = "empty" };
            var emptyCanvas = CreateHeadlessCanvas();
            emptyCanvas.GraphModel.Nodes.Add(empty);
            var emptyChanges = 0;
            emptyCanvas.GraphChanged += (_, _) => emptyChanges++;
            var emptyView = (FrameworkElement)registry.BuildNodeContent(emptyCanvas, empty);
            var emptyButton = FindLogicalDescendants<Button>(emptyView).Single();
            emptyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            return initial
                && moved
                && Equals(movedButton.Content, "Move B -> A")
                && movedButton.IsEnabled
                && link.TargetSlot == 1
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == link.Id
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == null
                && changes == 2
                && Equals(emptyButton.Content, "Swap A/B")
                && !emptyButton.IsEnabled
                && empty.InputParameters.All(port => port.LinkId == null)
                && emptyChanges == 0;
        }));

        Run("BuiltIn Math swap rejects duplicate target-slot links without mutation", () => RunOnSta(() =>
        {
            CreateBuiltInMathRegistry(out var registry);
            var target = new AddNumberNodeModel { Id = "duplicate-slot-target" };
            var firstLink = new GraphLink
            {
                Id = "duplicate-slot-first",
                OriginNodeId = "source-first",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            var secondLink = new GraphLink
            {
                Id = "duplicate-slot-second",
                OriginNodeId = "source-second",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            target.InputParameters.Single(port => port.PortId == "inputA").LinkId = firstLink.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(firstLink);
            canvas.GraphModel.Links.Add(secondLink);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var button = FindLogicalDescendants<Button>(view).Single();

            InvalidOperationException? failure = null;
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            return failure?.Message.Contains(target.Id, StringComparison.Ordinal) == true
                && failure.Message.Contains("slot 1", StringComparison.OrdinalIgnoreCase)
                && failure.Message.Contains("at most one", StringComparison.OrdinalIgnoreCase)
                && firstLink.TargetSlot == 1
                && secondLink.TargetSlot == 1
                && target.InputParameters.Single(port => port.PortId == "inputA").LinkId == firstLink.Id
                && target.InputParameters.Single(port => port.PortId == "inputB").LinkId == null
                && changes == 0;
        }));

        Run("BuiltIn Math swap rejects a missing runtime port without mutation", () => RunOnSta(() =>
        {
            CreateBuiltInMathRegistry(out var registry);
            var target = new AddNumberNodeModel { Id = "missing-port-target" };
            target.InputParameters.RemoveAll(port => port.PortId == "inputB");
            var firstLink = new GraphLink
            {
                Id = "missing-port-first",
                OriginNodeId = "source-first",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            var secondLink = new GraphLink
            {
                Id = "missing-port-second",
                OriginNodeId = "source-second",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 2,
            };
            target.InputParameters.Single(port => port.PortId == "inputA").LinkId = firstLink.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(firstLink);
            canvas.GraphModel.Links.Add(secondLink);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var button = FindLogicalDescendants<Button>(view).Single();

            InvalidOperationException? failure = null;
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            return failure?.Message.Contains("inputB", StringComparison.Ordinal) == true
                && failure.Message.Contains("exactly one runtime input", StringComparison.OrdinalIgnoreCase)
                && firstLink.TargetSlot == 1
                && secondLink.TargetSlot == 2
                && target.InputParameters.Single().LinkId == firstLink.Id
                && changes == 0;
        }));

        Run("BuiltIn Math swap rejects duplicate runtime ports without mutation", () => RunOnSta(() =>
        {
            CreateBuiltInMathRegistry(out var registry);
            var target = new AddNumberNodeModel { Id = "duplicate-port-target" };
            var firstRuntime = target.InputParameters.Single(port => port.PortId == "inputA");
            var secondRuntime = target.InputParameters.Single(port => port.PortId == "inputB");
            firstRuntime.LinkId = "duplicate-port-first";
            secondRuntime.LinkId = "duplicate-port-second";
            target.InputParameters.Add(new PortParameter
            {
                PortId = "inputB",
                LinkId = "duplicate-runtime-state",
                Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                PortDirection = EPortDirection.None,
            });
            var firstLink = new GraphLink
            {
                Id = firstRuntime.LinkId,
                OriginNodeId = "source-first",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 1,
            };
            var secondLink = new GraphLink
            {
                Id = secondRuntime.LinkId,
                OriginNodeId = "source-second",
                OriginSlot = 0,
                TargetNodeId = target.Id,
                TargetSlot = 2,
            };
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(target);
            canvas.GraphModel.Links.Add(firstLink);
            canvas.GraphModel.Links.Add(secondLink);
            var changes = 0;
            canvas.GraphChanged += (_, _) => changes++;
            var view = (FrameworkElement)registry.BuildNodeContent(canvas, target);
            var button = FindLogicalDescendants<Button>(view).Single();

            InvalidOperationException? failure = null;
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            return failure?.Message.Contains("inputB", StringComparison.Ordinal) == true
                && failure.Message.Contains("exactly one runtime input", StringComparison.OrdinalIgnoreCase)
                && firstLink.TargetSlot == 1
                && secondLink.TargetSlot == 2
                && firstRuntime.LinkId == "duplicate-port-first"
                && secondRuntime.LinkId == "duplicate-port-second"
                && target.InputParameters.Last().LinkId == "duplicate-runtime-state"
                && changes == 0;
        }));

        Run("BuiltIn Math views own formulas descriptions controls spacing and theme in XAML", () =>
        {
            var projectPath = FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj");
            var project = XDocument.Load(projectPath);
            var views = new[]
            {
                new BuiltInMathViewContract("AddNumberView", "A + B", "输出两个数字输入的和"),
                new BuiltInMathViewContract("MultiplyNumberView", "A * B", "输出两个数字输入的乘积"),
                new BuiltInMathViewContract("SubtractNumberView", "A - B", "输出两个数字输入的差值"),
                new BuiltInMathViewContract("DivideNumberView", "A / B", "输出两个数字输入的商，除数为 0 时返回 0"),
            };
            var forbidden = new[]
            {
                "new StackPanel",
                "new TextBlock",
                "new TextBox",
                "new Button",
                "new Border",
                "new Grid",
            };

            return views.All(view =>
            {
                var relativePath = @"Views\" + view.ViewName + ".xaml";
                var xaml = File.ReadAllText(FindRepositoryFile(
                    "NodeCraft.BuiltIn", "Views", view.ViewName + ".xaml"));
                var codeBehind = File.ReadAllText(FindRepositoryFile(
                    "NodeCraft.BuiltIn", "Views", view.ViewName + ".xaml.cs"));
                return !project.Descendants("Page").Any(item =>
                        (string?)item.Attribute("Remove") == relativePath)
                    && !project.Descendants("EmbeddedResource").Any(item =>
                        (string?)item.Attribute("Include") == relativePath)
                    && ViewCompilesToBaml(
                        typeof(AddNumberView).Assembly,
                        view.ViewName)
                    && xaml.Contains("Text=\"" + view.Formula + "\"", StringComparison.Ordinal)
                    && xaml.Contains("Text=\"" + view.Description + "\"", StringComparison.Ordinal)
                    && xaml.Contains("x:Name=\"InputAValue\"", StringComparison.Ordinal)
                    && xaml.Contains("x:Name=\"InputBValue\"", StringComparison.Ordinal)
                    && xaml.Contains("x:Name=\"SwapInputsButton\"", StringComparison.Ordinal)
                    && xaml.Contains("TextWrapping=\"Wrap\"", StringComparison.Ordinal)
                    && xaml.Contains("Margin=", StringComparison.Ordinal)
                    && xaml.Contains("DynamicResource", StringComparison.Ordinal)
                    && !xaml.Contains("#", StringComparison.Ordinal)
                    && forbidden.All(text => !codeBehind.Contains(text, StringComparison.Ordinal));
            });
        });
    }

    private static IReadOnlyList<FlowNodeRegistration> CreateBuiltInMathRegistry(
        out FlowNodeRegistry registry)
    {
        var plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
        plugin.Register(context);
        registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
        return context.Registrations
            .Where(item => item.Definition.Category == "Math")
            .ToArray();
    }

    private static bool BuiltInMathRegistrationMatches(
        FlowNodeRegistration registration,
        BuiltInMathContract expected)
    {
        var inputs = registration.Definition.InputPorts;
        var output = registration.Definition.OutputPorts.SingleOrDefault();
        return registration.Definition.TypeKey == expected.TypeKey
            && registration.Definition.DisplayName == expected.DisplayName
            && registration.Definition.Category == "Math"
            && registration.PaletteDisplayName == expected.DisplayName
            && registration.PaletteDescription == expected.Formula
            && registration.PaletteCategoryIconKind == "CalculatorVariant"
            && registration.PaletteIconKind == expected.Icon
            && registration.ShowInPalette
            && registration.IsPaletteCategoryExpanded
            && registration.NodeModelType == expected.ModelType
            && registration.ExecutorFactory().GetType() == expected.ExecutorType
            && registration.ContentFactory != null
            && inputs.Count == 2
            && inputs[0].Id == "inputA"
            && inputs[0].DisplayName == "A"
            && inputs[1].Id == "inputB"
            && inputs[1].DisplayName == "B"
            && inputs.All(input => input.IOType == EIOType.Input
                && input.DataType.Equals(FlowDataType.Number)
                && input.PreferredDirection == EPortDirection.Left
                && input.IsRequired)
            && output?.Id == "output"
            && output.DisplayName == expected.OutputName
            && output.IOType == EIOType.Output
            && output.DataType.Equals(FlowDataType.Number)
            && output.PreferredDirection == EPortDirection.Right
            && !output.IsRequired;
    }

    private sealed record BuiltInMathContract(
        string TypeKey,
        string DisplayName,
        string Formula,
        string Icon,
        string OutputName,
        Type ModelType,
        Type ExecutorType);

    private sealed record BuiltInMathViewContract(
        string ViewName,
        string Formula,
        string Description);
}
