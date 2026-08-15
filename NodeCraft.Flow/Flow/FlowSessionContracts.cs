using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NodeCraft.Flow
{
    public sealed class FlowNodeSessionContext
    {
        internal FlowNodeSessionContext(
            WorkflowNode node,
            FlowNodeDefinition definition,
            ILogger logger)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public WorkflowNode Node { get; }

        public FlowNodeDefinition Definition { get; }

        public ILogger Logger { get; }
    }

    public interface IFlowNodeSessionLifecycle
    {
        Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);

        Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
    }

    public interface IFlowNodeSessionInitializer
    {
        Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
            FlowNodeSessionContext context,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken);
    }

    public interface IReadOnlySessionValueStore
    {
        bool TryGetPortValue(string nodeId, int outputSlot, out object value);
    }

    public interface IFlowIterationSource
    {
        Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken);
    }

    public enum GraphExecutionSessionState
    {
        Created,
        Starting,
        Running,
        Faulted,
        Stopping,
        Stopped,
    }
}
