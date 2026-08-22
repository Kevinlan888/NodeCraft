using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class NotEqualNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = NotEqualExecutor.FlowNodeTypeKey;

        public NotEqualNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "!=";
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
