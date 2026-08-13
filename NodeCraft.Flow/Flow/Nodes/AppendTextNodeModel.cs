using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class AppendTextNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = AppendTextExecutor.FlowNodeTypeKey;

        public string SuffixText { get; set; } = " from DemoApp";

        public AppendTextNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Append Text";

            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Input,
                    Parameter = new Parameter { ParameterType = "String" },
                    PortDirection = EPortDirection.None,
                }
            };

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
            node.Inputs[BuiltInNodePorts.Suffix] = SuffixText;
        }
    }
}