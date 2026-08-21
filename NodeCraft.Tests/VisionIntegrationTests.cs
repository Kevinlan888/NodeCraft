using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft;
using NodeCraft.Flow;
using NodeCraft.BuiltIn.Nodes;
using NodeCraft.Vision.Camera;
using NodeCraft.Vision.Nodes;
using NodeCraft.Vision.Plugin;

internal static partial class Program
{
    private static async Task RunVisionIntegrationTestsAsync()
    {
        await RunAsync("Vision graph runs once into an image preview and cleans up", async () =>
        {
            var fixture = CreateIntegrationFixture();
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(11));
            var registry = CreateVisionRegistry(fixture);
            var workflow = CreateVisionWorkflow();
            var executor = new GraphExecutor(workflow, registry);
            var previewNode = new FlowImagePreviewNodeModel { Id = "preview" };
            var callbackCount = 0;
            FlowExecutionContext firstContext = null;

            var controller = new FlowExecutionController();
            await controller.RunOnceAsync(
                executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    firstContext ??= context;
                    registry.ApplyExecutionResults(new[] { previewNode }, context);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(12));
            await controller.RunOnceAsync(
                executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    registry.ApplyExecutionResults(new[] { previewNode }, context);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            object firstOutput = null;
            var hasFirstOutput = firstContext != null
                && firstContext.TryGetPortValue("preview", 0, out firstOutput);
            var firstImage = firstOutput as FlowImage;
            return callbackCount == 2
                && hasFirstOutput
                && firstImage != null
                && firstImage.FrameId == 11
                && previewNode.CurrentImage != null
                && previewNode.CurrentImage.FrameId == 12
                && !ReferenceEquals(previewNode.CurrentImage, firstOutput)
                && fixture.Device.ConnectCount == 2
                && fixture.Device.StopCount == 2
                && fixture.Device.DisconnectCount == 2
                && fixture.Scope.AcquireCount == 2
                && fixture.Scope.DisposeCount == 2;
        });

        await RunAsync("continuous Vision graph serializes callbacks and takes the newest pending frame", async () =>
        {
            var fixture = CreateIntegrationFixture();
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(20));
            var registry = CreateVisionRegistry(fixture);
            var executor = new GraphExecutor(CreateVisionWorkflow(), registry);
            var controller = new FlowExecutionController();
            using var cancellation = new CancellationTokenSource();
            var firstCallbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackFrames = new List<ulong>();
            var activeCallbacks = 0;
            var maxActiveCallbacks = 0;

            var runTask = controller.RunContinuouslyAsync(
                executor.CreateSession(),
                async (context, iteration, elapsed) =>
                {
                    var active = Interlocked.Increment(ref activeCallbacks);
                    maxActiveCallbacks = Math.Max(maxActiveCallbacks, active);
                    if (context.TryGetPortValue("preview", 0, out var value) && value is FlowImage image)
                    {
                        callbackFrames.Add(image.FrameId);
                    }

                    if (callbackFrames.Count == 1)
                    {
                        firstCallbackEntered.TrySetResult(true);
                        await releaseFirst.Task;
                    }
                    else
                    {
                        cancellation.Cancel();
                    }

                    Interlocked.Decrement(ref activeCallbacks);
                },
                cancellation.Token);

            await firstCallbackEntered.Task;
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(21));
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(22));
            await Task.Delay(20);
            releaseFirst.TrySetResult(true);
            await runTask;

            return callbackFrames.SequenceEqual(new[] { 20UL, 22UL })
                && maxActiveCallbacks == 1
                && fixture.Device.ConnectCount == 1
                && fixture.Device.DisconnectCount == 1;
        });

        await RunAsync("legacy Vision camera IP graph link is rejected before session startup", () =>
        {
            var fixture = CreateIntegrationFixture();
            RegisterVisionNodesForGraphModelConversion(fixture);

            var graph = new GraphModel
            {
                Nodes = new List<NodeModel>
                {
                    new StringValueNodeModel
                    {
                        Id = "ip",
                        ValueText = "192.168.1.10",
                    },
                    new VisionCameraNodeModel
                    {
                        Id = "camera",
                        IpAddress = "192.168.1.20",
                    },
                },
                Links = new List<GraphLink>
                {
                    new GraphLink
                    {
                        Id = "legacy-ip-link",
                        OriginNodeId = "ip",
                        OriginSlot = 0,
                        TargetNodeId = "camera",
                        TargetSlot = 1,
                    },
                },
            };

            try
            {
                GraphModelWorkflowAdapter.Convert(graph);
                return Task.FromResult(false);
            }
            catch (InvalidOperationException exception)
            {
                return Task.FromResult(exception.Message.Contains("unknown target slot 1", StringComparison.Ordinal)
                    && exception.Message.Contains("legacy-ip-link", StringComparison.Ordinal)
                    && fixture.Device.ConnectCount == 0);
            }
        });
    }

    private static FlowNodeRegistry CreateVisionRegistry(IntegrationFixture fixture)
    {
        var plugin = VisionPlugin.CreateForTesting(
            fixture.Factory,
            fixture.Scope,
            new IntegrationClock(),
            new VisionCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
        var registrationContext = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(registrationContext);
        var registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, registrationContext.Registrations);
        return registry;
    }

    private static void RegisterVisionNodesForGraphModelConversion(IntegrationFixture fixture)
    {
        var plugin = VisionPlugin.CreateForTesting(
            fixture.Factory,
            fixture.Scope,
            new IntegrationClock(),
            new VisionCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
        var registrationContext = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(registrationContext);

        foreach (var registration in registrationContext.Registrations)
        {
            NodeExecutorFactory.Registry.Register(registration);
        }
    }

    private static WorkflowDocument CreateVisionWorkflow()
    {
        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "camera",
            TypeKey = VisionCameraNodeModel.FlowNodeTypeKey,
            Inputs = { ["ipAddress"] = "192.168.1.10" },
        });
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "preview",
            TypeKey = FlowImagePreviewNodeModel.FlowNodeTypeKey,
            Inputs =
            {
                ["image"] = new LinkRef { SourceNodeId = "camera", SourceSlot = 0 },
            },
        });
        return workflow;
    }

    private static VisionRawFrame CreateIntegrationFrame(ulong frameId)
    {
        return new VisionRawFrame(
            frameId,
            frameId * 10,
            new VisionRawImage(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { (byte)frameId, 2, 3 }));
    }

    private sealed class IntegrationFixture
    {
        internal IntegrationFixture(
            IntegrationDeviceFactory factory,
            IntegrationScopeFactory scope,
            IntegrationDevice device)
        {
            Factory = factory;
            Scope = scope;
            Device = device;
        }

        internal IntegrationDeviceFactory Factory { get; }
        internal IntegrationScopeFactory Scope { get; }
        internal IntegrationDevice Device { get; }
    }

    private static IntegrationFixture CreateIntegrationFixture()
    {
        var device = new IntegrationDevice();
        var scope = new IntegrationScopeFactory();
        return new IntegrationFixture(new IntegrationDeviceFactory(device), scope, device);
    }

    private sealed class IntegrationClock : IMonotonicClock
    {
        private readonly DateTime _origin = DateTime.UtcNow;

        public TimeSpan Now => DateTime.UtcNow - _origin;
    }

    private sealed class IntegrationScopeFactory : ICameraRuntimeScopeFactory
    {
        internal int AcquireCount;
        internal int DisposeCount;

        public IDisposable Acquire()
        {
            AcquireCount++;
            return new Scope(this);
        }

        private sealed class Scope : IDisposable
        {
            private readonly IntegrationScopeFactory _owner;

            internal Scope(IntegrationScopeFactory owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner.DisposeCount++;
            }
        }
    }

    private sealed class IntegrationDeviceFactory : IVisionCameraDeviceFactory
    {
        private readonly IntegrationDevice _device;

        internal IntegrationDeviceFactory(IntegrationDevice device)
        {
            _device = device;
        }

        public int Discover() => 1;

        public IVisionCameraDevice OpenByIp(string ipAddress) => _device;
    }

    private sealed class IntegrationDevice : IVisionCameraDevice
    {
        internal ConcurrentQueue<VisionRawFrame> Frames { get; } = new ConcurrentQueue<VisionRawFrame>();
        internal int ConnectCount;
        internal int StopCount;
        internal int DisconnectCount;

        public void Connect() => Interlocked.Increment(ref ConnectCount);

        public void StartGrabbing()
        {
        }

        public VisionRawFrame TryGetFrame(uint timeoutMilliseconds)
            => Frames.TryDequeue(out var frame) ? frame : null;

        public void StopGrabbing() => Interlocked.Increment(ref StopCount);

        public void Disconnect() => Interlocked.Increment(ref DisconnectCount);

        public void Dispose()
        {
        }
    }
}
