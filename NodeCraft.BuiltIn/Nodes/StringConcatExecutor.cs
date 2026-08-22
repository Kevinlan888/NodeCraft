using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class StringConcatExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.string-concat";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            var separator = node?.Inputs.TryGetValue(BuiltInPortIds.Separator, out var separatorValue) == true
                ? separatorValue as string ?? string.Empty
                : string.Empty;
            var values = new List<string>();
            var dynamicPorts = definition.InputPorts
                .Where(port => port != null && port.IsDynamic)
                .ToArray();

            foreach (var port in dynamicPorts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!inputs.TryGetValue(port.Id, out var value))
                {
                    throw new InvalidOperationException(
                        $"String input '{port.Id}' was not provided for node '{node?.Id ?? string.Empty}'.");
                }

                if (value != null && value is not string)
                {
                    throw new InvalidOperationException(
                        $"String input '{port.Id}' must be a string for node '{node?.Id ?? string.Empty}'.");
                }

                values.Add(value as string ?? string.Empty);
            }

            if (values.Count < 2)
            {
                throw new InvalidOperationException(
                    $"String Concat node '{node?.Id ?? string.Empty}' requires at least two inputs.");
            }

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = string.Join(separator, values),
            };
            return Task.FromResult(outputs);
        }
    }
}
