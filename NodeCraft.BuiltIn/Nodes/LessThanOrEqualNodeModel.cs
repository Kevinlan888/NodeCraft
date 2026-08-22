using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class LessThanOrEqualNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = LessThanOrEqualExecutor.FlowNodeTypeKey;

        public LessThanOrEqualNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "<=";
            InputParameters = AddNumberNodeModel.CreateBinaryNumberInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
