using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class IfExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.if";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = inputs.TryGetValue(FlowPorts.Condition, out var value) && NodeValueConverter.ToBoolean(value);
            var outputPort = condition ? FlowPorts.True : FlowPorts.False;

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [outputPort] = FlowControlSignal.Active
            };

            return Task.FromResult(outputs);
        }
    }
}
