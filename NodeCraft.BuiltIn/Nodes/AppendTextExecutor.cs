using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class AppendTextExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.append-text";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs.TryGetValue(BuiltInPortIds.Input, out var inputValue)
                ? inputValue as string ?? string.Empty
                : string.Empty;
            var suffix = node.Inputs.TryGetValue(BuiltInPortIds.Suffix, out var suffixValue)
                ? suffixValue as string ?? string.Empty
                : string.Empty;
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = input + suffix,
            };
            return Task.FromResult(outputs);
        }
    }
}
