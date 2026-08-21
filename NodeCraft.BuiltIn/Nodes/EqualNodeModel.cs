using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
                    PortId = BuiltInPortIds.InputA,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInPortIds.InputB,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
