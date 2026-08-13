using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class EqualExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.equal";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            inputs.TryGetValue(BuiltInNodePorts.InputA, out var left);
            inputs.TryGetValue(BuiltInNodePorts.InputB, out var right);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = Equals(left, right)
            };

            return Task.FromResult(outputs);
        }
    }
}