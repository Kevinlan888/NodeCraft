using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    public sealed class VirtualCameraNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = "nodecraft.vision.virtual-camera";
        internal const double DefaultFrameRate = 18.0;
        internal const double MinimumFrameRate = 0.1;
        internal const double MaximumFrameRate = 1000.0;

        public VirtualCameraNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Virtual Camera";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                CreateOutput("image", FlowDataType.Image.Key),
                CreateOutput("imagePath", FlowDataType.String.Key),
                CreateOutput("imageDirectory", FlowDataType.String.Key),
            };
        }

        public string SourcePath { get; set; } = "builtin://vision/sample-set";

        public VirtualCameraLoadMode LoadMode { get; set; } = VirtualCameraLoadMode.Preload;

        public double FrameRate { get; set; } = DefaultFrameRate;

        public int MaxPreloadedImages { get; set; } = 100;

        public long MaxPreloadedBytes { get; set; } = 536870912L;

        public bool SkipErrorImages { get; set; }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            node.Inputs["sourcePath"] = SourcePath ?? string.Empty;
            node.Inputs["loadMode"] = LoadMode;
            node.Inputs["frameRate"] = FrameRate;
            node.Inputs["maxPreloadedImages"] = MaxPreloadedImages;
            node.Inputs["maxPreloadedBytes"] = MaxPreloadedBytes;
            node.Inputs["skipErrorImages"] = SkipErrorImages;
        }

        internal static bool IsValidFrameRate(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= MinimumFrameRate
                && value <= MaximumFrameRate;
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
