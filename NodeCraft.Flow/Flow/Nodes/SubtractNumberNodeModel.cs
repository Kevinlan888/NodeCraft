namespace NodeCraft.Flow.Nodes
{
    public class SubtractNumberNodeModel : AddNumberNodeModel
    {
        public new const string FlowNodeTypeKey = SubtractNumberExecutor.FlowNodeTypeKey;

        public SubtractNumberNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Subtract";
            InputParameters = CreateBinaryNumberInputs();
            OutputParameters = CreateNumberOutput();
        }
    }
}