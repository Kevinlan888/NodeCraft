using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class BooleanNotNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = BooleanNotExecutor.FlowNodeTypeKey;

        public BooleanNotNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Boolean Not";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Input,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
