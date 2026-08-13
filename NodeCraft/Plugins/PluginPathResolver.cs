using System;
using System.IO;

namespace NodeCraft.Plugins
{
    public static class PluginPathResolver
    {
        public static string ResolveEntryAssembly(PluginManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var resolvedPath = ResolveContainedPath(manifest, manifest.EntryAssembly, "entryAssembly");
            if (!File.Exists(resolvedPath))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "entryAssembly",
                    $"The file '{resolvedPath}' does not exist.");
            }

            return resolvedPath;
        }

        public static string ResolvePrivateLibraryDirectory(PluginManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return ResolveContainedPath(manifest, manifest.PrivateLibraryPath, "privateLibraryPath");
        }

        private static string ResolveContainedPath(PluginManifest manifest, string relativePath, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(manifest.PluginDirectory))
            {
                throw new InvalidDataException("Plugin manifest does not specify a plugin directory.");
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    fieldName,
                    "The value is required.");
            }

            var pluginRoot = Path.GetFullPath(manifest.PluginDirectory);
            var candidatePath = Path.GetFullPath(Path.Combine(pluginRoot, relativePath));

            if (!IsPathContained(pluginRoot, candidatePath))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    fieldName,
                    $"The path '{relativePath}' resolves outside the plugin root.");
            }

            return candidatePath;
        }

        internal static bool IsPathContained(string rootPath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)
                || string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            var canonicalRoot = Path.GetFullPath(rootPath);
            var canonicalCandidatePath = Path.GetFullPath(candidatePath);
            var canonicalRootWithSeparator = EnsureTrailingDirectorySeparator(canonicalRoot);

            return string.Equals(canonicalCandidatePath, canonicalRoot, StringComparison.OrdinalIgnoreCase)
                || canonicalCandidatePath.StartsWith(
                    canonicalRootWithSeparator,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static InvalidDataException CreateInvalidDataException(
            string pluginDirectory,
            string fieldName,
            string message)
        {
            return new InvalidDataException(
                $"Plugin manifest in '{pluginDirectory}' has invalid {fieldName}: {message}");
        }
    }
}
