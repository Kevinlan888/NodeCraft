using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    Parameter = new Parameter { ParameterType = FlowDataType.String.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
