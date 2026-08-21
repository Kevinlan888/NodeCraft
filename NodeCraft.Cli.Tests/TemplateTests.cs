using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal sealed class CSharpSourceFile
    {
        public CSharpSourceFile(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }

        public string Text { get; }
    }

    internal static class PluginPortOwnershipSyntax
    {
        public static bool ValidateNoLegacyDependencies(IEnumerable<CSharpSourceFile> sources)
        {
            var roots = Parse(sources);
            return roots != null && roots.All(root => !HasLegacyDependency(root));
        }

        public static bool ValidateGeneratedSources(
            IEnumerable<CSharpSourceFile> sources,
            string pluginEntryPath,
            string nodeModelPath,
            string nodeExecutorPath)
        {
            var roots = Parse(sources);
            if (roots == null || roots.Any(HasLegacyDependency))
            {
                return false;
            }

            var declarations = roots
                .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                .Where(declaration => declaration.Identifier.ValueText == "NodePortIds")
                .ToArray();
            if (declarations.Length != 1)
            {
                return false;
            }

            var declaration = declarations[0];
            if (declaration.Parent is not BaseNamespaceDeclarationSyntax
                || !string.Equals(declaration.SyntaxTree.FilePath, nodeModelPath, StringComparison.OrdinalIgnoreCase)
                || !HasExactlyModifiers(declaration.Modifiers, SyntaxKind.InternalKeyword, SyntaxKind.StaticKeyword)
                || !HasExactPortFields(declaration))
            {
                return false;
            }

            var pluginEntry = FindRoot(roots, pluginEntryPath);
            var nodeModel = FindRoot(roots, nodeModelPath);
            var nodeExecutor = FindRoot(roots, nodeExecutorPath);
            return pluginEntry != null
                && nodeModel != null
                && nodeExecutor != null
                && HasMemberAccess(pluginEntry, "NodePortIds", "Output")
                && HasMemberAccess(nodeModel, "NodePortIds", "Value")
                && HasMemberAccess(nodeModel, "NodePortIds", "Output")
                && HasMemberAccess(nodeExecutor, "NodePortIds", "Value")
                && HasMemberAccess(nodeExecutor, "NodePortIds", "Output");
        }

        private static CompilationUnitSyntax[]? Parse(IEnumerable<CSharpSourceFile> sources)
        {
            var roots = sources
                .Select(source => CSharpSyntaxTree.ParseText(source.Text, path: source.Path))
                .Select(tree => new { Tree = tree, Diagnostics = tree.GetDiagnostics().ToArray() })
                .ToArray();
            if (roots.Length == 0 || roots.Any(item => item.Diagnostics.Length != 0))
            {
                return null;
            }

            return roots.Select(item => item.Tree.GetCompilationUnitRoot()).ToArray();
        }

        private static bool HasLegacyDependency(CompilationUnitSyntax root)
        {
            return root.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(usingDirective =>
                    usingDirective.Name != null
                    && IsLegacyFlowNodesName(usingDirective.Name))
                || root.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Any(identifier => identifier.Identifier.ValueText == "BuiltInNodePorts");
        }

        private static bool IsLegacyFlowNodesName(NameSyntax name)
        {
            var identifiers = name
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Select(identifier => identifier.Identifier.ValueText)
                .Where(identifier => identifier != "global")
                .ToArray();
            return identifiers.SequenceEqual(new[] { "NodeCraft", "Flow", "Nodes" });
        }

        private static CompilationUnitSyntax? FindRoot(
            IEnumerable<CompilationUnitSyntax> roots,
            string path)
        {
            return roots.SingleOrDefault(root =>
                string.Equals(root.SyntaxTree.FilePath, path, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasExactlyModifiers(SyntaxTokenList modifiers, params SyntaxKind[] expected)
        {
            return modifiers.Count == expected.Length
                && expected.All(kind => modifiers.Any(modifier => modifier.IsKind(kind)));
        }

        private static bool HasExactPortFields(ClassDeclarationSyntax declaration)
        {
            var fields = declaration.Members.OfType<FieldDeclarationSyntax>().ToArray();
            if (declaration.Members.Count != 2 || fields.Length != 2)
            {
                return false;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (!HasExactlyModifiers(field.Modifiers, SyntaxKind.InternalKeyword, SyntaxKind.ConstKeyword)
                    || field.Declaration.Type is not PredefinedTypeSyntax predefinedType
                    || !predefinedType.Keyword.IsKind(SyntaxKind.StringKeyword)
                    || field.Declaration.Variables.Count != 1)
                {
                    return false;
                }

                var variable = field.Declaration.Variables[0];
                if (variable.Initializer?.Value is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression)
                    || !values.TryAdd(variable.Identifier.ValueText, literal.Token.ValueText))
                {
                    return false;
                }
            }

            return values.Count == 2
                && values.TryGetValue("Value", out var value)
                && value == "value"
                && values.TryGetValue("Output", out var output)
                && output == "output";
        }

        private static bool HasMemberAccess(
            CompilationUnitSyntax root,
            string owner,
            string member)
        {
            return root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(access => access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                    && access.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == owner
                    && access.Name.Identifier.ValueText == member);
        }
    }

    internal static class TemplateTests
    {
        private static ProjectOptions CreateOptions(bool withUi, bool withPrivateDependency)
        {
            return new ProjectOptions
            {
                ProjectName = "MyPlugin",
                DisplayName = "My Nodes",
                PluginId = "company.myplugin.nodes",
                TypeKeyPrefix = "company.myplugin.nodes",
                FlowProjectPath = @"..\NodeCraft.Flow\NodeCraft.Flow.csproj",
                IncludeCustomUi = withUi,
                IncludePrivateDependency = withPrivateDependency,
            };
        }

        private static CSharpSourceFile[] CreateCSharpTemplateSources(ProjectOptions options)
        {
            var sources = new List<CSharpSourceFile>
            {
                new CSharpSourceFile("Plugin/MyPlugin.cs", TemplateText.PluginEntryFull(options)),
                new CSharpSourceFile("Nodes/MyPluginNodeModel.cs", TemplateText.Fill(TemplateText.NodeModel, options)),
                new CSharpSourceFile("Nodes/MyPluginNodeExecutor.cs", TemplateText.NodeExecutorFull(options)),
            };
            if (options.IncludeCustomUi)
            {
                sources.Add(new CSharpSourceFile(
                    "Views/MyPluginNodeEditor.xaml.cs",
                    TemplateText.Fill(TemplateText.NodeEditorCode, options)));
            }

            if (options.IncludePrivateDependency)
            {
                sources.Add(new CSharpSourceFile(
                    "PrivateDependency/MyPluginFormatter.cs",
                    TemplateText.Fill(TemplateText.PrivateFormatter, options)));
            }

            return sources.ToArray();
        }

        public static void RunAll()
        {
            Program.Run("fill substitutes all core placeholders", () =>
            {
                var text = TemplateText.Fill(TemplateText.PluginJson, CreateOptions(false, false));
                return text.Contains("company.myplugin.nodes")
                    && text.Contains("MyPlugin.dll")
                    && text.Contains("MyPlugin.Plugin.MyPlugin");
            });

            Program.Run("csproj core has flow reference and staging target", () =>
            {
                var text = TemplateText.BuildCsproj(CreateOptions(false, false));
                return text.Contains(@"..\NodeCraft.Flow\NodeCraft.Flow.csproj")
                    && text.Contains("StagePluginPackage")
                    && !text.Contains("EmbeddedResource Include")
                    && !text.Contains("PrivateDependency");
            });

            Program.Run("csproj with ui embeds the view", () =>
            {
                var text = TemplateText.BuildCsproj(CreateOptions(true, false));
                return text.Contains(@"EmbeddedResource Include=""Views\MyPluginNodeEditor.xaml""")
                    && text.Contains(@"Page Remove=""Views\MyPluginNodeEditor.xaml""");
            });

            Program.Run("csproj with private dependency references and stages it", () =>
            {
                var text = TemplateText.BuildCsproj(CreateOptions(false, true));
                return text.Contains(@"PrivateDependency\PrivateDependency.csproj")
                    && text.Contains("MyPlugin.PrivateDependency.dll")
                    && text.Contains(@"Compile Remove=""PrivateDependency\**\*.cs""");
            });

            Program.Run("csproj staging target deletes private dll from package root", () =>
            {
                var withPrivate = TemplateText.BuildCsproj(CreateOptions(false, true));
                var core = TemplateText.BuildCsproj(CreateOptions(false, false));
                return withPrivate.Contains(
                        @"<Delete Files=""$(TargetDir)NodeCraft.Flow.dll;$(TargetDir)CommonControls.WPF.dll;$(TargetDir)lib\NodeCraft.Flow.dll;$(TargetDir)lib\CommonControls.WPF.dll;$(TargetDir)MyPlugin.PrivateDependency.dll"" />")
                    && !core.Contains("PrivateDependency.dll");
            });

            Program.Run("plugin entry with ui includes ContentFactory", () =>
            {
                var text = TemplateText.PluginEntryFull(CreateOptions(true, false));
                return text.Contains("ContentFactory = MyPluginNodeEditor.CreateContent,")
                    && text.Contains("using MyPlugin.Views;");
            });

            Program.Run("plugin entry without ui omits ContentFactory", () =>
            {
                var text = TemplateText.PluginEntryFull(CreateOptions(false, false));
                return !text.Contains("ContentFactory")
                    && !text.Contains("MyPlugin.Views");
            });

            Program.Run("editor code resource name matches csproj embedding", () =>
            {
                var options = CreateOptions(true, false);
                return TemplateText.Fill(TemplateText.NodeEditorCode, options)
                    .Contains(@"""MyPlugin.Views.MyPluginNodeEditor.xaml""");
            });

            Program.Run("type key uses prefix and camel-cased node name", () =>
            {
                var options = CreateOptions(false, false);
                return TemplateText.NodeExecutorFull(options)
                    .Contains("company.myplugin.nodes.myPlugin");
            });

            Program.Run("generated C# templates own one local port identifier declaration", () =>
            {
                var defaultSources = CreateCSharpTemplateSources(CreateOptions(false, false));
                var allFeatureSources = CreateCSharpTemplateSources(CreateOptions(true, true));
                return PluginPortOwnershipSyntax.ValidateGeneratedSources(
                        defaultSources,
                        "Plugin/MyPlugin.cs",
                        "Nodes/MyPluginNodeModel.cs",
                        "Nodes/MyPluginNodeExecutor.cs")
                    && PluginPortOwnershipSyntax.ValidateGeneratedSources(
                        allFeatureSources,
                        "Plugin/MyPlugin.cs",
                        "Nodes/MyPluginNodeModel.cs",
                        "Nodes/MyPluginNodeExecutor.cs");
            });

            Program.Run("generated port ownership policy rejects raw-text decoys", () =>
            {
                const string pluginEntryDecoy = @"
/*
internal static class NodePortIds
internal const string Value = ""value"";
internal const string Output = ""output"";
*/
using NodeCraft.Flow . Nodes;

namespace Decoy
{
    internal sealed class PluginEntry
    {
        internal string Output => ""output"";
    }
}";
                const string nodeModelDecoy = @"
namespace Decoy
{
    internal sealed class NodeModel
    {
        internal string Value => ""value"";
        internal string Output => ""output"";
    }
}";
                const string nodeExecutorDecoy = @"
namespace Decoy
{
    internal sealed class NodeExecutor
    {
        internal string Value => ""value"";
        internal string Output => ""output"";
    }
}";
                const string globalAliasUsing = @"
using global::NodeCraft.Flow.Nodes;
namespace Decoy { internal sealed class Consumer { } }
";
                const string escapedIdentifierUsing = @"
using NodeCraft.Flow.@Nodes;
namespace Decoy { internal sealed class Consumer { } }
";

                return !PluginPortOwnershipSyntax.ValidateGeneratedSources(
                    new[]
                    {
                        new CSharpSourceFile("Plugin/Decoy.cs", pluginEntryDecoy),
                        new CSharpSourceFile("Nodes/DecoyNodeModel.cs", nodeModelDecoy),
                        new CSharpSourceFile("Nodes/DecoyNodeExecutor.cs", nodeExecutorDecoy),
                    },
                    "Plugin/Decoy.cs",
                    "Nodes/DecoyNodeModel.cs",
                    "Nodes/DecoyNodeExecutor.cs")
                    && !PluginPortOwnershipSyntax.ValidateNoLegacyDependencies(new[]
                    {
                        new CSharpSourceFile("GlobalAliasUsing.cs", globalAliasUsing),
                    })
                    && !PluginPortOwnershipSyntax.ValidateNoLegacyDependencies(new[]
                    {
                        new CSharpSourceFile("EscapedIdentifierUsing.cs", escapedIdentifierUsing),
                    });
            });

            Program.Run("no template leaves unreplaced placeholders", () =>
            {
                var options = CreateOptions(true, true);
                var allTemplates = new[]
                {
                    TemplateText.PluginJson,
                    TemplateText.BuildCsproj(options),
                    TemplateText.PluginEntryFull(options),
                    TemplateText.NodeModel,
                    TemplateText.NodeExecutorFull(options),
                    TemplateText.NodeEditorXaml,
                    TemplateText.NodeEditorCode,
                    TemplateText.PrivateCsproj,
                    TemplateText.PrivateFormatter,
                };
                foreach (var template in allTemplates)
                {
                    if (TemplateText.Fill(template, options).Contains("{{"))
                    {
                        return false;
                    }
                }

                return true;
            });

            Program.Run("published target copies to Plugins folder under project name", () =>
            {
                var text = TemplateText.BuildCsproj(CreateOptions(false, false));
                return text.Contains(@"$(NodeCraftDeployPath)\Plugins\$(ProjectName)");
            });
        }
    }
}
