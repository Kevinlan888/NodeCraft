using NodeCraft.Flow;

namespace NodeCraft.BuiltIn.Nodes
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
