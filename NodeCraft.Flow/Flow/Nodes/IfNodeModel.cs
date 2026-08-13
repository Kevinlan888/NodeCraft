using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class IfNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = IfExecutor.FlowNodeTypeKey;

        public IfNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "If";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = FlowPorts.Condition,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.Left,
                }
            };
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = FlowPorts.True,
                    Parameter = new Parameter { ParameterType = FlowDataType.Control.Key },
                    PortDirection = EPortDirection.Right,
                },
                new PortParameter
                {
                    PortId = FlowPorts.False,
                    Parameter = new Parameter { ParameterType = FlowDataType.Control.Key },
                    PortDirection = EPortDirection.Right,
                }
            };
        }
    }
}
