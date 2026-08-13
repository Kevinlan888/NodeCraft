using System;
using System.Linq;

namespace NodeCraft.Flow
{
    public sealed class PluginMetadata
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public Version Version { get; set; }

        internal void Validate()
        {
            ValidateId(Id, nameof(Id));

            if (Version == null)
            {
                throw new InvalidOperationException("Plugin metadata must include a version.");
            }
        }

        internal static void ValidateId(string pluginId, string paramName)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin id must be non-empty.", paramName);
            }

            if (pluginId.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("Plugin id must be a stable identifier without whitespace.", paramName);
            }
        }
    }
}
