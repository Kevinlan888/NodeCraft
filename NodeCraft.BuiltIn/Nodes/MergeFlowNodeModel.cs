using System.Collections.Generic;
using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class MergeFlowNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = MergeFlowExecutor.FlowNodeTypeKey;

        public MergeFlowNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Merge Flow";
            InputParameters = new List<PortParameter>();
            OutputParameters = new List<PortParameter>
            {
                new PortParameter
                {
                    PortId = BuiltInPortIds.FlowOut,
                    Parameter = new Parameter { ParameterType = FlowDataType.Control.Key },
                    PortDirection = EPortDirection.None,
                },
            };
        }
    }
}
