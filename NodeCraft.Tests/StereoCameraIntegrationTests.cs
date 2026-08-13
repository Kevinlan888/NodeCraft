using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;
using NodeCraft.Vision.StereoCamera.Nodes;
using NodeCraft.Vision.StereoCamera.Plugin;

internal static partial class Program
{
    private static async Task RunStereoCameraIntegrationTestsAsync()
    {
        await RunAsync("stereo camera graph runs once into a color preview and cleans up", async () =>
        {
            var fixture = CreateIntegrationFixture();
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(11));
            var registry = CreateVisionRegistry(fixture);
            var workflow = CreateVisionWorkflow(0);
            var executor = new GraphExecutor(workflow, registry);
            var previewNode = new FlowImagePreviewNodeModel { Id = "preview" };
            var callbackCount = 0;
            FlowExecutionContext callbackContext = null;
            var controller = new FlowExecutionController();

            await controller.RunOnceAsync(
                executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    callbackContext = context;
                    registry.ApplyExecutionResults(new[] { previewNode }, context);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            var secondFrame = CreateIntegrationFrame(12);
            fixture.Device.Frames.Enqueue(secondFrame);
            await controller.RunOnceAsync(
                executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    registry.ApplyExecutionResults(new[] { previewNode }, context);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            object output = null;
            var hasOutput = callbackContext != null
                && callbackContext.TryGetPortValue("preview", 0, out output);
            var color = output as FlowImage;
            var passed = callbackCount == 2
                && hasOutput
                && color != null
                && color.FrameId == 11
                && previewNode.CurrentImage != null
                && previewNode.CurrentImage.FrameId == 12
                && ReferenceEquals(previewNode.CurrentImage, output) == false
                && fixture.Device.ConnectCount == 2
                && fixture.Device.StopCount == 2
                && fixture.Device.DisconnectCount == 2
                && fixture.Scope.AcquireCount == 2
                && fixture.Scope.DisposeCount == 2;
            return passed;
        });

        await RunAsync("continuous stereo camera graph serializes callbacks and takes the newest pending frame", async () =>
        {
            var fixture = CreateIntegrationFixture();
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(20));
            var registry = CreateVisionRegistry(fixture);
            var workflow = CreateVisionWorkflow(1);
            var executor = new GraphExecutor(workflow, registry);
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
                        await releaseFirst.Task.ConfigureAwait(false);
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

        await RunAsync("disconnect faults a pending latest frame before it can be delivered", async () =>
        {
            var fixture = CreateIntegrationFixture();
            fixture.Device.Frames.Enqueue(CreateIntegrationFrame(30));
            var registry = CreateVisionRegistry(fixture);
            var session = new GraphExecutor(CreateVisionWorkflow(0), registry).CreateSession();
            await session.StartAsync(CancellationToken.None);
            await Task.Delay(20);
            fixture.Device.DisconnectNow(new InvalidOperationException("unplugged"));
            var faulted = false;
            try
            {
                await session.ExecuteIterationAsync(CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                faulted = exception.Message == "unplugged";
            }

            await session.StopAsync();
            await session.DisposeAsync();
            return faulted && fixture.Device.DisconnectCount == 1;
        });
    }

    private static FlowNodeRegistry CreateVisionRegistry(IntegrationFixture fixture)
    {
        var plugin = StereoCameraPlugin.CreateForTesting(
            fixture.Factory,
            fixture.Scope,
            new IntegrationClock(),
            new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
        var registrationContext = new PluginRegistrationContext(
            NullLogger.Instance,
            new Version(1, 0));
        plugin.Register(registrationContext);
        var registry = new FlowNodeRegistry();
        registry.RegisterPlugin(plugin.Metadata.Id, registrationContext.Registrations);
        return registry;
    }

    private static WorkflowDocument CreateVisionWorkflow(int previewSlot)
    {
        var workflow = new WorkflowDocument();
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "camera",
            TypeKey = StereoCameraNodeModel.FlowNodeTypeKey,
            Inputs = { ["ipAddress"] = "192.168.1.10" },
        });
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "preview",
            TypeKey = FlowImagePreviewNodeModel.FlowNodeTypeKey,
            Inputs =
            {
                ["image"] = new LinkRef { SourceNodeId = "camera", SourceSlot = previewSlot },
            },
        });
        return workflow;
    }

    private static RawStereoFrame CreateIntegrationFrame(ulong frameId)
    {
        return new RawStereoFrame(
            frameId,
            frameId * 10,
            new RawCameraImage(
                1,
                1,
                3,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { (byte)frameId, 2, 3 }),
            new RawCameraImage(
                1,
                1,
                2,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                new byte[] { (byte)frameId, 0 }));
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
            internal Scope(IntegrationScopeFactory owner) { _owner = owner; }
            public void Dispose() { _owner.DisposeCount++; }
        }
    }

    private sealed class IntegrationDeviceFactory : IStereoCameraDeviceFactory
    {
        private readonly IntegrationDevice _device;
        internal IntegrationDeviceFactory(IntegrationDevice device) { _device = device; }
        public int Discover() => 1;
        public IStereoCameraDevice OpenByIp(string ipAddress) => _device;
    }

    private sealed class IntegrationDevice : IStereoCameraDevice
    {
        private Action<Exception> _disconnect;
        internal ConcurrentQueue<RawStereoFrame> Frames { get; } = new ConcurrentQueue<RawStereoFrame>();
        internal int ConnectCount;
        internal int StopCount;
        internal int DisconnectCount;

        public void Connect() => Interlocked.Increment(ref ConnectCount);
        public void RegisterDisconnectCallback(Action<Exception> callback) => _disconnect = callback;
        public void UnregisterDisconnectCallback() => _disconnect = null;
        public CameraCalibration ReadCalibration(CameraStream stream, bool isLeftReference)
            => new CameraCalibration(1, 1, new double[9], new double[12], new double[16], false);
        public void StartGrabbing() { }
        public RawStereoFrame TryGetFrame(uint timeoutMilliseconds)
            => Frames.TryDequeue(out var frame) ? frame : null;
        public void StopGrabbing() => Interlocked.Increment(ref StopCount);
        public void Disconnect() => Interlocked.Increment(ref DisconnectCount);
        public void Dispose() { }
        internal void DisconnectNow(Exception exception) => _disconnect?.Invoke(exception);
    }
}
