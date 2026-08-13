using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class TextPreviewExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.text-preview";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var input = inputs.TryGetValue(BuiltInNodePorts.Input, out var inputValue)
                ? inputValue
                : null;

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = input
            };

            return Task.FromResult(outputs);
        }
    }
}