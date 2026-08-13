using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class AppendTextExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.append-text";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var input = inputs.TryGetValue(BuiltInNodePorts.Input, out var inputValue) ? inputValue as string ?? string.Empty : string.Empty;
            var suffix = node.Inputs.TryGetValue(BuiltInNodePorts.Suffix, out var suffixValue) ? suffixValue as string ?? string.Empty : string.Empty;

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = input + suffix
            };

            return Task.FromResult(outputs);
        }
    }
}