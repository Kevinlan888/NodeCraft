using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;

namespace NodeCraft.Plugins
{
    public sealed class PluginLoader
    {
        private const string ManifestPhase = "manifest";
        private const string DependencyLoadPhase = "dependency load";
        private const string EntryPointCreationPhase = "entry-point creation";
        private const string RegistrationPhase = "registration";
        private const string ValidationPhase = "validation";
        private static readonly IReadOnlyCollection<string> SharedAssemblyNames = CreateSharedAssemblyNames();

        private readonly FlowNodeRegistry _registry;
        private readonly Version _supportedApiVersion;
        private readonly ILoggerFactory _loggerFactory;
        private readonly HashSet<string> _manifestPluginIds
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PluginLoader(
            FlowNodeRegistry registry,
            Version supportedApiVersion,
            ILoggerFactory loggerFactory)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _supportedApiVersion = supportedApiVersion ?? throw new ArgumentNullException(nameof(supportedApiVersion));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public PluginLoadReport LoadAll(string pluginsDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginsDirectory))
            {
                throw new ArgumentException("Plugins directory is required.", nameof(pluginsDirectory));
            }

            if (!Directory.Exists(pluginsDirectory))
            {
                return new PluginLoadReport(Array.Empty<PluginLoadResult>());
            }

            var results = new List<PluginLoadResult>();
            foreach (var pluginDirectory in Directory
                .EnumerateDirectories(pluginsDirectory)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                results.Add(LoadSinglePlugin(pluginDirectory, manifestPath));
            }

            return new PluginLoadReport(results);
        }

        private PluginLoadResult LoadSinglePlugin(string pluginDirectory, string manifestPath)
        {
            var fallbackPluginId = Path.GetFileName(Path.GetFullPath(pluginDirectory));
            PluginManifest manifest = null;
            ILogger logger = null;
            PluginLoadContext loadContext = null;
            Assembly entryAssembly = null;

            try
            {
                manifest = PluginManifestReader.Read(manifestPath, _supportedApiVersion);
            }
            catch (Exception ex)
            {
                logger = CreateLogger(fallbackPluginId);
                return CreateFailureResult(fallbackPluginId, logger, ManifestPhase, ex, null);
            }

            logger = CreateLogger(manifest.Id);

            if (!_manifestPluginIds.Add(manifest.Id))
            {
                return CreateFailureResult(
                    manifest.Id,
                    logger,
                    ValidationPhase,
                    new InvalidOperationException(
                        $"Plugin id '{manifest.Id}' is duplicated within the plugin scan."),
                    null);
            }

            try
            {
                var entryAssemblyPath = PluginPathResolver.ResolveEntryAssembly(manifest);
                var privateLibraryDirectory = PluginPathResolver.ResolvePrivateLibraryDirectory(manifest);
                loadContext = new PluginLoadContext(
                    entryAssemblyPath,
                    manifest.PluginDirectory,
                    privateLibraryDirectory,
                    SharedAssemblyNames);
                entryAssembly = loadContext.LoadFromAssemblyPath(entryAssemblyPath);
            }
            catch (Exception ex)
            {
                return CreateFailureResult(manifest.Id, logger, DependencyLoadPhase, ex, loadContext);
            }

            IFlowPlugin plugin;
            try
            {
                var entryType = entryAssembly.GetType(manifest.EntryType, throwOnError: false, ignoreCase: false);
                if (entryType == null)
                {
                    throw new InvalidOperationException(
                        $"Plugin entry type '{manifest.EntryType}' was not found in '{entryAssembly.FullName}'.");
                }

                if (!typeof(IFlowPlugin).IsAssignableFrom(entryType))
                {
                    throw new InvalidOperationException(
                        $"Plugin entry type '{manifest.EntryType}' does not implement {nameof(IFlowPlugin)}.");
                }

                plugin = Activator.CreateInstance(entryType) as IFlowPlugin;
                if (plugin == null)
                {
                    throw new InvalidOperationException(
                        $"Plugin entry type '{manifest.EntryType}' could not be instantiated.");
                }
            }
            catch (Exception ex)
            {
                return CreateFailureResult(manifest.Id, logger, EntryPointCreationPhase, ex, loadContext);
            }

            try
            {
                ValidatePlugin(manifest, plugin);
            }
            catch (Exception ex)
            {
                return CreateFailureResult(manifest.Id, logger, ValidationPhase, ex, loadContext);
            }

            try
            {
                var context = new PluginRegistrationContext(logger, _supportedApiVersion);
                plugin.Register(context);
                _registry.RegisterPlugin(manifest.Id, context.Registrations);
                logger.LogInformation("Plugin loaded successfully.");
                return PluginLoadResult.Succeeded(manifest.Id, loadContext);
            }
            catch (Exception ex)
            {
                return CreateFailureResult(manifest.Id, logger, RegistrationPhase, ex, loadContext);
            }
        }

        private void ValidatePlugin(PluginManifest manifest, IFlowPlugin plugin)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (plugin.Metadata == null)
            {
                throw new InvalidOperationException("Plugin metadata is required.");
            }

            if (string.IsNullOrWhiteSpace(plugin.Metadata.Id))
            {
                throw new InvalidOperationException("Plugin metadata must include a non-empty id.");
            }

            if (plugin.Metadata.Id.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                throw new InvalidOperationException(
                    $"Plugin metadata id '{plugin.Metadata.Id}' must be a stable identifier without whitespace.");
            }

            if (plugin.Metadata.Version == null)
            {
                throw new InvalidOperationException("Plugin metadata must include a version.");
            }

            if (!string.Equals(plugin.Metadata.Id, manifest.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Plugin metadata id '{plugin.Metadata.Id}' does not match manifest id '{manifest.Id}'.");
            }
        }

        private PluginLoadResult CreateFailureResult(
            string pluginId,
            ILogger logger,
            string phase,
            Exception exception,
            PluginLoadContext loadContext)
        {
            try
            {
                logger?.LogError(exception, "Plugin load failed during " + phase + ".");
            }
            finally
            {
                loadContext?.Unload();
            }

            return PluginLoadResult.Failed(pluginId, phase, exception);
        }

        private ILogger CreateLogger(string pluginId)
        {
            var safePluginId = string.IsNullOrWhiteSpace(pluginId)
                ? "unknown-plugin"
                : pluginId;

            try
            {
                return _loggerFactory.CreateLogger("NodeCraft.Plugin." + safePluginId);
            }
            catch
            {
                return NullLogger.Instance;
            }
        }

        private static IReadOnlyCollection<string> CreateSharedAssemblyNames()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                typeof(IFlowPlugin).Assembly.GetName().Name,
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly.GetName().Name,
                typeof(CommonControls.WPF.CommonControlTheme).Assembly.GetName().Name,
                typeof(System.Windows.FrameworkElement).Assembly.GetName().Name,
                typeof(System.Windows.DependencyObject).Assembly.GetName().Name,
                typeof(System.Windows.Markup.MarkupExtension).Assembly.GetName().Name,
            };
        }

    }
}
