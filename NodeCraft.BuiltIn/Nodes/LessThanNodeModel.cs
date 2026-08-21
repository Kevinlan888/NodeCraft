using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
