using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class JsonSerializeNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = JsonSerializeExecutor.FlowNodeTypeKey;

        public JsonSerializeNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "JSON Serialize";

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
                    Parameter = new Parameter { ParameterType = FlowDataType.String.Key },
                    PortDirection = EPortDirection.None,
                }
            };
        }
    }
}
