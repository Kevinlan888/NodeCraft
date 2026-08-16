using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Flow
{
    internal static class GraphModelLinkReconciler
    {
        public static void Reconcile(GraphModel graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            graph.Nodes ??= new List<NodeModel>();
            graph.Links ??= new List<GraphLink>();

            var nodeLookup = BuildNodeLookup(graph.Nodes);
            var definitionsByNodeId = new Dictionary<string, FlowNodeDefinition>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.ExecutorType)
                    && NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
                {
                    FlowDynamicInputResolver.MaterializeNodePorts(node, registration.Definition);
                    definitionsByNodeId[node.Id] = FlowDynamicInputResolver.ResolveDefinition(
                        registration.Definition,
                        FlowDynamicInputResolver.GetDynamicPortIds(node));
                }
            }

            ClearInputLinkIds(graph.Nodes);

            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            var occupiedTargetSlots = new HashSet<Tuple<string, int>>();

            foreach (var link in graph.Links)
            {
                if (link == null)
                {
                    throw new InvalidOperationException("Graph contains a null link.");
                }

                if (string.IsNullOrWhiteSpace(link.Id))
                {
                    throw new InvalidOperationException("Graph contains a link with an empty Id.");
                }

                if (!linkIds.Add(link.Id))
                {
                    throw new InvalidOperationException($"Graph contains duplicate link Id '{link.Id}'.");
                }

                var sourceNode = ResolveLinkedNode(nodeLookup, link.OriginNodeId, link.Id, "origin");
                var targetNode = ResolveLinkedNode(nodeLookup, link.TargetNodeId, link.Id, "target");
                var sourceRegistration = ResolveRegistration(sourceNode, link.Id, "origin");
                var targetRegistration = ResolveRegistration(targetNode, link.Id, "target");

                if (link.OriginSlot < 0 || link.OriginSlot >= sourceRegistration.Definition.OutputPorts.Count)
                {
                    throw new InvalidOperationException(
                        $"Link '{link.Id}' references unknown origin slot {link.OriginSlot} on node '{sourceNode.Id}'.");
                }

                if (!definitionsByNodeId.TryGetValue(targetNode.Id, out var targetDefinitionModel))
                {
                    throw new InvalidOperationException(
                        $"Link '{link.Id}' target node '{targetNode.Id}' has no resolved input definition.");
                }

                if (link.TargetSlot < 0 || link.TargetSlot >= targetDefinitionModel.InputPorts.Count)
                {
                    throw new InvalidOperationException(
                        $"Link '{link.Id}' references unknown target slot {link.TargetSlot} on node '{targetNode.Id}'.");
                }

                var targetDefinition = targetDefinitionModel.InputPorts[link.TargetSlot];
                var targetSlot = Tuple.Create(targetNode.Id, link.TargetSlot);
                if (!occupiedTargetSlots.Add(targetSlot))
                {
                    var reason = targetDefinition.AllowMultipleConnections
                        ? "cannot be represented by the target port's single LinkId"
                        : "does not allow multiple connections";
                    throw new InvalidOperationException(
                        $"Link '{link.Id}' duplicates target slot {link.TargetSlot} on node '{targetNode.Id}', which {reason}.");
                }

                var matchingPorts = (targetNode.InputParameters ?? new List<PortParameter>())
                    .Where(port => port != null
                        && string.Equals(port.PortId, targetDefinition.Id, StringComparison.Ordinal))
                    .ToList();
                if (matchingPorts.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Link '{link.Id}' target slot {link.TargetSlot} maps to registered port '{targetDefinition.Id}', "
                        + $"but node '{targetNode.Id}' declares {matchingPorts.Count} matching input ports.");
                }

                matchingPorts[0].LinkId = link.Id;
            }
        }

        private static Dictionary<string, NodeModel> BuildNodeLookup(IEnumerable<NodeModel> nodes)
        {
            var lookup = new Dictionary<string, NodeModel>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    throw new InvalidOperationException("Graph contains a null node.");
                }

                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    throw new InvalidOperationException("Graph contains a node with an empty Id.");
                }

                if (!lookup.TryAdd(node.Id, node))
                {
                    throw new InvalidOperationException($"Graph contains duplicate node Id '{node.Id}'.");
                }
            }

            return lookup;
        }

        private static void ClearInputLinkIds(IEnumerable<NodeModel> nodes)
        {
            foreach (var node in nodes)
            {
                foreach (var inputPort in node.InputParameters ?? Enumerable.Empty<PortParameter>())
                {
                    if (inputPort != null)
                    {
                        inputPort.LinkId = null;
                    }
                }
            }
        }

        private static NodeModel ResolveLinkedNode(
            IReadOnlyDictionary<string, NodeModel> nodeLookup,
            string nodeId,
            string linkId,
            string endpoint)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || !nodeLookup.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException(
                    $"Link '{linkId}' references missing {endpoint} node '{nodeId ?? string.Empty}'.");
            }

            return node;
        }

        private static FlowNodeRegistration ResolveRegistration(NodeModel node, string linkId, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(node.ExecutorType)
                || !NodeExecutorFactory.Registry.TryResolve(node.ExecutorType, out var registration))
            {
                throw new InvalidOperationException(
                    $"Link '{linkId}' {endpoint} node '{node.Id}' uses unregistered executor type '{node.ExecutorType ?? string.Empty}'.");
            }

            return registration;
        }
    }
}
