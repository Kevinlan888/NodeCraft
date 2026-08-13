using System;
using System.Text;

namespace NodeCraft.Cli
{
    /// <summary>
    /// Static templates for generated plugin projects. Placeholders are
    /// {{Name}} tokens filled by <see cref="Fill"/>.
    /// </summary>
    public static class TemplateText
    {
        public static string Fill(string template, ProjectOptions options)
        {
            return template
                .Replace("{{ProjectName}}", options.ProjectName)
                .Replace("{{Namespace}}", options.Namespace)
                .Replace("{{PluginClassName}}", options.PluginClassName)
                .Replace("{{PluginId}}", options.PluginId)
                .Replace("{{DisplayName}}", options.DisplayName)
                .Replace("{{NodeName}}", options.NodeName)
                .Replace("{{NodeDisplayName}}", options.DisplayName)
                .Replace("{{TypeKey}}", options.TypeKey)
                .Replace("{{FlowProjectPath}}", options.FlowProjectPath)
                .Replace("{{PrivateAssemblyName}}", options.PrivateAssemblyName);
        }

        public static string PluginJson =>
            @"{
  ""id"": ""{{PluginId}}"",
  ""entryAssembly"": ""{{ProjectName}}.dll"",
  ""entryType"": ""{{Namespace}}.Plugin.{{PluginClassName}}"",
  ""apiVersion"": ""1.0"",
  ""privateLibraryPath"": ""lib""
}";

        public static string BuildCsproj(ProjectOptions options)
        {
            var builder = new StringBuilder();
            builder.Append(@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
    <LangVersion>9.0</LangVersion>
    <RootNamespace>{{Namespace}}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include=""{{FlowProjectPath}}"" Private=""false"" />
");

            if (options.IncludePrivateDependency)
            {
                builder.Append(@"    <ProjectReference Include=""PrivateDependency\PrivateDependency.csproj"" Private=""false"" />
");
            }

            builder.Append(@"  </ItemGroup>

  <ItemGroup>
    <None Update=""plugin.json"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
");

            if (options.IncludeCustomUi)
            {
                builder.Append(@"    <Page Remove=""Views\{{NodeName}}NodeEditor.xaml"" />
    <EmbeddedResource Include=""Views\{{NodeName}}NodeEditor.xaml"" />
");
            }

            if (options.IncludePrivateDependency)
            {
                builder.Append(@"    <Compile Remove=""PrivateDependency\**\*.cs"" />
    <EmbeddedResource Remove=""PrivateDependency\**\*"" />
    <None Remove=""PrivateDependency\**\*"" />
    <Page Remove=""PrivateDependency\**\*"" />
");
            }

            builder.Append(@"  </ItemGroup>

  <Target Name=""StagePluginPackage"" AfterTargets=""Build"">
");

            if (options.IncludePrivateDependency)
            {
                builder.Append(@"    <MakeDir Directories=""$(TargetDir)lib"" />
    <Copy
      SourceFiles=""$(MSBuildThisFileDirectory)PrivateDependency\bin\$(Configuration)\$(TargetFramework)\{{PrivateAssemblyName}}.dll""
      DestinationFolder=""$(TargetDir)lib""
      SkipUnchangedFiles=""true"" />
");
            }

            var deleteFiles = "$(TargetDir)NodeCraft.Flow.dll;$(TargetDir)CommonControls.WPF.dll;$(TargetDir)lib\\NodeCraft.Flow.dll;$(TargetDir)lib\\CommonControls.WPF.dll";
            if (options.IncludePrivateDependency)
            {
                deleteFiles += ";$(TargetDir){{PrivateAssemblyName}}.dll";
            }

            builder.Append($"    <Delete Files=\"{deleteFiles}\" />\n");
            builder.Append(@"  </Target>

  <Target Name=""PublishPlugin"" AfterTargets=""Build"" Condition=""'$(NodeCraftDeployPath)' != ''"">
    <MakeDir Directories=""$(NodeCraftDeployPath)\Plugins\$(ProjectName)"" />
    <ItemGroup>
      <PluginPackageFiles Include=""$(TargetDir)plugin.json;$(TargetDir)$(ProjectName).dll"" />
      <PluginPackageFiles Include=""$(TargetDir)$(ProjectName).pdb"" Condition=""Exists('$(TargetDir)$(ProjectName).pdb')"" />
    </ItemGroup>
    <Copy
      SourceFiles=""@(PluginPackageFiles)""
      DestinationFolder=""$(NodeCraftDeployPath)\Plugins\$(ProjectName)""
      SkipUnchangedFiles=""true"" />
    <Copy
      SourceFiles=""$(TargetDir)lib\*""
      DestinationFolder=""$(NodeCraftDeployPath)\Plugins\$(ProjectName)\lib""
      SkipUnchangedFiles=""true"" />
  </Target>
</Project>
");

            return Fill(builder.ToString(), options);
        }

        private static string PluginEntryHeader =>
            @"using System;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
using {{Namespace}}.Nodes;
";

        private static string PluginEntryCore =>
            @"namespace {{Namespace}}.Plugin
{
    public sealed class {{PluginClassName}} : IFlowPlugin
    {
        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = ""{{PluginId}}"",
            DisplayName = ""{{DisplayName}}"",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            context.Nodes.Register(Create{{NodeName}}Registration());
            context.Logger.LogInformation(""Registered {{DisplayName}} nodes."");
        }

        private static FlowNodeRegistration Create{{NodeName}}Registration()
        {
            return new FlowNodeRegistration(
                new FlowNodeDefinition
                {
                    TypeKey = {{NodeName}}Executor.FlowNodeTypeKey,
                    DisplayName = ""{{NodeDisplayName}}"",
                    Category = ""Value"",
                    OutputPorts =
                    {
                        new FlowPortDefinition
                        {
                            Id = BuiltInNodePorts.Output,
                            DisplayName = ""Value"",
                            IOType = EIOType.Output,
                            DataType = FlowDataType.String,
                            PreferredDirection = EPortDirection.Right,
                        },
                    },
                },
                () => new {{NodeName}}Executor())
            {
                NodeModelType = typeof({{NodeName}}NodeModel),
                NodeFactory = () => new {{NodeName}}NodeModel(),
                PaletteDisplayName = ""{{NodeDisplayName}}"",
                PaletteDescription = ""{{DisplayName}} node created by nodecraft-cli."",
";

        private static string PluginEntryUi =>
            @"                ContentFactory = {{NodeName}}NodeEditor.CreateContent,
";

        private static string PluginEntryEnd =>
            @"            };
        }
    }
}
";

        public static string PluginEntryFull(ProjectOptions options)
        {
            var builder = new StringBuilder(PluginEntryHeader);
            if (options.IncludeCustomUi)
            {
                builder.Append("using {{Namespace}}.Views;\n");
            }

            builder.Append("\n").Append(PluginEntryCore);
            if (options.IncludeCustomUi)
            {
                builder.Append(PluginEntryUi);
            }

            builder.Append(PluginEntryEnd);
            return Fill(builder.ToString(), options);
        }

        public static string NodeModel =>
            @"using System.Collections.Generic;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

namespace {{Namespace}}.Nodes
{
    public sealed class {{NodeName}}NodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public string TextValue { get; set; } = ""{{NodeDisplayName}}"";

        public {{NodeName}}NodeModel()
        {
            ExecutorType = {{NodeName}}Executor.FlowNodeTypeKey;
            Name = ""{{NodeDisplayName}}"";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter
                    {
                        ParameterType = FlowDataType.String.Key,
                    },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInNodePorts.Value] = TextValue ?? string.Empty;
        }
    }
}
";

        private static string NodeExecutorHeader =>
            @"using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
";

        public static string NodeExecutorFull(ProjectOptions options)
        {
            var builder = new StringBuilder(NodeExecutorHeader);
            if (options.IncludePrivateDependency)
            {
                builder.Append("using {{Namespace}}.PrivateDependency;\n");
            }

            builder.Append(@"
namespace {{Namespace}}.Nodes
{
    public sealed class {{NodeName}}Executor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = ""{{TypeKey}}"";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            node.Inputs.TryGetValue(BuiltInNodePorts.Value, out var value);
            var text = value as string ?? string.Empty;
");
            if (options.IncludePrivateDependency)
            {
                builder.Append("            var formatted = {{NodeName}}Formatter.Format(text);\n");
            }
            else
            {
                builder.Append("            var formatted = text;\n");
            }

            builder.Append(@"            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = formatted,
            };

            return Task.FromResult(outputs);
        }
    }
}
");
            return Fill(builder.ToString(), options);
        }

        public static string NodeEditorXaml =>
            @"<UserControl xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
             xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
             MinWidth=""180"">
    <Border
            Padding=""8""
            CornerRadius=""8""
            Background=""{DynamicResource colorSubtleBackground}""
            BorderBrush=""{DynamicResource colorNeutralStroke1}""
            BorderThickness=""1"">
        <StackPanel>
            <TextBlock Text=""{{NodeDisplayName}}""
                       FontWeight=""SemiBold""
                       Foreground=""{DynamicResource colorNeutralForeground1}""
                       Margin=""0,0,0,6"" />
            <TextBox x:Name=""ValueEditor""
                     Text=""{Binding TextValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"" />
        </StackPanel>
    </Border>
</UserControl>
";

        public static string NodeEditorCode =>
            @"using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using NodeCraft.Flow;
using {{Namespace}}.Nodes;

namespace {{Namespace}}.Views
{
    public class {{NodeName}}NodeEditor : UserControl
    {
        private readonly FlowCanvas _canvas;
        private readonly {{NodeName}}NodeModel _node;
        private readonly TextBox _valueEditor;
        private bool _isInitializing = true;

        public {{NodeName}}NodeEditor(FlowCanvas canvas, {{NodeName}}NodeModel node)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _node = node ?? throw new ArgumentNullException(nameof(node));

            var root = LoadEditorRoot();
            Content = root.Content;
            _valueEditor = root.FindName(""ValueEditor"") as TextBox
                ?? throw new InvalidOperationException(""{{NodeName}}NodeEditor is missing ValueEditor."");

            NameScope.SetNameScope(this, new NameScope());
            RegisterName(""ValueEditor"", _valueEditor);

            _valueEditor.TextChanged += ValueEditor_OnTextChanged;

            DataContext = _node;
            _valueEditor.Text = _node.TextValue ?? string.Empty;
            _isInitializing = false;
        }

        public static FrameworkElement CreateContent(FlowCanvas canvas, NodeModel node)
        {
            if (node is not {{NodeName}}NodeModel editorNode)
            {
                throw new InvalidOperationException(""{{NodeName}}NodeEditor requires a {{NodeName}}NodeModel."");
            }

            return new {{NodeName}}NodeEditor(canvas, editorNode);
        }

        private static UserControl LoadEditorRoot()
        {
            var assembly = typeof({{NodeName}}NodeEditor).Assembly;
            using var stream = assembly.GetManifestResourceStream(""{{Namespace}}.Views.{{NodeName}}NodeEditor.xaml"");
            if (stream == null)
            {
                throw new InvalidOperationException(""{{NodeName}}NodeEditor.xaml was not embedded into the plugin assembly."");
            }

            using var reader = new StreamReader(stream);
            return XamlReader.Parse(reader.ReadToEnd()) as UserControl
                ?? throw new InvalidOperationException(""{{NodeName}}NodeEditor.xaml did not produce a UserControl root."");
        }

        private void ValueEditor_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _node.TextValue = _valueEditor.Text ?? string.Empty;
            _canvas.NotifyGraphChanged();
        }
    }
}
";

        public static string PrivateCsproj =>
            @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net8.0-windows</TargetFrameworks>
    <Nullable>disable</Nullable>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>{{PrivateAssemblyName}}</AssemblyName>
    <RootNamespace>{{Namespace}}.PrivateDependency</RootNamespace>
  </PropertyGroup>
</Project>
";

        public static string PrivateFormatter =>
            @"namespace {{Namespace}}.PrivateDependency
{
    public static class {{NodeName}}Formatter
    {
        public static string Format(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value + "" (private)"";
        }
    }
}
";
    }
}
