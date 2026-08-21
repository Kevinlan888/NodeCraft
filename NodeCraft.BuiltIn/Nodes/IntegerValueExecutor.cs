using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class IntegerValueExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.integer-value";

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
                [BuiltInPortIds.Output] = (int)System.Math.Round(
                    NodeValueConverter.ToDouble(value)),
            };

            return Task.FromResult(outputs);
        }
    }
}
