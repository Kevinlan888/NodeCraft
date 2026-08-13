using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
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
                    PortId = BuiltInNodePorts.Input,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                }
            };
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}