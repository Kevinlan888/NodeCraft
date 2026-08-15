using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunSessionNodeInitializationTestsAsync()
    {
        await RunAsync("session value store is write-once and read-only after sealing", async () =>
        {
            var store = new SessionValueStore();
            var view = store.CreateReadOnlyView();
            var value = new object();

            store.SetPortValue("camera", 0, value);
            var firstRead = view.TryGetPortValue("camera", 0, out var first)
                && ReferenceEquals(first, value);
            var duplicateRejected = Throws<InvalidOperationException>(
                () => store.SetPortValue("camera", 0, new object()));

            store.Seal();
            var sealedRejected = Throws<InvalidOperationException>(
                () => store.SetPortValue("camera", 1, new object()));
            store.Clear();

            await Task.CompletedTask;
            return firstRead
                && duplicateRejected
                && sealedRejected
                && !view.TryGetPortValue("camera", 0, out _);
        });

        await RunAsync("session link to iteration-only output is rejected", async () =>
        {
            var source = CreateDefinition("test.stage.source");
            source.OutputPorts[0].Availability = FlowPortAvailability.Iteration;
            var target = CreateDefinition("test.stage.target");
            target.InputPorts.Add(new FlowPortDefinition
            {
                Id = "calibration",
                IOType = EIOType.Input,
                DataType = FlowDataType.String,
                IsRequired = true,
                Availability = FlowPortAvailability.Session,
            });

            var workflow = new WorkflowDocument();
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "source",
                TypeKey = source.TypeKey,
            });
            workflow.Nodes.Add(new WorkflowNode
            {
                Id = "target",
                TypeKey = target.TypeKey,
                Inputs =
                {
                    ["calibration"] = new LinkRef { SourceNodeId = "source", SourceSlot = 0 },
                },
            });

            var registry = new FlowNodeRegistry();
            registry.Register(new FlowNodeRegistration(source, () => new ValidationTestExecutor()));
            registry.Register(new FlowNodeRegistration(target, () => new ValidationTestExecutor()));
            var validation = new GraphExecutor(workflow, registry).Validate();

            await Task.CompletedTask;
            return validation.Errors.Any(error =>
                error.Code == "SessionInputUnavailable"
                && error.NodeId == "target"
                && error.PortId == "calibration");
        });

        await RunAsync("runtime output validation rejects unknown and wrong-stage outputs", async () =>
        {
            var definition = CreateDefinition("test.stage.outputs");
            definition.OutputPorts[0].Availability = FlowPortAvailability.Session;
            var node = new WorkflowNode { Id = "node", TypeKey = definition.TypeKey };

            var unknown = Throws<InvalidOperationException>(() =>
                FlowRuntimeValueValidator.ValidateSessionOutputs(
                    node,
                    definition,
                    new Dictionary<string, object> { ["missing"] = "value" }));
            var wrongStage = Throws<InvalidOperationException>(() =>
                FlowRuntimeValueValidator.ValidateIterationOutputs(
                    node,
                    definition,
                    new Dictionary<string, object> { ["output"] = "value" }));

            await Task.CompletedTask;
            return unknown && wrongStage;
        });

        await RunAsync("linked iteration input does not fall back to a port default", async () =>
        {
            var fixture = CreateMissingIterationSourceFixture();
            await using var session = fixture.Executor.CreateSession();
            await session.StartAsync(CancellationToken.None);
            var context = await session.ExecuteIterationAsync(CancellationToken.None);

            return context.Statuses["target"] == FlowNodeExecutionStatus.Skipped
                && fixture.Target.ExecuteCount == 0;
        });
    }

    private static MissingIterationSourceFixture CreateMissingIterationSourceFixture()
    {
        var source = CreateDefinition("test.stage.iteration-source");
        source.OutputPorts[0].DataType = FlowDataType.Number;
        source.OutputPorts[0].Availability = FlowPortAvailability.Iteration;

        var target = CreateDefinition("test.stage.iteration-target");
        target.InputPorts.Add(new FlowPortDefinition
        {
            Id = "input",
            IOType = EIOType.Input,
            DataType = FlowDataType.Number,
            IsRequired = true,
            DefaultValue = 99d,
            Availability = FlowPortAvailability.Iteration,
        });

        var sourceExecutor = new EmptyOutputExecutor();
        var targetExecutor = new CountingExecutor();
        var registry = new FlowNodeRegistry();
        registry.Register(new FlowNodeRegistration(source, () => sourceExecutor));
        registry.Register(new FlowNodeRegistration(target, () => targetExecutor));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "source",
            TypeKey = source.TypeKey,
        });
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "target",
            TypeKey = target.TypeKey,
            Inputs =
            {
                ["input"] = new LinkRef { SourceNodeId = "source", SourceSlot = 0 },
            },
        });

        return new MissingIterationSourceFixture(
            new GraphExecutor(workflow, registry),
            targetExecutor);
    }

    private sealed class MissingIterationSourceFixture
    {
        public MissingIterationSourceFixture(GraphExecutor executor, CountingExecutor target)
        {
            Executor = executor;
            Target = target;
        }

        public GraphExecutor Executor { get; }

        public CountingExecutor Target { get; }
    }

    private sealed class ValidationTestExecutor : IFlowNodeExecutor
    {
        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }
    }

    private sealed class EmptyOutputExecutor : IFlowNodeExecutor
    {
        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }
    }

    private sealed class CountingExecutor : IFlowNodeExecutor
    {
        public int ExecuteCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }
    }
}
