using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    public sealed class VisionCameraNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = "nodecraft.vision.camera";

        public VisionCameraNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Vision Camera";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                CreateOutput("image", FlowDataType.Image.Key),
            };
        }

        public string IpAddress { get; set; } = string.Empty;

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs["ipAddress"] = IpAddress ?? string.Empty;
        }

        private static PortParameter CreateOutput(string portId, string parameterType)
        {
            return new PortParameter
            {
                PortId = portId,
                Parameter = new Parameter { ParameterType = parameterType },
                PortDirection = EPortDirection.None,
            };
        }
    }
}
