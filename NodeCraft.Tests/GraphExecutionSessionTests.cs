using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunGraphExecutionSessionLifecycleTestsAsync()
    {
        await RunAsync("graph session starts topologically and stops in reverse", async () =>
        {
            var calls = new List<string>();
            var fixture = CreateLifecycleFixture(calls);
            await using var session = fixture.Executor.CreateSession();

            await session.StartAsync(CancellationToken.None);
            await session.StopAsync();
            await session.StopAsync();

            return calls.SequenceEqual(new[]
            {
                "create:a",
                "create:b",
                "start:a",
                "start:b",
                "stop:b",
                "stop:a",
            })
                && session.State == GraphExecutionSessionState.Stopped;
        });

        await RunAsync("graph session cleans already started nodes after start failure", async () =>
        {
            var calls = new List<string>();
            var fixture = CreateLifecycleFixture(calls, failingTypeKey: "test.session.b");
            await using var session = fixture.Executor.CreateSession();

            try
            {
                await session.StartAsync(CancellationToken.None);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                await session.StopAsync();
                return ex.Message == "start failed: b"
                    && calls.SequenceEqual(new[]
                    {
                        "create:a",
                        "create:b",
                        "start:a",
                        "start:b",
                        "stop:a",
                    });
            }
        });

        await RunAsync("graph session rejects duplicate workflow node ids before creating executors", async () =>
        {
            var registry = new FlowNodeRegistry();
            var createCount = 0;
            registry.Register(new FlowNodeRegistration(
                CreateDefinition("test.session.duplicate"),
                () =>
                {
                    createCount++;
                    return new RecordingSessionExecutor(new List<string>(), "node");
                }));

            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode { Id = "duplicate", TypeKey = "test.session.duplicate" });
            workflow.Nodes.Add(new WorkflowNode { Id = "duplicate", TypeKey = "test.session.duplicate" });

            try
            {
                new GraphExecutor(workflow, registry).CreateSession();
                return false;
            }
            catch (Exception ex)
            {
                await Task.CompletedTask;
                return ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                    && createCount == 0;
            }
        });
    }

    private static async Task RunGraphExecutionSessionIterationTestsAsync()
    {
        await RunAsync("graph session prepares a fresh serial iteration context", async () =>
        {
            var fixture = CreateIterationFixture();
            await using var session = fixture.Executor.CreateSession();
            await session.StartAsync(CancellationToken.None);

            var first = await session.ExecuteIterationAsync(CancellationToken.None);
            var second = await session.ExecuteIterationAsync(CancellationToken.None);

            return !ReferenceEquals(first, second)
                && fixture.ExecutorInstance.PrepareCount == 2
                && fixture.ExecutorInstance.ExecuteCount == 2
                && first.TryGetPortValue("source", 0, out var firstValue)
                && second.TryGetPortValue("source", 0, out var secondValue)
                && (int)firstValue == 1
                && (int)secondValue == 2;
        });

        await RunAsync("graph session never overlaps concurrent iterations", async () =>
        {
            var fixture = CreateIterationFixture(blockExecution: true);
            await using var session = fixture.Executor.CreateSession();
            await session.StartAsync(CancellationToken.None);

            var firstTask = session.ExecuteIterationAsync(CancellationToken.None);
            await fixture.ExecutorInstance.FirstExecutionEntered.Task;
            var secondTask = session.ExecuteIterationAsync(CancellationToken.None);
            var raced = await Task.WhenAny(
                fixture.ExecutorInstance.SecondExecutionEntered.Task,
                Task.Delay(TimeSpan.FromMilliseconds(50))) == fixture.ExecutorInstance.SecondExecutionEntered.Task;

            fixture.ExecutorInstance.ReleaseExecution();
            await Task.WhenAll(firstTask, secondTask);

            return !raced
                && fixture.ExecutorInstance.MaxConcurrentExecutions == 1;
        });

        await RunAsync("legacy graph execution creates one session iteration and cleans it", async () =>
        {
            var fixture = CreateIterationFixture();
            var context = await fixture.Executor.ExecuteAsync(CancellationToken.None);

            return fixture.ExecutorInstance.StartCount == 1
                && fixture.ExecutorInstance.StopCount == 1
                && fixture.ExecutorInstance.PrepareCount == 1
                && fixture.ExecutorInstance.ExecuteCount == 1
                && context.TryGetPortValue("source", 0, out var value)
                && (int)value == 1;
        });

        await RunAsync("graph session faults after a downstream iteration exception", async () =>
        {
            var fixture = CreateIterationFixture(failExecution: true);
            await using var session = fixture.Executor.CreateSession();
            await session.StartAsync(CancellationToken.None);

            try
            {
                await session.ExecuteIterationAsync(CancellationToken.None);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message == "iteration failed"
                    && session.State == GraphExecutionSessionState.Faulted;
            }
        });

        await RunAsync("graph session stop cancels a blocked iteration source before cleanup", async () =>
        {
            var fixture = CreateBlockedSourceFixture();
            await using var session = fixture.Executor.CreateSession();
            await session.StartAsync(CancellationToken.None);
            var iterationTask = session.ExecuteIterationAsync(CancellationToken.None);
            await fixture.ExecutorInstance.PrepareEntered.Task;

            var stopTask = session.StopAsync();
            var stopCompletedBeforeRelease = await Task.WhenAny(
                stopTask,
                Task.Delay(TimeSpan.FromMilliseconds(100))) == stopTask;
            fixture.ExecutorInstance.ReleasePrepare();
            await stopTask;

            try
            {
                await iterationTask;
            }
            catch (OperationCanceledException)
            {
            }

            return !stopCompletedBeforeRelease
                && fixture.ExecutorInstance.StopCount == 1
                && session.State == GraphExecutionSessionState.Stopped;
        });
    }

    private static IterationFixture CreateIterationFixture(
        bool blockExecution = false,
        bool failExecution = false)
    {
        var registry = new FlowNodeRegistry();
        var executor = new IterationTestExecutor(blockExecution, failExecution);
        registry.Register(new FlowNodeRegistration(
            CreateDefinition("test.iteration.source"),
            () => executor));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "source",
            TypeKey = "test.iteration.source",
            DisplayName = "Source",
        });

        return new IterationFixture(new GraphExecutor(workflow, registry), executor);
    }

    private static BlockedSourceFixture CreateBlockedSourceFixture()
    {
        var registry = new FlowNodeRegistry();
        var executor = new BlockedSourceExecutor();
        registry.Register(new FlowNodeRegistration(
            CreateDefinition("test.iteration.blocked"),
            () => executor));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "blocked",
            TypeKey = "test.iteration.blocked",
        });
        return new BlockedSourceFixture(new GraphExecutor(workflow, registry), executor);
    }

    private static LifecycleFixture CreateLifecycleFixture(
        List<string> calls,
        string failingTypeKey = null)
    {
        var registry = new FlowNodeRegistry();
        registry.Register(new FlowNodeRegistration(
            CreateDefinition("test.session.a"),
            () =>
            {
                calls.Add("create:a");
                return new RecordingSessionExecutor(calls, "a");
            }));
        registry.Register(new FlowNodeRegistration(
            CreateDefinition("test.session.b"),
            () =>
            {
                calls.Add("create:b");
                return new RecordingSessionExecutor(calls, "b", failingTypeKey == "test.session.b");
            }));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "a",
            TypeKey = "test.session.a",
            DisplayName = "A",
        });
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "b",
            TypeKey = "test.session.b",
            DisplayName = "B",
            Inputs =
            {
                ["input"] = new LinkRef { SourceNodeId = "a", SourceSlot = 0 },
            },
        });

        return new LifecycleFixture(new GraphExecutor(workflow, registry));
    }

    private static FlowNodeDefinition CreateDefinition(string typeKey)
    {
        var definition = new FlowNodeDefinition
        {
            TypeKey = typeKey,
            DisplayName = typeKey,
            Category = "Tests",
        };
        definition.OutputPorts.Add(new FlowPortDefinition
        {
            Id = "output",
            DisplayName = "Output",
            IOType = EIOType.Output,
            DataType = FlowDataType.String,
            PreferredDirection = EPortDirection.Right,
        });

        if (typeKey.EndsWith(".b", StringComparison.Ordinal))
        {
            definition.InputPorts.Add(new FlowPortDefinition
            {
                Id = "input",
                DisplayName = "Input",
                IOType = EIOType.Input,
                DataType = FlowDataType.String,
                IsRequired = true,
                PreferredDirection = EPortDirection.Left,
            });
        }

        return definition;
    }

    private sealed class LifecycleFixture
    {
        public LifecycleFixture(GraphExecutor executor)
        {
            Executor = executor;
        }

        public GraphExecutor Executor { get; }
    }

    private sealed class IterationFixture
    {
        public IterationFixture(GraphExecutor executor, IterationTestExecutor executorInstance)
        {
            Executor = executor;
            ExecutorInstance = executorInstance;
        }

        public GraphExecutor Executor { get; }

        public IterationTestExecutor ExecutorInstance { get; }
    }

    private sealed class BlockedSourceFixture
    {
        public BlockedSourceFixture(GraphExecutor executor, BlockedSourceExecutor executorInstance)
        {
            Executor = executor;
            ExecutorInstance = executorInstance;
        }

        public GraphExecutor Executor { get; }

        public BlockedSourceExecutor ExecutorInstance { get; }
    }

    private sealed class RecordingSessionExecutor : IFlowNodeExecutor, IFlowNodeSessionLifecycle
    {
        private readonly IList<string> _calls;
        private readonly string _name;
        private readonly bool _failStart;

        public RecordingSessionExecutor(IList<string> calls, string name, bool failStart = false)
        {
            _calls = calls;
            _name = name;
            _failStart = failStart;
        }

        public Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            _calls.Add("start:" + _name);
            if (_failStart)
            {
                throw new InvalidOperationException("start failed: " + _name);
            }

            return Task.CompletedTask;
        }

        public Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            _calls.Add("stop:" + _name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object> { ["output"] = _name });
        }
    }

    private sealed class IterationTestExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowIterationSource
    {
        private readonly bool _blockExecution;
        private readonly TaskCompletionSource<bool> _releaseExecution
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeExecutions;

        public IterationTestExecutor(bool blockExecution, bool failExecution)
        {
            _blockExecution = blockExecution;
            _failExecution = failExecution;
        }

        private readonly bool _failExecution;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public int MaxConcurrentExecutions { get; private set; }

        public TaskCompletionSource<bool> FirstExecutionEntered { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SecondExecutionEntered { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCount++;
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = Interlocked.Increment(ref _activeExecutions);
            MaxConcurrentExecutions = Math.Max(MaxConcurrentExecutions, active);
            var count = ++ExecuteCount;
            if (count == 1)
            {
                FirstExecutionEntered.TrySetResult(true);
            }
            else
            {
                SecondExecutionEntered.TrySetResult(true);
            }

            try
            {
                if (_failExecution)
                {
                    throw new InvalidOperationException("iteration failed");
                }

                if (_blockExecution)
                {
                    await _releaseExecution.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return new Dictionary<string, object> { ["output"] = count };
            }
            finally
            {
                Interlocked.Decrement(ref _activeExecutions);
            }
        }

        public void ReleaseExecution()
        {
            _releaseExecution.TrySetResult(true);
        }
    }

    private sealed class BlockedSourceExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowIterationSource
    {
        private readonly TaskCompletionSource<bool> _releasePrepare
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> PrepareEntered { get; }
            = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCount { get; private set; }

        public Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public async Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            PrepareEntered.TrySetResult(true);
            await _releasePrepare.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object> { ["output"] = 1 });
        }

        public void ReleasePrepare()
        {
            _releasePrepare.TrySetResult(true);
        }
    }
}
