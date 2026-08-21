using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class StringValueNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = StringValueExecutor.FlowNodeTypeKey;

        public string ValueText { get; set; } = "ComfyUI";

        public StringValueNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "String Value";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.String.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInPortIds.Value] = ValueText;
        }
    }
}
