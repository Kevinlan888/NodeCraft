using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class IfExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.if";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = inputs.TryGetValue(BuiltInPortIds.Condition, out var value)
                && NodeValueConverter.ToBoolean(value);
            var outputPort = condition ? BuiltInPortIds.True : BuiltInPortIds.False;
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [outputPort] = FlowControlSignal.Active,
            };
            return Task.FromResult(outputs);
        }
    }
}
