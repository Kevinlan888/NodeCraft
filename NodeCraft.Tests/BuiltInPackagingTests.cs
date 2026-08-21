using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Plugins;

internal static partial class Program
{
    private static async Task RunBuiltInPackagingTestsAsync()
    {
        Run("BuiltIn plugin project imports an explicit safe packaging target", () =>
        {
            var projectPath = FindRepositoryFile(
                "NodeCraft.BuiltIn",
                "NodeCraft.BuiltIn.csproj");
            var targetPath = FindRepositoryFile(
                "NodeCraft.BuiltIn",
                "Build",
                "BuiltInPackaging.targets");
            var project = XDocument.Load(projectPath);
            var targetDocument = XDocument.Load(targetPath);
            var import = project.Descendants("Import").SingleOrDefault(element =>
                string.Equals(
                    ((string?)element.Attribute("Project"))?.Replace('/', '\\'),
                    "Build\\BuiltInPackaging.targets",
                    StringComparison.OrdinalIgnoreCase));
            var target = targetDocument.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "StageBuiltInPlugin",
                    StringComparison.Ordinal));
            var validationTarget = targetDocument.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "ValidateBuiltInPackageRoot",
                    StringComparison.Ordinal));
            var validator = validationTarget?.Elements()
                .SingleOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "ValidateBuiltInPackageRootTask",
                        StringComparison.Ordinal));
            var canonicalOutput = validator?.Elements("Output").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("PropertyName"),
                    "_BuiltInPackageRootCanonical",
                    StringComparison.Ordinal));
            var escapedProperty = validationTarget?
                .Descendants("_BuiltInPackageRootEscaped")
                .SingleOrDefault();
            var removeDirectory = target?.Elements("RemoveDir").SingleOrDefault();
            var makeDirectory = target?.Elements("MakeDir").SingleOrDefault();
            var copy = target?.Elements("Copy").SingleOrDefault();
            var errors = target?.Elements("Error").ToArray() ?? Array.Empty<XElement>();

            return import != null
                && target != null
                && string.Equals(
                    (string?)target.Attribute("DependsOnTargets"),
                    "Build",
                    StringComparison.Ordinal)
                && validationTarget != null
                && string.Equals(
                    (string?)validationTarget.Attribute("BeforeTargets"),
                    "StageBuiltInPlugin",
                    StringComparison.Ordinal)
                && !validationTarget.Descendants("RemoveDir").Any()
                && !validationTarget.Descendants("MakeDir").Any()
                && !validationTarget.Descendants("Copy").Any()
                && validator != null
                && canonicalOutput != null
                && escapedProperty != null
                && string.Equals(
                    escapedProperty.Value,
                    "$([MSBuild]::Escape($(_BuiltInPackageRootCanonical)))",
                    StringComparison.Ordinal)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("$(TargetPath)", StringComparison.Ordinal) == true)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("plugin.json", StringComparison.Ordinal) == true)
                && string.Equals(
                    (string?)removeDirectory?.Attribute("Directories"),
                    "$(_BuiltInPackageRootEscaped)",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)makeDirectory?.Attribute("Directories"),
                    "$(_BuiltInPackageRootEscaped)",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("SourceFiles"),
                    "$(TargetPath);$(MSBuildProjectDirectory)\\plugin.json",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("DestinationFolder"),
                    "$(_BuiltInPackageRootEscaped)",
                    StringComparison.Ordinal);
        });

        Run("NodeCraft host declares build-order-only BuiltIn staging", () =>
        {
            var projectPath = FindRepositoryFile("NodeCraft", "NodeCraft.csproj");
            var project = XDocument.Load(projectPath);
            var projectReference = project.Descendants("ProjectReference").SingleOrDefault(element =>
                string.Equals(
                    ((string?)element.Attribute("Include"))?.Replace('/', '\\'),
                    "..\\NodeCraft.BuiltIn\\NodeCraft.BuiltIn.csproj",
                    StringComparison.OrdinalIgnoreCase));
            var target = project.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "StageBuiltInPluginForHost",
                    StringComparison.Ordinal));
            var msbuild = target?.Elements("MSBuild").SingleOrDefault();
            var properties = (string?)msbuild?.Attribute("Properties") ?? string.Empty;
            var rawPackageRoot = target?
                .Descendants("_BuiltInHostPackageRoot")
                .SingleOrDefault();
            var escapedPackageRoot = target?
                .Descendants("_BuiltInHostPackageRootEscaped")
                .SingleOrDefault();
            var hostSourceRoot = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("Host project directory was not found.");
            var hostSources = Directory
                .EnumerateFiles(hostSourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .Select(File.ReadAllText)
                .ToArray();

            return projectReference != null
                && string.Equals(
                    (string?)projectReference.Attribute("ReferenceOutputAssembly"),
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    (string?)projectReference.Attribute("Private"),
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                && target != null
                && string.Equals(
                    (string?)target.Attribute("AfterTargets"),
                    "Build",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)msbuild?.Attribute("Targets"),
                    "StageBuiltInPlugin",
                    StringComparison.Ordinal)
                && ((string?)msbuild?.Attribute("Projects"))?.Contains(
                    "NodeCraft.BuiltIn.csproj",
                    StringComparison.OrdinalIgnoreCase) == true
                && properties.Contains("Configuration=$(Configuration)", StringComparison.Ordinal)
                && properties.Contains("TargetFramework=$(TargetFramework)", StringComparison.Ordinal)
                && rawPackageRoot != null
                && string.Equals(
                    rawPackageRoot.Value,
                    "$(TargetDir)Plugins\\NodeCraft.BuiltIn",
                    StringComparison.Ordinal)
                && escapedPackageRoot != null
                && string.Equals(
                    escapedPackageRoot.Value,
                    "$([MSBuild]::Escape($(_BuiltInHostPackageRoot)))",
                    StringComparison.Ordinal)
                && properties.Contains(
                    "BuiltInPackageRoot=$(_BuiltInHostPackageRootEscaped)",
                    StringComparison.Ordinal)
                && !properties.Contains("BuiltInPackageRoot=$(TargetDir)", StringComparison.Ordinal)
                && hostSources.All(source =>
                    !source.Contains("using NodeCraft.BuiltIn", StringComparison.Ordinal)
                    && !source.Contains("BuiltInPlugin.Register", StringComparison.Ordinal));
        });

        await RunAsync("BuiltIn validation rejects non-stageable raw roots explicitly", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-builtin-root-validation-");
            var projectPath = FindRepositoryFile(
                "NodeCraft.BuiltIn",
                "NodeCraft.BuiltIn.csproj");
            var projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("BuiltIn project directory was not found.");
            var fileSystemRoot = Path.GetPathRoot(root.Path)
                ?? throw new InvalidOperationException("Temporary directory root was not found.");
            var driveRelativeRoot = fileSystemRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var outputDirectory = Path.Combine(root.Path, "output");
            var cases = new[]
            {
                new PackageRootValidationCase("file-system root", fileSystemRoot),
                new PackageRootValidationCase("current directory", "."),
                new PackageRootValidationCase(
                    "repository ancestor traversal",
                    Path.Combine(projectDirectory, "..")),
                new PackageRootValidationCase("project directory", projectDirectory),
                new PackageRootValidationCase("drive-relative root", driveRelativeRoot),
                new PackageRootValidationCase(
                    "output directory",
                    outputDirectory,
                    outputDirectory),
                new PackageRootValidationCase(
                    "semicolon",
                    Path.Combine(root.Path, "package;injected")),
                new PackageRootValidationCase(
                    "apostrophe",
                    Path.Combine(root.Path, "package's")),
                new PackageRootValidationCase(
                    "trailing dot component",
                    Path.Combine(root.Path, "package.")),
                new PackageRootValidationCase(
                    "trailing space component",
                    Path.Combine(root.Path, "package ")),
                new PackageRootValidationCase(
                    "Win32 extended device namespace",
                    @"\\?\" + Path.Combine(root.Path, "package")),
                new PackageRootValidationCase(
                    "Win32 device namespace",
                    @"\\.\" + Path.Combine(root.Path, "package")),
                new PackageRootValidationCase(
                    "NT device namespace",
                    @"\??\" + Path.Combine(root.Path, "package")),
                new PackageRootValidationCase(
                    "slash-equivalent device namespace",
                    "//?/" + Path.Combine(root.Path, "package").Replace('\\', '/')),
                new PackageRootValidationCase(
                    "extended UNC device namespace",
                    @"\\?\UNC\server\share\package"),
            };

            foreach (var testCase in cases)
            {
                var arguments = new List<string>
                {
                    "msbuild",
                    projectPath,
                    "-t:ValidateBuiltInPackageRoot",
                    "-p:Configuration=Release",
                    "-p:TargetFramework=net8.0-windows",
                    "-p:BuiltInPackageRoot=" + EscapeMsBuildCommandLineProperty(testCase.PackageRoot),
                };
                if (testCase.OutputPath != null)
                {
                    arguments.Add(
                        "-p:OutputPath="
                        + EscapeMsBuildCommandLineProperty(testCase.OutputPath));
                }

                var result = await RunDotNetAsync(arguments.ToArray()).ConfigureAwait(false);
                var output = result.StandardOutput + Environment.NewLine + result.StandardError;
                if (result.ExitCode == 0
                    || !output.Contains("Built-in plugin package root", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Non-stageable root case '" + testCase.Name + "' was not rejected explicitly. " + output);
                }
            }

            return true;
        });

        await RunAsync("BuiltIn synthetic staging rejects path aliases before package mutation", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-builtin-synthetic-stage-");
            var targetSource = FindRepositoryFile(
                "NodeCraft.BuiltIn",
                "Build",
                "BuiltInPackaging.targets");

            var rejectionCases = new[]
            {
                new SyntheticStageRejectionCase(
                    "project directory",
                    project => project.ProjectDirectory,
                    project => project.ProjectDirectory),
                new SyntheticStageRejectionCase(
                    "repo-like ancestor",
                    project => project.RepositoryDirectory,
                    project => project.RepositoryDirectory),
                new SyntheticStageRejectionCase(
                    "TargetDir",
                    project => project.TargetDirectory,
                    project => project.TargetDirectory),
                new SyntheticStageRejectionCase(
                    "strict OutputPath ancestor",
                    project => project.OutputAncestorDirectory,
                    project => project.OutputAncestorDirectory),
                new SyntheticStageRejectionCase(
                    "case and trailing-separator alias",
                    project => project.ProjectDirectory.ToUpperInvariant()
                        + Path.DirectorySeparatorChar,
                    project => project.ProjectDirectory),
                new SyntheticStageRejectionCase(
                    "extended device alias",
                    project => @"\\?\" + project.ProjectDirectory,
                    project => project.ProjectDirectory),
                new SyntheticStageRejectionCase(
                    "DOS short-name-like component",
                    project => Path.Combine(project.CaseDirectory, "package~1"),
                    project => Path.Combine(project.CaseDirectory, "package~1")),
                new SyntheticStageRejectionCase(
                    "reparse-point package root",
                    project => CreateSyntheticPackageLink(project),
                    project => project.LinkedPackageTarget),
                new SyntheticStageRejectionCase(
                    "existing package with an unexpected entry",
                    project => Path.Combine(project.CaseDirectory, "package-with-extra"),
                    project => Path.Combine(project.CaseDirectory, "package-with-extra")),
            };

            foreach (var testCase in rejectionCases)
            {
                var caseDirectory = Path.Combine(
                    root.Path,
                    "reject-" + Guid.NewGuid().ToString("N"));
                var project = CreateSyntheticBuiltInProject(caseDirectory, targetSource);
                var rawCandidate = testCase.CreateCandidate(project);
                var actualCandidate = testCase.GetActualCandidate(project);
                var candidatePath = NormalizeSyntheticCandidateForGuard(rawCandidate);
                var isCandidateReparsePoint = Directory.Exists(candidatePath)
                    && (File.GetAttributes(candidatePath) & FileAttributes.ReparsePoint) != 0;
                try
                {
                    Directory.CreateDirectory(actualCandidate);
                    var sentinelPath = Path.Combine(actualCandidate, "sentinel.txt");
                    File.WriteAllText(sentinelPath, "keep");

                    EnsureSyntheticStageScope(
                        root.Path,
                        project,
                        rawCandidate,
                        actualCandidate,
                        sentinelPath);
                    var result = await RunSyntheticStageAsync(project, rawCandidate).ConfigureAwait(false);
                    var output = result.StandardOutput + Environment.NewLine + result.StandardError;
                    if (result.ExitCode == 0
                        || !output.Contains("Built-in plugin package root", StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(sentinelPath)
                        || !string.Equals(File.ReadAllText(sentinelPath), "keep", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Synthetic rejection case '" + testCase.Name
                            + "' did not fail before package mutation. " + output);
                    }
                }
                finally
                {
                    if (isCandidateReparsePoint && Directory.Exists(candidatePath))
                    {
                        Directory.Delete(candidatePath);
                    }
                }
            }

            var positiveCaseDirectory = Path.Combine(
                root.Path,
                "positive-" + Guid.NewGuid().ToString("N"));
            var positiveProject = CreateSyntheticBuiltInProject(
                positiveCaseDirectory,
                targetSource);
            var prefixSiblingRoot = Path.Combine(
                positiveCaseDirectory,
                "repo-sibling",
                "NodeCraft.BuiltIn");
            var prefixSiblingSentinel = Path.Combine(
                positiveCaseDirectory,
                "repo-sibling-sentinel.txt");
            File.WriteAllText(prefixSiblingSentinel, "keep");
            EnsureSyntheticStageScope(
                root.Path,
                positiveProject,
                prefixSiblingRoot,
                prefixSiblingRoot,
                Path.Combine(prefixSiblingRoot, "plugin.json"),
                requireSentinel: false);

            var positiveResult = await RunSyntheticStageAsync(
                positiveProject,
                prefixSiblingRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(positiveResult, "Synthetic prefix-sibling staging");
            return IsExactBuiltInPackage(prefixSiblingRoot)
                && File.Exists(prefixSiblingSentinel)
                && string.Equals(
                    File.ReadAllText(prefixSiblingSentinel),
                    "keep",
                    StringComparison.Ordinal);
        });

        await RunAsync("BuiltIn explicit staging creates only the minimal package and preserves siblings", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-builtin-explicit-stage-");
            var pluginsRoot = Path.Combine(root.Path, "Plugins");
            var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.BuiltIn");
            var siblingRoot = Path.Combine(pluginsRoot, "Adjacent.Plugin");
            var sentinelPath = Path.Combine(siblingRoot, "sentinel.txt");
            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(siblingRoot);
            File.WriteAllText(Path.Combine(packageRoot, "plugin.json"), "stale manifest");
            File.WriteAllText(Path.Combine(packageRoot, "NodeCraft.BuiltIn.dll"), "stale assembly");
            File.WriteAllText(sentinelPath, "keep");

            var result = await RunDotNetAsync(
                "msbuild",
                FindRepositoryFile("NodeCraft.BuiltIn", "NodeCraft.BuiltIn.csproj"),
                "-t:StageBuiltInPlugin",
                "-p:Configuration=Release",
                "-p:TargetFramework=net8.0-windows",
                "-p:BuiltInPackageRoot=" + packageRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(result, "Explicit BuiltIn staging");

            return IsExactBuiltInPackage(packageRoot)
                && File.Exists(sentinelPath)
                && string.Equals(File.ReadAllText(sentinelPath), "keep", StringComparison.Ordinal);
        });

        await RunAsync("ordinary NodeCraft host rebuild stages BuiltIn without touching adjacent plugins", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-builtin-host-stage-");
            var artifactsRoot = Path.Combine(root.Path, "artifacts");
            var hostProject = FindRepositoryFile("NodeCraft", "NodeCraft.csproj");

            var firstBuild = await RunDotNetAsync(
                "build",
                hostProject,
                "--configuration",
                "Release",
                "--framework",
                "net8.0-windows",
                "--artifacts-path",
                artifactsRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(firstBuild, "Initial NodeCraft host build");

            var hostExecutable = Directory
                .EnumerateFiles(artifactsRoot, "NodeCraft.exe", SearchOption.AllDirectories)
                .Single();
            var hostRoot = Path.GetDirectoryName(hostExecutable)
                ?? throw new InvalidOperationException("Host output directory was not found.");
            var pluginsRoot = Path.Combine(hostRoot, "Plugins");
            var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.BuiltIn");
            var siblingRoot = Path.Combine(pluginsRoot, "Adjacent.Plugin");
            var sentinelPath = Path.Combine(siblingRoot, "sentinel.txt");
            Directory.CreateDirectory(siblingRoot);
            File.WriteAllText(sentinelPath, "keep");

            var rebuild = await RunDotNetAsync(
                "build",
                hostProject,
                "--configuration",
                "Release",
                "--framework",
                "net8.0-windows",
                "--artifacts-path",
                artifactsRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(rebuild, "NodeCraft host rebuild");

            return IsExactBuiltInPackage(packageRoot)
                && !File.Exists(Path.Combine(hostRoot, "NodeCraft.BuiltIn.dll"))
                && File.Exists(sentinelPath)
                && string.Equals(File.ReadAllText(sentinelPath), "keep", StringComparison.Ordinal);
        });

        Run("real PluginLoader loads and creates all 18 staged BuiltIn nodes", () =>
        {
            var root = CreateTemporaryPluginDirectory("nodecraft-builtin-real-loader-");
            var passed = false;
            try
            {
                var pluginsRoot = Path.Combine(root, "Plugins");
                var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.BuiltIn");
                Directory.CreateDirectory(packageRoot);
                CopyFileToDirectory(FindBuiltInAssembly(), packageRoot);
                CopyFileToDirectory(
                    FindRepositoryFile("NodeCraft.BuiltIn", "plugin.json"),
                    packageRoot);

                var result = RunDotNetAsync(
                        Assembly.GetExecutingAssembly().Location,
                        "--built-in-real-loader-child",
                        pluginsRoot)
                    .GetAwaiter()
                    .GetResult();
                EnsureProcessSucceeded(result, "BuiltIn real-loader child");
                passed = true;
            }
            finally
            {
                EnsureOwnedBuiltInTestRoot(root);
                DeleteDirectoryIfExists(root);
            }

            return passed && !Directory.Exists(root);
        });
    }

    private static void RunBuiltInRealLoaderChild(string[] args)
    {
        Run("BuiltIn real-loader child", () =>
        {
            var optionIndex = Array.IndexOf(args, "--built-in-real-loader-child");
            if (optionIndex < 0 || optionIndex + 1 >= args.Length)
            {
                throw new ArgumentException("BuiltIn real-loader child requires a Plugins directory.");
            }

            return LoadBuiltInPackageWithRealLoader(args[optionIndex + 1]);
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool LoadBuiltInPackageWithRealLoader(string pluginsRoot)
    {
        FlowNodeRegistry? registry = null;
        PluginLoader? loader = null;
        PluginLoadReport report = null!;
        List<NodeModel>? nodes = null;
        List<System.Windows.FrameworkElement>? contents = null;
        FlowCanvas? canvas = null;

        try
        {
            registry = new FlowNodeRegistry();
            loader = new PluginLoader(
                registry,
                new Version(1, 0),
                NullLoggerFactory.Instance);
            report = loader.LoadAll(pluginsRoot);
            var typeKeys = registry.CreatePaletteCategories()
                .SelectMany(category => category.Items)
                .Select(item => item.TypeKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (report.Results.Count != 1
                || report.Failures.Count != 0
                || !report.Results[0].IsSuccess
                || typeKeys.Length != 18)
            {
                return false;
            }

            nodes = new List<NodeModel>();
            contents = new List<System.Windows.FrameworkElement>();
            var loadedRegistry = registry;
            var loadedNodes = nodes;
            var loadedContents = contents;
            return RunOnSta(() =>
                RunWithThemedWindow(window =>
                {
                    foreach (var typeKey in typeKeys)
                    {
                        if (!loadedRegistry.TryCreateNodeByTypeKey(typeKey, out var node)
                            || node == null)
                        {
                            return false;
                        }

                        node.Id = "loaded-" + loadedNodes.Count;
                        loadedNodes.Add(node);
                    }

                    canvas = new FlowCanvas
                    {
                        GraphModel = new GraphModel
                        {
                            Nodes = loadedNodes,
                            Links = new List<GraphLink>(),
                        },
                    };
                    var panel = new System.Windows.Controls.StackPanel();
                    window.Content = panel;
                    foreach (var node in loadedNodes)
                    {
                        if (loadedRegistry.BuildNodeContent(canvas, node)
                            is not System.Windows.FrameworkElement content)
                        {
                            return false;
                        }

                        loadedContents.Add(content);
                        panel.Children.Add(content);
                    }

                    window.UpdateLayout();
                    return loadedNodes.Count == 18
                        && loadedContents.Count == 18;
                }));
        }
        finally
        {
            contents?.Clear();
            nodes?.Clear();
            contents = null;
            nodes = null;
            canvas = null;
            loader = null;
            registry = null;
            UnloadPluginLoadContexts(ref report);
        }
    }

    private static bool IsExactBuiltInPackage(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            return false;
        }

        var rootFiles = Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var forbiddenSharedAssemblies = new[]
        {
            "CommonControls.WPF.dll",
            "Microsoft.Extensions.Logging.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "NodeCraft.Flow.dll",
            "PresentationCore.dll",
            "PresentationFramework.dll",
            "System.Xaml.dll",
            "WindowsBase.dll",
        };
        var allFiles = Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToArray();

        return rootFiles.SequenceEqual(
                new[] { "NodeCraft.BuiltIn.dll", "plugin.json" },
                StringComparer.OrdinalIgnoreCase)
            && !Directory.EnumerateDirectories(packageRoot).Any()
            && !allFiles.Intersect(
                forbiddenSharedAssemblies,
                StringComparer.OrdinalIgnoreCase).Any();
    }

    private static string FindBuiltInAssembly()
    {
        return FindRepositoryFile(
            "NodeCraft.BuiltIn",
            "bin",
            GetBuildMetadata("BuildConfiguration"),
            GetBuildMetadata("BuildTargetFramework"),
            "NodeCraft.BuiltIn.dll");
    }

    private static void EnsureOwnedBuiltInTestRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedPrefix = Path.Combine(
            Path.GetFullPath(Path.GetTempPath()),
            "nodecraft-builtin-real-loader-");
        if (!fullPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to delete an unexpected BuiltIn test path: " + fullPath);
        }
    }

    private static SyntheticBuiltInProject CreateSyntheticBuiltInProject(
        string caseDirectory,
        string targetSource)
    {
        var repositoryDirectory = Path.Combine(caseDirectory, "repo");
        var projectDirectory = Path.Combine(repositoryDirectory, "project");
        var buildDirectory = Path.Combine(projectDirectory, "Build");
        var targetDirectory = Path.Combine(projectDirectory, "bin")
            + Path.DirectorySeparatorChar;
        var outputAncestorDirectory = Path.Combine(projectDirectory, "out");
        var outputDirectory = Path.Combine(outputAncestorDirectory, "child")
            + Path.DirectorySeparatorChar;
        var projectPath = Path.Combine(projectDirectory, "SyntheticBuiltIn.proj");
        var importedTargetPath = Path.Combine(buildDirectory, "BuiltInPackaging.targets");

        Directory.CreateDirectory(buildDirectory);
        File.Copy(targetSource, importedTargetPath);
        File.WriteAllText(Path.Combine(projectDirectory, "plugin.json"), "{}");
        new XDocument(
            new XElement(
                "Project",
                new XAttribute("DefaultTargets", "Build"),
                new XElement(
                    "PropertyGroup",
                    new XElement("TargetDir", targetDirectory),
                    new XElement("OutputPath", outputDirectory),
                    new XElement("TargetPath", "$(TargetDir)NodeCraft.BuiltIn.dll")),
                new XElement(
                    "Target",
                    new XAttribute("Name", "Build"),
                    new XElement(
                        "MakeDir",
                        new XAttribute("Directories", "$(TargetDir)")),
                    new XElement(
                        "WriteLinesToFile",
                        new XAttribute("File", "$(TargetPath)"),
                        new XAttribute("Lines", "synthetic built-in assembly"),
                        new XAttribute("Overwrite", "true"))),
                new XElement(
                    "Import",
                    new XAttribute("Project", "Build\\BuiltInPackaging.targets"))))
            .Save(projectPath);

        return new SyntheticBuiltInProject(
            caseDirectory,
            repositoryDirectory,
            projectDirectory,
            projectPath,
            importedTargetPath,
            targetDirectory,
            outputAncestorDirectory,
            Path.Combine(caseDirectory, "linked-package-target"));
    }

    private static string CreateSyntheticPackageLink(SyntheticBuiltInProject project)
    {
        Directory.CreateDirectory(project.LinkedPackageTarget);
        var linkPath = Path.Combine(project.CaseDirectory, "linked-package");
        try
        {
            Directory.CreateSymbolicLink(linkPath, project.LinkedPackageTarget);
        }
        catch (IOException exception) when (exception.Message.Contains(
            "privilege",
            StringComparison.OrdinalIgnoreCase))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(project.LinkedPackageTarget);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start junction creation.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Failed to create synthetic package junction. "
                    + standardOutput
                    + Environment.NewLine
                    + standardError,
                    exception);
            }
        }

        return linkPath;
    }

    private static void EnsureSyntheticStageScope(
        string uniqueRoot,
        SyntheticBuiltInProject project,
        string rawCandidate,
        string expectedActualCandidate,
        string sentinelPath,
        bool requireSentinel = true)
    {
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var ownedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(uniqueRoot));
        if (!Path.GetFileName(ownedRoot).StartsWith(
                "nodecraft-builtin-synthetic-stage-",
                StringComparison.OrdinalIgnoreCase)
            || !IsStrictDescendant(temporaryRoot, ownedRoot))
        {
            throw new InvalidOperationException(
                "Synthetic staging root is not an owned unique temporary directory: " + ownedRoot);
        }

        var candidatePath = NormalizeSyntheticCandidateForGuard(rawCandidate);
        var resolvedCandidate = candidatePath;
        if (Directory.Exists(candidatePath))
        {
            var candidateInfo = new DirectoryInfo(candidatePath);
            if ((candidateInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                resolvedCandidate = candidateInfo.ResolveLinkTarget(true)?.FullName
                    ?? throw new InvalidOperationException(
                        "Synthetic package link could not be resolved: " + candidatePath);
            }
        }

        var expectedCandidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(expectedActualCandidate));
        resolvedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedCandidate));
        var projectPath = Path.GetFullPath(project.ProjectPath);
        var importedTargetPath = Path.GetFullPath(project.ImportedTargetPath);
        var sentinelFullPath = Path.GetFullPath(sentinelPath);
        foreach (var path in new[]
                 {
                     projectPath,
                     importedTargetPath,
                     candidatePath,
                     resolvedCandidate,
                     expectedCandidate,
                     sentinelFullPath,
                 })
        {
            if (!IsStrictDescendant(ownedRoot, path))
            {
                throw new InvalidOperationException(
                    "Refusing synthetic Stage outside its owned temporary root: " + path);
            }
        }

        if (!string.Equals(
                resolvedCandidate,
                expectedCandidate,
                StringComparison.OrdinalIgnoreCase)
            || !IsStrictDescendant(expectedCandidate, sentinelFullPath)
            || (requireSentinel && !File.Exists(sentinelFullPath)))
        {
            throw new InvalidOperationException(
                "Synthetic Stage candidate or sentinel did not resolve to the expected owned path.");
        }
    }

    private static string NormalizeSyntheticCandidateForGuard(string rawCandidate)
    {
        var normalized = rawCandidate.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"\\" + normalized.Substring(8);
        }
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(4);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalized));
    }

    private static bool IsStrictDescendant(string ancestor, string descendant)
    {
        var canonicalAncestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));
        var canonicalDescendant = Path.TrimEndingDirectorySeparator(Path.GetFullPath(descendant));
        return canonicalDescendant.StartsWith(
            canonicalAncestor + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Task<ProcessResult> RunSyntheticStageAsync(
        SyntheticBuiltInProject project,
        string packageRoot)
    {
        return RunDotNetAsync(
            "msbuild",
            project.ProjectPath,
            "-t:StageBuiltInPlugin",
            "-p:BuiltInPackageRoot=" + EscapeMsBuildCommandLineProperty(packageRoot));
    }

    private static string EscapeMsBuildCommandLineProperty(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunDotNetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new ProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static void EnsureProcessSucceeded(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            operation + " failed with exit code " + result.ExitCode + ". "
            + result.StandardOutput + Environment.NewLine + result.StandardError);
    }

    private sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }
    }

    private sealed class PackageRootValidationCase
    {
        public PackageRootValidationCase(
            string name,
            string packageRoot,
            string? outputPath = null)
        {
            Name = name;
            PackageRoot = packageRoot;
            OutputPath = outputPath;
        }

        public string Name { get; }

        public string PackageRoot { get; }

        public string? OutputPath { get; }
    }

    private sealed class SyntheticStageRejectionCase
    {
        public SyntheticStageRejectionCase(
            string name,
            Func<SyntheticBuiltInProject, string> createCandidate,
            Func<SyntheticBuiltInProject, string> getActualCandidate)
        {
            Name = name;
            CreateCandidate = createCandidate;
            GetActualCandidate = getActualCandidate;
        }

        public string Name { get; }

        public Func<SyntheticBuiltInProject, string> CreateCandidate { get; }

        public Func<SyntheticBuiltInProject, string> GetActualCandidate { get; }
    }

    private sealed class SyntheticBuiltInProject
    {
        public SyntheticBuiltInProject(
            string caseDirectory,
            string repositoryDirectory,
            string projectDirectory,
            string projectPath,
            string importedTargetPath,
            string targetDirectory,
            string outputAncestorDirectory,
            string linkedPackageTarget)
        {
            CaseDirectory = caseDirectory;
            RepositoryDirectory = repositoryDirectory;
            ProjectDirectory = projectDirectory;
            ProjectPath = projectPath;
            ImportedTargetPath = importedTargetPath;
            TargetDirectory = targetDirectory;
            OutputAncestorDirectory = outputAncestorDirectory;
            LinkedPackageTarget = linkedPackageTarget;
        }

        public string CaseDirectory { get; }

        public string RepositoryDirectory { get; }

        public string ProjectDirectory { get; }

        public string ProjectPath { get; }

        public string ImportedTargetPath { get; }

        public string TargetDirectory { get; }

        public string OutputAncestorDirectory { get; }

        public string LinkedPackageTarget { get; }
    }
}
