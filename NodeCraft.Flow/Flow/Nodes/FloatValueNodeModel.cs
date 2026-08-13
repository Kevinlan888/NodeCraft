using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class FloatValueNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = FloatValueExecutor.FlowNodeTypeKey;

        public double FloatValue { get; set; } = 3.14;

        public FloatValueNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Float Value";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInNodePorts.Value] = FloatValue;
        }
    }
}