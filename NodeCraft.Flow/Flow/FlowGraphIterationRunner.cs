using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NodeCraft.Flow
{
    internal static class FlowGraphIterationRunner
    {
        public static async Task ExecuteAsync(
            IReadOnlyList<WorkflowNode> sortedNodes,
            IReadOnlyDictionary<string, IFlowNodeExecutor> executors,
            IReadOnlyDictionary<string, FlowNodeDefinition> definitionsByNodeId,
            FlowExecutionContext context,
            IReadOnlySessionValueStore sessionValues,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            logger.LogTrace("Graph execution started ({NodeCount} nodes).", sortedNodes.Count);

            foreach (var node in sortedNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var definition = definitionsByNodeId[node.Id];
                var inputs = ResolveInputs(
                    node,
                    definition,
                    definitionsByNodeId,
                    context,
                    sessionValues);
                var executor = executors[node.Id];

                if (ShouldSkipNode(node, definition, inputs))
                {
                    context.MarkSkipped(node.Id);
                    logger.LogTrace("Skipping node '{NodeId}'.", node.Id);
                    continue;
                }

                context.MarkRunning(node.Id);
                logger.LogTrace("Executing node '{NodeId}' ({TypeKey}).", node.Id, node.TypeKey);

                IReadOnlyDictionary<string, object> outputs;
                try
                {
                    outputs = await executor.ExecuteAsync(
                            context,
                            node,
                            definition,
                            inputs,
                            cancellationToken)
                        .ConfigureAwait(false);

                    FlowRuntimeValueValidator.ValidateIterationOutputs(node, definition, outputs);

                    context.MarkSucceeded(node.Id);
                }
                catch (Exception exception)
                {
                    context.MarkFailed(node.Id, exception);
                    logger.LogError(exception, "Node '{NodeId}' failed.", node.Id);
                    throw;
                }

                foreach (var pair in outputs)
                {
                    var slot = FlowRuntimeValueValidator.FindOutputSlot(definition, pair.Key);
                    context.SetPortValue(node.Id, slot, pair.Value);
                }
            }

            logger.LogTrace("Graph iteration finished.");
            logger.LogTrace("Graph execution finished.");
        }

        private static Dictionary<string, object> ResolveInputs(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, FlowNodeDefinition> definitionsByNodeId,
            FlowExecutionContext context,
            IReadOnlySessionValueStore sessionValues)
        {
            var inputs = new Dictionary<string, object>();

            foreach (var inputPort in definition.InputPorts)
            {
                if (!node.Inputs.TryGetValue(inputPort.Id, out var configured))
                {
                    if (inputPort.DefaultValue != null)
                    {
                        inputs[inputPort.Id] = inputPort.DefaultValue;
                    }

                    continue;
                }

                if (configured is LinkRef linkRef)
                {
                    if (!definitionsByNodeId.TryGetValue(
                            linkRef.SourceNodeId,
                            out var sourceDefinition))
                    {
                        throw new InvalidOperationException(
                            $"Link source node '{linkRef.SourceNodeId}' was not found.");
                    }

                    var sourcePort = sourceDefinition.OutputPorts[linkRef.SourceSlot];
                    if (sourcePort.Availability == FlowPortAvailability.Iteration
                        && context.TryGetPortValue(
                            linkRef.SourceNodeId,
                            linkRef.SourceSlot,
                            out var currentValue))
                    {
                        inputs[inputPort.Id] = currentValue;
                    }
                    else if (sourcePort.Availability == FlowPortAvailability.Session
                        && sessionValues.TryGetPortValue(
                            linkRef.SourceNodeId,
                            linkRef.SourceSlot,
                            out var sessionValue))
                    {
                        inputs[inputPort.Id] = sessionValue;
                    }

                    // A configured link with a missing source value does not fall back to DefaultValue.
                }
                else
                {
                    inputs[inputPort.Id] = configured;
                }
            }

            return inputs;
        }

        private static bool ShouldSkipNode(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs)
        {
            return !HasActiveControlInput(node, definition, inputs)
                || HasMissingRequiredRuntimeInput(definition, inputs);
        }

        private static bool HasActiveControlInput(
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs)
        {
            var controlInputIds = definition.InputPorts
                .Where(port => port.IsControlPort)
                .Select(port => port.Id)
                .ToList();

            if (controlInputIds.Count == 0)
            {
                return true;
            }

            var hasControlLink = controlInputIds.Any(id =>
                node.Inputs.TryGetValue(id, out var value) && value is LinkRef);

            if (!hasControlLink)
            {
                return true;
            }

            foreach (var portId in controlInputIds)
            {
                if (inputs.TryGetValue(portId, out var value) && IsActiveControlValue(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasMissingRequiredRuntimeInput(
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs)
        {
            foreach (var inputPort in definition.InputPorts)
            {
                if (!inputPort.IsRequired || inputPort.IsControlPort)
                {
                    continue;
                }

                if (!inputs.ContainsKey(inputPort.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActiveControlValue(object value)
        {
            if (value is FlowControlSignal signal)
            {
                return signal == FlowControlSignal.Active;
            }

            if (value is IEnumerable values && value is not string)
            {
                foreach (var item in values)
                {
                    if (IsActiveControlValue(item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
