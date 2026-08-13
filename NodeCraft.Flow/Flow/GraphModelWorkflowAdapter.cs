using System.Collections.Generic;
using System.Linq;

namespace NodeCraft.Flow
{
    public static class GraphModelWorkflowAdapter
    {
        // 等价 ComfyUI graphToPrompt：GraphModel.Links 是权威来源，端口 LinkId 会先按目标槽位重建，
        // → API 格式（WorkflowNode.Inputs 内联 LinkRef）。
        public static WorkflowDocument Convert(GraphModel graph)
        {
            var document = new WorkflowDocument();
            if (graph == null)
            {
                return document;
            }

            GraphModelLinkReconciler.Reconcile(graph);

            var linkLookup = (graph.Links ?? new List<GraphLink>())
                .ToDictionary(link => link.Id, link => link, System.StringComparer.Ordinal);

            foreach (var node in graph.Nodes)
            {
                var workflowNode = new WorkflowNode
                {
                    Id = node.Id,
                    TypeKey = node.ExecutorType,
                    DisplayName = node.Name,
                    X = node.X,
                    Y = node.Y,
                };

                if (node is IWorkflowNodeValueProvider valueProvider)
                {
                    valueProvider.WriteWorkflowInputs(workflowNode);
                }

                foreach (var inputPort in node.InputParameters ?? Enumerable.Empty<PortParameter>())
                {
                    if (string.IsNullOrWhiteSpace(inputPort.LinkId))
                    {
                        continue;
                    }

                    if (!linkLookup.TryGetValue(inputPort.LinkId, out var link))
                    {
                        continue;
                    }

                    workflowNode.Inputs[inputPort.PortId] = new LinkRef
                    {
                        SourceNodeId = link.OriginNodeId,
                        SourceSlot = link.OriginSlot,
                    };
                }

                document.Nodes.Add(workflowNode);
            }

            return document;
        }
    }
}
