using System;

namespace NodeCraft.Flow
{
    public class NodeExecutorFactory
    {
        public static FlowNodeRegistry Registry { get; } = new FlowNodeRegistry();

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
