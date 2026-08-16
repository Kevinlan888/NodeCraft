using System.Collections.Generic;

namespace NodeCraft.Flow
{
    public class WorkflowDocument
    {
        public WorkflowDocument()
        {
            Nodes = new List<WorkflowNode>();
        }

        public string SchemaVersion { get; set; } = "1.0";

        public List<WorkflowNode> Nodes { get; set; }
    }

    public class WorkflowNode
    {
        public WorkflowNode()
        {
            Inputs = new Dictionary<string, object>();
            DynamicInputPortIds = new List<string>();
        }

        public string Id { get; set; }

        public string TypeKey { get; set; }

        public string DisplayName { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public Dictionary<string, object> Inputs { get; set; }

        public List<string> DynamicInputPortIds { get; set; }
    }

    public class FlowValidationResult
    {
        public FlowValidationResult()
        {
            Errors = new List<FlowValidationError>();
        }

        public List<FlowValidationError> Errors { get; }

        public bool IsValid => Errors.Count == 0;
    }

    public class FlowValidationError
    {
        public string Code { get; set; }

        public string Message { get; set; }

        public string NodeId { get; set; }

        public string PortId { get; set; }

        public string ConnectionId { get; set; }
    }
}
