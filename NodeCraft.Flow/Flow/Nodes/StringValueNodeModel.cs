using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
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
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = "String" },
                    PortDirection = EPortDirection.None,
                }
            };
        }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInNodePorts.Value] = ValueText;
        }
    }
}