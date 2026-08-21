using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

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

            var value = inputs.TryGetValue(SamplePortIds.Input, out var inputValue)
                ? inputValue as string ?? inputValue?.ToString() ?? string.Empty
                : string.Empty;

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [SamplePortIds.Output] = value,
            };

            return Task.FromResult(outputs);
        }
    }
}
