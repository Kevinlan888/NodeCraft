using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NodeCraft.Cli
{
    public static class ProjectGenerator
    {
        /// <summary>
        /// Generates a plugin project into <paramref name="targetDirectory"/>.
        /// Throws <see cref="IOException"/> when the directory exists and is non-empty.
        /// Returns the generated file paths relative to the target directory.
        /// </summary>
        public static string[] Generate(ProjectOptions options, string targetDirectory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                throw new IOException($"Target directory '{targetDirectory}' is not empty.");
            }

            Directory.CreateDirectory(targetDirectory);

            var files = new List<string>();
            Add(files, targetDirectory, options.ProjectName + ".csproj", TemplateText.BuildCsproj(options));
            Add(files, targetDirectory, "plugin.json", TemplateText.Fill(TemplateText.PluginJson, options));
            Add(files, targetDirectory, @"Plugin\" + options.PluginClassName + ".cs", TemplateText.PluginEntryFull(options));
            Add(files, targetDirectory, @"Nodes\" + options.NodeName + "NodeModel.cs", TemplateText.Fill(TemplateText.NodeModel, options));
            Add(files, targetDirectory, @"Nodes\" + options.NodeName + "NodeExecutor.cs", TemplateText.NodeExecutorFull(options));

            if (options.IncludeCustomUi)
            {
                Add(files, targetDirectory, @"Views\" + options.NodeName + "NodeEditor.xaml", TemplateText.Fill(TemplateText.NodeEditorXaml, options));
                Add(files, targetDirectory, @"Views\" + options.NodeName + "NodeEditor.xaml.cs", TemplateText.Fill(TemplateText.NodeEditorCode, options));
            }

            if (options.IncludePrivateDependency)
            {
                Add(files, targetDirectory, @"PrivateDependency\PrivateDependency.csproj", TemplateText.Fill(TemplateText.PrivateCsproj, options));
                Add(files, targetDirectory, @"PrivateDependency\" + options.NodeName + "Formatter.cs", TemplateText.Fill(TemplateText.PrivateFormatter, options));
            }

            return files.ToArray();
        }

        private static void Add(List<string> files, string targetDirectory, string relativePath, string content)
        {
            // The relative paths use Windows separators (the generated projects target
            // WPF); normalize to the host separator so subdirectories are real on any OS.
            var fullPath = Path.Combine(targetDirectory, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content);
            files.Add(relativePath);
        }
    }
}
