namespace NodeCraft.Flow.Nodes
{
    public class DivideNumberNodeModel : AddNumberNodeModel
    {
        public new const string FlowNodeTypeKey = DivideNumberExecutor.FlowNodeTypeKey;

        public DivideNumberNodeModel()
        {
            ExecutorType = FlowNodeTypeKey;
            Name = "Divide";
            InputParameters = CreateBinaryNumberInputs();
            OutputParameters = CreateNumberOutput();
        }
    }
}