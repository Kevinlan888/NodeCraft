using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NodeCraft.Plugins
{
    public static class PluginManifestReader
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static PluginManifest Read(string manifestPath, Version hostApiVersion)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentException("Manifest path is required.", nameof(manifestPath));
            }

            if (hostApiVersion == null)
            {
                throw new ArgumentNullException(nameof(hostApiVersion));
            }

            var canonicalManifestPath = Path.GetFullPath(manifestPath);
            var pluginDirectory = Path.GetDirectoryName(canonicalManifestPath);
            if (string.IsNullOrWhiteSpace(pluginDirectory))
            {
                throw new InvalidDataException(
                    $"Plugin manifest path '{canonicalManifestPath}' does not have a containing directory.");
            }

            string manifestJson;
            try
            {
                manifestJson = File.ReadAllText(canonicalManifestPath);
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException)
            {
                throw CreateInvalidDataException(
                    pluginDirectory,
                    "manifest",
                    $"The manifest file '{canonicalManifestPath}' could not be read.",
                    ex);
            }

            PluginManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw CreateInvalidDataException(
                    pluginDirectory,
                    "manifest",
                    $"The manifest file '{canonicalManifestPath}' is not valid JSON.",
                    ex);
            }

            if (manifest == null)
            {
                throw CreateInvalidDataException(
                    pluginDirectory,
                    "manifest",
                    $"The manifest file '{canonicalManifestPath}' did not contain a plugin manifest object.");
            }

            manifest.PluginDirectory = Path.GetFullPath(pluginDirectory);
            if (string.IsNullOrWhiteSpace(manifest.PrivateLibraryPath))
            {
                manifest.PrivateLibraryPath = "lib";
            }

            ValidateId(manifest);

            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "entryAssembly",
                    "The entry assembly is required.");
            }

            if (!manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "entryAssembly",
                    "The entry assembly must be a .dll.");
            }

            if (string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "entryType",
                    "The entry type is required.");
            }

            if (!Version.TryParse(manifest.ApiVersion, out var manifestApiVersion))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "apiVersion",
                    "The API version must be a valid version string.");
            }

            if (manifestApiVersion.Major != hostApiVersion.Major)
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "apiVersion",
                    $"The API major version '{manifestApiVersion.Major}' is not supported by host major version '{hostApiVersion.Major}'.");
            }

            _ = PluginPathResolver.ResolveEntryAssembly(manifest);
            _ = PluginPathResolver.ResolvePrivateLibraryDirectory(manifest);
            return manifest;
        }

        private static InvalidDataException CreateInvalidDataException(
            string pluginDirectory,
            string fieldName,
            string message,
            Exception innerException = null)
        {
            return new InvalidDataException(
                $"Plugin manifest in '{pluginDirectory}' has invalid {fieldName}: {message}",
                innerException);
        }

        private static void ValidateId(PluginManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "id",
                    "Plugin id must be non-empty.");
            }

            if (manifest.Id.Any(char.IsWhiteSpace))
            {
                throw CreateInvalidDataException(
                    manifest.PluginDirectory,
                    "id",
                    "Plugin id must be a stable identifier without whitespace.");
            }
        }
    }
}
