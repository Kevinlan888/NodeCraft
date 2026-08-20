using System.Collections.Generic;
using NodeCraft.Flow;

namespace Node.Algorithm.Nodes
{
    public sealed class WaybillRecognizerNodeModel : NodeModel, IWorkflowNodeValueProvider
    {
        public const string FlowNodeTypeKey = "nodecraft.algorithm.waybill-recognizer";

        public WaybillRecognizerNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Waybill Recognizer";
            InputParameters = new List<PortParameter>
            {
                CreatePort("image", FlowDataType.Image.Key),
            };
            OutputParameters = new List<PortParameter>
            {
                CreatePort("count", FlowDataType.Number.Key),
                CreatePort("detections", FlowDataType.Object.Key),
                CreatePort("annotatedImage", FlowDataType.Image.Key),
            };
        }

        public string ModelPath { get; set; } = "models/baseline-2-960.onnx";

        public float Confidence { get; set; } = 0.35f;

        public float Iou { get; set; } = 0.50f;

        public float MinMaskAreaRatio { get; set; } = 0.0001f;

        public int MaxDetections { get; set; } = 100;

        public int NumThreads { get; set; }

        public void WriteWorkflowInputs(WorkflowNode node)
        {
            if (node == null)
            {
                throw new System.ArgumentNullException(nameof(node));
            }

            node.Inputs["modelPath"] = ModelPath ?? string.Empty;
            node.Inputs["confidence"] = Confidence;
            node.Inputs["iou"] = Iou;
            node.Inputs["minMaskAreaRatio"] = MinMaskAreaRatio;
            node.Inputs["maxDetections"] = MaxDetections;
            node.Inputs["numThreads"] = NumThreads;
        }

        private static PortParameter CreatePort(string portId, string parameterType)
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
