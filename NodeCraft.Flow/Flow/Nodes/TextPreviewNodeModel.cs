using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class TextPreviewNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = TextPreviewExecutor.FlowNodeTypeKey;

        public string LastPreviewText { get; set; } = string.Empty;

        public TextPreviewNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Text Preview";

            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Input,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                }
            };

            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }
    }
}