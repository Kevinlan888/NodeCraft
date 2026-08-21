using System;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.BuiltIn.Plugin;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RegisterBuiltInPluginForTests()
    {
        if (NodeExecutorFactory.Registry.Contains(StringValueNodeModel.FlowNodeTypeKey))
        {
            return;
        }

        var plugin = new BuiltInPlugin();
        var context = new PluginRegistrationContext(NullLogger.Instance, new Version(1, 0));
        plugin.Register(context);
        NodeExecutorFactory.Registry.RegisterPlugin(plugin.Metadata.Id, context.Registrations);
    }
}