using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Number.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInPortIds.Value] = IntegerValue;
        }
    }
}
