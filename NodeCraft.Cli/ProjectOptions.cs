using System;

namespace NodeCraft.Cli
{
    public sealed class ProjectOptions
    {
        public string ProjectName { get; set; }

        public string DisplayName { get; set; }

        public string PluginId { get; set; }

        public string TypeKeyPrefix { get; set; }

        public string FlowProjectPath { get; set; }

        public bool IncludeCustomUi { get; set; }

        public bool IncludePrivateDependency { get; set; }

        public string Namespace => ProjectName;

        /// <summary>MyPlugin → MyPlugin; MyNodes → MyNodesPlugin.</summary>
        public string PluginClassName =>
            ProjectName.EndsWith("Plugin", StringComparison.Ordinal)
                ? ProjectName
                : ProjectName + "Plugin";

        public string NodeName => ProjectName;

        public string NodeKey => char.ToLowerInvariant(ProjectName[0]) + ProjectName.Substring(1);

        public string TypeKey => TypeKeyPrefix + "." + NodeKey;

        public string PrivateAssemblyName => ProjectName + ".PrivateDependency";
    }
}
