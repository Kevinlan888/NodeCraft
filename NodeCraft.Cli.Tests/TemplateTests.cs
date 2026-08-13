using System;
using System.IO;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
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
