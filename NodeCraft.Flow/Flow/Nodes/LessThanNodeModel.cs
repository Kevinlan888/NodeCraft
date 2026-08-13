using System.Collections.Generic;

namespace NodeCraft.Flow.Nodes
{
    public class LessThanNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = LessThanExecutor.FlowNodeTypeKey;

        public LessThanNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Less Than";
            InputParameters = AddNumberNodeModel.CreateBinaryNumberInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}