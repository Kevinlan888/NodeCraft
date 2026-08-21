using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class GreaterThanNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = GreaterThanExecutor.FlowNodeTypeKey;

        public GreaterThanNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Greater Than";
            InputParameters = AddNumberNodeModel.CreateBinaryNumberInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
