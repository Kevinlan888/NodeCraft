using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private static async Task RunBuiltInPreviewNodeTestsAsync()
    {
        Run("BuiltIn project and manifest expose the requested plugin identity", () =>
        {
            var projectPath = FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj");
            var projectText = File.ReadAllText(projectPath);
            var project = XDocument.Load(projectPath);
            var properties = project.Root?.Elements("PropertyGroup").FirstOrDefault();
            var projectReference = project.Descendants("ProjectReference").Single();
            var xamlPaths = new[]
            {
                @"Views\StringValueEditor.xaml",
                @"Views\AppendTextEditor.xaml",
                @"Views\TextPreviewView.xaml",
                @"Views\JsonSerializeView.xaml",
                @"Views\ToStringView.xaml",
                @"Views\StringConcatEditor.xaml",
            };

            using var manifest = JsonDocument.Parse(File.ReadAllText(
                FindRepositoryFile("NodeCraft.BuiltIn", "plugin.json")));
            var root = manifest.RootElement;
            var solution = File.ReadAllText(FindRepositoryFile("NodeCraft.sln"));

            return (string?)properties?.Element("TargetFramework") == "net8.0-windows"
                && (string?)properties?.Element("UseWPF") == "true"
                && (string?)properties?.Element("Nullable") == "disable"
                && (string?)properties?.Element("LangVersion") == "9.0"
                && (string?)properties?.Element("PlatformTarget") == "x64"
                && (string?)properties?.Element("Prefer32Bit") == "false"
                && (string?)properties?.Element("AssemblyName") == "NodeCraft.BuiltIn"
                && (string?)properties?.Element("RootNamespace") == "NodeCraft.BuiltIn"
                && (string?)projectReference.Attribute("Include") == @"..\NodeCraft.Flow\NodeCraft.Flow.csproj"
                && (string?)projectReference.Attribute("Private") == "false"
                && xamlPaths.All(path => !projectText.Contains($"<Page Remove=\"{path}\" />", StringComparison.Ordinal)
                    && !projectText.Contains($"<EmbeddedResource Include=\"{path}\" />", StringComparison.Ordinal))
                && PreviewViewsCompileToBaml(xamlPaths)
                && root.GetProperty("id").GetString() == "nodecraft.builtin"
                && root.GetProperty("entryAssembly").GetString() == "NodeCraft.BuiltIn.dll"
                && root.GetProperty("entryType").GetString() == "NodeCraft.BuiltIn.Plugin.BuiltInPlugin"
                && root.GetProperty("apiVersion").GetString() == "1.0"
                && root.GetProperty("privateLibraryPath").GetString() == "lib"
                && solution.Contains("{C8F6B4D1-1F73-4D7C-A58E-9B2E6F307A41}", StringComparison.Ordinal);
        });

        Run("BuiltIn plugin stages exactly six Preview node contracts", () =>
        {
            var plugin = new BuiltInPlugin();
            var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
            plugin.Register(context);
            var registrations = context.Registrations
                .Where(item => item.Definition.Category == "Preview")
                .ToArray();
            var expected = new[]
            {
                new PreviewContract("nodecraft.builtin.string-value", "String Value", "固定字符串输出", "FormatText", typeof(StringValueNodeModel), typeof(StringValueExecutor), Array.Empty<PortContract>(), new[] { new PortContract("output", "Value", FlowDataType.String, false) }),
                new PreviewContract("nodecraft.builtin.append-text", "Append Text", "给字符串追加后缀", "ViewDashboardOutline", typeof(AppendTextNodeModel), typeof(AppendTextExecutor), new[] { new PortContract("input", "Input", FlowDataType.String, true) }, new[] { new PortContract("output", "Output", FlowDataType.String, false) }),
                new PreviewContract("nodecraft.builtin.text-preview", "Text Preview", "显示任意输入的文本结果", "EyeOutline", typeof(TextPreviewNodeModel), typeof(TextPreviewExecutor), new[] { new PortContract("input", "Input", FlowDataType.Object, true) }, new[] { new PortContract("output", "Output", FlowDataType.Object, false) }),
                new PreviewContract("nodecraft.builtin.json-serialize", "JSON Serialize", "将任意输入格式化为多行 JSON", "ViewDashboardOutline", typeof(JsonSerializeNodeModel), typeof(JsonSerializeExecutor), new[] { new PortContract("input", "Input", FlowDataType.Object, true) }, new[] { new PortContract("output", "JSON", FlowDataType.String, false) }),
                new PreviewContract("nodecraft.builtin.to-string", "To String", "将任意输入转换为字符串", "FormatText", typeof(ToStringNodeModel), typeof(ToStringExecutor), new[] { new PortContract("input", "Input", FlowDataType.Object, true) }, new[] { new PortContract("output", "Output", FlowDataType.String, false) }),
                new PreviewContract("nodecraft.builtin.string-concat", "String Concat", "按顺序连接多个字符串", "ViewDashboardOutline", typeof(StringConcatNodeModel), typeof(StringConcatExecutor), Array.Empty<PortContract>(), new[] { new PortContract("output", "Output", FlowDataType.String, false) }),
            };

            return plugin.Metadata.Id == "nodecraft.builtin"
                && plugin.Metadata.DisplayName == "Built-in Nodes"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && registrations.Length == expected.Length
                && registrations.Select(item => item.Definition.TypeKey)
                    .SequenceEqual(expected.Select(item => item.TypeKey), StringComparer.Ordinal)
                && registrations.Zip(expected, RegistrationMatchesContract).All(value => value);
        });

        Run("BuiltIn Preview factories return fresh matching models and executors", () =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out _);
            return registrations.All(registration =>
            {
                var firstNode = registration.NodeFactory();
                var secondNode = registration.NodeFactory();
                var firstExecutor = registration.ExecutorFactory();
                var secondExecutor = registration.ExecutorFactory();
                return firstNode.GetType() == registration.NodeModelType
                    && secondNode.GetType() == registration.NodeModelType
                    && !ReferenceEquals(firstNode, secondNode)
                    && firstNode.ExecutorType == registration.Definition.TypeKey
                    && secondNode.ExecutorType == registration.Definition.TypeKey
                    && firstExecutor.GetType() == secondExecutor.GetType()
                    && !ReferenceEquals(firstExecutor, secondExecutor);
            });
        });

        Run("BuiltIn Preview models preserve defaults and plugin-local port contracts", () =>
        {
            var value = new StringValueNodeModel();
            var append = new AppendTextNodeModel();
            var preview = new TextPreviewNodeModel();
            var json = new JsonSerializeNodeModel();
            return value.ValueText == "ComfyUI"
                && append.SuffixText == " from DemoApp"
                && preview.LastPreviewText == string.Empty
                && value.OutputParameters.Single().PortId == "output"
                && append.InputParameters.Single().PortId == "input"
                && append.OutputParameters.Single().PortId == "output"
                && preview.InputParameters.Single().Parameter.ParameterType == FlowDataType.Object.Key
                && preview.OutputParameters.Single().Parameter.ParameterType == FlowDataType.Object.Key
                && json.InputParameters.Single().Parameter.ParameterType == FlowDataType.Object.Key
                && json.OutputParameters.Single().Parameter.ParameterType == FlowDataType.String.Key
                && !typeof(BuiltInPortIds).IsPublic;
        });

        await RunAsync("BuiltIn Preview executors preserve values, JSON formatting, and cancellation", async () =>
        {
            var definition = new FlowNodeDefinition();
            var context = new FlowExecutionContext();
            var stringNode = new WorkflowNode { Inputs = { ["value"] = "NodeCraft" } };
            var appendNode = new WorkflowNode { Inputs = { ["suffix"] = " rocks" } };
            var stringOutput = await new StringValueExecutor().ExecuteAsync(context, stringNode, definition, new Dictionary<string, object>(), CancellationToken.None);
            var appendOutput = await new AppendTextExecutor().ExecuteAsync(context, appendNode, definition, new Dictionary<string, object> { ["input"] = "NodeCraft" }, CancellationToken.None);
            var previewValue = new object();
            var previewOutput = await new TextPreviewExecutor().ExecuteAsync(context, new WorkflowNode(), definition, new Dictionary<string, object> { ["input"] = previewValue }, CancellationToken.None);
            var jsonOutput = await new JsonSerializeExecutor().ExecuteAsync(context, new WorkflowNode(), definition, new Dictionary<string, object> { ["input"] = new Dictionary<string, object> { ["name"] = "NodeCraft" } }, CancellationToken.None);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            return Equals(stringOutput["output"], "NodeCraft")
                && Equals(appendOutput["output"], "NodeCraft rocks")
                && ReferenceEquals(previewOutput["output"], previewValue)
                && Equals(jsonOutput["output"], "{\r\n  \"name\": \"NodeCraft\"\r\n}")
                && Throws<OperationCanceledException>(() => new JsonSerializeExecutor().ExecuteAsync(context, new WorkflowNode(), definition, new Dictionary<string, object>(), cancelled.Token).GetAwaiter().GetResult());
        });

        Run("BuiltIn Preview content factories return fresh typed XAML views", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out var registry);
            var expectedViewTypes = new[]
            {
                typeof(StringValueEditor),
                typeof(AppendTextEditor),
                typeof(TextPreviewView),
                typeof(JsonSerializeView),
                typeof(ToStringView),
                typeof(StringConcatEditor),
            };

            return registrations.Zip(expectedViewTypes, (registration, viewType) =>
            {
                var canvas = CreateHeadlessCanvas();
                var node = registration.NodeFactory();
                canvas.GraphModel.Nodes.Add(node);
                var first = registry.BuildNodeContent(canvas, node);
                var second = registry.BuildNodeContent(canvas, node);
                return first?.GetType() == viewType
                    && second?.GetType() == viewType
                    && !ReferenceEquals(first, second);
            }).All(value => value);
        }));

        Run("BuiltIn string editors update models and notify once per real edit", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out var registry);
            var valueRegistration = registrations.Single(item => item.Definition.TypeKey == StringValueNodeModel.FlowNodeTypeKey);
            var appendRegistration = registrations.Single(item => item.Definition.TypeKey == AppendTextNodeModel.FlowNodeTypeKey);
            var valueNode = new StringValueNodeModel();
            var appendNode = new AppendTextNodeModel();
            var valueCanvas = CreateHeadlessCanvas();
            var appendCanvas = CreateHeadlessCanvas();
            var valueChanges = 0;
            var appendChanges = 0;
            valueCanvas.GraphChanged += (_, _) => valueChanges++;
            appendCanvas.GraphChanged += (_, _) => appendChanges++;
            var valueView = (FrameworkElement)registry.BuildNodeContent(valueCanvas, valueNode);
            var appendView = (FrameworkElement)registry.BuildNodeContent(appendCanvas, appendNode);
            var valueEditor = FindLogicalDescendants<TextBox>(valueView).Single();
            var suffixEditor = FindLogicalDescendants<TextBox>(appendView).Single();

            valueEditor.Text = "ComfyUI";
            suffixEditor.Text = " from DemoApp";
            valueEditor.Text = "NodeCraft";
            suffixEditor.Text = " plugin";

            return valueNode.ValueText == "NodeCraft"
                && appendNode.SuffixText == " plugin"
                && valueChanges == 1
                && appendChanges == 1
                && valueRegistration.ContentFactory != null
                && appendRegistration.ContentFactory != null;
        }));

        Run("BuiltIn Preview content factories reject mismatched models informatively", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out _);
            return registrations.All(registration =>
            {
                try
                {
                    registration.ContentFactory(new FlowCanvas(), new NodeModel());
                    return false;
                }
                catch (InvalidOperationException exception)
                {
                    return exception.Message.Contains(registration.NodeModelType.Name, StringComparison.Ordinal);
                }
            });
        }));

        Run("BuiltIn Text Preview rebuild displays the latest result or placeholder", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out var registry);
            var registration = registrations.Single(item => item.Definition.TypeKey == TextPreviewNodeModel.FlowNodeTypeKey);
            var canvas = CreateHeadlessCanvas();
            var node = new TextPreviewNodeModel { Id = "preview" };
            canvas.GraphModel.Nodes.Add(node);
            var emptyView = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var emptyTexts = FindLogicalDescendants<TextBox>(emptyView).Select(item => item.Text).ToArray();
            var execution = new FlowExecutionContext();
            execution.SetPortValue(node.Id, 0, "current preview");
            registry.ApplyExecutionResults(new[] { node }, execution);
            var currentView = (FrameworkElement)registry.BuildNodeContent(canvas, node);
            var currentTexts = FindLogicalDescendants<TextBox>(currentView).Select(item => item.Text).ToArray();

            return registration.ExecutionResultHandler != null
                && emptyTexts.Contains("等待执行后显示文本结果", StringComparer.Ordinal)
                && currentTexts.Contains("current preview", StringComparer.Ordinal)
                && node.LastPreviewText == "current preview";
        }));

        Run("BuiltIn JSON view summarizes unary input connection through its local registry", () => RunOnSta(() =>
        {
            var registrations = CreateBuiltInPreviewRegistry(out var registry);
            var source = new StringValueNodeModel { Id = "source", Name = "Prompt" };
            var json = new JsonSerializeNodeModel { Id = "json" };
            var link = new GraphLink
            {
                Id = "source-json",
                OriginNodeId = source.Id,
                OriginSlot = 0,
                TargetNodeId = json.Id,
                TargetSlot = 1,
            };
            json.InputParameters.Single().LinkId = link.Id;
            var canvas = CreateHeadlessCanvas();
            canvas.GraphModel.Nodes.Add(source);
            canvas.GraphModel.Nodes.Add(json);
            canvas.GraphModel.Links.Add(link);
            var unlinkedCanvas = CreateHeadlessCanvas();
            var unlinked = new JsonSerializeNodeModel();
            unlinkedCanvas.GraphModel.Nodes.Add(unlinked);

            var linkedView = (FrameworkElement)registry.BuildNodeContent(canvas, json);
            var unlinkedView = (FrameworkElement)registry.BuildNodeContent(unlinkedCanvas, unlinked);
            var linkedTexts = FindLogicalDescendants<TextBlock>(linkedView).Select(item => item.Text).ToArray();
            var unlinkedTexts = FindLogicalDescendants<TextBlock>(unlinkedView).Select(item => item.Text).ToArray();

            return registrations.Count == 6
                && linkedTexts.Contains("Prompt · Value", StringComparer.Ordinal)
                && unlinkedTexts.Contains("未连接", StringComparer.Ordinal);
        }));

        Run("BuiltIn JSON view requires the registry content route", () => RunOnSta(() =>
        {
            try
            {
                JsonSerializeView.CreateContent(new FlowCanvas(), new JsonSerializeNodeModel());
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message.Contains("registry", StringComparison.OrdinalIgnoreCase);
            }
        }));

        Run("BuiltIn Preview views keep layout in embedded themed XAML", () =>
        {
            var views = new[]
            {
                new[] { "StringValueEditor", "ValueEditor" },
                new[] { "AppendTextEditor", "SuffixEditor" },
                new[] { "TextPreviewView", "PreviewText" },
                new[] { "JsonSerializeView", "InputValue" },
                new[] { "ToStringView", "Input" },
                new[] { "StringConcatEditor", "SeparatorEditor" },
            };
            var forbidden = new[] { "new StackPanel", "new TextBlock", "new TextBox", "new Button", "new Border" };
            return views.All(view =>
            {
                var xaml = File.ReadAllText(FindRepositoryFile("NodeCraft.BuiltIn", "Views", view[0] + ".xaml"));
                var codeBehind = File.ReadAllText(FindRepositoryFile("NodeCraft.BuiltIn", "Views", view[0] + ".xaml.cs"));
                return xaml.Contains("DynamicResource", StringComparison.Ordinal)
                    && xaml.Contains("x:Name=\"" + view[1] + "\"", StringComparison.Ordinal)
                    && !xaml.Contains("#", StringComparison.Ordinal)
                    && forbidden.All(text => !codeBehind.Contains(text, StringComparison.Ordinal));
            });
        });
    }

    private static IReadOnlyList<FlowNodeRegistration> CreateBuiltInPreviewRegistry(out FlowNodeRegistry registry)
    {
        var plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
        plugin.Register(context);
        registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
        return context.Registrations
            .Where(item => item.Definition.Category == "Preview")
            .ToArray();
    }

    private static bool RegistrationMatchesContract(FlowNodeRegistration registration, PreviewContract expected)
    {
        var firstNode = registration.NodeFactory?.Invoke();
        var secondNode = registration.NodeFactory?.Invoke();
        var firstExecutor = registration.ExecutorFactory();
        var secondExecutor = registration.ExecutorFactory();
        return registration.Definition.TypeKey == expected.TypeKey
            && registration.Definition.DisplayName == expected.DisplayName
            && registration.Definition.Category == "Preview"
            && registration.PaletteDisplayName == expected.DisplayName
            && registration.PaletteDescription == expected.Description
            && registration.PaletteCategoryIconKind == "ViewDashboardOutline"
            && registration.PaletteIconKind == expected.Icon
            && registration.ShowInPalette
            && registration.NodeModelType == expected.ModelType
            && firstNode?.GetType() == expected.ModelType
            && secondNode?.GetType() == expected.ModelType
            && !ReferenceEquals(firstNode, secondNode)
            && firstExecutor.GetType() == expected.ExecutorType
            && secondExecutor.GetType() == expected.ExecutorType
            && !ReferenceEquals(firstExecutor, secondExecutor)
            && registration.ContentFactory != null
            && PortsMatch(registration.Definition.InputPorts, expected.Inputs, EIOType.Input, EPortDirection.Left)
            && PortsMatch(registration.Definition.OutputPorts, expected.Outputs, EIOType.Output, EPortDirection.Right);
    }

    private static bool PortsMatch(
        IReadOnlyList<FlowPortDefinition> actual,
        IReadOnlyList<PortContract> expected,
        EIOType ioType,
        EPortDirection direction)
    {
        return actual.Count == expected.Count
            && actual.Zip(expected, (port, contract) =>
                port.Id == contract.Id
                && port.DisplayName == contract.DisplayName
                && port.IOType == ioType
                && port.DataType.Equals(contract.DataType)
                && port.PreferredDirection == direction
                && port.IsRequired == contract.IsRequired).All(value => value);
    }

    private sealed record PreviewContract(
        string TypeKey,
        string DisplayName,
        string Description,
        string Icon,
        Type ModelType,
        Type ExecutorType,
        IReadOnlyList<PortContract> Inputs,
        IReadOnlyList<PortContract> Outputs);

    private sealed record PortContract(
        string Id,
        string DisplayName,
        FlowDataType DataType,
        bool IsRequired);

    private static bool PreviewViewsCompileToBaml(string[] xamlPaths)
    {
        var assembly = typeof(NodeCraft.BuiltIn.Plugin.BuiltInPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "NodeCraft.BuiltIn.g.resources");
        if (stream == null)
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = new System.Resources.ResourceReader(stream))
        {
            foreach (var entry in reader.Cast<System.Collections.DictionaryEntry>())
            {
                keys.Add((string)entry.Key);
            }
        }

        return xamlPaths.All(path =>
        {
            var viewName = System.IO.Path.GetFileNameWithoutExtension(path);
            return keys.Contains(
                "views/" + viewName.ToLowerInvariant() + ".baml",
                StringComparer.Ordinal);
        });
    }
}
