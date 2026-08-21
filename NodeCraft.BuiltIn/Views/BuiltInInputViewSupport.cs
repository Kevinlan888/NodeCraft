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
            var firstConnection = FindTargetLink(canvas, node.Id, firstSlot);
            var secondConnection = FindTargetLink(canvas, node.Id, secondSlot);

            firstValue.Text = DescribeConnection(canvas, firstConnection);
            secondValue.Text = DescribeConnection(canvas, secondConnection);
            swapButton.Content = BuildSwapButtonLabel(
                firstLabel,
                secondLabel,
                firstConnection,
                secondConnection);
            swapButton.IsEnabled = firstConnection != null || secondConnection != null;
            swapButton.Click += (_, __) =>
            {
                var currentFirstConnection = FindTargetLink(canvas, node.Id, firstSlot);
                var currentSecondConnection = FindTargetLink(canvas, node.Id, secondSlot);
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

                SetPortLinkId(node, dataInputs[0].Port.Id, currentSecondConnection?.Id);
                SetPortLinkId(node, dataInputs[1].Port.Id, currentFirstConnection?.Id);
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

        private static GraphLink FindTargetLink(FlowCanvas canvas, string nodeId, int slot)
        {
            return canvas.GraphModel?.Links?.FirstOrDefault(link =>
                string.Equals(link?.TargetNodeId, nodeId, StringComparison.Ordinal)
                && link.TargetSlot == slot);
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
            GraphLink firstConnection,
            GraphLink secondConnection)
        {
            if (firstConnection != null && secondConnection != null)
            {
                return "Swap " + firstLabel + "/" + secondLabel;
            }

            if (firstConnection != null)
            {
                return "Move " + firstLabel + " -> " + secondLabel;
            }

            if (secondConnection != null)
            {
                return "Move " + secondLabel + " -> " + firstLabel;
            }

            return "Swap " + firstLabel + "/" + secondLabel;
        }

        private static void SetPortLinkId(NodeModel node, string portId, string linkId)
        {
            var runtimePort = node.InputParameters?.SingleOrDefault(port =>
                string.Equals(port?.PortId, portId, StringComparison.Ordinal));
            if (runtimePort != null)
            {
                runtimePort.LinkId = linkId;
            }
        }
    }
}
