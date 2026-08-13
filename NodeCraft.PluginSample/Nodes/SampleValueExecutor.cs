using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;
using NodeCraft.PluginSample.PrivateDependency;

namespace NodeCraft.PluginSample.Nodes
{
    public sealed class SampleValueExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "company.sample.nodes.value";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            node.Inputs.TryGetValue(BuiltInNodePorts.Value, out var value);
            var formatted = PrivateValueFormatter.Format(value as string ?? string.Empty);
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = formatted,
            };

            return Task.FromResult(outputs);
        }
    }
}
