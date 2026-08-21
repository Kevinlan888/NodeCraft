using System;
using System.IO;
using System.Linq;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal static class GeneratorTests
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

        private static string GenerateToTemp(ProjectOptions options, out string[] files)
        {
            var root = Path.Combine(Path.GetTempPath(), "nodecraft-cli-tests-" + Guid.NewGuid().ToString("N"));
            files = ProjectGenerator.Generate(options, root);
            return root;
        }

        private static string ReadGeneratedFile(string root, string relativePath)
        {
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
        }

        private static string FindSampleProjectDirectory()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "NodeCraft.PluginSample", "NodeCraft.PluginSample.csproj");
                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(candidate)!;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate NodeCraft.PluginSample.csproj for tests.");
        }

        private static bool IsBuildOutputPath(string path)
        {
            return path
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var startIndex = 0;
            while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += value.Length;
            }

            return count;
        }

        public static void RunAll()
        {
            Program.Run("generates five core files", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, false), out var files);
                try
                {
                    return files.Length == 5
                        && files.Contains("MyPlugin.csproj")
                        && files.Contains("plugin.json")
                        && files.Contains(@"Plugin\MyPlugin.cs")
                        && files.Contains(@"Nodes\MyPluginNodeModel.cs")
                        && files.Contains(@"Nodes\MyPluginNodeExecutor.cs");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("plugin.json is valid json with the right id", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, false), out _);
                try
                {
                    var json = File.ReadAllText(Path.Combine(root, "plugin.json"));
                    return json.Contains("\"id\": \"company.myplugin.nodes\"")
                        && json.Contains("\"entryType\": \"MyPlugin.Plugin.MyPlugin\"");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("with ui generates editor xaml and code", () =>
            {
                var root = GenerateToTemp(CreateOptions(true, false), out var files);
                try
                {
                    return files.Contains(@"Views\MyPluginNodeEditor.xaml")
                        && files.Contains(@"Views\MyPluginNodeEditor.xaml.cs");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("with private dependency generates the sub-project", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, true), out var files);
                try
                {
                    return files.Contains(@"PrivateDependency\PrivateDependency.csproj")
                        && files.Contains(@"PrivateDependency\MyPluginFormatter.cs");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("executor uses the private formatter when selected", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, true), out _);
                try
                {
                    var executor = ReadGeneratedFile(root, @"Nodes\MyPluginNodeExecutor.cs");
                    return executor.Contains("MyPluginFormatter.Format");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("generated node model wires executor and output port", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, false), out _);
                try
                {
                    var model = ReadGeneratedFile(root, @"Nodes\MyPluginNodeModel.cs");
                    return model.Contains("ExecutorType = MyPluginExecutor.FlowNodeTypeKey")
                        && model.Contains("Name = \"My Nodes\"")
                        && model.Contains("ParameterType = FlowDataType.String.Key");
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("actual generated C# owns its port identifiers", () =>
            {
                var root = GenerateToTemp(CreateOptions(false, false), out var files);
                try
                {
                    var generatedCSharp = string.Join(
                        "\n",
                        files
                            .Where(file => string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
                            .Select(file => ReadGeneratedFile(root, file)));

                    return CountOccurrences(generatedCSharp, "internal static class NodePortIds") == 1
                        && !generatedCSharp.Contains("using NodeCraft.Flow.Nodes;", StringComparison.Ordinal)
                        && !generatedCSharp.Contains("BuiltInNodePorts", StringComparison.Ordinal);
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("active sample C# owns its port identifiers", () =>
            {
                var sources = Directory
                    .EnumerateFiles(FindSampleProjectDirectory(), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !IsBuildOutputPath(path))
                    .Select(File.ReadAllText)
                    .ToArray();

                return sources.Length > 0
                    && sources.All(source => !source.Contains("using NodeCraft.Flow.Nodes;", StringComparison.Ordinal))
                    && sources.All(source => !source.Contains("BuiltInNodePorts", StringComparison.Ordinal));
            });

            Program.Run("generated files contain no unreplaced placeholders", () =>
            {
                var root = GenerateToTemp(CreateOptions(true, true), out var files);
                try
                {
                    foreach (var file in files)
                    {
                        if (ReadGeneratedFile(root, file).Contains("{{"))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("generates into an existing empty directory", () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "nodecraft-cli-tests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(root);
                    var files = ProjectGenerator.Generate(CreateOptions(false, false), root);
                    return files.Length == 5 && File.Exists(Path.Combine(root, "plugin.json"));
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });

            Program.Run("refuses to generate into an existing non-empty directory", () =>
            {
                var root = Path.Combine(Path.GetTempPath(), "nodecraft-cli-tests-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");
                    var threw = false;
                    try
                    {
                        ProjectGenerator.Generate(CreateOptions(false, false), root);
                    }
                    catch (IOException)
                    {
                        threw = true;
                    }

                    return threw && File.Exists(Path.Combine(root, "keep.txt"));
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            });
        }
    }
}
