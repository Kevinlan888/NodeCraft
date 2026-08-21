namespace NodeCraft.BuiltIn.Nodes
{
    public class BooleanOrNodeModel : BooleanAndNodeModel
    {
        public new const string FlowNodeTypeKey = BooleanOrExecutor.FlowNodeTypeKey;

        public BooleanOrNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Boolean Or";
            InputParameters = BooleanNodePorts.CreateBinaryBooleanInputs();
            OutputParameters = BooleanNodePorts.CreateBooleanOutput();
        }
    }
}
