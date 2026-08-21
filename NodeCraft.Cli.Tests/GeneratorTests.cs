using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal static class GeneratorTests
    {
        private const int GeneratedBuildTimeoutMilliseconds = 120_000;
        private const int GeneratedBuildTerminationTimeoutMilliseconds = 10_000;
        private const int GeneratedBuildOutputDrainTimeoutMilliseconds = 10_000;

        private static ProjectOptions CreateOptions(
            bool withUi,
            bool withPrivateDependency,
            string? flowProjectPath = null)
        {
            return new ProjectOptions
            {
                ProjectName = "MyPlugin",
                DisplayName = "My Nodes",
                PluginId = "company.myplugin.nodes",
                TypeKeyPrefix = "company.myplugin.nodes",
                FlowProjectPath = flowProjectPath ?? @"..\NodeCraft.Flow\NodeCraft.Flow.csproj",
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

        private static string FindFlowProjectPath()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "NodeCraft.Flow", "NodeCraft.Flow.csproj");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate NodeCraft.Flow.csproj for generated build tests.");
        }

        private static bool IsBuildOutputPath(string path)
        {
            return path
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static bool BuildGeneratedProject(ProjectOptions options)
        {
            var root = GenerateToTemp(options, out _);
            try
            {
                var projectPath = Path.Combine(root, options.ProjectName + ".csproj");
                var startInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("build");
                startInfo.ArgumentList.Add(projectPath);
                startInfo.ArgumentList.Add("--nologo");
                startInfo.ArgumentList.Add("-p:NuGetAudit=false");
                startInfo.ArgumentList.Add("-p:RestoreIgnoreFailedSources=true");

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not start dotnet build for generated plugin.");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var exited = process.WaitForExit(GeneratedBuildTimeoutMilliseconds);
                if (!exited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between the timeout and the kill request.
                    }
                    catch (Win32Exception)
                    {
                        // The bounded waits below still prevent this test from hanging.
                    }

                    process.WaitForExit(GeneratedBuildTerminationTimeoutMilliseconds);
                }

                var outputTasks = new Task[] { outputTask, errorTask };
                var outputDrained = Task.WaitAll(
                    outputTasks,
                    GeneratedBuildOutputDrainTimeoutMilliseconds);
                if (!exited || !outputDrained)
                {
                    return false;
                }

                var output = outputTask.Result + errorTask.Result;
                return process.ExitCode == 0
                    && !output.Contains("error CS", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
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
                var root = GenerateToTemp(CreateOptions(true, true), out var files);
                try
                {
                    var sources = files
                        .Where(file => string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase))
                        .Select(file => new CSharpSourceFile(file, ReadGeneratedFile(root, file)))
                        .ToArray();

                    return sources.Length == 5
                        && PluginPortOwnershipSyntax.ValidateGeneratedSources(
                            sources,
                            @"Plugin\MyPlugin.cs",
                            @"Nodes\MyPluginNodeModel.cs",
                            @"Nodes\MyPluginNodeExecutor.cs");
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
                    .Select(path => new CSharpSourceFile(path, File.ReadAllText(path)))
                    .ToArray();

                return sources.Length > 0
                    && PluginPortOwnershipSyntax.ValidateNoLegacyDependencies(sources);
            });

            Program.Run("generated default and UI private projects compile", () =>
            {
                var flowProjectPath = FindFlowProjectPath();
                return BuildGeneratedProject(CreateOptions(false, false, flowProjectPath))
                    && BuildGeneratedProject(CreateOptions(true, true, flowProjectPath));
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
