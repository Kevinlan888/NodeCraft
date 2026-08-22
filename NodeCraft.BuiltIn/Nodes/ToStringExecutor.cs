using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class ToStringExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "nodecraft.builtin.to-string";

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs != null && inputs.TryGetValue(BuiltInPortIds.Input, out var inputValue)
                ? inputValue
                : null;
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInPortIds.Output] = FormatValue(input),
            };
            return Task.FromResult(outputs);
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is bool
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal)
            {
                return (value as IFormattable)?.ToString(null, CultureInfo.InvariantCulture)
                    ?? string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }
    }
}
