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
        private readonly IReadOnlyDictionary<string, FlowNodeDefinition> _definitionsByNodeId;
        private readonly SessionValueStore _sessionValueStore = new SessionValueStore();
        private readonly IReadOnlySessionValueStore _readOnlySessionValues;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stopCancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim _iterationGate = new SemaphoreSlim(1, 1);
        private readonly List<StartedLifecycle> _startedLifecycles = new List<StartedLifecycle>();
        private readonly List<Exception> _startupCleanupErrors = new List<Exception>();
        private GraphExecutionSessionState _state = GraphExecutionSessionState.Created;
        private CancellationTokenSource _startupCancellation;
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

            _definitionsByNodeId = _sessionContexts.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Definition,
                StringComparer.Ordinal);
            _readOnlySessionValues = _sessionValueStore.CreateReadOnlyView();
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
                        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            _stopCancellation.Token);
                        _startTask = StartCoreAsync(_startupCancellation.Token, _startupCancellation);
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
                _stopTask = StopCoreAsync(_startTask);
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

        private async Task StartCoreAsync(
            CancellationToken cancellationToken,
            CancellationTokenSource startupCancellation)
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

                    var context = _sessionContexts[node.Id];
                    await lifecycle.StartSessionAsync(context, cancellationToken)
                        .ConfigureAwait(false);

                    var cleanupStartedLifecycle = false;
                    lock (_stateGate)
                    {
                        if (_state == GraphExecutionSessionState.Starting
                            && !cancellationToken.IsCancellationRequested)
                        {
                            _startedLifecycles.Add(new StartedLifecycle(lifecycle, context));
                        }
                        else
                        {
                            cleanupStartedLifecycle = true;
                        }
                    }

                    if (cleanupStartedLifecycle)
                    {
                        try
                        {
                            await lifecycle.StopSessionAsync(context, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            lock (_stateGate)
                            {
                                _startupCleanupErrors.Add(exception);
                            }

                            _logger.LogError(
                                exception,
                                "Graph session cleanup failed for node '{NodeId}'.",
                                context.Node.Id);
                            throw;
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        throw new OperationCanceledException(cancellationToken);
                    }
                }

                lock (_stateGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_state != GraphExecutionSessionState.Starting)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    _state = GraphExecutionSessionState.Running;
                }
            }
            catch (Exception)
            {
                var stopInProgress = false;
                lock (_stateGate)
                {
                    stopInProgress = _state == GraphExecutionSessionState.Stopping
                        || _state == GraphExecutionSessionState.Stopped;
                    if (!stopInProgress)
                    {
                        _state = GraphExecutionSessionState.Faulted;
                    }
                }

                if (!stopInProgress)
                {
                    try
                    {
                        await StopStartedLifecyclesCoreAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogError(cleanupException, "Graph session cleanup failed after start failure.");
                    }
                }

                throw;
            }
            finally
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_startupCancellation, startupCancellation))
                    {
                        _startupCancellation = null;
                    }
                }

                startupCancellation.Dispose();
            }
        }

        private async Task StopCoreAsync(Task startTask)
        {
            if (startTask != null)
            {
                try
                {
                    await startTask.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Graph session startup ended before stop cleanup.");
                }
            }

            await StopStartedLifecyclesCoreAsync().ConfigureAwait(false);
        }

        private async Task StopStartedLifecyclesCoreAsync()
        {
            await _iterationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            List<StartedLifecycle> started;
            List<Exception> cleanupErrors;
            try
            {
                lock (_stateGate)
                {
                    started = _startedLifecycles.AsEnumerable().Reverse().ToList();
                    _startedLifecycles.Clear();
                    cleanupErrors = _startupCleanupErrors.ToList();
                    _startupCleanupErrors.Clear();
                }

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
                        _definitionsByNodeId,
                        context,
                        _readOnlySessionValues,
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
