using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class EqualNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = EqualExecutor.FlowNodeTypeKey;

        public EqualNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Equal";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInNodePorts.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                }
            };
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}