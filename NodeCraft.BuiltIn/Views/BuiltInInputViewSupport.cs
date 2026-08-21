using System;
using System.Linq;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Views
{
    internal static class BuiltInInputViewSupport
    {
        internal static string DescribeUnaryInput(FlowCanvas canvas, NodeModel node)
        {
            if (canvas?.NodeRegistry == null)
            {
                throw new InvalidOperationException(
                    "Built-in input views must be attached through FlowNodeRegistry.BuildNodeContent.");
            }

            if (node == null
                || string.IsNullOrWhiteSpace(node.ExecutorType)
                || !canvas.NodeRegistry.TryResolve(node.ExecutorType, out var registration))
            {
                throw new InvalidOperationException(
                    "Built-in input views require a node registered with the canvas registry.");
            }

            var dataInputs = registration.Definition.InputPorts
                .Where(port => port != null && !port.IsControlPort)
                .ToArray();
            if (dataInputs.Length != 1)
            {
                throw new InvalidOperationException(
                    node.Name + " must have exactly one data input for its unary input view.");
            }

            var input = node.InputParameters?.SingleOrDefault(parameter =>
                string.Equals(parameter?.PortId, dataInputs[0].Id, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(input?.LinkId))
            {
                return "未连接";
            }

            var link = canvas.GraphModel?.Links?.FirstOrDefault(candidate =>
                string.Equals(candidate?.Id, input.LinkId, StringComparison.Ordinal)
                && string.Equals(candidate.TargetNodeId, node.Id, StringComparison.Ordinal));
            if (link == null)
            {
                return "已连接";
            }

            var source = canvas.GraphModel?.Nodes?.FirstOrDefault(candidate =>
                string.Equals(candidate?.Id, link.OriginNodeId, StringComparison.Ordinal));
            if (source == null
                || string.IsNullOrWhiteSpace(source.ExecutorType)
                || !canvas.NodeRegistry.TryResolve(source.ExecutorType, out var sourceRegistration)
                || link.OriginSlot < 0
                || link.OriginSlot >= sourceRegistration.Definition.OutputPorts.Count)
            {
                return "已连接";
            }

            var output = sourceRegistration.Definition.OutputPorts[link.OriginSlot];
            if (string.IsNullOrWhiteSpace(source.Name)
                || string.IsNullOrWhiteSpace(output?.DisplayName))
            {
                return "已连接";
            }

            return source.Name + " · " + output.DisplayName;
        }
    }
}
