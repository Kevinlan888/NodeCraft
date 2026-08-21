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
            var packageRootProperty = targetDocument
                .Descendants("BuiltInPackageRoot")
                .SingleOrDefault();
            var removeDirectory = target?.Elements("RemoveDir").SingleOrDefault();
            var copy = target?.Elements("Copy").SingleOrDefault();
            var errors = target?.Elements("Error").ToArray() ?? Array.Empty<XElement>();

            return import != null
                && target != null
                && string.Equals(
                    (string?)target.Attribute("DependsOnTargets"),
                    "Build",
                    StringComparison.Ordinal)
                && packageRootProperty != null
                && string.Equals(
                    NormalizeCondition((string?)packageRootProperty.Attribute("Condition")),
                    "'$(BuiltInPackageRoot)'==''",
                    StringComparison.Ordinal)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("$(TargetPath)", StringComparison.Ordinal) == true)
                && errors.Any(element =>
                    ((string?)element.Attribute("Condition"))?.Contains("plugin.json", StringComparison.Ordinal) == true)
                && errors.Any(element =>
                    ((string?)element.Attribute("Text"))?.Contains("root", StringComparison.OrdinalIgnoreCase) == true)
                && string.Equals(
                    (string?)removeDirectory?.Attribute("Directories"),
                    "$(BuiltInPackageRoot)",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("SourceFiles"),
                    "$(TargetPath);$(MSBuildProjectDirectory)\\plugin.json",
                    StringComparison.Ordinal)
                && string.Equals(
                    (string?)copy?.Attribute("DestinationFolder"),
                    "$(BuiltInPackageRoot)",
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
                && properties.Contains(
                    "BuiltInPackageRoot=$(TargetDir)Plugins\\NodeCraft.BuiltIn",
                    StringComparison.Ordinal)
                && hostSources.All(source =>
                    !source.Contains("using NodeCraft.BuiltIn", StringComparison.Ordinal)
                    && !source.Contains("BuiltInPlugin.Register", StringComparison.Ordinal));
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
            File.WriteAllText(Path.Combine(packageRoot, "stale.txt"), "stale");
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

    private static string NormalizeCondition(string? value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty);
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
}
