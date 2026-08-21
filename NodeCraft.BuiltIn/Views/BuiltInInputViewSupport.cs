using System;
using System.Linq;
using System.Windows.Controls;
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

        internal static void BindBinary(
            FlowCanvas canvas,
            NodeModel node,
            TextBlock firstValue,
            TextBlock secondValue,
            Button swapButton)
        {
            if (firstValue == null)
            {
                throw new ArgumentNullException(nameof(firstValue));
            }

            if (secondValue == null)
            {
                throw new ArgumentNullException(nameof(secondValue));
            }

            if (swapButton == null)
            {
                throw new ArgumentNullException(nameof(swapButton));
            }

            var registration = ResolveRegistration(canvas, node);
            var dataInputs = registration.Definition.InputPorts
                .Select((port, index) => new { Port = port, Index = index })
                .Where(item => item.Port != null && !item.Port.IsControlPort)
                .ToArray();
            if (dataInputs.Length != 2)
            {
                throw new InvalidOperationException(
                    node.Name + " must have exactly two data inputs for its binary input view.");
            }

            var firstSlot = dataInputs[0].Index;
            var secondSlot = dataInputs[1].Index;
            var firstLabel = dataInputs[0].Port.DisplayName ?? dataInputs[0].Port.Id;
            var secondLabel = dataInputs[1].Port.DisplayName ?? dataInputs[1].Port.Id;
            var firstConnections = FindTargetLinks(canvas, node.Id, firstSlot);
            var secondConnections = FindTargetLinks(canvas, node.Id, secondSlot);
            var firstConnection = firstConnections.Length == 1 ? firstConnections[0] : null;
            var secondConnection = secondConnections.Length == 1 ? secondConnections[0] : null;

            firstValue.Text = firstConnections.Length > 1
                ? "已连接"
                : DescribeConnection(canvas, firstConnection);
            secondValue.Text = secondConnections.Length > 1
                ? "已连接"
                : DescribeConnection(canvas, secondConnection);
            swapButton.Content = BuildSwapButtonLabel(
                firstLabel,
                secondLabel,
                firstConnections.Length > 0,
                secondConnections.Length > 0);
            swapButton.IsEnabled = firstConnections.Length > 0 || secondConnections.Length > 0;
            swapButton.Click += (_, __) =>
            {
                var currentFirstConnections = FindTargetLinks(canvas, node.Id, firstSlot);
                var currentSecondConnections = FindTargetLinks(canvas, node.Id, secondSlot);
                var firstRuntimePorts = FindRuntimeInputs(node, dataInputs[0].Port.Id);
                var secondRuntimePorts = FindRuntimeInputs(node, dataInputs[1].Port.Id);

                ValidateTargetLinkCount(node, firstSlot, currentFirstConnections.Length);
                ValidateTargetLinkCount(node, secondSlot, currentSecondConnections.Length);
                ValidateRuntimePortCount(node, dataInputs[0].Port.Id, firstRuntimePorts.Length);
                ValidateRuntimePortCount(node, dataInputs[1].Port.Id, secondRuntimePorts.Length);

                var currentFirstConnection = currentFirstConnections.Length == 1
                    ? currentFirstConnections[0]
                    : null;
                var currentSecondConnection = currentSecondConnections.Length == 1
                    ? currentSecondConnections[0]
                    : null;
                var firstRuntimePort = firstRuntimePorts[0];
                var secondRuntimePort = secondRuntimePorts[0];
                if (currentFirstConnection == null && currentSecondConnection == null)
                {
                    return;
                }

                if (currentFirstConnection != null)
                {
                    currentFirstConnection.TargetSlot = secondSlot;
                }

                if (currentSecondConnection != null)
                {
                    currentSecondConnection.TargetSlot = firstSlot;
                }

                firstRuntimePort.LinkId = currentSecondConnection?.Id;
                secondRuntimePort.LinkId = currentFirstConnection?.Id;
                canvas.NotifyGraphChanged();
            };
        }

        private static FlowNodeRegistration ResolveRegistration(FlowCanvas canvas, NodeModel node)
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

            return registration;
        }

        private static GraphLink[] FindTargetLinks(FlowCanvas canvas, string nodeId, int slot)
        {
            return canvas.GraphModel?.Links?
                .Where(link => string.Equals(link?.TargetNodeId, nodeId, StringComparison.Ordinal)
                    && link.TargetSlot == slot)
                .ToArray()
                ?? Array.Empty<GraphLink>();
        }

        private static PortParameter[] FindRuntimeInputs(NodeModel node, string portId)
        {
            return node.InputParameters?
                .Where(port => string.Equals(port?.PortId, portId, StringComparison.Ordinal))
                .ToArray()
                ?? Array.Empty<PortParameter>();
        }

        private static void ValidateTargetLinkCount(NodeModel node, int slot, int count)
        {
            if (count > 1)
            {
                throw new InvalidOperationException(
                    "Node '" + node.Id + "' binary input slot " + slot
                    + " must have at most one target link; found " + count + ".");
            }
        }

        private static void ValidateRuntimePortCount(NodeModel node, string portId, int count)
        {
            if (count != 1)
            {
                throw new InvalidOperationException(
                    "Node '" + node.Id + "' binary input '" + portId
                    + "' must have exactly one runtime input parameter; found " + count + ".");
            }
        }

        private static string DescribeConnection(FlowCanvas canvas, GraphLink link)
        {
            if (link == null)
            {
                return "未连接";
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

        private static string BuildSwapButtonLabel(
            string firstLabel,
            string secondLabel,
            bool firstConnected,
            bool secondConnected)
        {
            if (firstConnected && secondConnected)
            {
                return "Swap " + firstLabel + "/" + secondLabel;
            }

            if (firstConnected)
            {
                return "Move " + firstLabel + " -> " + secondLabel;
            }

            if (secondConnected)
            {
                return "Move " + secondLabel + " -> " + firstLabel;
            }

            return "Swap " + firstLabel + "/" + secondLabel;
        }
    }
}
