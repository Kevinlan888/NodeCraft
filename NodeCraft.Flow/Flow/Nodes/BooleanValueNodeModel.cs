using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class BooleanValueNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = BooleanValueExecutor.FlowNodeTypeKey;

        public bool BooleanValue { get; set; } = true;

        public BooleanValueNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Boolean Value";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInNodePorts.Value] = BooleanValue;
        }
    }
}