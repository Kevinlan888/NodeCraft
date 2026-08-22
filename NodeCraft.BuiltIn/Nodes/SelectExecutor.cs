using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class SelectExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.select";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (!inputs.TryGetValue(BuiltInPortIds.Condition, out var conditionValue)
                || !inputs.TryGetValue(BuiltInPortIds.TrueValue, out var trueValue)
                || !inputs.TryGetValue(BuiltInPortIds.FalseValue, out var falseValue))
            {
                throw new InvalidOperationException(
                    $"Select node '{node?.Id ?? string.Empty}' requires condition, trueValue, and falseValue inputs.");
            }

            var selected = NodeValueConverter.ToBoolean(conditionValue)
                ? trueValue
                : falseValue;
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = selected,
            };
            return Task.FromResult(outputs);
        }
    }
}
