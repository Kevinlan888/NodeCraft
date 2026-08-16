using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NodeCraft.Flow
{
    internal sealed class FlowInputPortDescriptor
    {
        public int Slot { get; set; }

        public FlowPortDefinition Definition { get; set; }

        public PortParameter RuntimePort { get; set; }
    }

    internal static class FlowDynamicInputResolver
    {
        public static void ValidateTemplate(FlowNodeDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var template = definition.DynamicInputTemplate;
            if (template == null)
            {
                return;
            }

            if (definition.InputPorts == null)
            {
                throw new InvalidOperationException(
                    $"Dynamic node '{definition.TypeKey ?? string.Empty}' must declare an input-port list.");
            }

            if (string.IsNullOrWhiteSpace(template.PortIdPrefix))
            {
                throw new InvalidOperationException("Dynamic input PortIdPrefix must not be empty.");
            }

            if (template.DataType == null)
            {
                throw new InvalidOperationException("Dynamic input DataType must not be null.");
            }

            if (template.MinCount < 0)
            {
                throw new InvalidOperationException("Dynamic input MinCount must be non-negative.");
            }

            if (template.InitialCount < 0)
            {
                throw new InvalidOperationException("Dynamic input InitialCount must be non-negative.");
            }

            if (template.MinCount > template.InitialCount)
            {
                throw new InvalidOperationException(
                    "Dynamic input InitialCount must be greater than or equal to MinCount.");
            }

            if (template.MaxCount.HasValue && template.MaxCount.Value < 0)
            {
                throw new InvalidOperationException("Dynamic input MaxCount must be non-negative.");
            }

            if (template.MaxCount.HasValue && template.InitialCount > template.MaxCount.Value)
            {
                throw new InvalidOperationException(
                    "Dynamic input InitialCount must be less than or equal to MaxCount.");
            }

            var prefix = template.PortIdPrefix.Trim();
            var generatedPrefix = prefix + "_";
            foreach (var fixedPort in definition.InputPorts.Where(port => port != null))
            {
                if (string.IsNullOrWhiteSpace(fixedPort.Id))
                {
                    continue;
                }

                if (string.Equals(fixedPort.Id, prefix, StringComparison.Ordinal)
                    || fixedPort.Id.StartsWith(generatedPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Dynamic input PortIdPrefix '{prefix}' collides with fixed input '{fixedPort.Id}'.");
                }
            }
        }

        public static void MaterializeNodePorts(NodeModel node, FlowNodeDefinition definition)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ValidateTemplate(definition);

            var existing = node.InputParameters ?? new List<PortParameter>();
            var hadRuntimePorts = existing.Count > 0;
            var fixedDefinitions = (definition.InputPorts ?? new List<FlowPortDefinition>())
                .Where(port => port != null)
                .ToList();
            var fixedIds = new HashSet<string>(
                fixedDefinitions.Where(port => !string.IsNullOrWhiteSpace(port.Id)).Select(port => port.Id),
                StringComparer.Ordinal);
            var fixedRuntime = new Dictionary<string, PortParameter>(StringComparer.Ordinal);
            var unknownFixedRuntime = new List<PortParameter>();
            var dynamicRuntime = new List<PortParameter>();

            foreach (var runtimePort in existing)
            {
                if (runtimePort == null)
                {
                    continue;
                }

                if (runtimePort.IsDynamic)
                {
                    if (definition.DynamicInputTemplate == null)
                    {
                        throw new InvalidOperationException(
                            $"Node '{node.Id}' contains dynamic input '{runtimePort.PortId ?? string.Empty}', "
                            + "but its definition no longer supports dynamic inputs.");
                    }

                    dynamicRuntime.Add(runtimePort);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(runtimePort.PortId)
                    || !fixedIds.Contains(runtimePort.PortId))
                {
                    unknownFixedRuntime.Add(runtimePort);
                    continue;
                }

                if (!fixedRuntime.TryAdd(runtimePort.PortId, runtimePort))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' declares duplicate fixed input '{runtimePort.PortId}'.");
                }
            }

            if (definition.DynamicInputTemplate != null && unknownFixedRuntime.Count > 0)
            {
                var unknownId = unknownFixedRuntime.FirstOrDefault()?.PortId ?? string.Empty;
                throw new InvalidOperationException(
                    $"Node '{node.Id}' declares unknown fixed input '{unknownId}' on a dynamic definition.");
            }

            ValidateDynamicRuntimePorts(node, definition, dynamicRuntime);

            var normalized = new List<PortParameter>();
            foreach (var fixedDefinition in fixedDefinitions)
            {
                if (!fixedRuntime.TryGetValue(fixedDefinition.Id, out var runtimePort))
                {
                    runtimePort = CreatePortParameter(fixedDefinition);
                }

                ApplyPortDefinition(runtimePort, fixedDefinition);
                normalized.Add(runtimePort);
            }

            if (definition.DynamicInputTemplate != null)
            {
                if (!hadRuntimePorts)
                {
                    dynamicRuntime = new List<PortParameter>();
                    for (var index = 1; index <= definition.DynamicInputTemplate.InitialCount; index++)
                    {
                        dynamicRuntime.Add(CreateDynamicPort(definition.DynamicInputTemplate, index));
                    }
                }

                EnsureDynamicCountInBounds(node, definition.DynamicInputTemplate, dynamicRuntime.Count);
                normalized.AddRange(dynamicRuntime);
            }
            else
            {
                normalized.AddRange(unknownFixedRuntime);
            }

            node.InputParameters = normalized;
        }

        public static IReadOnlyList<string> GetDynamicPortIds(NodeModel node)
        {
            if (node == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in node.InputParameters ?? Enumerable.Empty<PortParameter>())
            {
                if (port == null || !port.IsDynamic)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(port.PortId))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' contains a dynamic input with an empty Id.");
                }

                if (!ids.Add(port.PortId))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' declares duplicate dynamic input '{port.PortId}'.");
                }

                result.Add(port.PortId);
            }

            return result;
        }

        public static IReadOnlyList<FlowInputPortDescriptor> ResolveNodeInputPorts(
            NodeModel node,
            FlowNodeDefinition definition)
        {
            MaterializeNodePorts(node, definition);
            var effectiveDefinition = ResolveDefinition(definition, GetDynamicPortIds(node));
            var runtimePorts = node.InputParameters ?? new List<PortParameter>();

            return effectiveDefinition.InputPorts
                .Select((port, slot) => new FlowInputPortDescriptor
                {
                    Slot = slot,
                    Definition = port,
                    RuntimePort = runtimePorts.FirstOrDefault(runtimePort =>
                        runtimePort != null
                        && string.Equals(runtimePort.PortId, port.Id, StringComparison.Ordinal)),
                })
                .ToList();
        }

        public static FlowNodeDefinition ResolveDefinition(
            FlowNodeDefinition registeredDefinition,
            IReadOnlyList<string> dynamicInputPortIds)
        {
            if (registeredDefinition == null)
            {
                throw new ArgumentNullException(nameof(registeredDefinition));
            }

            ValidateTemplate(registeredDefinition);
            var dynamicIds = dynamicInputPortIds ?? Array.Empty<string>();
            var dynamicIdSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dynamicId in dynamicIds)
            {
                if (string.IsNullOrWhiteSpace(dynamicId) || !dynamicIdSet.Add(dynamicId))
                {
                    throw new InvalidOperationException(
                        $"Node definition '{registeredDefinition.TypeKey ?? string.Empty}' contains duplicate or empty dynamic input Id.");
                }
            }

            if (dynamicIds.Count > 0 && registeredDefinition.DynamicInputTemplate == null)
            {
                throw new InvalidOperationException(
                    $"Node definition '{registeredDefinition.TypeKey ?? string.Empty}' does not support dynamic input ports.");
            }

            var effective = new FlowNodeDefinition
            {
                TypeKey = registeredDefinition.TypeKey,
                DisplayName = registeredDefinition.DisplayName,
                Category = registeredDefinition.Category,
                Version = registeredDefinition.Version,
                InputPorts = new List<FlowPortDefinition>(),
                OutputPorts = registeredDefinition.OutputPorts == null
                    ? new List<FlowPortDefinition>()
                    : new List<FlowPortDefinition>(registeredDefinition.OutputPorts),
                DynamicInputTemplate = registeredDefinition.DynamicInputTemplate,
            };

            foreach (var inputPort in registeredDefinition.InputPorts ?? new List<FlowPortDefinition>())
            {
                if (inputPort == null)
                {
                    continue;
                }

                if (inputPort.IsDynamic)
                {
                    throw new InvalidOperationException(
                        $"Registered input '{inputPort.Id ?? string.Empty}' cannot be dynamic without runtime metadata.");
                }

                effective.InputPorts.Add(inputPort);
            }

            if (registeredDefinition.DynamicInputTemplate != null)
            {
                foreach (var dynamicId in dynamicIds)
                {
                    effective.InputPorts.Add(CreateDynamicDefinition(
                        registeredDefinition.DynamicInputTemplate,
                        dynamicId));
                }
            }

            return effective;
        }

        public static bool TryAddDynamicPort(
            NodeModel node,
            FlowNodeDefinition definition,
            out PortParameter port,
            out string error)
        {
            port = null;
            error = null;

            try
            {
                MaterializeNodePorts(node, definition);
                var template = definition.DynamicInputTemplate;
                if (template == null)
                {
                    error = $"Node '{node?.Id ?? string.Empty}' does not support dynamic input ports.";
                    return false;
                }

                var dynamicPorts = node.InputParameters.Where(item => item != null && item.IsDynamic).ToList();
                if (template.MaxCount.HasValue && dynamicPorts.Count >= template.MaxCount.Value)
                {
                    error = $"Node '{node.Id}' already has the maximum of {template.MaxCount.Value} dynamic inputs.";
                    return false;
                }

                var usedIds = new HashSet<string>(dynamicPorts.Select(item => item.PortId), StringComparer.Ordinal);
                var suffix = 1;
                string portId;
                do
                {
                    portId = template.PortIdPrefix.Trim() + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }
                while (!usedIds.Add(portId));

                port = CreateDynamicPort(template, ParseDynamicSuffix(portId, template.PortIdPrefix));
                port.PortId = portId;
                node.InputParameters.Add(port);
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryRemoveDynamicPort(
            NodeModel node,
            FlowNodeDefinition definition,
            string portId,
            out int removedSlot,
            out string error)
        {
            removedSlot = -1;
            error = null;

            try
            {
                MaterializeNodePorts(node, definition);
                if (definition.DynamicInputTemplate == null)
                {
                    error = $"Node '{node?.Id ?? string.Empty}' does not support dynamic input ports.";
                    return false;
                }

                var descriptors = ResolveNodeInputPorts(node, definition);
                var descriptor = descriptors.FirstOrDefault(item =>
                    string.Equals(item.Definition.Id, portId, StringComparison.Ordinal));
                if (descriptor == null)
                {
                    error = $"Dynamic input '{portId ?? string.Empty}' was not found on node '{node.Id}'.";
                    return false;
                }

                if (!descriptor.Definition.IsDynamic || descriptor.RuntimePort == null || !descriptor.RuntimePort.IsDynamic)
                {
                    error = $"Input '{portId}' is fixed and cannot be removed.";
                    return false;
                }

                var dynamicCount = node.InputParameters.Count(item => item != null && item.IsDynamic);
                if (dynamicCount <= definition.DynamicInputTemplate.MinCount)
                {
                    error = $"Node '{node.Id}' must keep at least {definition.DynamicInputTemplate.MinCount} dynamic inputs.";
                    return false;
                }

                node.InputParameters.Remove(descriptor.RuntimePort);
                removedSlot = descriptor.Slot;
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void ValidateDynamicRuntimePorts(
            NodeModel node,
            FlowNodeDefinition definition,
            IReadOnlyList<PortParameter> dynamicPorts)
        {
            if (dynamicPorts.Count == 0)
            {
                return;
            }

            var template = definition.DynamicInputTemplate;
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"Node '{node.Id}' contains dynamic ports but its definition has no dynamic input template.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dynamicPort in dynamicPorts)
            {
                if (string.IsNullOrWhiteSpace(dynamicPort.PortId)
                    || !ids.Add(dynamicPort.PortId)
                    || !TryParseDynamicSuffix(dynamicPort.PortId, template.PortIdPrefix, out _))
                {
                    throw new InvalidOperationException(
                        $"Node '{node.Id}' contains invalid or duplicate dynamic input '{dynamicPort.PortId ?? string.Empty}'.");
                }
            }

            EnsureDynamicCountInBounds(node, template, dynamicPorts.Count);
        }

        private static void EnsureDynamicCountInBounds(
            NodeModel node,
            FlowDynamicInputTemplate template,
            int count)
        {
            if (count < template.MinCount)
            {
                throw new InvalidOperationException(
                    $"Node '{node.Id}' has {count} dynamic inputs, below the minimum of {template.MinCount}.");
            }

            if (template.MaxCount.HasValue && count > template.MaxCount.Value)
            {
                throw new InvalidOperationException(
                    $"Node '{node.Id}' has {count} dynamic inputs, above the maximum of {template.MaxCount.Value}.");
            }
        }

        private static PortParameter CreateDynamicPort(FlowDynamicInputTemplate template, int suffix)
        {
            return new PortParameter
            {
                PortId = template.PortIdPrefix.Trim() + "_" + suffix.ToString(CultureInfo.InvariantCulture),
                IsDynamic = true,
                Parameter = new Parameter
                {
                    ParameterType = template.DataType.Key,
                    Value = template.DefaultValue,
                },
                PortDirection = template.PreferredDirection,
            };
        }

        private static FlowPortDefinition CreateDynamicDefinition(
            FlowDynamicInputTemplate template,
            string portId)
        {
            var suffix = ParseDynamicSuffix(portId, template.PortIdPrefix);
            return new FlowPortDefinition
            {
                Id = portId,
                DisplayName = string.IsNullOrWhiteSpace(template.DisplayNamePrefix)
                    ? portId
                    : template.DisplayNamePrefix.Trim() + " " + suffix.ToString(CultureInfo.InvariantCulture),
                IOType = EIOType.Input,
                DataType = template.DataType,
                PreferredDirection = template.PreferredDirection,
                IsRequired = template.IsRequired,
                AllowMultipleConnections = false,
                DefaultValue = template.DefaultValue,
                Availability = template.Availability,
                IsDynamic = true,
            };
        }

        private static PortParameter CreatePortParameter(FlowPortDefinition definition)
        {
            return new PortParameter
            {
                PortId = definition.Id,
                Parameter = new Parameter
                {
                    ParameterType = definition.DataType?.Key ?? string.Empty,
                    Value = definition.DefaultValue,
                },
                PortDirection = definition.PreferredDirection,
            };
        }

        private static void ApplyPortDefinition(PortParameter runtimePort, FlowPortDefinition definition)
        {
            runtimePort.PortId = definition.Id;
            runtimePort.IsDynamic = definition.IsDynamic;
            runtimePort.Parameter ??= new Parameter();
            runtimePort.Parameter.ParameterType = definition.DataType?.Key ?? string.Empty;
            if (runtimePort.Parameter.Value == null && definition.DefaultValue != null)
            {
                runtimePort.Parameter.Value = definition.DefaultValue;
            }

            if (runtimePort.PortDirection == EPortDirection.None)
            {
                runtimePort.PortDirection = definition.PreferredDirection;
            }
        }

        private static bool TryParseDynamicSuffix(
            string portId,
            string prefix,
            out int suffix)
        {
            suffix = 0;
            var expectedPrefix = (prefix ?? string.Empty).Trim() + "_";
            if (!portId.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var suffixText = portId.Substring(expectedPrefix.Length);
            return int.TryParse(
                       suffixText,
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out suffix)
                && suffix > 0
                && string.Equals(
                    portId,
                    expectedPrefix + suffix.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
        }

        private static int ParseDynamicSuffix(string portId, string prefix)
        {
            if (!TryParseDynamicSuffix(portId, prefix, out var suffix))
            {
                throw new InvalidOperationException(
                    $"Dynamic input Id '{portId}' does not match prefix '{prefix}'.");
            }

            return suffix;
        }
    }
}
