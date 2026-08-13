using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class BooleanAndExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.boolean-and";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = inputs.TryGetValue(BuiltInNodePorts.InputA, out var leftValue) && NodeValueConverter.ToBoolean(leftValue);
            var right = inputs.TryGetValue(BuiltInNodePorts.InputB, out var rightValue) && NodeValueConverter.ToBoolean(rightValue);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = left && right
            };

            return Task.FromResult(outputs);
        }
    }
}