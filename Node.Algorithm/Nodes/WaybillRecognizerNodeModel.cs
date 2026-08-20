using System.Collections.Generic;
using NodeCraft.Flow;

namespace Node.Algorithm.Nodes
{
    public sealed class WaybillRecognizerNodeModel : NodeModel
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
