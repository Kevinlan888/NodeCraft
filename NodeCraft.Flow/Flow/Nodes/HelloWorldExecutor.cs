using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class HelloWorldExecutor : IFlowNodeExecutor
    {
        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputStr = string.Empty;

            if (inputs.TryGetValue(BuiltInNodePorts.Input, out var value) && value is string text)
            {
                inputStr = text;
            }

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = string.IsNullOrEmpty(inputStr) ? "Hello World" : $"{inputStr} Hello World"
            };

            return Task.FromResult(outputs);
        }
    }
}
