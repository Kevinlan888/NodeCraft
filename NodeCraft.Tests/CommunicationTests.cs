using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Communication.Plugin;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunCommunicationTestsAsync()
    {
        await RunAsync("Communication project exposes the plugin manifest", () =>
        {
            var projectPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "NodeCraft.Communication.csproj");
            var manifestPath = FindRepositoryFile(
                "NodeCraft.Communication",
                "plugin.json");
            var manifest = File.ReadAllText(manifestPath);

            return Task.FromResult(
                File.Exists(projectPath)
                && manifest.Contains("nodecraft.communication", StringComparison.Ordinal)
                && manifest.Contains(
                    "NodeCraft.Communication.Plugin.CommunicationPlugin",
                    StringComparison.Ordinal));
        });

        Run("Communication plugin exposes stable metadata and TCP registration", () =>
        {
            var plugin = new CommunicationPlugin();
            var context = new PluginRegistrationContext(
                NullLogger.Instance,
                new Version(1, 0));
            plugin.Register(context);

            return plugin.Metadata.Id == "nodecraft.communication"
                && plugin.Metadata.DisplayName == "Communication"
                && plugin.Metadata.Version.Equals(new Version(1, 0, 0))
                && context.Registrations.Any(registration =>
                    registration.Definition.TypeKey
                        == "nodecraft.communication.tcp-client-send");
        });
    }
}
