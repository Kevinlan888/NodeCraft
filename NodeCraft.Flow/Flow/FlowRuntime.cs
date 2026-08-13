using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Flow
{
    public interface IFlowNodeExecutor
    {
        Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken);
    }

    public enum FlowNodeExecutionStatus
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        Skipped,
    }

    public enum FlowControlSignal
    {
        Active,
    }

    public class FlowExecutionContext
    {
        private readonly Dictionary<Tuple<string, int>, object> _values = new Dictionary<Tuple<string, int>, object>();
        private readonly Dictionary<string, FlowNodeExecutionStatus> _statuses = new Dictionary<string, FlowNodeExecutionStatus>();
        private readonly Dictionary<string, Exception> _errors = new Dictionary<string, Exception>();

        public void SetPortValue(string nodeId, int slot, object value)
        {
            _values[Tuple.Create(nodeId, slot)] = value;
        }

        public bool TryGetPortValue(string nodeId, int slot, out object value)
        {
            return _values.TryGetValue(Tuple.Create(nodeId, slot), out value);
        }

        public IReadOnlyDictionary<Tuple<string, int>, object> Values => _values;

        public IReadOnlyDictionary<string, FlowNodeExecutionStatus> Statuses => _statuses;

        public IReadOnlyDictionary<string, Exception> Errors => _errors;

        public void MarkRunning(string nodeId)
        {
            _statuses[nodeId] = FlowNodeExecutionStatus.Running;
        }

        public void MarkSucceeded(string nodeId)
        {
            _statuses[nodeId] = FlowNodeExecutionStatus.Succeeded;
            _errors.Remove(nodeId);
        }

        public void MarkFailed(string nodeId, Exception exception)
        {
            _statuses[nodeId] = FlowNodeExecutionStatus.Failed;
            _errors[nodeId] = exception;
        }

        public void MarkSkipped(string nodeId)
        {
            _statuses[nodeId] = FlowNodeExecutionStatus.Skipped;
        }
    }
}
