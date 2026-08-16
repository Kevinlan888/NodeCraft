using System;
using NodeCraft.Flow;

namespace NodeCraft.Communication.Plugin
{
    public sealed class CommunicationPlugin : IFlowPlugin
    {
        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.communication",
            DisplayName = "Communication",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
        }
    }
}
