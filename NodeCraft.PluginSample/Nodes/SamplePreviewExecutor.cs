using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;
using NodeCraft.Flow.Nodes;

namespace NodeCraft.PluginSample.Nodes
{
    public sealed class SamplePreviewExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "company.sample.nodes.preview";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = inputs.TryGetValue(BuiltInNodePorts.Input, out var inputValue)
                ? inputValue as string ?? inputValue?.ToString() ?? string.Empty
                : string.Empty;

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = value,
            };

            return Task.FromResult(outputs);
        }
    }
}
