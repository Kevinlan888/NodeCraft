namespace NodeCraft.Flow
{
    public interface IWorkflowNodeValueProvider
    {
        void WriteWorkflowInputs(WorkflowNode node);
    }
}