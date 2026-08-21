using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class GreaterThanExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.greater-than";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = inputs.TryGetValue(BuiltInPortIds.InputA, out var leftValue)
                ? NodeValueConverter.ToDouble(leftValue)
                : 0d;
            var right = inputs.TryGetValue(BuiltInPortIds.InputB, out var rightValue)
                ? NodeValueConverter.ToDouble(rightValue)
                : 0d;
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = left > right,
            };
            return Task.FromResult(outputs);
        }
    }
}
