using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using CommonControls.WPF;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Plugin;
using NodeCraft.BuiltIn.Views;
using NodeCraft.Flow;
using BusinessBorder = System.Windows.Controls.Border;

internal static partial class Program
{
    private static async Task RunBuiltInPluginContractTestsAsync()
    {
        Run("BuiltIn plugin stages the exact 18 node registrations", () =>
        {
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
            var contracts = CreateBuiltInContracts();
            var registrations = StageBuiltInPlugin(out var plugin);
            var actualTypeKeys = registrations
                .Select(item => item.Definition.TypeKey)
                .ToArray();
            var legacyTypeKeys = new[]
            {
                "node.string-value",
                "node.add-number",
                "node.if",
                "node.json-serialize",
            };

            return plugin.Metadata.Id == "nodecraft.builtin"
                && plugin.Metadata.DisplayName == "Built-in Nodes"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && registrations.Count == 18
                && actualTypeKeys.SequenceEqual(expectedTypeKeys, StringComparer.Ordinal)
                && actualTypeKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 18
                && actualTypeKeys.All(key => key.StartsWith("nodecraft.builtin.", StringComparison.Ordinal))
                && legacyTypeKeys.All(key => !actualTypeKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                && registrations.Zip(contracts, RegistrationFactoriesMatch).All(value => value);
        });

        Run("BuiltIn definitions match snapshots before and after registry injection", () =>
        {
            var contracts = CreateBuiltInContracts();
            var registrations = StageBuiltInPlugin(out var plugin);
            var beforeRegistration = registrations.Zip(
                    contracts,
                    (registration, contract) => RegistrationMatchesDefinitionSnapshot(
                        registration,
                        contract,
                        expectControlInput: false))
                .All(value => value);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, registrations);
            var afterRegistration = contracts.All(contract =>
                RegistrationMatchesDefinitionSnapshot(
                    registry.Resolve(contract.TypeKey),
                    contract,
                    expectControlInput: true));

            return beforeRegistration && afterRegistration;
        });

        Run("BuiltIn duplicate staged batches are rejected atomically", () =>
        {
            var registrations = StageBuiltInPlugin(out var plugin);
            var batch = registrations.ToList();
            batch.Add(new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = registrations[0].Definition.TypeKey,
                    DisplayName = "Duplicate",
                    Category = "Preview",
                },
                () => new StringValueExecutor())
            {
                ShowInPalette = false,
            });
            var registry = new FlowNodeRegistry();
            var rejected = Throws<InvalidOperationException>(() =>
                registry.RegisterPlugin(plugin.Metadata.Id, batch));

            return rejected
                && registrations.All(item => !registry.Contains(item.Definition.TypeKey));
        });

        Run("BuiltIn views are exact fresh themed XAML resources", () => RunOnSta(() =>
        {
            var contracts = CreateBuiltInContracts();
            var registrations = StageBuiltInPlugin(out var plugin);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, registrations);
            var assembly = typeof(BuiltInPlugin).Assembly;
            var expectedResourceNames = new[] { "NodeCraft.BuiltIn.g.resources" };
            var actualResourceNames = assembly.GetManifestResourceNames()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var projectDirectory = Path.GetDirectoryName(
                FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj"))!;
            var viewsDirectory = Path.Combine(projectDirectory, "Views");
            var expectedXamlFiles = contracts
                .Select(contract => contract.ViewType.Name + ".xaml")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actualXamlFiles = Directory.GetFiles(viewsDirectory, "*.xaml")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedBamlKeys = contracts
                .Select(contract => "views/" + contract.ViewType.Name.ToLowerInvariant() + ".baml")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actualBamlKeys = EnumerateResourceKeys(
                    assembly,
                    "NodeCraft.BuiltIn.g.resources")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            return actualResourceNames.SequenceEqual(expectedResourceNames, StringComparer.Ordinal)
                && actualBamlKeys.SequenceEqual(expectedBamlKeys, StringComparer.Ordinal)
                && actualXamlFiles.SequenceEqual(expectedXamlFiles, StringComparer.Ordinal)
                && actualXamlFiles.All(name => File.Exists(
                    Path.Combine(viewsDirectory, name + ".cs")))
                && RunWithThemedWindow(window =>
                {
                    var canvas = CreateHeadlessCanvas();
                    var panel = new StackPanel();
                    window.Content = new ScrollViewer { Content = panel };
                    var views = new List<FrameworkElement>();

                    for (var index = 0; index < contracts.Length; index++)
                    {
                        var contract = contracts[index];
                        var registration = registry.Resolve(contract.TypeKey);
                        var node = registration.NodeFactory();
                        node.Id = "built-in-contract-" + index;
                        canvas.GraphModel.Nodes.Add(node);
                        var first = registry.BuildNodeContent(canvas, node) as FrameworkElement;
                        var second = registry.BuildNodeContent(canvas, node) as FrameworkElement;
                        if (first == null
                            || second == null
                            || first.GetType() != contract.ViewType
                            || second.GetType() != contract.ViewType
                            || ReferenceEquals(first, second)
                            || registration.ContentFactory.Method.DeclaringType != contract.ViewType)
                        {
                            return false;
                        }

                        views.Add(first);
                        views.Add(second);
                        panel.Children.Add(first);
                        panel.Children.Add(second);
                    }

                    window.UpdateLayout();
                    return views.Count == 36 && ReferencesAreUnique(views);
                });
        }));

        Run("BuiltIn XAML and code-behind obey source policy", () =>
        {
            var contracts = CreateBuiltInContracts();
            var projectDirectory = Path.GetDirectoryName(
                FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj"))!;
            var viewsDirectory = Path.Combine(projectDirectory, "Views");
            var expectedCodeBehindFiles = contracts
                .Select(contract => contract.ViewType.Name + ".xaml.cs")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actualCodeBehindFiles = Directory.GetFiles(viewsDirectory, "*.xaml.cs")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var commentOnlyResources = XDocument.Parse(
                "<Grid><!-- {DynamicResource colorCommentOnly} #fff --></Grid>");
            var realDynamicResource = XDocument.Parse(
                "<Grid Background=\"{DynamicResource colorReal}\" />");
            var realHexColor = XDocument.Parse(
                "<Grid Background=\"#AABBCC\" />");
            var bypassAllocations = FindBusinessControlAllocations(
                typeof(SourcePolicyIlFixture));

            return actualCodeBehindFiles.SequenceEqual(expectedCodeBehindFiles, StringComparer.Ordinal)
                && !XamlHasDynamicResourceAttribute(commentOnlyResources)
                && !XamlHasHexColorAttribute(commentOnlyResources)
                && XamlHasDynamicResourceAttribute(realDynamicResource)
                && XamlHasHexColorAttribute(realHexColor)
                && bypassAllocations.Contains(typeof(Grid))
                && bypassAllocations.Contains(typeof(Button))
                && bypassAllocations.Contains(typeof(BusinessBorder))
                && contracts.All(contract =>
                {
                    var viewName = contract.ViewType.Name;
                    var xaml = XDocument.Load(Path.Combine(
                        viewsDirectory,
                        viewName + ".xaml"));
                    return XamlHasDynamicResourceAttribute(xaml)
                        && !XamlHasHexColorAttribute(xaml)
                        && FindBusinessControlAllocations(contract.ViewType).Count == 0;
                });
        });

        await RunAsync("BuiltIn executors cover every family through the local registry", async () =>
        {
            var registrations = StageBuiltInPlugin(out var plugin);
            var registry = new FlowNodeRegistry();
            registry.RegisterPlugin(plugin.Metadata.Id, registrations);
            var previewPayload = new Dictionary<string, object>
            {
                ["kind"] = "preview",
            };
            var workflow = new WorkflowDocument
            {
                Nodes = new List<WorkflowNode>
                {
                    ContractNode("string", "nodecraft.builtin.string-value", ("value", "NodeCraft")),
                    ContractNode("integer", "nodecraft.builtin.integer-value", ("value", 42)),
                    ContractNode("float", "nodecraft.builtin.float-value", ("value", 3.25d)),
                    ContractNode("boolean", "nodecraft.builtin.boolean-value", ("value", true)),
                    ContractNode("boolean-false", "nodecraft.builtin.boolean-value", ("value", false)),
                    ContractNode("append", "nodecraft.builtin.append-text", ("input", "NodeCraft"), ("suffix", " rocks")),
                    ContractNode("preview", "nodecraft.builtin.text-preview", ("input", previewPayload)),
                    ContractNode(
                        "json",
                        "nodecraft.builtin.json-serialize",
                        ("input", new Dictionary<string, object> { ["name"] = "NodeCraft" })),
                    ContractNode("add", "nodecraft.builtin.add-number", ("inputA", 2), ("inputB", 3)),
                    ContractNode("multiply", "nodecraft.builtin.multiply-number", ("inputA", 2.5d), ("inputB", 4)),
                    ContractNode("subtract", "nodecraft.builtin.subtract-number", ("inputA", 9), ("inputB", 4)),
                    ContractNode("divide", "nodecraft.builtin.divide-number", ("inputA", 9), ("inputB", 4)),
                    ContractNode("divide-zero", "nodecraft.builtin.divide-number", ("inputA", 9), ("inputB", 0)),
                    ContractNode("greater", "nodecraft.builtin.greater-than", ("inputA", 4), ("inputB", 3)),
                    ContractNode("greater-false", "nodecraft.builtin.greater-than", ("inputA", 2), ("inputB", 3)),
                    ContractNode("less", "nodecraft.builtin.less-than", ("inputA", 2), ("inputB", 3)),
                    ContractNode("less-false", "nodecraft.builtin.less-than", ("inputA", 3), ("inputB", 2)),
                    ContractNode("equal", "nodecraft.builtin.equal", ("inputA", "same"), ("inputB", "same")),
                    ContractNode("equal-false", "nodecraft.builtin.equal", ("inputA", "left"), ("inputB", "right")),
                    ContractNode("and", "nodecraft.builtin.boolean-and", ("inputA", true), ("inputB", true)),
                    ContractNode("and-false", "nodecraft.builtin.boolean-and", ("inputA", true), ("inputB", false)),
                    ContractNode("or", "nodecraft.builtin.boolean-or", ("inputA", false), ("inputB", true)),
                    ContractNode("or-false", "nodecraft.builtin.boolean-or", ("inputA", false), ("inputB", false)),
                    ContractNode("not", "nodecraft.builtin.boolean-not", ("input", false)),
                    ContractNode("not-false", "nodecraft.builtin.boolean-not", ("input", true)),
                },
            };
            var context = await new GraphExecutor(workflow, registry).ExecuteAsync();
            var trueContext = await new GraphExecutor(
                CreateIfContractWorkflow(condition: true),
                registry).ExecuteAsync();
            var falseContext = await new GraphExecutor(
                CreateIfContractWorkflow(condition: false),
                registry).ExecuteAsync();
            var allSucceeded = workflow.Nodes.All(node =>
                context.Statuses[node.Id] == FlowNodeExecutionStatus.Succeeded);

            return allSucceeded
                && GetContractOutput(context, "string") is string stringOutput && stringOutput == "NodeCraft"
                && GetContractOutput(context, "integer") is int integerOutput && integerOutput == 42
                && GetContractOutput(context, "float") is double floatOutput && floatOutput == 3.25d
                && GetContractOutput(context, "boolean") is bool booleanOutput && booleanOutput
                && Equals(GetContractOutput(context, "boolean-false"), false)
                && Equals(GetContractOutput(context, "append"), "NodeCraft rocks")
                && ReferenceEquals(GetContractOutput(context, "preview"), previewPayload)
                && Equals(GetContractOutput(context, "json"), "{\r\n  \"name\": \"NodeCraft\"\r\n}")
                && Equals(GetContractOutput(context, "add"), 5d)
                && Equals(GetContractOutput(context, "multiply"), 10d)
                && Equals(GetContractOutput(context, "subtract"), 5d)
                && Equals(GetContractOutput(context, "divide"), 2.25d)
                && Equals(GetContractOutput(context, "divide-zero"), 0d)
                && Equals(GetContractOutput(context, "greater"), true)
                && Equals(GetContractOutput(context, "greater-false"), false)
                && Equals(GetContractOutput(context, "less"), true)
                && Equals(GetContractOutput(context, "less-false"), false)
                && Equals(GetContractOutput(context, "equal"), true)
                && Equals(GetContractOutput(context, "equal-false"), false)
                && Equals(GetContractOutput(context, "and"), true)
                && Equals(GetContractOutput(context, "and-false"), false)
                && Equals(GetContractOutput(context, "or"), true)
                && Equals(GetContractOutput(context, "or-false"), false)
                && Equals(GetContractOutput(context, "not"), true)
                && Equals(GetContractOutput(context, "not-false"), false)
                && IfBranchMatches(trueContext, condition: true)
                && IfBranchMatches(falseContext, condition: false);
        });
    }

    private static BuiltInContract[] CreateBuiltInContracts()
    {
        return new[]
        {
            Contract("nodecraft.builtin.string-value", "String Value", "Preview", "ViewDashboardOutline", "FormatText", typeof(StringValueNodeModel), typeof(StringValueExecutor), typeof(StringValueEditor), Array.Empty<BuiltInPortContract>(), new[] { Port("output", "Value", FlowDataType.String, false) }),
            Contract("nodecraft.builtin.append-text", "Append Text", "Preview", "ViewDashboardOutline", "ViewDashboardOutline", typeof(AppendTextNodeModel), typeof(AppendTextExecutor), typeof(AppendTextEditor), new[] { Port("input", "Input", FlowDataType.String, true) }, new[] { Port("output", "Output", FlowDataType.String, false) }),
            Contract("nodecraft.builtin.text-preview", "Text Preview", "Preview", "ViewDashboardOutline", "EyeOutline", typeof(TextPreviewNodeModel), typeof(TextPreviewExecutor), typeof(TextPreviewView), new[] { Port("input", "Input", FlowDataType.Object, true) }, new[] { Port("output", "Output", FlowDataType.Object, false) }),
            Contract("nodecraft.builtin.json-serialize", "JSON Serialize", "Preview", "ViewDashboardOutline", "ViewDashboardOutline", typeof(JsonSerializeNodeModel), typeof(JsonSerializeExecutor), typeof(JsonSerializeView), new[] { Port("input", "Input", FlowDataType.Object, true) }, new[] { Port("output", "JSON", FlowDataType.String, false) }),
            Contract("nodecraft.builtin.integer-value", "Integer Value", "Value", "FormatListNumbered", "Numeric", typeof(IntegerValueNodeModel), typeof(IntegerValueExecutor), typeof(IntegerValueEditor), Array.Empty<BuiltInPortContract>(), new[] { Port("output", "Value", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.float-value", "Float Value", "Value", "FormatListNumbered", "Numeric", typeof(FloatValueNodeModel), typeof(FloatValueExecutor), typeof(FloatValueEditor), Array.Empty<BuiltInPortContract>(), new[] { Port("output", "Value", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.boolean-value", "Boolean Value", "Value", "FormatListNumbered", "ToggleSwitchOutline", typeof(BooleanValueNodeModel), typeof(BooleanValueExecutor), typeof(BooleanValueEditor), Array.Empty<BuiltInPortContract>(), new[] { Port("output", "Value", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.add-number", "Add", "Math", "CalculatorVariant", "Plus", typeof(AddNumberNodeModel), typeof(AddNumberExecutor), typeof(AddNumberView), NumberInputs(), new[] { Port("output", "Sum", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.multiply-number", "Multiply", "Math", "CalculatorVariant", "Close", typeof(MultiplyNumberNodeModel), typeof(MultiplyNumberExecutor), typeof(MultiplyNumberView), NumberInputs(), new[] { Port("output", "Product", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.subtract-number", "Subtract", "Math", "CalculatorVariant", "Minus", typeof(SubtractNumberNodeModel), typeof(SubtractNumberExecutor), typeof(SubtractNumberView), NumberInputs(), new[] { Port("output", "Difference", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.divide-number", "Divide", "Math", "CalculatorVariant", "DivisionBox", typeof(DivideNumberNodeModel), typeof(DivideNumberExecutor), typeof(DivideNumberView), NumberInputs(), new[] { Port("output", "Quotient", FlowDataType.Number, false) }),
            Contract("nodecraft.builtin.greater-than", "Greater Than", "Logic", "SourceBranch", "SourceBranch", typeof(GreaterThanNodeModel), typeof(GreaterThanExecutor), typeof(GreaterThanView), BinaryInputs(FlowDataType.Number), new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.less-than", "Less Than", "Logic", "SourceBranch", "SourceBranch", typeof(LessThanNodeModel), typeof(LessThanExecutor), typeof(LessThanView), BinaryInputs(FlowDataType.Number), new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.equal", "Equal", "Logic", "SourceBranch", "SourceBranch", typeof(EqualNodeModel), typeof(EqualExecutor), typeof(EqualView), BinaryInputs(FlowDataType.Object), new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.boolean-and", "Boolean And", "Logic", "SourceBranch", "SourceBranch", typeof(BooleanAndNodeModel), typeof(BooleanAndExecutor), typeof(BooleanAndView), BinaryInputs(FlowDataType.Boolean), new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.boolean-or", "Boolean Or", "Logic", "SourceBranch", "SourceBranch", typeof(BooleanOrNodeModel), typeof(BooleanOrExecutor), typeof(BooleanOrView), BinaryInputs(FlowDataType.Boolean), new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.boolean-not", "Boolean Not", "Logic", "SourceBranch", "SourceBranch", typeof(BooleanNotNodeModel), typeof(BooleanNotExecutor), typeof(BooleanNotView), new[] { Port("input", "Input", FlowDataType.Boolean, true) }, new[] { Port("output", "Result", FlowDataType.Boolean, false) }),
            Contract("nodecraft.builtin.if", "If", "Logic", "SourceBranch", "SourceBranch", typeof(IfNodeModel), typeof(IfExecutor), typeof(IfView), new[] { Port("condition", "Condition", FlowDataType.Boolean, true) }, new[] { Port("true", "True", FlowDataType.Control, false), Port("false", "False", FlowDataType.Control, false) }),
        };
    }

    private static BuiltInContract Contract(
        string typeKey,
        string displayName,
        string category,
        string categoryIcon,
        string icon,
        Type modelType,
        Type executorType,
        Type viewType,
        BuiltInPortContract[] inputs,
        BuiltInPortContract[] outputs)
    {
        return new BuiltInContract(
            typeKey,
            displayName,
            category,
            categoryIcon,
            icon,
            modelType,
            executorType,
            viewType,
            inputs,
            outputs);
    }

    private static BuiltInPortContract Port(
        string id,
        string displayName,
        FlowDataType dataType,
        bool isRequired)
    {
        return new BuiltInPortContract(id, displayName, dataType, isRequired);
    }

    private static BuiltInPortContract[] NumberInputs()
    {
        return BinaryInputs(FlowDataType.Number);
    }

    private static BuiltInPortContract[] BinaryInputs(FlowDataType dataType)
    {
        return new[]
        {
            Port("inputA", "A", dataType, true),
            Port("inputB", "B", dataType, true),
        };
    }

    private static IReadOnlyList<FlowNodeRegistration> StageBuiltInPlugin(
        out BuiltInPlugin plugin)
    {
        plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(context);
        return context.Registrations;
    }

    private static IEnumerable<string> EnumerateResourceKeys(
        Assembly assembly,
        string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return Array.Empty<string>();
        }

        using var reader = new System.Resources.ResourceReader(stream);
        return reader.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToArray();
    }

    private static bool RegistrationFactoriesMatch(
        FlowNodeRegistration registration,
        BuiltInContract contract)
    {
        if (registration.Definition == null
            || registration.NodeModelType == null
            || registration.NodeFactory == null
            || registration.ExecutorFactory == null
            || registration.ContentFactory == null)
        {
            return false;
        }

        var firstModel = registration.NodeFactory();
        var secondModel = registration.NodeFactory();
        var firstExecutor = registration.ExecutorFactory();
        var secondExecutor = registration.ExecutorFactory();
        return registration.NodeModelType == contract.ModelType
            && firstModel != null
            && secondModel != null
            && firstModel.GetType() == contract.ModelType
            && secondModel.GetType() == contract.ModelType
            && firstModel.ExecutorType == contract.TypeKey
            && secondModel.ExecutorType == contract.TypeKey
            && !ReferenceEquals(firstModel, secondModel)
            && firstExecutor != null
            && secondExecutor != null
            && firstExecutor.GetType() == contract.ExecutorType
            && secondExecutor.GetType() == contract.ExecutorType
            && !ReferenceEquals(firstExecutor, secondExecutor);
    }

    private static bool RegistrationMatchesDefinitionSnapshot(
        FlowNodeRegistration registration,
        BuiltInContract contract,
        bool expectControlInput)
    {
        var definition = registration.Definition;
        var dataInputs = expectControlInput
            ? definition.InputPorts.Skip(1).ToArray()
            : definition.InputPorts.ToArray();
        var flowInputMatches = !expectControlInput
            ? definition.InputPorts.All(port => !port.IsControlPort)
            : definition.InputPorts.Count == contract.Inputs.Length + 1
                && definition.InputPorts.Count(port => port.IsControlPort) == 1
                && definition.InputPorts[0].Id == FlowPorts.FlowIn
                && definition.InputPorts[0].DisplayName == "Flow In"
                && definition.InputPorts[0].IOType == EIOType.Input
                && definition.InputPorts[0].DataType.Equals(FlowDataType.Control)
                && definition.InputPorts[0].PreferredDirection == EPortDirection.Top
                && !definition.InputPorts[0].IsRequired
                && !definition.InputPorts[0].AllowMultipleConnections;

        return definition.TypeKey == contract.TypeKey
            && definition.DisplayName == contract.DisplayName
            && definition.Category == contract.Category
            && registration.PaletteDisplayName == contract.DisplayName
            && registration.PaletteCategoryIconKind == contract.CategoryIcon
            && registration.PaletteIconKind == contract.Icon
            && flowInputMatches
            && PortsMatch(dataInputs, contract.Inputs, EIOType.Input)
            && PortsMatch(definition.OutputPorts, contract.Outputs, EIOType.Output);
    }

    private static bool PortsMatch(
        IReadOnlyList<FlowPortDefinition> actual,
        IReadOnlyList<BuiltInPortContract> expected,
        EIOType ioType)
    {
        return actual.Count == expected.Count
            && actual.Zip(expected, (port, contract) =>
                    port.Id == contract.Id
                    && port.DisplayName == contract.DisplayName
                    && port.DataType.Equals(contract.DataType)
                    && port.IsRequired == contract.IsRequired
                    && port.IOType == ioType)
                .All(value => value);
    }

    private static bool ReferencesAreUnique(IReadOnlyList<FrameworkElement> views)
    {
        for (var index = 0; index < views.Count; index++)
        {
            for (var other = index + 1; other < views.Count; other++)
            {
                if (ReferenceEquals(views[index], views[other]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static WorkflowNode ContractNode(
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

    private static object GetContractOutput(
        FlowExecutionContext context,
        string nodeId,
        int slot = 0)
    {
        if (!context.TryGetPortValue(nodeId, slot, out var value))
        {
            throw new InvalidOperationException(
                $"Node '{nodeId}' did not produce output slot {slot}.");
        }

        return value;
    }

    private static WorkflowDocument CreateIfContractWorkflow(bool condition)
    {
        var suffix = condition ? "true" : "false";
        return new WorkflowDocument
        {
            Nodes = new List<WorkflowNode>
            {
                ContractNode(
                    "if-" + suffix,
                    "nodecraft.builtin.if",
                    ("condition", condition)),
                ContractNode(
                    "true-" + suffix,
                    "nodecraft.builtin.string-value",
                    ("value", "TRUE"),
                    ("flowIn", new LinkRef
                    {
                        SourceNodeId = "if-" + suffix,
                        SourceSlot = 0,
                    })),
                ContractNode(
                    "false-" + suffix,
                    "nodecraft.builtin.string-value",
                    ("value", "FALSE"),
                    ("flowIn", new LinkRef
                    {
                        SourceNodeId = "if-" + suffix,
                        SourceSlot = 1,
                    })),
            },
        };
    }

    private static bool IfBranchMatches(
        FlowExecutionContext context,
        bool condition)
    {
        var suffix = condition ? "true" : "false";
        var selectedSlot = condition ? 0 : 1;
        var unselectedSlot = condition ? 1 : 0;
        var selectedNode = condition ? "true-" + suffix : "false-" + suffix;
        var skippedNode = condition ? "false-" + suffix : "true-" + suffix;
        return context.Statuses["if-" + suffix] == FlowNodeExecutionStatus.Succeeded
            && context.Statuses[selectedNode] == FlowNodeExecutionStatus.Succeeded
            && context.Statuses[skippedNode] == FlowNodeExecutionStatus.Skipped
            && context.TryGetPortValue(
                "if-" + suffix,
                selectedSlot,
                out var selectedSignal)
            && Equals(selectedSignal, FlowControlSignal.Active)
            && !context.TryGetPortValue(
                "if-" + suffix,
                unselectedSlot,
                out _)
            && Equals(
                GetContractOutput(context, selectedNode),
                condition ? "TRUE" : "FALSE");
    }

    private static bool XamlHasDynamicResourceAttribute(XDocument document)
    {
        const string prefix = "{DynamicResource ";
        return document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value.Trim())
            .Any(value => value.StartsWith(prefix, StringComparison.Ordinal)
                && value.EndsWith("}", StringComparison.Ordinal)
                && value.Length > prefix.Length + 1);
    }

    private static bool XamlHasHexColorAttribute(XDocument document)
    {
        return document.Descendants()
            .SelectMany(element => element.Attributes())
            .Any(attribute => Regex.IsMatch(
                attribute.Value,
                @"(?<![0-9A-Fa-f])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})(?![0-9A-Fa-f])",
                RegexOptions.CultureInvariant));
    }

    private static IReadOnlyList<Type> FindBusinessControlAllocations(Type rootType)
    {
        var businessControlTypes = new[]
        {
            typeof(StackPanel),
            typeof(Grid),
            typeof(BusinessBorder),
            typeof(TextBlock),
            typeof(TextBox),
            typeof(CheckBox),
            typeof(Button),
            typeof(RoundButton),
        };
        return EnumerateDeclaredTypeTree(rootType)
            .SelectMany(EnumerateDeclaredMethods)
            .SelectMany(FindConstructedTypes)
            .Where(constructedType => businessControlTypes.Any(
                businessType => businessType.IsAssignableFrom(constructedType)))
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<Type> EnumerateDeclaredTypeTree(Type rootType)
    {
        yield return rootType;
        foreach (var nestedType in rootType.GetNestedTypes(
            BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var type in EnumerateDeclaredTypeTree(nestedType))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<MethodBase> EnumerateDeclaredMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        return type.GetConstructors(flags).Cast<MethodBase>()
            .Concat(type.GetMethods(flags));
    }

    private static IEnumerable<Type> FindConstructedTypes(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il == null)
        {
            yield break;
        }

        var offset = 0;
        while (offset < il.Length)
        {
            var opCode = ReadOpCode(il, ref offset);
            if (opCode.Equals(OpCodes.Newobj))
            {
                var token = BitConverter.ToInt32(il, offset);
                var declaringTypeArguments = method.DeclaringType?.IsGenericType == true
                    ? method.DeclaringType.GetGenericArguments()
                    : null;
                var methodArguments = method is MethodInfo methodInfo && methodInfo.IsGenericMethod
                    ? methodInfo.GetGenericArguments()
                    : null;
                var constructor = method.Module.ResolveMethod(
                    token,
                    declaringTypeArguments,
                    methodArguments);
                if (constructor?.DeclaringType != null)
                {
                    yield return constructor.DeclaringType;
                }
            }

            offset += GetOperandSize(opCode.OperandType, il, offset);
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var firstByte = il[offset++];
        var value = firstByte == 0xFE
            ? (short)(0xFE00 | il[offset++])
            : (short)firstByte;
        var opCode = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .Single(candidate => candidate.Value == value);
        return opCode;
    }

    private static int GetOperandSize(
        OperandType operandType,
        byte[] il,
        int operandOffset)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                return 4;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                return 4 + (BitConverter.ToInt32(il, operandOffset) * 4);
            default:
                throw new InvalidOperationException(
                    $"Unsupported IL operand type '{operandType}'.");
        }
    }

    private sealed class SourcePolicyIlFixture
    {
        internal static object CreateImplicitGrid()
        {
            Grid grid = new();
            return grid;
        }

        internal static object CreateQualifiedButton()
        {
            return new global::System.Windows.Controls.Button();
        }

        internal static object CreateAliasedBorder()
        {
            return new BusinessBorder();
        }
    }

    private sealed record BuiltInContract(
        string TypeKey,
        string DisplayName,
        string Category,
        string CategoryIcon,
        string Icon,
        Type ModelType,
        Type ExecutorType,
        Type ViewType,
        BuiltInPortContract[] Inputs,
        BuiltInPortContract[] Outputs);

    private sealed record BuiltInPortContract(
        string Id,
        string DisplayName,
        FlowDataType DataType,
        bool IsRequired);
}
