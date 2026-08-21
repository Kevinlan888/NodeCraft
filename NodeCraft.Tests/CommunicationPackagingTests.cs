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
    private static async Task RunCommunicationPackagingTestsAsync()
    {
        Run("Communication plugin project imports an explicit safe packaging target", () =>
        {
            var projectPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "NodeCraft.Communication.csproj");
            var targetPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "Build",
                "CommunicationPackaging.targets");
            var project = XDocument.Load(projectPath);
            var targetDocument = XDocument.Load(targetPath);
            var import = project.Descendants("Import").SingleOrDefault(element =>
                string.Equals(
                    ((string?)element.Attribute("Project"))?.Replace('/', '\\'),
                    "Build\\CommunicationPackaging.targets",
                    StringComparison.OrdinalIgnoreCase));
            var target = targetDocument.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "StageCommunicationPlugin",
                    StringComparison.Ordinal));
            var validationTarget = targetDocument.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "ValidateCommunicationPackageRoot",
                    StringComparison.Ordinal));
            var validator = validationTarget?.Elements()
                .SingleOrDefault(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "ValidateCommunicationPackageRootTask",
                        StringComparison.Ordinal));
            var canonicalOutput = validator?.Elements("Output").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("PropertyName"),
                    "_CommunicationPackageRootCanonical",
                    StringComparison.Ordinal));
            var escapedProperty = validationTarget?
                .Descendants("_CommunicationPackageRootEscaped")
                .SingleOrDefault();
            var makeDirectory = target?.Elements("MakeDir").SingleOrDefault();
            var copy = target?.Elements("Copy").SingleOrDefault();
            var errors = target?.Elements("Error").ToArray() ?? Array.Empty<XElement>();
            var inlineCode = targetDocument.Descendants("Code")
                .Select(element => element.Value)
                .ToArray();

            return import != null
                && target != null
                && string.Equals(
                    (string?)target.Attribute("DependsOnTargets"),
                    "Build",
                    StringComparison.Ordinal)
                && validationTarget != null
                && string.Equals(
                    (string?)validationTarget.Attribute("BeforeTargets"),
                    "StageCommunicationPlugin",
                    StringComparison.Ordinal)
                && !validationTarget.Descendants("RemoveDir").Any()
                && !validationTarget.Descendants("MakeDir").Any()
                && !validationTarget.Descendants("Copy").Any()
                && validator != null
                && canonicalOutput != null
                && escapedProperty != null
                && string.Equals(
                    escapedProperty.Value,
                    "$([MSBuild]::Escape($(_CommunicationPackageRootCanonical)))",
                    StringComparison.Ordinal)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("$(TargetPath)", StringComparison.Ordinal) == true)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("plugin.json", StringComparison.Ordinal) == true)
                && !targetDocument.Descendants("RemoveDir").Any()
                && inlineCode.All(code =>
                    !code.Contains("Directory.Delete", StringComparison.Ordinal))
                && string.Equals(
                    (string?)makeDirectory?.Attribute("Directories"),
                    "$(_CommunicationPackageRootEscaped)",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("SourceFiles"),
                    "$(TargetPath);$(MSBuildProjectDirectory)\\plugin.json",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("DestinationFolder"),
                    "$(_CommunicationPackageRootEscaped)",
                    StringComparison.Ordinal);
        });

        Run("NodeCraft host declares build-order-only Communication staging", () =>
        {
            var projectPath = FindRepositoryFile("NodeCraft", "NodeCraft.csproj");
            var project = XDocument.Load(projectPath);
            var projectReference = project.Descendants("ProjectReference").SingleOrDefault(element =>
                string.Equals(
                    ((string?)element.Attribute("Include"))?.Replace('/', '\\'),
                    "..\\NodeCraft.Communication\\NodeCraft.Communication.csproj",
                    StringComparison.OrdinalIgnoreCase));
            var target = project.Descendants("Target").SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Name"),
                    "StageCommunicationPluginForHost",
                    StringComparison.Ordinal));
            var msbuild = target?.Elements("MSBuild").SingleOrDefault();
            var properties = (string?)msbuild?.Attribute("Properties") ?? string.Empty;
            var rawPackageRoot = target?
                .Descendants("_CommunicationHostPackageRoot")
                .SingleOrDefault();
            var escapedPackageRoot = target?
                .Descendants("_CommunicationHostPackageRootEscaped")
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
                    "StageCommunicationPlugin",
                    StringComparison.Ordinal)
                && ((string?)msbuild?.Attribute("Projects"))?.Contains(
                    "NodeCraft.Communication.csproj",
                    StringComparison.OrdinalIgnoreCase) == true
                && properties.Contains("Configuration=$(Configuration)", StringComparison.Ordinal)
                && properties.Contains("TargetFramework=$(TargetFramework)", StringComparison.Ordinal)
                && rawPackageRoot != null
                && string.Equals(
                    rawPackageRoot.Value,
                    "$(TargetDir)Plugins\\NodeCraft.Communication",
                    StringComparison.Ordinal)
                && escapedPackageRoot != null
                && string.Equals(
                    escapedPackageRoot.Value,
                    "$([MSBuild]::Escape($(_CommunicationHostPackageRoot)))",
                    StringComparison.Ordinal)
                && properties.Contains(
                    "CommunicationPackageRoot=$(_CommunicationHostPackageRootEscaped)",
                    StringComparison.Ordinal)
                && !properties.Contains("CommunicationPackageRoot=$(TargetDir)", StringComparison.Ordinal)
                && hostSources.All(source =>
                    !source.Contains("using NodeCraft.Communication", StringComparison.Ordinal)
                    && !source.Contains("CommunicationPlugin.Register", StringComparison.Ordinal));
        });

        await RunAsync("Communication validation rejects non-stageable raw roots explicitly", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-communication-root-validation-");
            var projectPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "NodeCraft.Communication.csproj");
            var projectDirectory = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("Communication project directory was not found.");
            var fileSystemRoot = Path.GetPathRoot(root.Path)
                ?? throw new InvalidOperationException("Temporary directory root was not found.");
            var driveRelativeRoot = fileSystemRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var outputDirectory = Path.Combine(root.Path, "output");
            var projectVolume = Path.GetPathRoot(projectDirectory)
                ?? throw new InvalidOperationException("Communication project volume was not found.");
            var otherVolume = Enumerable.Range('A', 26)
                .Select(value => ((char)value) + @":\")
                .First(path => !string.Equals(
                    path,
                    projectVolume,
                    StringComparison.OrdinalIgnoreCase));
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
                new PackageRootValidationCase(
                    "ordinary loopback UNC",
                    @"\\localhost\C$\nodecraft-communication-package"),
                new PackageRootValidationCase(
                    "cross-volume root",
                    Path.Combine(otherVolume, "nodecraft-communication-package")),
            };

            foreach (var testCase in cases)
            {
                var arguments = new List<string>
                {
                    "msbuild",
                    projectPath,
                    "-t:ValidateCommunicationPackageRoot",
                    "-p:Configuration=Release",
                    "-p:TargetFramework=net8.0-windows",
                    "-p:CommunicationPackageRoot=" + EscapeMsBuildCommandLineProperty(testCase.PackageRoot),
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
                    || !output.Contains("Communication plugin package root", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Non-stageable root case '" + testCase.Name + "' was not rejected explicitly. " + output);
                }
            }

            return true;
        });

        await RunAsync("Communication synthetic staging rejects path aliases before package mutation", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-communication-synthetic-stage-");
            var targetSource = FindRepositoryFile(
                "NodeCraft.Communication",
                "Build",
                "CommunicationPackaging.targets");

            var rejectionCases = new[]
            {
                new CommunicationSyntheticStageRejectionCase(
                    "project directory",
                    project => project.ProjectDirectory,
                    project => project.ProjectDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "repo-like ancestor",
                    project => project.RepositoryDirectory,
                    project => project.RepositoryDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "TargetDir",
                    project => project.TargetDirectory,
                    project => project.TargetDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "strict OutputPath ancestor",
                    project => project.OutputAncestorDirectory,
                    project => project.OutputAncestorDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "case and trailing-separator alias",
                    project => project.ProjectDirectory.ToUpperInvariant()
                        + Path.DirectorySeparatorChar,
                    project => project.ProjectDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "extended device alias",
                    project => @"\\?\" + project.ProjectDirectory,
                    project => project.ProjectDirectory),
                new CommunicationSyntheticStageRejectionCase(
                    "DOS short-name-like component",
                    project => Path.Combine(project.CaseDirectory, "package~1"),
                    project => Path.Combine(project.CaseDirectory, "package~1")),
                new CommunicationSyntheticStageRejectionCase(
                    "reparse-point package root",
                    project => CreateSyntheticPackageLink(project),
                    project => project.LinkedPackageTarget,
                    allowFinalCandidateReparse: true),
                new CommunicationSyntheticStageRejectionCase(
                    "existing package with an unexpected file",
                    project => Path.Combine(project.CaseDirectory, "package-with-extra"),
                    project => Path.Combine(project.CaseDirectory, "package-with-extra")),
                new CommunicationSyntheticStageRejectionCase(
                    "existing package with unexpected manifest casing",
                    project => Path.Combine(project.CaseDirectory, "package-with-casing"),
                    project => Path.Combine(project.CaseDirectory, "package-with-casing"),
                    sentinelRelativePath: "Plugin.json"),
                new CommunicationSyntheticStageRejectionCase(
                    "existing package with an unexpected directory",
                    project => Path.Combine(project.CaseDirectory, "package-with-directory"),
                    project => Path.Combine(project.CaseDirectory, "package-with-directory"),
                    sentinelRelativePath: Path.Combine("unexpected", "sentinel.txt")),
            };

            foreach (var testCase in rejectionCases)
            {
                var caseDirectory = Path.Combine(
                    root.Path,
                    "reject-" + Guid.NewGuid().ToString("N"));
                var project = CreateSyntheticCommunicationProject(caseDirectory, targetSource);
                var syntheticLinkPath = Path.Combine(project.CaseDirectory, "linked-package");
                try
                {
                    var rawCandidate = testCase.CreateCandidate(project);
                    var actualCandidate = testCase.GetActualCandidate(project);
                    var candidatePath = NormalizeSyntheticCandidateForGuard(rawCandidate);
                    Directory.CreateDirectory(actualCandidate);
                    var sentinelPath = Path.Combine(
                        actualCandidate,
                        testCase.SentinelRelativePath);
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(sentinelPath)
                        ?? throw new InvalidOperationException("Synthetic sentinel directory was not found."));
                    File.WriteAllText(sentinelPath, "keep");

                    EnsureSyntheticStageScope(
                        root.Path,
                        project,
                        rawCandidate,
                        actualCandidate,
                        sentinelPath,
                        allowFinalCandidateReparse: testCase.AllowFinalCandidateReparse);
                    var result = await RunSyntheticStageAsync(project, rawCandidate).ConfigureAwait(false);
                    var output = result.StandardOutput + Environment.NewLine + result.StandardError;
                    if (result.ExitCode == 0
                        || !output.Contains("Communication plugin package root", StringComparison.OrdinalIgnoreCase)
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
                    if (Directory.Exists(syntheticLinkPath)
                        && (File.GetAttributes(syntheticLinkPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(syntheticLinkPath);
                    }
                }
            }

            var positiveCaseDirectory = Path.Combine(
                root.Path,
                "positive-" + Guid.NewGuid().ToString("N"));
            var positiveProject = CreateSyntheticCommunicationProject(
                positiveCaseDirectory,
                targetSource,
                repositoryName: "repo-sibling");
            var prefixSiblingRoot = Path.Combine(
                positiveCaseDirectory,
                "repo");
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
            EnsureOwnedPathHasNoReparsePoint(root.Path, prefixSiblingSentinel);

            var positiveResult = await RunSyntheticStageAsync(
                positiveProject,
                prefixSiblingRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(positiveResult, "Synthetic prefix-sibling staging");
            return IsExactCommunicationPackage(prefixSiblingRoot)
                && File.Exists(prefixSiblingSentinel)
                && string.Equals(
                    File.ReadAllText(prefixSiblingSentinel),
                    "keep",
                    StringComparison.Ordinal);
        });

        await RunAsync("Communication explicit staging creates only the minimal package and preserves siblings", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-communication-explicit-stage-");
            var pluginsRoot = Path.Combine(root.Path, "Plugins");
            var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.Communication");
            var siblingRoot = Path.Combine(pluginsRoot, "Adjacent.Plugin");
            var sentinelPath = Path.Combine(siblingRoot, "sentinel.txt");
            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(siblingRoot);
            File.WriteAllText(Path.Combine(packageRoot, "plugin.json"), "stale manifest");
            File.WriteAllText(Path.Combine(packageRoot, "NodeCraft.Communication.dll"), "stale assembly");
            File.WriteAllText(sentinelPath, "keep");

            var result = await RunDotNetAsync(
                "msbuild",
                FindRepositoryFile("NodeCraft.Communication", "NodeCraft.Communication.csproj"),
                "-t:StageCommunicationPlugin",
                "-p:Configuration=Release",
                "-p:TargetFramework=net8.0-windows",
                "-p:CommunicationPackageRoot=" + packageRoot).ConfigureAwait(false);
            EnsureProcessSucceeded(result, "Explicit Communication staging");

            return IsExactCommunicationPackage(packageRoot)
                && File.Exists(sentinelPath)
                && string.Equals(File.ReadAllText(sentinelPath), "keep", StringComparison.Ordinal);
        });

        await RunAsync("ordinary NodeCraft host rebuild stages Communication without touching adjacent plugins", async () =>
        {
            using var root = new TemporaryDirectory("nodecraft-communication-host-stage-");
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
            var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.Communication");
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

            return IsExactCommunicationPackage(packageRoot)
                && !File.Exists(Path.Combine(hostRoot, "NodeCraft.Communication.dll"))
                && File.Exists(sentinelPath)
                && string.Equals(File.ReadAllText(sentinelPath), "keep", StringComparison.Ordinal);
        });

        Run("real PluginLoader loads and creates the staged Communication node", () =>
        {
            var root = CreateTemporaryPluginDirectory("nodecraft-communication-real-loader-");
            var passed = false;
            try
            {
                var pluginsRoot = Path.Combine(root, "Plugins");
                var packageRoot = Path.Combine(pluginsRoot, "NodeCraft.Communication");
                Directory.CreateDirectory(packageRoot);
                CopyFileToDirectory(FindCommunicationAssembly(), packageRoot);
                CopyFileToDirectory(
                    FindRepositoryFile("NodeCraft.Communication", "plugin.json"),
                    packageRoot);

                var result = RunDotNetAsync(
                        Assembly.GetExecutingAssembly().Location,
                        "--communication-real-loader-child",
                        pluginsRoot)
                    .GetAwaiter()
                    .GetResult();
                EnsureProcessSucceeded(result, "Communication real-loader child");
                passed = true;
            }
            finally
            {
                EnsureOwnedCommunicationTestRoot(root);
                DeleteDirectoryIfExists(root);
            }

            return passed && !Directory.Exists(root);
        });
    }

    private static void RunCommunicationRealLoaderChild(string[] args)
    {
        Run("Communication real-loader child", () =>
        {
            var optionIndex = Array.IndexOf(args, "--communication-real-loader-child");
            if (optionIndex < 0 || optionIndex + 1 >= args.Length)
            {
                throw new ArgumentException("Communication real-loader child requires a Plugins directory.");
            }

            return LoadCommunicationPackageWithRealLoader(args[optionIndex + 1]);
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool LoadCommunicationPackageWithRealLoader(string pluginsRoot)
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
                || typeKeys.Length != 1)
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
                    return loadedNodes.Count == 1
                        && loadedContents.Count == 1;
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

    private static bool IsExactCommunicationPackage(string packageRoot)
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
                new[] { "NodeCraft.Communication.dll", "plugin.json" },
                StringComparer.OrdinalIgnoreCase)
            && !Directory.EnumerateDirectories(packageRoot).Any()
            && !allFiles.Intersect(
                forbiddenSharedAssemblies,
                StringComparer.OrdinalIgnoreCase).Any();
    }

    private static string FindCommunicationAssembly()
    {
        return FindRepositoryFile(
            "NodeCraft.Communication",
            "bin",
            GetBuildMetadata("BuildConfiguration"),
            GetBuildMetadata("BuildTargetFramework"),
            "NodeCraft.Communication.dll");
    }

    private static void EnsureOwnedCommunicationTestRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedPrefix = Path.Combine(
            Path.GetFullPath(Path.GetTempPath()),
            "nodecraft-communication-real-loader-");
        if (!fullPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to delete an unexpected Communication test path: " + fullPath);
        }
    }

    private static SyntheticCommunicationProject CreateSyntheticCommunicationProject(
        string caseDirectory,
        string targetSource,
        string repositoryName = "repo")
    {
        var repositoryDirectory = Path.Combine(caseDirectory, repositoryName);
        var projectDirectory = Path.Combine(repositoryDirectory, "project");
        var buildDirectory = Path.Combine(projectDirectory, "Build");
        var targetDirectory = Path.Combine(projectDirectory, "bin")
            + Path.DirectorySeparatorChar;
        var outputAncestorDirectory = Path.Combine(projectDirectory, "out");
        var outputDirectory = Path.Combine(outputAncestorDirectory, "child")
            + Path.DirectorySeparatorChar;
        var projectPath = Path.Combine(projectDirectory, "SyntheticCommunication.proj");
        var importedTargetPath = Path.Combine(buildDirectory, "CommunicationPackaging.targets");

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
                    new XElement("TargetPath", "$(TargetDir)NodeCraft.Communication.dll")),
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
                    new XAttribute("Project", "Build\\CommunicationPackaging.targets"))))
            .Save(projectPath);

        return new SyntheticCommunicationProject(
            caseDirectory,
            repositoryDirectory,
            projectDirectory,
            projectPath,
            importedTargetPath,
            targetDirectory,
            outputAncestorDirectory,
            Path.Combine(caseDirectory, "linked-package-target"));
    }

    private static string CreateSyntheticPackageLink(SyntheticCommunicationProject project)
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
        SyntheticCommunicationProject project,
        string rawCandidate,
        string expectedActualCandidate,
        string sentinelPath,
        bool requireSentinel = true,
        bool allowFinalCandidateReparse = false)
    {
        var temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var ownedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(uniqueRoot));
        if (!Path.GetFileName(ownedRoot).StartsWith(
                "nodecraft-communication-synthetic-stage-",
                StringComparison.OrdinalIgnoreCase)
            || !IsStrictDescendant(temporaryRoot, ownedRoot))
        {
            throw new InvalidOperationException(
                "Synthetic staging root is not an owned unique temporary directory: " + ownedRoot);
        }

        var candidatePath = NormalizeSyntheticCandidateForGuard(rawCandidate);
        var expectedCandidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(expectedActualCandidate));
        var projectPath = Path.GetFullPath(project.ProjectPath);
        var importedTargetPath = Path.GetFullPath(project.ImportedTargetPath);
        var sentinelFullPath = Path.GetFullPath(sentinelPath);
        foreach (var path in new[]
                 {
                     projectPath,
                     importedTargetPath,
                     candidatePath,
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

        EnsureOwnedPathHasNoReparsePoint(temporaryRoot, ownedRoot);
        EnsureOwnedPathHasNoReparsePoint(ownedRoot, projectPath);
        EnsureOwnedPathHasNoReparsePoint(ownedRoot, importedTargetPath);
        EnsureOwnedPathHasNoReparsePoint(
            ownedRoot,
            candidatePath,
            allowFinalCandidateReparse);

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

        resolvedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedCandidate));
        if (!IsStrictDescendant(ownedRoot, resolvedCandidate))
        {
            throw new InvalidOperationException(
                "Refusing resolved synthetic Stage outside its owned temporary root: "
                + resolvedCandidate);
        }

        EnsureOwnedPathHasNoReparsePoint(ownedRoot, resolvedCandidate);
        EnsureOwnedPathHasNoReparsePoint(ownedRoot, expectedCandidate);
        EnsureOwnedPathHasNoReparsePoint(ownedRoot, sentinelFullPath);

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


    private static Task<ProcessResult> RunSyntheticStageAsync(
        SyntheticCommunicationProject project,
        string packageRoot)
    {
        return RunDotNetAsync(
            "msbuild",
            project.ProjectPath,
            "-t:StageCommunicationPlugin",
            "-p:CommunicationPackageRoot=" + EscapeMsBuildCommandLineProperty(packageRoot));
    }

    private sealed class CommunicationSyntheticStageRejectionCase
    {
        public CommunicationSyntheticStageRejectionCase(
            string name,
            Func<SyntheticCommunicationProject, string> createCandidate,
            Func<SyntheticCommunicationProject, string> getActualCandidate,
            string sentinelRelativePath = "sentinel.txt",
            bool allowFinalCandidateReparse = false)
        {
            Name = name;
            CreateCandidate = createCandidate;
            GetActualCandidate = getActualCandidate;
            SentinelRelativePath = sentinelRelativePath;
            AllowFinalCandidateReparse = allowFinalCandidateReparse;
        }

        public string Name { get; }

        public Func<SyntheticCommunicationProject, string> CreateCandidate { get; }

        public Func<SyntheticCommunicationProject, string> GetActualCandidate { get; }

        public string SentinelRelativePath { get; }

        public bool AllowFinalCandidateReparse { get; }
    }

    private sealed class SyntheticCommunicationProject
    {
        public SyntheticCommunicationProject(
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
