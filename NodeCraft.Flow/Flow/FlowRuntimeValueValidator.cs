using System;
using System.Collections.Generic;

namespace NodeCraft.Flow
{
    internal static class FlowRuntimeValueValidator
    {
        internal static void ValidateSessionOutputs(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> outputs)
        {
            ValidateOutputs(node, definition, outputs, FlowPortAvailability.Session);
        }

        internal static void ValidateIterationOutputs(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> outputs)
        {
            ValidateOutputs(node, definition, outputs, FlowPortAvailability.Iteration);
        }

        internal static int FindOutputSlot(
            FlowNodeDefinition definition,
            string portId)
        {
            for (var index = 0; index < definition.OutputPorts.Count; index++)
            {
                if (string.Equals(definition.OutputPorts[index].Id, portId, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static void ValidateOutputs(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> outputs,
            FlowPortAvailability availability)
        {
            if (outputs == null)
            {
                throw new InvalidOperationException(
                    $"Node '{node.Id}' returned null outputs.");
            }

            foreach (var pair in outputs)
            {
                var slot = FindOutputSlot(definition, pair.Key);
                if (slot < 0)
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' returned unknown output '{pair.Key}'.");
                }

                var outputPort = definition.OutputPorts[slot];
                if (outputPort.Availability != availability)
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' returned output '{pair.Key}', "
                        + $"but the port does not declare {availability} availability.");
                }

                var dataType = outputPort.DataType ?? FlowDataType.Object;
                if (!dataType.AcceptsValue(pair.Value))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' returned output '{pair.Key}' "
                        + $"with an incompatible value for data type '{dataType}'.");
                }
            }
        }
    }
}
