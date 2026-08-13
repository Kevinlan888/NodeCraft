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

                ValidateRequiredInputs(node, registration.Definition, result);
            }

            foreach (var node in _workflow.Nodes)
            {
                if (!_registry.TryResolve(node.TypeKey, out var registration))
                {
                    continue;
                }

                foreach (var pair in node.Inputs)
                {
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
                    var targetPort = registration.Definition.GetInputPort(pair.Key);
                    if (sourcePort == null || targetPort == null)
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
            var validation = Validate();
            if (!validation.IsValid)
            {
                _logger.LogError("Graph validation failed with {ErrorCount} errors.", validation.Errors.Count);
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors.Select(error => error.Message)));
            }

            var context = new FlowExecutionContext();
            var sorted = TopologicalSort(_workflow);
            _logger.LogInformation("Graph execution started ({NodeCount} nodes).", sorted.Count);

            foreach (var node in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var registration = _registry.Resolve(node.TypeKey);
                var inputs = ResolveInputs(node, registration.Definition, context);
                var executor = registration.ExecutorFactory();

                if (ShouldSkipNode(node, registration.Definition, inputs))
                {
                    context.MarkSkipped(node.Id);
                    _logger.LogDebug("Skipping node '{NodeId}'.", node.Id);
                    continue;
                }

                context.MarkRunning(node.Id);
                _logger.LogDebug("Executing node '{NodeId}' ({TypeKey}).", node.Id, node.TypeKey);

                IReadOnlyDictionary<string, object> outputs;
                try
                {
                    outputs = await executor.ExecuteAsync(context, node, registration.Definition, inputs, cancellationToken);
                    context.MarkSucceeded(node.Id);
                }
                catch (Exception ex)
                {
                    context.MarkFailed(node.Id, ex);
                    _logger.LogError(ex, "Node '{NodeId}' failed.", node.Id);
                    throw;
                }

                foreach (var pair in outputs)
                {
                    var slot = FindOutputSlot(registration.Definition, pair.Key);
                    if (slot >= 0)
                    {
                        context.SetPortValue(node.Id, slot, pair.Value);
                    }
                }
            }

            _logger.LogInformation("Graph execution finished.");

            return context;
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

                    indegree[node.Id]++;
                    if (!dependents[linkRef.SourceNodeId].Contains(node.Id))
                    {
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

        private Dictionary<string, object> ResolveInputs(WorkflowNode node, FlowNodeDefinition definition, FlowExecutionContext context)
        {
            var inputs = new Dictionary<string, object>();

            foreach (var inputPort in definition.InputPorts)
            {
                if (!node.Inputs.TryGetValue(inputPort.Id, out var configured))
                {
                    continue;
                }

                if (configured is LinkRef linkRef)
                {
                    // 上游被跳过时此处取不到值 → 输入缺失 → 下游也跳过（跳过状态自然传播）。
                    if (context.TryGetPortValue(linkRef.SourceNodeId, linkRef.SourceSlot, out var portValue))
                    {
                        inputs[inputPort.Id] = portValue;
                    }
                }
                else
                {
                    inputs[inputPort.Id] = configured;
                }
            }

            return inputs;
        }

        private bool ShouldSkipNode(WorkflowNode node, FlowNodeDefinition definition, IReadOnlyDictionary<string, object> inputs)
        {
            return !HasActiveControlInput(node, definition, inputs)
                || HasMissingRequiredRuntimeInput(definition, inputs);
        }

        private bool HasActiveControlInput(WorkflowNode node, FlowNodeDefinition definition, IReadOnlyDictionary<string, object> inputs)
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

        private static bool HasMissingRequiredRuntimeInput(FlowNodeDefinition definition, IReadOnlyDictionary<string, object> inputs)
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

            if (value is System.Collections.IEnumerable values && value is not string)
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

        private static FlowPortDefinition GetPortAtSlot(IReadOnlyList<FlowPortDefinition> ports, int slot)
        {
            return slot >= 0 && slot < ports.Count ? ports[slot] : null;
        }

        private static int FindOutputSlot(FlowNodeDefinition definition, string portId)
        {
            for (int i = 0; i < definition.OutputPorts.Count; i++)
            {
                if (string.Equals(definition.OutputPorts[i].Id, portId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
