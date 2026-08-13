using System;

namespace NodeCraft.Flow
{
    public interface IFlowPlugin
    {
        PluginMetadata Metadata { get; }

        void Register(IPluginContext context);
    }
}
