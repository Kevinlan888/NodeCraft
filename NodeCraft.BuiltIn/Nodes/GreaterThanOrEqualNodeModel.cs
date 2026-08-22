using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
{
    public class GreaterThanOrEqualNodeModel : NodeModel
    {
        public const string FlowNodeTypeKey = GreaterThanOrEqualExecutor.FlowNodeTypeKey;

        public GreaterThanOrEqualNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = ">=";
            InputParameters = AddNumberNodeModel.CreateBinaryNumberInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
