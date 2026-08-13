using NodeCraft.Flow.Nodes;

namespace NodeCraft.Flow
{
    public static class FlowDemoDocumentFactory
    {
        public static WorkflowDocument CreateGreetingWorkflow(string inputText, string suffix)
        {
            var sourceNodeId = "node-source";
            var appendNodeId = "node-append";
            var helloNodeId = "node-hello";

            var document = new WorkflowDocument();
            document.Nodes.Add(new WorkflowNode
            {
                Id = sourceNodeId,
                TypeKey = StringValueExecutor.FlowNodeTypeKey,
                DisplayName = "Source",
                X = 40,
                Y = 40,
                Inputs =
                {
                    [BuiltInNodePorts.Value] = inputText ?? string.Empty
                }
            });
            document.Nodes.Add(new WorkflowNode
            {
                Id = appendNodeId,
                TypeKey = AppendTextExecutor.FlowNodeTypeKey,
                DisplayName = "Append",
                X = 240,
                Y = 40,
                Inputs =
                {
                    [BuiltInNodePorts.Suffix] = suffix ?? string.Empty,
                    [BuiltInNodePorts.Input] = new LinkRef { SourceNodeId = sourceNodeId, SourceSlot = 0 },
                }
            });
            document.Nodes.Add(new WorkflowNode
            {
                Id = helloNodeId,
                TypeKey = HelloworldNodeModel.FlowNodeTypeKey,
                DisplayName = "Hello World",
                X = 460,
                Y = 40,
                Inputs =
                {
                    [BuiltInNodePorts.Input] = new LinkRef { SourceNodeId = appendNodeId, SourceSlot = 0 },
                }
            });

            return document;
        }

        public static WorkflowDocument CreateInvalidWorkflow()
        {
            var document = new WorkflowDocument();
            document.Nodes.Add(new WorkflowNode
            {
                Id = "broken-node",
                TypeKey = AppendTextExecutor.FlowNodeTypeKey,
                DisplayName = "Broken Append",
                Inputs =
                {
                    [BuiltInNodePorts.Suffix] = "!"
                }
            });

            return document;
        }
    }
}