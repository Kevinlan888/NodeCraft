using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class StringConcatNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = StringConcatExecutor.FlowNodeTypeKey;

        public StringConcatNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "String Concat";
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

        public string Separator { get; set; } = string.Empty;

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs[BuiltInPortIds.Separator] = Separator ?? string.Empty;
        }
    }
}
