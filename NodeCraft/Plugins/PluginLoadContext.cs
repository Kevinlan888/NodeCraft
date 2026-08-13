using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace NodeCraft.Plugins
{
    public sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> FrameworkFallbackExactAssemblyNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Accessibility",
            "Microsoft.CSharp",
            "Microsoft.VisualBasic",
            "Microsoft.VisualBasic.Core",
            "Microsoft.Win32.Primitives",
            "Microsoft.Win32.Registry",
            "mscorlib",
            "netstandard",
            "PresentationCore",
            "PresentationFramework",
            "PresentationFramework-SystemCore",
            "PresentationFramework-SystemData",
            "PresentationFramework-SystemDrawing",
            "PresentationFramework-SystemXml",
            "PresentationFramework-SystemXmlLinq",
            "PresentationFramework.Aero",
            "PresentationFramework.Aero2",
            "PresentationFramework.AeroLite",
            "PresentationFramework.Classic",
            "PresentationFramework.Luna",
            "PresentationFramework.Royale",
            "ReachFramework",
            "System.IO.Packaging",
            "System.Printing",
            "System.Windows.Input.Manipulations",
            "System.Xaml",
            "UIAutomationClient",
            "UIAutomationClientSideProviders",
            "UIAutomationProvider",
            "UIAutomationTypes",
            "WindowsBase",
            "WindowsFormsIntegration",
        };

        private static readonly HashSet<string> TrustedPlatformAssemblyNames =
            CreateTrustedPlatformAssemblyNames();

        private readonly AssemblyDependencyResolver _dependencyResolver;
        private readonly string _pluginRoot;
        private readonly string _entryDirectory;
        private readonly string _privateLibraryDirectory;
        private readonly HashSet<string> _sharedAssemblyNames;
        private readonly string[] _managedProbeDirectories;
        private readonly string[] _nativeProbeDirectories;

        public PluginLoadContext(
            string entryAssemblyPath,
            string pluginRoot,
            string privateLibraryDirectory,
            IReadOnlyCollection<string> sharedAssemblyNames)
            : base(
                "NodeCraft.Plugin:" + Path.GetFileNameWithoutExtension(entryAssemblyPath ?? string.Empty),
                isCollectible: true)
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new ArgumentException("Entry assembly path is required.", nameof(entryAssemblyPath));
            }

            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                throw new ArgumentException("Plugin root is required.", nameof(pluginRoot));
            }

            if (string.IsNullOrWhiteSpace(privateLibraryDirectory))
            {
                throw new ArgumentException("Private library directory is required.", nameof(privateLibraryDirectory));
            }

            if (sharedAssemblyNames == null)
            {
                throw new ArgumentNullException(nameof(sharedAssemblyNames));
            }

            var canonicalEntryAssemblyPath = Path.GetFullPath(entryAssemblyPath);
            _pluginRoot = Path.GetFullPath(pluginRoot);
            _entryDirectory = Path.GetDirectoryName(canonicalEntryAssemblyPath)
                ?? throw new ArgumentException(
                    "Entry assembly path must have a containing directory.",
                    nameof(entryAssemblyPath));
            _privateLibraryDirectory = Path.GetFullPath(privateLibraryDirectory);
            _dependencyResolver = new AssemblyDependencyResolver(canonicalEntryAssemblyPath);
            _sharedAssemblyNames = new HashSet<string>(
                sharedAssemblyNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            _managedProbeDirectories = CreateProbeDirectories(_entryDirectory, _privateLibraryDirectory);
            _nativeProbeDirectories = CreateProbeDirectories(_entryDirectory, _privateLibraryDirectory);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (assemblyName == null)
            {
                throw new ArgumentNullException(nameof(assemblyName));
            }

            if (_sharedAssemblyNames.Contains(assemblyName.Name))
            {
                return LoadSharedAssembly(assemblyName);
            }

            var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                var containedPath = RequireContainedManagedResolverPath(assemblyName, resolvedPath);
                return LoadFromAssemblyPath(containedPath);
            }

            var probedPath = ProbeManagedAssemblyPath(assemblyName);
            if (probedPath != null)
            {
                return LoadFromAssemblyPath(probedPath);
            }

            return IsTrustedPlatformAssemblyName(assemblyName.Name)
                ? null
                : throw new FileNotFoundException(
                    $"Plugin dependency '{assemblyName.FullName}' could not be resolved from '{_entryDirectory}' or '{_privateLibraryDirectory}'.");
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                var containedPath = RequireContainedNativeResolverPath(unmanagedDllName, resolvedPath);
                return LoadUnmanagedDllFromPath(containedPath);
            }

            var probedPath = ProbeNativeLibraryPath(unmanagedDllName);
            if (probedPath != null)
            {
                return LoadUnmanagedDllFromPath(probedPath);
            }

            throw new DllNotFoundException(
                $"Plugin native dependency '{unmanagedDllName}' could not be resolved inside plugin root '{_pluginRoot}' from '{_entryDirectory}' or '{_privateLibraryDirectory}'.");
        }

        private static string[] CreateProbeDirectories(params string[] directories)
        {
            return directories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static Assembly LoadSharedAssembly(AssemblyName assemblyName)
        {
            var loadedAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    assemblyName.Name,
                    StringComparison.OrdinalIgnoreCase));
            return loadedAssembly ?? AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }

        private string ProbeManagedAssemblyPath(AssemblyName assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                return null;
            }

            foreach (var directory in _managedProbeDirectories)
            {
                var candidatePath = TryResolveContainedProbePath(
                    _pluginRoot,
                    directory,
                    assemblyName.Name + ".dll");
                if (candidatePath != null && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private string ProbeNativeLibraryPath(string unmanagedDllName)
        {
            foreach (var directory in _nativeProbeDirectories)
            {
                foreach (var candidateFileName in GetNativeLibraryCandidateNames(unmanagedDllName))
                {
                    var candidatePath = TryResolveContainedProbePath(
                        _pluginRoot,
                        directory,
                        candidateFileName);
                    if (candidatePath != null && File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> GetNativeLibraryCandidateNames(string unmanagedDllName)
        {
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
            {
                yield break;
            }

            yield return unmanagedDllName;

            if (!Path.HasExtension(unmanagedDllName))
            {
                yield return unmanagedDllName + ".dll";
            }
        }

        private string RequireContainedManagedResolverPath(AssemblyName assemblyName, string resolvedPath)
        {
            var canonicalPath = Path.GetFullPath(resolvedPath);
            if (!PluginPathResolver.IsPathContained(_pluginRoot, canonicalPath))
            {
                throw new FileLoadException(
                    $"Plugin dependency '{assemblyName.FullName}' resolved outside plugin root '{_pluginRoot}': '{canonicalPath}'.");
            }

            return canonicalPath;
        }

        private string RequireContainedNativeResolverPath(string unmanagedDllName, string resolvedPath)
        {
            var canonicalPath = Path.GetFullPath(resolvedPath);
            if (!PluginPathResolver.IsPathContained(_pluginRoot, canonicalPath))
            {
                throw new DllNotFoundException(
                    $"Plugin native dependency '{unmanagedDllName}' resolved outside plugin root '{_pluginRoot}': '{canonicalPath}'.");
            }

            return canonicalPath;
        }

        private static string TryResolveContainedProbePath(
            string pluginRoot,
            string directory,
            string candidateFileName)
        {
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(candidateFileName))
            {
                return null;
            }

            var candidatePath = Path.GetFullPath(Path.Combine(directory, candidateFileName));
            return PluginPathResolver.IsPathContained(directory, candidatePath)
                && PluginPathResolver.IsPathContained(pluginRoot, candidatePath)
                ? candidatePath
                : null;
        }

        private static HashSet<string> CreateTrustedPlatformAssemblyNames()
        {
            var trustedPlatformAssemblies =
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            return CreateTrustedPlatformAssemblyNames(
                trustedPlatformAssemblies,
                CreateTrustedFrameworkDirectoryCandidates());
        }

        private static HashSet<string> CreateTrustedPlatformAssemblyNames(
            string trustedPlatformAssemblies,
            IEnumerable<string> frameworkDirectoryCandidates)
        {
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trustedFrameworkDirectories = CreateTrustedFrameworkDirectories(
                frameworkDirectoryCandidates);

            if (trustedFrameworkDirectories.Length > 0
                && !string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                foreach (var assemblyPath in trustedPlatformAssemblies.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(assemblyPath))
                    {
                        continue;
                    }

                    string canonicalAssemblyPath;
                    try
                    {
                        canonicalAssemblyPath = Path.GetFullPath(assemblyPath);
                    }
                    catch (Exception ex) when (
                        ex is ArgumentException
                        || ex is NotSupportedException
                        || ex is PathTooLongException)
                    {
                        continue;
                    }

                    var assemblyDirectory = Path.GetDirectoryName(canonicalAssemblyPath);
                    if (string.IsNullOrWhiteSpace(assemblyDirectory)
                        || !trustedFrameworkDirectories.Contains(
                            Path.GetFullPath(assemblyDirectory),
                            StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var assemblyName = Path.GetFileNameWithoutExtension(canonicalAssemblyPath);
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        assemblyNames.Add(assemblyName);
                    }
                }
            }

            return assemblyNames;
        }

        private static IEnumerable<string> CreateTrustedFrameworkDirectoryCandidates()
        {
            return new[]
            {
                RuntimeEnvironment.GetRuntimeDirectory(),
                Path.GetDirectoryName(typeof(System.Windows.FrameworkElement).Assembly.Location),
                Path.GetDirectoryName(typeof(System.Windows.Media.Visual).Assembly.Location),
                Path.GetDirectoryName(typeof(System.Windows.DependencyObject).Assembly.Location),
                Path.GetDirectoryName(typeof(System.Windows.Markup.MarkupExtension).Assembly.Location),
            };
        }

        private static string[] CreateTrustedFrameworkDirectories(
            IEnumerable<string> frameworkDirectoryCandidates)
        {
            if (frameworkDirectoryCandidates == null)
            {
                return Array.Empty<string>();
            }

            var knownDotnetInstallationRoots = CreateKnownDotnetInstallationRoots();
            return frameworkDirectoryCandidates
                .Select(directory => TryGetVerifiedSharedFrameworkDirectory(
                    directory,
                    knownDotnetInstallationRoots))
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyCollection<string> CreateKnownDotnetInstallationRoots()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT_X86"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT_ARM64"),
                Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
                CombineDirectory(programFiles, "dotnet"),
                CombineDirectory(programFilesX86, "dotnet"),
                CombineDirectory(userProfile, ".dotnet"),
                CombineDirectory(localApplicationData, Path.Combine("Microsoft", "dotnet")),
            };

            return candidates
                .Select(TryGetExistingDirectoryFullPath)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string CombineDirectory(string parentDirectory, string childDirectory)
        {
            return string.IsNullOrWhiteSpace(parentDirectory)
                ? null
                : Path.Combine(parentDirectory, childDirectory);
        }

        private static string TryGetExistingDirectoryFullPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            try
            {
                var canonicalDirectory = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(directory));
                return Directory.Exists(canonicalDirectory)
                    ? canonicalDirectory
                    : null;
            }
            catch (Exception ex) when (
                ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException)
            {
                return null;
            }
        }

        private static string TryGetVerifiedSharedFrameworkDirectory(
            string directory,
            IReadOnlyCollection<string> knownDotnetInstallationRoots)
        {
            if (string.IsNullOrWhiteSpace(directory)
                || knownDotnetInstallationRoots == null)
            {
                return null;
            }

            DirectoryInfo versionDirectory;
            try
            {
                versionDirectory = new DirectoryInfo(Path.GetFullPath(directory));
            }
            catch (Exception ex) when (
                ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException)
            {
                return null;
            }

            var frameworkDirectory = versionDirectory.Parent;
            var sharedDirectory = frameworkDirectory?.Parent;
            var dotnetInstallationRoot = sharedDirectory?.Parent;
            if (!versionDirectory.Exists
                || !IsFrameworkVersionDirectoryName(versionDirectory.Name)
                || frameworkDirectory == null
                || sharedDirectory == null
                || dotnetInstallationRoot == null
                || !string.Equals(sharedDirectory.Name, "shared", StringComparison.OrdinalIgnoreCase)
                || !IsTrustedSharedFrameworkName(frameworkDirectory.Name)
                || !knownDotnetInstallationRoots.Contains(
                    dotnetInstallationRoot.FullName,
                    StringComparer.OrdinalIgnoreCase)
                || !File.Exists(Path.Combine(
                    versionDirectory.FullName,
                    frameworkDirectory.Name + ".deps.json")))
            {
                return null;
            }

            return versionDirectory.FullName;
        }

        private static bool IsFrameworkVersionDirectoryName(string directoryName)
        {
            var prereleaseSeparator = directoryName?.IndexOf('-') ?? -1;
            var versionText = prereleaseSeparator >= 0
                ? directoryName.Substring(0, prereleaseSeparator)
                : directoryName;
            return Version.TryParse(versionText, out _);
        }

        private static bool IsTrustedSharedFrameworkName(string directoryName)
        {
            return string.Equals(
                    directoryName,
                    "Microsoft.NETCore.App",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    directoryName,
                    "Microsoft.WindowsDesktop.App",
                    StringComparison.OrdinalIgnoreCase)
                || directoryName.StartsWith(
                    "Microsoft.WindowsDesktop.App.",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedPlatformAssemblyName(string assemblyName)
        {
            return IsTrustedPlatformAssemblyName(
                assemblyName,
                TrustedPlatformAssemblyNames);
        }

        private static bool IsTrustedPlatformAssemblyName(
            string assemblyName,
            IReadOnlyCollection<string> trustedPlatformAssemblyNames)
        {
            return !string.IsNullOrWhiteSpace(assemblyName)
                && ((trustedPlatformAssemblyNames != null
                        && trustedPlatformAssemblyNames.Contains(
                            assemblyName,
                            StringComparer.OrdinalIgnoreCase))
                    || IsFrameworkFallbackAssemblyName(assemblyName));
        }

        private static bool IsFrameworkFallbackAssemblyName(string assemblyName)
        {
            return !string.IsNullOrWhiteSpace(assemblyName)
                && (string.Equals(assemblyName, "System", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                    || FrameworkFallbackExactAssemblyNames.Contains(assemblyName));
        }
    }
}
