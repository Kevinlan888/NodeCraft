using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class SelectNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = SelectExecutor.FlowNodeTypeKey;

        public SelectNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Select";
            InputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Condition,
                    Parameter = new Parameter { ParameterType = FlowDataType.Boolean.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInPortIds.TrueValue,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
                new PortParameter
                {
                    PortId = BuiltInPortIds.FalseValue,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
            };
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.Output,
                    Parameter = new Parameter { ParameterType = FlowDataType.Object.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
