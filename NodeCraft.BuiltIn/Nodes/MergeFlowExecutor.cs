using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class MergeFlowExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.merge-flow";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasActiveBranch = definition?.InputPorts
                ?.Where(port => port != null && port.IsDynamic && port.IsControlPort)
                .Any(port => inputs != null
                    && inputs.TryGetValue(port.Id, out var value)
                    && Equals(value, FlowControlSignal.Active)) == true;

            if (!hasActiveBranch)
            {
                return Task.FromResult<IReadOnlyDictionary<string, object>>(
                    new Dictionary<string, object>());
            }

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.FlowOut] = FlowControlSignal.Active,
            };
            return Task.FromResult(outputs);
        }
    }
}
