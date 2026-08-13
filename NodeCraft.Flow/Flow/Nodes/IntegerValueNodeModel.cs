using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class IntegerValueNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = IntegerValueExecutor.FlowNodeTypeKey;

        public int IntegerValue { get; set; } = 42;

        public IntegerValueNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Integer Value";
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
            node.Inputs[BuiltInNodePorts.Value] = IntegerValue;
        }
    }
}