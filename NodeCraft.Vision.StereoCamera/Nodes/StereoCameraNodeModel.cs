using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Nodes
{
    public sealed class StereoCameraNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = "nodecraft.vision.stereo-camera.camera";

        public StereoCameraNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Stereo Camera";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = "ipAddress",
                    Parameter = new Parameter { ParameterType = FlowDataType.String.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = new List<PortParameter>
            {
                CreateOutput("colorImage", FlowDataType.Image.Key),
                CreateOutput("depthImage", FlowDataType.Image.Key),
                CreateOutput("colorCalibration", FlowDataType.CameraCalibration.Key),
                CreateOutput("depthCalibration", FlowDataType.CameraCalibration.Key),
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
