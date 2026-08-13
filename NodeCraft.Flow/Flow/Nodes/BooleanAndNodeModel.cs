using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class BooleanAndNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = BooleanAndExecutor.FlowNodeTypeKey;

        public BooleanAndNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Boolean And";
            InputParameters = BooleanNodePorts.CreateBinaryBooleanInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}