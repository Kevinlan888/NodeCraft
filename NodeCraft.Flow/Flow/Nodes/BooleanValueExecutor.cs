using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class BooleanValueExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.boolean-value";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Inputs.TryGetValue(BuiltInNodePorts.Value, out var value);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = NodeValueConverter.ToBoolean(value)
            };

            return Task.FromResult(outputs);
        }
    }
}