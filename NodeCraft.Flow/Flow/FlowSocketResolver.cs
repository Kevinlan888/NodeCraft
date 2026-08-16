using System;
using System.Collections.Generic;
using System.Linq;
using NodeCraft.Localization;

namespace NodeCraft.Flow
{
    internal sealed class FlowSocketDescriptor
    {
        public int Slot { get; set; }

        public FlowPortDefinition Definition { get; set; }

        public PortParameter RuntimePort { get; set; }
    }

    internal sealed class FlowSocketVisualStyle
    {
        public double Diameter { get; set; }

        public string BrushResourceKey { get; set; }

        public double LabelOpacity { get; set; }

        public double LabelFontSize { get; set; }
    }

    internal static class FlowSocketResolver
    {
        public static FlowSocketVisualStyle ResolveVisualStyle(FlowPortDefinition definition, PortParameter runtimePort)
        {
            var typeKey = definition?.DataType?.Key ?? runtimePort?.Parameter?.ParameterType ?? string.Empty;
            var isControl = string.Equals(typeKey, FlowDataType.Control.Key, StringComparison.OrdinalIgnoreCase);
            return isControl
                ? new FlowSocketVisualStyle
                {
                    Diameter = 12,
                    BrushResourceKey = "colorStatusWarningBackground3",
                    LabelOpacity = 1,
                    LabelFontSize = 11,
                }
                : new FlowSocketVisualStyle
                {
                    Diameter = 12,
                    BrushResourceKey = "colorBrandStroke1",
                    LabelOpacity = 1,
                    LabelFontSize = 11,
                };
        }

        public static IReadOnlyList<FlowSocketDescriptor> Resolve(NodeModel node, FlowNodeDefinition definition, bool isInput)
        {
            var result = new List<FlowSocketDescriptor>();

            if (isInput)
            {
                if (node == null || definition == null)
                {
                    return result;
                }

                foreach (var inputPort in FlowDynamicInputResolver.ResolveNodeInputPorts(node, definition))
                {
                    result.Add(new FlowSocketDescriptor
                    {
                        Slot = inputPort.Slot,
                        Definition = inputPort.Definition,
                        RuntimePort = inputPort.RuntimePort,
                    });
                }

                return result;
            }

            var definitions = isInput ? definition?.InputPorts : definition?.OutputPorts;
            var runtimePorts = isInput ? node?.InputParameters : node?.OutputParameters;

            if (definitions == null)
            {
                return result;
            }

            for (int slot = 0; slot < definitions.Count; slot++)
            {
                var portDefinition = definitions[slot];
                if (portDefinition == null)
                {
                    continue;
                }

                result.Add(new FlowSocketDescriptor
                {
                    Slot = slot,
                    Definition = portDefinition,
                    RuntimePort = runtimePorts?.FirstOrDefault(port =>
                        port != null && string.Equals(port.PortId, portDefinition.Id, StringComparison.Ordinal)),
                });
            }

            return result;
        }

        public static string ResolveLabel(FlowPortDefinition definition, PortParameter runtimePort)
        {
            var portId = definition?.Id ?? runtimePort?.PortId;
            if (string.IsNullOrWhiteSpace(portId))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(definition?.DisplayName))
            {
                var displayNameKey = "FlowPort_" + ToResourceToken(definition.DisplayName);
                var localizedDisplayName = LanguageManager.GetString(displayNameKey);
                if (!string.Equals(localizedDisplayName, displayNameKey, StringComparison.Ordinal))
                {
                    return localizedDisplayName;
                }
            }

            var portKey = "FlowPort_" + portId.Replace(" ", string.Empty);
            var localizedPortName = LanguageManager.GetString(portKey);
            if (!string.Equals(localizedPortName, portKey, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(definition?.DisplayName)
                    || IsGenericDisplayName(definition.DisplayName, portId)))
            {
                return localizedPortName;
            }

            if (!string.IsNullOrWhiteSpace(definition?.DisplayName))
            {
                return definition.DisplayName;
            }

            return portId;
        }

        private static string ToResourceToken(string displayName)
        {
            var token = displayName.Replace(" ", string.Empty);
            return token.Length == 0
                ? token
                : char.ToLowerInvariant(token[0]) + token.Substring(1);
        }

        private static bool IsGenericDisplayName(string displayName, string portId)
        {
            var normalizedDisplayName = displayName.Replace(" ", string.Empty);
            var normalizedPortId = portId.Replace(" ", string.Empty);
            return string.Equals(normalizedDisplayName, normalizedPortId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "FlowIn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "Condition", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "False", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "B", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "Input", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "Output", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "Value", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDisplayName, "Suffix", StringComparison.OrdinalIgnoreCase);
        }
    }
}
