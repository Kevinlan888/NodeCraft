namespace NodeCraft.Flow.Nodes
{
    public class MultiplyNumberNodeModel : AddNumberNodeModel
    {
        public new const string FlowNodeTypeKey = MultiplyNumberExecutor.FlowNodeTypeKey;

        public MultiplyNumberNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Multiply";
            InputParameters = CreateBinaryNumberInputs();
            OutputParameters = CreateNumberOutput();
        }
    }
}