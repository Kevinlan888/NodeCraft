using NodeCraft.Flow.Nodes;
using System;
using System.Collections.Generic;

namespace NodeCraft.Flow
{
    public class NodeExecutorFactory
    {
        static NodeExecutorFactory()
        {
            Registry = new FlowNodeRegistry();
            BuiltInNodeRegistration.RegisterDefaults();
        }

        public static FlowNodeRegistry Registry { get; }

        public static FlowNodeRegistration ResolveRegistration(string typeKey)
        {
            if (string.IsNullOrWhiteSpace(typeKey))
            {
                throw new ArgumentException("Type key is required.", nameof(typeKey));
            }

            return Registry.Resolve(typeKey);
        }
    }
}
