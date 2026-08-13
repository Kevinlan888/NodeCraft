using System;
using System.IO;
using System.Linq;

namespace NodeCraft.Cli
{
    public sealed class NewCommand
    {
        private readonly Questionnaire _questionnaire;
        private readonly TextWriter _output;
        private readonly string _workingDirectory;

        public NewCommand(Questionnaire questionnaire, TextWriter output, string workingDirectory)
        {
            _questionnaire = questionnaire ?? throw new ArgumentNullException(nameof(questionnaire));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        }

        public int Run(string[] args)
        {
            try
            {
                return RunCore(args);
            }
            catch (OperationCanceledException)
            {
                _output.WriteLine("Aborted: input ended before the project was generated.");
                return 1;
            }
            catch (IOException ex)
            {
                _output.WriteLine($"Error: could not write the project: {ex.Message}");
                return 1;
            }
            catch (UnauthorizedAccessException ex)
            {
                _output.WriteLine($"Error: access denied while writing the project: {ex.Message}");
                return 1;
            }
        }

        private int RunCore(string[] args)
        {
            var force = args.Contains("--force", StringComparer.Ordinal);
            var projectName = args.FirstOrDefault(argument => !argument.StartsWith("-", StringComparison.Ordinal));

            if (projectName == null)
            {
                projectName = AskRequired("Project name", null, PluginIdValidator.ValidateProjectName);
            }
            else
            {
                var nameError = PluginIdValidator.ValidateProjectName(projectName);
                if (nameError != null)
                {
                    _output.WriteLine($"Error: {nameError}");
                    return 1;
                }
            }

            var options = new ProjectOptions
            {
                ProjectName = projectName,
                DisplayName = AskRequired(
                    "Plugin display name",
                    projectName,
                    // A typed display name containing '{{' would be re-substituted by
                    // TemplateText.Fill ({{DisplayName}} is replaced before {{TypeKey}}),
                    // a '"' or '\' would break the generated C# string literals, and a
                    // leading '{' would break the generated editor XAML; reject all.
                    value => value.Contains("{{") || value.Contains("\"") || value.Contains("\\")
                        || value.StartsWith("{")
                        ? "Display name must not contain '{{', quotes or backslashes, or start with '{'."
                        : null),
                PluginId = AskRequired(
                    "Plugin ID",
                    "company." + projectName.ToLowerInvariant() + ".nodes",
                    PluginIdValidator.ValidatePluginId),
                TypeKeyPrefix = null,
                FlowProjectPath = null,
            };

            options.TypeKeyPrefix = AskRequired(
                "Node TypeKey prefix",
                options.PluginId,
                // The prefix flows into the generated TypeKey string constant; the
                // same substitution, quote, backslash and leading-brace hazards
                // apply as for the display name.
                value => value.Contains("{{") || value.Contains("\"") || value.Contains("\\")
                    || value.StartsWith("{")
                    ? "TypeKey prefix must not contain '{{', quotes or backslashes, or start with '{'."
                    : null);
            var targetDirectory = Path.Combine(_workingDirectory, options.ProjectName);
            options.FlowProjectPath = AskFlowProjectPath(targetDirectory);

            var features = _questionnaire.SelectFeatures(new[]
            {
                "Custom node UI (ContentFactory + XAML)",
                "Private dependency project (PrivateDependency)",
            });
            options.IncludeCustomUi = features.Contains(0);
            options.IncludePrivateDependency = features.Contains(1);

            if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                if (!force)
                {
                    var overwrite = _questionnaire.AskConfirm(
                        $"Target directory {options.ProjectName} already exists. Overwrite?", false);
                    if (!overwrite)
                    {
                        _output.WriteLine("Aborted. Existing files were left untouched.");
                        return 0;
                    }
                }

                Directory.Delete(targetDirectory, recursive: true);
            }

            var files = ProjectGenerator.Generate(options, targetDirectory);

            _output.WriteLine();
            _output.WriteLine($"Generated: {options.ProjectName}/");
            foreach (var file in files)
            {
                _output.WriteLine($"  {file}");
            }

            _output.WriteLine($"Next step: dotnet build {options.ProjectName}");
            return 0;
        }

        private string AskRequired(string prompt, string defaultValue, Func<string, string> validate)
        {
            var value = _questionnaire.AskString(prompt, defaultValue, validate);
            if (value == null)
            {
                throw new OperationCanceledException();
            }

            return value;
        }

        private string AskFlowProjectPath(string targetDirectory)
        {
            while (true)
            {
                var path = AskRequired(
                    "Path to NodeCraft.Flow.csproj",
                    @"..\NodeCraft.Flow\NodeCraft.Flow.csproj",
                    null);
                string absolute;
                try
                {
                    // MSBuild resolves the ProjectReference relative to the
                    // GENERATED project's directory, so validate against that
                    // directory — the default "..\NodeCraft.Flow\..." then works
                    // when the plugin project sits next to the repository's
                    // NodeCraft.Flow folder. The typed path may use Windows-style
                    // separators (the generated csproj targets Windows), so
                    // normalize them for the existence check only; the original
                    // text is what lands in the generated ProjectReference.
                    var normalizedPath = path.Replace('\\', Path.DirectorySeparatorChar);
                    absolute = Path.GetFullPath(normalizedPath, targetDirectory);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    _output.WriteLine($"Error: '{path}' is not a valid path ({ex.Message}).");
                    continue;
                }

                if (File.Exists(absolute))
                {
                    return path;
                }

                _output.WriteLine($"Error: no NodeCraft.Flow.csproj at '{absolute}' "
                    + $"(relative to the generated '{targetDirectory}' directory). "
                    + "Give the path to the repository's NodeCraft.Flow project file.");
            }
        }
    }
}
