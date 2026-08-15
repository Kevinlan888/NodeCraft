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

        await RunAsync("graph session initializes nodes topologically and reuses session inputs", async () =>
        {
            var fixture = CreateSessionFixture();
            await using var session = fixture.Executor.CreateSession();

            await session.StartAsync(CancellationToken.None);
            var first = await session.ExecuteIterationAsync(CancellationToken.None);
            var second = await session.ExecuteIterationAsync(CancellationToken.None);
            await session.StopAsync();

            return fixture.Algorithm.InitializeCount == 1
                && fixture.Algorithm.ExecuteCount == 2
                && fixture.Camera.PrepareCount == 2
                && fixture.Algorithm.PrepareCount == 2
                && fixture.Algorithm.InitializedCalibration != null
                && ReferenceEquals(
                    fixture.Camera.Calibration,
                    fixture.Algorithm.InitializedCalibration)
                && fixture.Algorithm.SeenImages.SequenceEqual(new object[] { 1d, 2d })
                && fixture.Calls.SequenceEqual(new[]
                {
                    "start:camera",
                    "initialize:camera",
                    "start:algorithm",
                    "initialize:algorithm",
                    "execute:camera:1",
                    "execute:algorithm:1",
                    "execute:camera:2",
                    "execute:algorithm:2",
                    "stop:algorithm",
                    "stop:camera",
                })
                && first.TryGetPortValue("camera", 1, out _)
                && second.TryGetPortValue("camera", 1, out _)
                && first.TryGetPortValue("algorithm", 0, out var firstResult)
                && Equals(firstResult, 1d)
                && second.TryGetPortValue("algorithm", 0, out var secondResult)
                && Equals(secondResult, 2d);
        });

        await RunAsync("required session input is checked without an initializer", async () =>
        {
            var fixture = CreateSessionInputOnlyFixture();
            await using var session = fixture.Executor.CreateSession();

            try
            {
                await session.StartAsync(CancellationToken.None);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message.Contains(
                           "SessionInputUnavailable",
                           StringComparison.Ordinal)
                    && exception.Message.Contains("consumer", StringComparison.Ordinal)
                    && fixture.Consumer.ExecuteCount == 0;
            }
        });

        await RunAsync("missing linked session value is not replaced by a default", async () =>
        {
            var defaultCalibration = CreateSessionCalibration();
            var fixture = CreateSessionFixture(
                cameraProducesCalibration: false,
                connectCalibration: true,
                defaultCalibration: defaultCalibration);
            await using var session = fixture.Executor.CreateSession();

            try
            {
                await session.StartAsync(CancellationToken.None);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message.Contains("SessionInputUnavailable", StringComparison.Ordinal)
                    && fixture.Algorithm.InitializeCount == 0;
            }
        });

        await RunAsync("unconfigured session input uses its default value", async () =>
        {
            var defaultCalibration = CreateSessionCalibration();
            var fixture = CreateSessionFixture(
                cameraProducesCalibration: false,
                connectCalibration: false,
                defaultCalibration: defaultCalibration);
            await using var session = fixture.Executor.CreateSession();

            await session.StartAsync(CancellationToken.None);
            await session.StopAsync();

            return fixture.Algorithm.InitializeCount == 1
                && ReferenceEquals(
                    fixture.Algorithm.InitializedCalibration,
                    defaultCalibration);
        });
    }

    private static SessionFixture CreateSessionFixture(
        bool cameraProducesCalibration = true,
        bool connectCalibration = true,
        object defaultCalibration = null)
    {
        var calls = new List<string>();
        var cameraDefinition = CreateSessionCameraDefinition();
        var algorithmDefinition = CreateSessionAlgorithmDefinition(defaultCalibration);
        var cameraExecutor = new CameraTestExecutor(calls, cameraProducesCalibration);
        var algorithmExecutor = new AlgorithmTestExecutor(calls);
        var registry = new FlowNodeRegistry();

        registry.Register(new FlowNodeRegistration(
            cameraDefinition,
            () => cameraExecutor));
        registry.Register(new FlowNodeRegistration(
            algorithmDefinition,
            () => algorithmExecutor));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "camera",
            TypeKey = cameraDefinition.TypeKey,
        });

        var algorithm = new WorkflowNode
        {
            Id = "algorithm",
            TypeKey = algorithmDefinition.TypeKey,
            Inputs =
            {
                ["image"] = new LinkRef { SourceNodeId = "camera", SourceSlot = 1 },
            },
        };
        if (connectCalibration)
        {
            algorithm.Inputs["calibration"] = new LinkRef
            {
                SourceNodeId = "camera",
                SourceSlot = 0,
            };
        }

        workflow.Nodes.Add(algorithm);
        return new SessionFixture(
            new GraphExecutor(workflow, registry),
            cameraExecutor,
            algorithmExecutor,
            calls);
    }

    private static SessionInputOnlyFixture CreateSessionInputOnlyFixture()
    {
        var calls = new List<string>();
        var cameraDefinition = CreateSessionCameraDefinition();
        var consumerDefinition = new FlowNodeDefinition
        {
            TypeKey = "test.session.consumer",
            DisplayName = "Consumer",
            Category = "Tests",
        };
        consumerDefinition.InputPorts.Add(new FlowPortDefinition
        {
            Id = "calibration",
            DisplayName = "Calibration",
            IOType = EIOType.Input,
            DataType = FlowDataType.CameraCalibration,
            IsRequired = true,
            Availability = FlowPortAvailability.Session,
        });

        var cameraExecutor = new CameraTestExecutor(calls, producesCalibration: false);
        var consumerExecutor = new RequiredSessionConsumerTestExecutor();
        var registry = new FlowNodeRegistry();
        registry.Register(new FlowNodeRegistration(
            cameraDefinition,
            () => cameraExecutor));
        registry.Register(new FlowNodeRegistration(
            consumerDefinition,
            () => consumerExecutor));

        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "camera",
            TypeKey = cameraDefinition.TypeKey,
        });
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "consumer",
            TypeKey = consumerDefinition.TypeKey,
            Inputs =
            {
                ["calibration"] = new LinkRef { SourceNodeId = "camera", SourceSlot = 0 },
            },
        });

        return new SessionInputOnlyFixture(
            new GraphExecutor(workflow, registry),
            consumerExecutor);
    }

    private static FlowNodeDefinition CreateSessionCameraDefinition()
    {
        var definition = new FlowNodeDefinition
        {
            TypeKey = "test.session.camera",
            DisplayName = "Camera",
            Category = "Tests",
        };
        definition.OutputPorts.Add(new FlowPortDefinition
        {
            Id = "calibration",
            DisplayName = "Calibration",
            IOType = EIOType.Output,
            DataType = FlowDataType.CameraCalibration,
            Availability = FlowPortAvailability.Session,
        });
        definition.OutputPorts.Add(new FlowPortDefinition
        {
            Id = "image",
            DisplayName = "Image",
            IOType = EIOType.Output,
            DataType = FlowDataType.Number,
            Availability = FlowPortAvailability.Iteration,
        });
        return definition;
    }

    private static FlowNodeDefinition CreateSessionAlgorithmDefinition(object defaultCalibration)
    {
        var definition = new FlowNodeDefinition
        {
            TypeKey = "test.session.algorithm",
            DisplayName = "Algorithm",
            Category = "Tests",
        };
        definition.InputPorts.Add(new FlowPortDefinition
        {
            Id = "calibration",
            DisplayName = "Calibration",
            IOType = EIOType.Input,
            DataType = FlowDataType.CameraCalibration,
            IsRequired = true,
            Availability = FlowPortAvailability.Session,
            DefaultValue = defaultCalibration,
        });
        definition.InputPorts.Add(new FlowPortDefinition
        {
            Id = "image",
            DisplayName = "Image",
            IOType = EIOType.Input,
            DataType = FlowDataType.Number,
            IsRequired = true,
            Availability = FlowPortAvailability.Iteration,
        });
        definition.OutputPorts.Add(new FlowPortDefinition
        {
            Id = "result",
            DisplayName = "Result",
            IOType = EIOType.Output,
            DataType = FlowDataType.Number,
            Availability = FlowPortAvailability.Iteration,
        });
        return definition;
    }

    private static CameraCalibration CreateSessionCalibration()
    {
        return new CameraCalibration(
            640,
            480,
            new double[9],
            new double[12],
            new double[16],
            isLeftReference: true);
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

    private sealed class SessionFixture
    {
        public SessionFixture(
            GraphExecutor executor,
            CameraTestExecutor camera,
            AlgorithmTestExecutor algorithm,
            IList<string> calls)
        {
            Executor = executor;
            Camera = camera;
            Algorithm = algorithm;
            Calls = calls;
        }

        public GraphExecutor Executor { get; }

        public CameraTestExecutor Camera { get; }

        public AlgorithmTestExecutor Algorithm { get; }

        public IList<string> Calls { get; }
    }

    private sealed class SessionInputOnlyFixture
    {
        public SessionInputOnlyFixture(
            GraphExecutor executor,
            RequiredSessionConsumerTestExecutor consumer)
        {
            Executor = executor;
            Consumer = consumer;
        }

        public GraphExecutor Executor { get; }

        public RequiredSessionConsumerTestExecutor Consumer { get; }
    }

    private sealed class CameraTestExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowNodeSessionInitializer,
        IFlowIterationSource
    {
        private readonly IList<string> _calls;
        private readonly bool _producesCalibration;

        public CameraTestExecutor(IList<string> calls, bool producesCalibration)
        {
            _calls = calls;
            _producesCalibration = producesCalibration;
            Calibration = CreateSessionCalibration();
        }

        public CameraCalibration Calibration { get; }

        public int InitializeCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public int StopCount { get; private set; }

        public Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            _calls.Add("start:camera");
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            StopCount++;
            _calls.Add("stop:camera");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
            FlowNodeSessionContext context,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            InitializeCount++;
            _calls.Add("initialize:camera");
            if (!_producesCalibration)
            {
                return Task.FromResult<IReadOnlyDictionary<string, object>>(
                    new Dictionary<string, object>());
            }

            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    ["calibration"] = Calibration,
                });
        }

        public Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            PrepareCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            var count = ++ExecuteCount;
            _calls.Add("execute:camera:" + count);
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    ["image"] = (double)count,
                });
        }
    }

    private sealed class AlgorithmTestExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowNodeSessionInitializer,
        IFlowIterationSource
    {
        private readonly IList<string> _calls;

        public AlgorithmTestExecutor(IList<string> calls)
        {
            _calls = calls;
        }

        public int InitializeCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public int StopCount { get; private set; }

        public CameraCalibration InitializedCalibration { get; private set; }

        public List<object> SeenImages { get; } = new List<object>();

        public Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            _calls.Add("start:algorithm");
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            StopCount++;
            _calls.Add("stop:algorithm");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
            FlowNodeSessionContext context,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            InitializeCount++;
            _calls.Add("initialize:algorithm");
            InitializedCalibration = inputs.TryGetValue("calibration", out var calibration)
                ? calibration as CameraCalibration
                : null;
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }

        public Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            PrepareCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            var image = inputs["image"];
            SeenImages.Add(image);
            var count = ++ExecuteCount;
            _calls.Add("execute:algorithm:" + count);
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    ["result"] = image,
                });
        }
    }

    private sealed class RequiredSessionConsumerTestExecutor : IFlowNodeExecutor
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
