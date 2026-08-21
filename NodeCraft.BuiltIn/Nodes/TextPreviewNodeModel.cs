using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    PortId = BuiltInPortIds.Input,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
