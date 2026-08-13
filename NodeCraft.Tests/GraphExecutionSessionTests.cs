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
}
