using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class ImagePreviewNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = ImagePreviewExecutor.FlowNodeTypeKey;

        public string LastImagePath { get; set; } = string.Empty;

        public string LastImageError { get; set; } = string.Empty;

        public ImagePreviewNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Image Preview";

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
    }
}