using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInPortIds.Value] = BooleanValue;
        }
    }
}
