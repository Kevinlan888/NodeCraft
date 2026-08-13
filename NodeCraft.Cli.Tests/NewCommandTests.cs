using System;
using System.IO;
using NodeCraft.Cli;

namespace NodeCraft.Cli.Tests
{
    internal static class NewCommandTests
    {
        private static string TempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "nodecraft-cli-tests-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Walks up from the current directory to locate the repository's
        /// NodeCraft.Flow.csproj (same pattern as NodeCraft.Tests).
        /// </summary>
        private static string FindFlowProjectPath()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "NodeCraft.Flow", "NodeCraft.Flow.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate NodeCraft.Flow.csproj for tests.");
        }

        private static int RunNew(string arguments, string input, string workingDirectory, out string output)
        {
            var writer = new StringWriter();
            // Fully qualified: this namespace's Program is the test runner.
            var exitCode = NodeCraft.Cli.Program.Run(
                arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                new StringReader(input),
                writer,
                workingDirectory);
            output = writer.ToString();
            return exitCode;
        }

        public static void RunAll()
        {
            Program.Run("new generates a project with default answers and an explicit flow path", () =>
            {
                var root = TempRoot();
                try
                {
                    // Empty lines: display name=default, plugin id=default,
                    // type key prefix=default; then the real flow path; features=none.
                    var input = "\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out _);
                    return exitCode == 0
                        && File.Exists(Path.Combine(root, "MyPlugin", "plugin.json"))
                        && File.Exists(Path.Combine(root, "MyPlugin", "Nodes", "MyPluginNodeExecutor.cs"));
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new prints the generated file listing", () =>
            {
                var root = TempRoot();
                try
                {
                    var input = "\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0
                        && output.Contains("Generated: MyPlugin/")
                        && output.Contains("plugin.json")
                        && output.Contains("Next step: dotnet build MyPlugin");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new re-prompts when flow path is invalid", () =>
            {
                var root = TempRoot();
                try
                {
                    // display name=default, plugin id=default, prefix=default,
                    // flow path=INVALID first, then the real path, features=none.
                    var input = "\n\n\nC:\\nope\\NodeCraft.Flow.csproj\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out _);
                    return exitCode == 0;
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new resolves relative flow paths against the generated project directory", () =>
            {
                // Regression: MSBuild resolves the ProjectReference relative to the
                // GENERATED project's directory, so "..\NodeCraft.Flow\..." must be
                // validated against that directory (working\MyPlugin), not the
                // working directory (working). Layout:
                //   working\
                //     NodeCraft.Flow\NodeCraft.Flow.csproj   (fake, existence check)
                //     MyPlugin\                              (generated project)
                var root = TempRoot();
                try
                {
                    var working = Path.Combine(root, "working");
                    Directory.CreateDirectory(Path.Combine(working, "NodeCraft.Flow"));
                    File.WriteAllText(Path.Combine(working, "NodeCraft.Flow", "NodeCraft.Flow.csproj"), "<Project />");

                    // Defaults, then the relative flow path, then features=none.
                    var input = "\n\n\n..\\NodeCraft.Flow\\NodeCraft.Flow.csproj\n\n";
                    var exitCode = RunNew("new MyPlugin", input, working, out _);
                    return exitCode == 0
                        && File.Exists(Path.Combine(working, "MyPlugin", "plugin.json"));
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new re-prompts on a flow path with invalid characters", () =>
            {
                var root = TempRoot();
                try
                {
                    // A flow path with characters invalid on Windows must not crash
                    // with a stack trace: it re-prompts, then succeeds. On Linux the
                    // characters are legal but the file does not exist, so it is
                    // filtered by the file-exists check either way.
                    var input = "\n\n\nC:\\bad\"path\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0 && !output.Contains("Exception");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new aborts cleanly when input ends mid-prompt", () =>
            {
                var root = TempRoot();
                try
                {
                    // Display name = default, then EOF (no more lines).
                    var exitCode = RunNew("new MyPlugin", "\n", root, out var output);
                    return exitCode == 1 && output.Contains("Aborted");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects an invalid project name argument", () =>
            {
                // A single token so the split cannot hide the invalid character.
                var exitCode = RunNew("new Bad-Name", "", TempRoot(), out _);
                return exitCode == 1;
            });

            Program.Run("new rejects a display name containing {{ and falls back to the default", () =>
            {
                var root = TempRoot();
                try
                {
                    // A typed display name with '{{' would be re-substituted by
                    // TemplateText.Fill; it must be rejected and re-prompted.
                    // After the rejection an empty line accepts the default.
                    var input = "Hello {{TypeKey}}\n\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    var entry = File.ReadAllText(Path.Combine(root, "MyPlugin", "Plugin", "MyPlugin.cs"));
                    return exitCode == 0
                        && output.Contains("Display name must not contain '{{', quotes or backslashes, or start with '{'.")
                        && entry.Contains("DisplayName = \"MyPlugin\"")
                        && !entry.Contains("{{");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects a display name containing quotes", () =>
            {
                var root = TempRoot();
                try
                {
                    // A '"' would break the generated C# string literal; it must be
                    // rejected and re-prompted, then the empty line takes the default.
                    var input = "Hello \"World\"\n\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    var entry = File.ReadAllText(Path.Combine(root, "MyPlugin", "Plugin", "MyPlugin.cs"));
                    return exitCode == 0
                        && output.Contains("Display name must not contain '{{', quotes or backslashes, or start with '{'.")
                        && entry.Contains("DisplayName = \"MyPlugin\"")
                        && !entry.Contains("Hello \"World\"");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects a display name containing a backslash", () =>
            {
                var root = TempRoot();
                try
                {
                    // A '\' would produce an unrecognized escape sequence in the
                    // generated C# string literal; it must be rejected and re-prompted.
                    var input = "My\\Nodes\n\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0 && output.Contains("backslashes");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects a display name starting with a brace", () =>
            {
                var root = TempRoot();
                try
                {
                    // A leading '{' would break the generated editor XAML at parse
                    // time; it must be rejected and re-prompted.
                    var input = "{Bad Name\n\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0 && output.Contains("start with '{'");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects a TypeKey prefix containing quotes or {{", () =>
            {
                var root = TempRoot();
                try
                {
                    // The prefix flows into the generated TypeKey constant; both
                    // hazards are rejected, then the empty line takes the default.
                    var input = "\n\n\"a{{\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    var executor = File.ReadAllText(Path.Combine(root, "MyPlugin", "Nodes", "MyPluginNodeExecutor.cs"));
                    return exitCode == 0
                        && output.Contains("TypeKey prefix must not contain '{{', quotes or backslashes, or start with '{'.")
                        && executor.Contains("FlowNodeTypeKey = \"company.myplugin.nodes.myPlugin\"");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new rejects a TypeKey prefix containing a backslash", () =>
            {
                var root = TempRoot();
                try
                {
                    // Same escape hazard as the display name, via the generated
                    // TypeKey constant; rejected and falls back to the default.
                    var input = "\n\nbad\\prefix\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0 && output.Contains("backslashes");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new asks before overwriting and respects n", () =>
            {
                var root = TempRoot();
                try
                {
                    var target = Path.Combine(root, "MyPlugin");
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");

                    // Defaults, real flow path, no features, then "n" to the prompt.
                    var input = "\n\n\n" + FindFlowProjectPath() + "\n\nn\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out _);
                    return exitCode == 0
                        && File.Exists(Path.Combine(target, "keep.txt"))
                        && !File.Exists(Path.Combine(target, "plugin.json"));
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new prints decline message when overwrite refused", () =>
            {
                var root = TempRoot();
                try
                {
                    var target = Path.Combine(root, "MyPlugin");
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");

                    var input = "\n\n\n" + FindFlowProjectPath() + "\n\nn\n";
                    var exitCode = RunNew("new MyPlugin", input, root, out var output);
                    return exitCode == 0 && output.Contains("Aborted. Existing files were left untouched.");
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("new with --force overwrites without asking", () =>
            {
                var root = TempRoot();
                try
                {
                    var target = Path.Combine(root, "MyPlugin");
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");

                    var input = "\n\n\n" + FindFlowProjectPath() + "\n\n";
                    var exitCode = RunNew("new MyPlugin --force", input, root, out _);
                    return exitCode == 0 && File.Exists(Path.Combine(target, "plugin.json"));
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            });

            Program.Run("unknown command exits with error", () =>
            {
                var exitCode = RunNew("bogus", "", TempRoot(), out var output);
                return exitCode == 1 && output.Contains("Usage");
            });

            Program.Run("help flag prints usage and exits zero", () =>
            {
                var exitCode = RunNew("--help", "", TempRoot(), out var output);
                return exitCode == 0 && output.Contains("Usage");
            });

            Program.Run("no arguments prints usage and exits with error", () =>
            {
                var exitCode = RunNew("", "", TempRoot(), out var output);
                return exitCode == 1 && output.Contains("Usage");
            });
        }
    }
}
