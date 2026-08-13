namespace NodeCraft.Plugins
{
    public sealed class PluginManifest
    {
        public string Id { get; set; }

        public string EntryAssembly { get; set; }

        public string EntryType { get; set; }

        public string ApiVersion { get; set; }

        public string PrivateLibraryPath { get; set; } = "lib";

        public string PluginDirectory { get; internal set; }
    }
}
