using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NodeCraft.Flow
{
    public sealed class GraphExecutionSession : IAsyncDisposable
    {
        private readonly object _stateGate = new object();
        private readonly IReadOnlyList<WorkflowNode> _orderedNodes;
        private readonly Dictionary<string, IFlowNodeExecutor> _executors;
        private readonly Dictionary<string, FlowNodeSessionContext> _sessionContexts;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stopCancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim _iterationGate = new SemaphoreSlim(1, 1);
        private readonly List<StartedLifecycle> _startedLifecycles = new List<StartedLifecycle>();
        private GraphExecutionSessionState _state = GraphExecutionSessionState.Created;
        private Task _startTask;
        private Task _stopTask;

        internal GraphExecutionSession(
            WorkflowDocument workflow,
            FlowNodeRegistry registry,
            IReadOnlyList<WorkflowNode> orderedNodes,
            ILogger logger)
        {
            Workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _orderedNodes = orderedNodes ?? throw new ArgumentNullException(nameof(orderedNodes));
            _logger = logger ?? NullLogger.Instance;
            _executors = new Dictionary<string, IFlowNodeExecutor>(StringComparer.Ordinal);
            _sessionContexts = new Dictionary<string, FlowNodeSessionContext>(StringComparer.Ordinal);

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in _orderedNodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id))
                {
                    throw new InvalidOperationException("Workflow nodes must have non-empty ids.");
                }

                if (!nodeIds.Add(node.Id))
                {
                    throw new InvalidOperationException($"Workflow contains duplicate node Id '{node.Id}'.");
                }

                var registration = Registry.Resolve(node.TypeKey);
                var executor = registration.ExecutorFactory();
                if (executor == null)
                {
                    throw new InvalidOperationException(
                        $"Executor factory for node '{node.Id}' returned null.");
                }

                _executors.Add(node.Id, executor);
                _sessionContexts.Add(
                    node.Id,
                    new FlowNodeSessionContext(node, registration.Definition, _logger));
            }
        }

        public WorkflowDocument Workflow { get; }

        public FlowNodeRegistry Registry { get; }

        public GraphExecutionSessionState State
        {
            get
            {
                lock (_stateGate)
                {
                    return _state;
                }
            }
        }

        public bool HasIterationSources => _executors.Values.Any(executor => executor is IFlowIterationSource);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_stateGate)
            {
                switch (_state)
                {
                    case GraphExecutionSessionState.Created:
                        _state = GraphExecutionSessionState.Starting;
                        _startTask = StartCoreAsync(cancellationToken);
                        return _startTask;
                    case GraphExecutionSessionState.Starting:
                        return _startTask ?? Task.CompletedTask;
                    case GraphExecutionSessionState.Running:
                        return Task.CompletedTask;
                    default:
                        throw new InvalidOperationException(
                            $"Graph execution session cannot start from state '{_state}'.");
                }
            }
        }

        public Task StopAsync()
        {
            lock (_stateGate)
            {
                if (_stopTask != null)
                {
                    return _stopTask;
                }

                if (_state == GraphExecutionSessionState.Stopped)
                {
                    _stopTask = Task.CompletedTask;
                    return _stopTask;
                }

                _state = GraphExecutionSessionState.Stopping;
                _stopCancellation.Cancel();
                _stopTask = StopCoreAsync();
                return _stopTask;
            }
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(DisposeCoreAsync());
        }

        public Task<FlowExecutionContext> ExecuteIterationAsync(CancellationToken cancellationToken)
        {
            return ExecuteIterationCoreAsync(cancellationToken);
        }

        internal IReadOnlyList<WorkflowNode> OrderedNodes => _orderedNodes;

        internal IReadOnlyDictionary<string, IFlowNodeExecutor> Executors => _executors;

        internal IReadOnlyDictionary<string, FlowNodeSessionContext> SessionContexts => _sessionContexts;

        internal CancellationToken StopToken => _stopCancellation.Token;

        private async Task StartCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                foreach (var node in _orderedNodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var executor = _executors[node.Id];
                    if (!(executor is IFlowNodeSessionLifecycle lifecycle))
                    {
                        continue;
                    }

                    await lifecycle.StartSessionAsync(_sessionContexts[node.Id], cancellationToken)
                        .ConfigureAwait(false);
                    lock (_stateGate)
                    {
                        _startedLifecycles.Add(new StartedLifecycle(lifecycle, _sessionContexts[node.Id]));
                    }
                }

                lock (_stateGate)
                {
                    _state = GraphExecutionSessionState.Running;
                }
            }
            catch (Exception)
            {
                lock (_stateGate)
                {
                    _state = GraphExecutionSessionState.Faulted;
                }

                try
                {
                    await StopCoreAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "Graph session cleanup failed after start failure.");
                }

                throw;
            }
        }

        private async Task StopCoreAsync()
        {
            await _iterationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            List<StartedLifecycle> started;
            try
            {
                lock (_stateGate)
                {
                    started = _startedLifecycles.AsEnumerable().Reverse().ToList();
                    _startedLifecycles.Clear();
                }

                var cleanupErrors = new List<Exception>();
                foreach (var item in started)
                {
                    try
                    {
                        await item.Lifecycle.StopSessionAsync(item.Context, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupErrors.Add(exception);
                        _logger.LogError(
                            exception,
                            "Graph session cleanup failed for node '{NodeId}'.",
                            item.Context.Node.Id);
                    }
                }

                lock (_stateGate)
                {
                    _state = GraphExecutionSessionState.Stopped;
                }

                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException("One or more graph session cleanup operations failed.", cleanupErrors);
                }
            }
            finally
            {
                _iterationGate.Release();
            }
        }

        private async Task<FlowExecutionContext> ExecuteIterationCoreAsync(CancellationToken cancellationToken)
        {
            lock (_stateGate)
            {
                if (_state != GraphExecutionSessionState.Running)
                {
                    throw new InvalidOperationException(
                        $"Graph execution session cannot execute an iteration from state '{_state}'.");
                }
            }

            await _iterationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopCancellation.Token);
            try
            {
                lock (_stateGate)
                {
                    if (_state != GraphExecutionSessionState.Running)
                    {
                        throw new InvalidOperationException(
                            $"Graph execution session cannot execute an iteration from state '{_state}'.");
                    }
                }

                foreach (var node in _orderedNodes)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    if (_executors[node.Id] is IFlowIterationSource source)
                    {
                        await source.PrepareIterationAsync(
                                _sessionContexts[node.Id],
                                linkedCancellation.Token)
                            .ConfigureAwait(false);
                    }
                }

                var context = new FlowExecutionContext();
                await FlowGraphIterationRunner.ExecuteAsync(
                        _orderedNodes,
                        _executors,
                        Registry,
                        context,
                        _logger,
                        linkedCancellation.Token)
                    .ConfigureAwait(false);
                return context;
            }
            catch (Exception exception)
            {
                var stopping = _stopCancellation.IsCancellationRequested;
                var callerCancelled = cancellationToken.IsCancellationRequested;
                if (!stopping && !(exception is OperationCanceledException && callerCancelled))
                {
                    lock (_stateGate)
                    {
                        if (_state == GraphExecutionSessionState.Running)
                        {
                            _state = GraphExecutionSessionState.Faulted;
                        }
                    }
                }

                throw;
            }
            finally
            {
                _iterationGate.Release();
            }
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            finally
            {
                _stopCancellation.Dispose();
                _iterationGate.Dispose();
            }
        }

        private sealed class StartedLifecycle
        {
            public StartedLifecycle(IFlowNodeSessionLifecycle lifecycle, FlowNodeSessionContext context)
            {
                Lifecycle = lifecycle;
                Context = context;
            }

            public IFlowNodeSessionLifecycle Lifecycle { get; }

            public FlowNodeSessionContext Context { get; }
        }
    }
}
