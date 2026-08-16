using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NodeCraft.Flow
{
    public class GraphExecutor
    {
        private readonly FlowNodeRegistry _registry;
        private readonly WorkflowDocument _workflow;
        private readonly ILogger<GraphExecutor> _logger;

        public GraphExecutor(WorkflowDocument workflow, FlowNodeRegistry registry = null, ILogger<GraphExecutor> logger = null)
        {
            _workflow = workflow ?? new WorkflowDocument();
            _registry = registry ?? NodeExecutorFactory.Registry;
            _logger = logger ?? NullLogger<GraphExecutor>.Instance;
        }

        public FlowValidationResult Validate()
        {
            var result = new FlowValidationResult();
            var nodeLookup = _workflow.Nodes.ToDictionary(node => node.Id, node => node);
            var definitionsByNodeId = new Dictionary<string, FlowNodeDefinition>(StringComparer.Ordinal);

            foreach (var node in _workflow.Nodes)
            {
                if (!_registry.TryResolve(node.TypeKey, out var registration))
                {
                    result.Errors.Add(new FlowValidationError
                    {
                        Code = "UnknownNodeType",
                        Message = $"Node '{node.DisplayName ?? node.Id}' references unregistered type '{node.TypeKey}'.",
                        NodeId = node.Id,
                    });
                    continue;
                }

                if (!TryResolveDefinition(node, registration, result, out var definition))
                {
                    continue;
                }

                definitionsByNodeId[node.Id] = definition;
                ValidateRequiredInputs(node, definition, result);
            }

            foreach (var node in _workflow.Nodes)
            {
                if (!definitionsByNodeId.TryGetValue(node.Id, out var definition))
                {
                    continue;
                }

                foreach (var pair in node.Inputs ?? new Dictionary<string, object>())
                {
                    var targetPort = definition.GetInputPort(pair.Key);
                    if (targetPort == null)
                    {
                        if (!(pair.Value is LinkRef)
                            && !IsDynamicInputKey(definition, pair.Key))
                        {
                            // Some model-backed nodes intentionally keep editor-only values
                            // in WorkflowNode.Inputs without declaring runtime input ports.
                            continue;
                        }

                        result.Errors.Add(new FlowValidationError
                        {
                            Code = "UnknownPort",
                            Message = $"Node '{node.DisplayName ?? node.Id}' input '{pair.Key}' references an unknown slot/port.",
                            NodeId = node.Id,
                            PortId = pair.Key,
                        });
                        continue;
                    }

                    if (!(pair.Value is LinkRef linkRef))
                    {
                        continue;
                    }

                    if (!nodeLookup.TryGetValue(linkRef.SourceNodeId, out var sourceNode))
                    {
                        result.Errors.Add(new FlowValidationError
                        {
                            Code = "DanglingLink",
                            Message = $"Node '{node.DisplayName ?? node.Id}' input '{pair.Key}' references missing source node '{linkRef.SourceNodeId}'.",
                            NodeId = node.Id,
                        });
                        continue;
                    }

                    if (!_registry.TryResolve(sourceNode.TypeKey, out var sourceRegistration))
                    {
                        continue;
                    }

                    var sourcePort = GetPortAtSlot(sourceRegistration.Definition.OutputPorts, linkRef.SourceSlot);
                    if (sourcePort == null)
                    {
                        result.Errors.Add(new FlowValidationError
                        {
                            Code = "UnknownPort",
                            Message = $"Node '{node.DisplayName ?? node.Id}' input '{pair.Key}' references an unknown slot/port.",
                            NodeId = node.Id,
                        });
                        continue;
                    }

                    if (!sourcePort.DataType.IsCompatibleWith(targetPort.DataType))
                    {
                        result.Errors.Add(new FlowValidationError
                        {
                            Code = "IncompatiblePortTypes",
                            Message = $"Cannot connect '{sourcePort.DataType}' to '{targetPort.DataType}'.",
                            NodeId = node.Id,
                            PortId = pair.Key,
                        });
                    }

                    if (!targetPort.IsControlPort
                        && targetPort.Availability == FlowPortAvailability.Session
                        && sourcePort.Availability != FlowPortAvailability.Session)
                    {
                        result.Errors.Add(new FlowValidationError
                        {
                            Code = "SessionInputUnavailable",
                            Message = $"Node '{node.DisplayName ?? node.Id}' input '{pair.Key}' "
                                + "requires a Session-capable source port.",
                            NodeId = node.Id,
                            PortId = pair.Key,
                        });
                    }
                }
            }

            var sortedNodes = TopologicalSort(_workflow);
            if (sortedNodes.Count != _workflow.Nodes.Count)
            {
                result.Errors.Add(new FlowValidationError
                {
                    Code = "CycleDetected",
                    Message = "Workflow contains a cycle and cannot be executed as a DAG.",
                });
            }

            _logger.LogInformation("Graph validation completed: {IsValid} ({ErrorCount} errors).", result.IsValid, result.Errors.Count);

            return result;
        }

        public GraphExecutionSession CreateSession()
        {
            var validation = Validate();
            if (!validation.IsValid)
            {
                _logger.LogError("Graph validation failed with {ErrorCount} errors.", validation.Errors.Count);
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    validation.Errors.Select(error => error.Message)));
            }

            return new GraphExecutionSession(
                _workflow,
                _registry,
                TopologicalSort(_workflow),
                _logger);
        }

        public async Task<FlowExecutionContext> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            GraphExecutionSession session = null;
            Exception primaryException = null;
            try
            {
                session = CreateSession();
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                return await session.ExecuteIterationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                primaryException = exception;
                throw;
            }
            finally
            {
                if (session != null)
                {
                    try
                    {
                        await session.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException) when (primaryException != null)
                    {
                        _logger.LogError(cleanupException, "Graph cleanup failed after a primary execution failure.");
                    }

                    try
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException) when (primaryException != null)
                    {
                        _logger.LogError(cleanupException, "Graph session disposal failed after a primary execution failure.");
                    }
                }
            }
        }

        public List<WorkflowNode> TopologicalSort(WorkflowDocument workflow)
        {
            var indegree = workflow.Nodes.ToDictionary(node => node.Id, node => 0);
            var dependents = workflow.Nodes.ToDictionary(node => node.Id, node => new List<string>());

            foreach (var node in workflow.Nodes)
            {
                foreach (var value in node.Inputs.Values)
                {
                    if (!(value is LinkRef linkRef) || !indegree.ContainsKey(linkRef.SourceNodeId))
                    {
                        continue;
                    }

                    if (!dependents[linkRef.SourceNodeId].Contains(node.Id))
                    {
                        indegree[node.Id]++;
                        dependents[linkRef.SourceNodeId].Add(node.Id);
                    }
                }
            }

            var queue = new Queue<WorkflowNode>(workflow.Nodes.Where(node => indegree[node.Id] == 0));
            var result = new List<WorkflowNode>();

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                result.Add(node);

                foreach (var dependentId in dependents[node.Id])
                {
                    if (!indegree.ContainsKey(dependentId))
                    {
                        continue;
                    }

                    indegree[dependentId]--;
                    if (indegree[dependentId] == 0)
                    {
                        queue.Enqueue(workflow.Nodes.First(item => item.Id == dependentId));
                    }
                }
            }

            return result;
        }

        private void ValidateRequiredInputs(WorkflowNode node, FlowNodeDefinition definition, FlowValidationResult result)
        {
            foreach (var inputPort in definition.InputPorts)
            {
                if (inputPort.IsControlPort || !inputPort.IsRequired)
                {
                    continue;
                }

                if (!node.Inputs.ContainsKey(inputPort.Id) && inputPort.DefaultValue == null)
                {
                    result.Errors.Add(new FlowValidationError
                    {
                        Code = "MissingRequiredInput",
                        Message = $"Node '{node.DisplayName ?? node.Id}' is missing required input '{inputPort.Id}'.",
                        NodeId = node.Id,
                        PortId = inputPort.Id,
                    });
                }
            }
        }

        private static bool TryResolveDefinition(
            WorkflowNode node,
            FlowNodeRegistration registration,
            FlowValidationResult result,
            out FlowNodeDefinition definition)
        {
            try
            {
                definition = FlowDynamicInputResolver.ResolveDefinition(
                    registration.Definition,
                    node.DynamicInputPortIds ?? new List<string>());
                return true;
            }
            catch (InvalidOperationException exception)
            {
                definition = null;
                result.Errors.Add(new FlowValidationError
                {
                    Code = "InvalidDynamicInputs",
                    Message = $"Node '{node.DisplayName ?? node.Id}' has invalid dynamic input metadata: {exception.Message}",
                    NodeId = node.Id,
                });
                return false;
            }
        }

        private static bool IsDynamicInputKey(FlowNodeDefinition definition, string portId)
        {
            var prefix = definition.DynamicInputTemplate?.PortIdPrefix?.Trim();
            return !string.IsNullOrWhiteSpace(prefix)
                && !string.IsNullOrWhiteSpace(portId)
                && portId.StartsWith(prefix + "_", StringComparison.Ordinal);
        }

        private static FlowPortDefinition GetPortAtSlot(IReadOnlyList<FlowPortDefinition> ports, int slot)
        {
            return slot >= 0 && slot < ports.Count ? ports[slot] : null;
        }

    }
}
