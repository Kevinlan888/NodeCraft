using System;
using Microsoft.Extensions.Logging;
using NodeCraft.BuiltIn.Registrations;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Plugin
{
    public sealed class BuiltInPlugin : IFlowPlugin
    {
        public PluginMetadata Metadata { get; } = new PluginMetadata
        {
            Id = "nodecraft.builtin",
            DisplayName = "Built-In Nodes",
            Version = new Version(1, 0, 0),
        };

        public void Register(IPluginContext context)
        {
            foreach (var registration in PreviewNodeRegistrations.CreateAll())
            {
                context.Nodes.Register(registration);
            }

            foreach (var registration in ValueNodeRegistrations.CreateAll())
            {
                context.Nodes.Register(registration);
            }

            foreach (var registration in MathNodeRegistrations.CreateAll())
            {
                context.Nodes.Register(registration);
            }

            foreach (var registration in LogicNodeRegistrations.CreateAll())
            {
                context.Nodes.Register(registration);
            }

            context.Logger.LogInformation("Registered built-in Preview, Value, Math, and Logic nodes.");
        }
    }
}
