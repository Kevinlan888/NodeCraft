using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow.Nodes
{
    public class JsonSerializeExecutor : IFlowNodeExecutor
    {
        public const string FlowNodeTypeKey = "node.json-serialize";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var input = inputs.TryGetValue(BuiltInNodePorts.Input, out var inputValue)
                ? inputValue
                : null;
            var json = input == null
                ? JsonSerializer.Serialize<object>(null, SerializerOptions)
                : JsonSerializer.Serialize(input, input.GetType(), SerializerOptions);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                [BuiltInNodePorts.Output] = json,
            };

            return Task.FromResult(outputs);
        }
    }
}
