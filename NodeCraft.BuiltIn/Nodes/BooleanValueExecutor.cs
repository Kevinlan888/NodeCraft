using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class BooleanValueExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.boolean-value";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Inputs.TryGetValue(BuiltInPortIds.Value, out var value);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = NodeValueConverter.ToBoolean(value),
            };

            return Task.FromResult(outputs);
        }
    }
}
