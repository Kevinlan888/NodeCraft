using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;
using NodeCraft.Vision.Nodes;

internal static partial class Program
{
    private static async Task RunVisionCameraCaptureTestsAsync()
    {
        await RunAsync("Vision capture starts and stops in the required order", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedVisionDevice(calls);
            device.Frames.Enqueue(CreateVisionFrame(1));
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));

            await session.StartAsync(CancellationToken.None);
            var item = await session.WaitForNextAsync(0, CancellationToken.None);
            await session.StopAsync();

            var expectedPrefix = new[]
            {
                "scope:acquire",
                "discover",
                "open:192.168.1.10",
                "connect",
                "trigger:off",
                "start",
            };
            return item.Value.FrameId == 1
                && expectedPrefix.SequenceEqual(calls.Take(expectedPrefix.Length))
                && calls.IndexOf("stop") > calls.IndexOf("start")
                && calls.IndexOf("disconnect") > calls.IndexOf("stop")
                && calls.IndexOf("device:dispose") > calls.IndexOf("disconnect")
                && calls.IndexOf("scope:dispose") > calls.IndexOf("device:dispose");
        });

        await RunAsync("Vision capture preserves startup failure and cleans resources", async () =>
        {
            var calls = new List<string>();
            var connectException = new InvalidOperationException("connect failed");
            var device = new ScriptedVisionDevice(calls) { ConnectException = connectException };
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions());
            Exception observed = null;
            try
            {
                await session.StartAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            await session.StopAsync();
            return ReferenceEquals(observed, connectException)
                && calls.Contains("device:dispose")
                && calls.Contains("scope:dispose");
        });

        await RunAsync("Vision capture publishes a complete image with frame metadata", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedVisionDevice(calls);
            device.Frames.Enqueue(CreateVisionFrame(4));
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions());
            await session.StartAsync(CancellationToken.None);
            var item = await session.WaitForNextAsync(0, CancellationToken.None);
            await session.StopAsync();
            return item.Value.FrameId == 4
                && item.Value.Width == 2
                && item.Value.Height == 1
                && item.Value.Buffer.Span.SequenceEqual(new byte[] { 4, 2, 3, 4, 5, 6 });
        });

        await RunAsync("Vision capture faults on malformed image", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedVisionDevice(calls);
            device.Frames.Enqueue(new VisionRawFrame(
                1,
                10,
                new VisionRawImage(2, 1, 6, FlowPixelFormat.Bgr24, FlowImageKind.Color, new byte[2])));
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions());
            await session.StartAsync(CancellationToken.None);
            var faulted = false;
            try
            {
                await session.WaitForNextAsync(0, CancellationToken.None);
            }
            catch (ArgumentException)
            {
                faulted = true;
            }

            await session.StopAsync();
            return faulted;
        });

        await RunAsync("Vision capture faults after no valid frame timeout", async () =>
        {
            var calls = new List<string>();
            var clock = new VisionTestClock();
            var device = new ScriptedVisionDevice(calls)
            {
                OnTryGetFrame = () =>
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    return null;
                },
            };
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                clock,
                new VisionCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
            await session.StartAsync(CancellationToken.None);
            var timedOut = false;
            try
            {
                await session.WaitForNextAsync(0, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                timedOut = true;
            }

            await session.StopAsync();
            return timedOut;
        });

        await RunAsync("Vision capture honors waiter cancellation", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedVisionDevice(calls)
            {
                OnTryGetFrame = () => null,
            };
            var fixture = CreateVisionFixture(calls, device);
            var session = new VisionCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions());
            await session.StartAsync(CancellationToken.None);
            using (var cancellation = new CancellationTokenSource())
            {
                var wait = session.WaitForNextAsync(0, cancellation.Token);
                cancellation.Cancel();
                var canceled = false;
                try
                {
                    await wait;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                await session.StopAsync();
                return canceled;
            }
        });

        await RunAsync("Vision executor exposes one image output", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedVisionDevice(calls);
            device.Frames.Enqueue(CreateVisionFrame(8));
            var fixture = CreateVisionFixture(calls, device);
            var executor = new VisionCameraExecutor(
                fixture.Factory,
                fixture.ScopeFactory,
                new VisionTestClock(),
                new VisionCameraCaptureOptions());
            var definition = new FlowNodeDefinition { TypeKey = "nodecraft.vision.camera" };
            var node = new WorkflowNode { Id = "camera", TypeKey = definition.TypeKey };
            node.Inputs["ipAddress"] = "192.168.1.10";
            var context = new FlowNodeSessionContext(node, definition, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            await executor.StartSessionAsync(context, CancellationToken.None);
            await executor.PrepareIterationAsync(context, CancellationToken.None);
            var outputs = await executor.ExecuteAsync(
                new FlowExecutionContext(),
                node,
                definition,
                new Dictionary<string, object>(),
                CancellationToken.None);
            await executor.StopSessionAsync(context, CancellationToken.None);
            return outputs.Keys.SequenceEqual(new[] { "image" })
                && outputs["image"] is FlowImage image
                && image.FrameId == 8;
        });
    }

    private static VisionRawFrame CreateVisionFrame(ulong frameId)
    {
        return new VisionRawFrame(
            frameId,
            frameId * 10,
            new VisionRawImage(
                2,
                1,
                6,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[] { (byte)frameId, 2, 3, 4, 5, 6 }));
    }

    private static VisionFixture CreateVisionFixture(
        List<string> calls,
        ScriptedVisionDevice device)
    {
        return new VisionFixture(
            new ScriptedVisionDeviceFactory(calls, device),
            new VisionRuntimeScopeFactory(calls));
    }

    private sealed class VisionFixture
    {
        internal VisionFixture(
            ScriptedVisionDeviceFactory factory,
            VisionRuntimeScopeFactory scopeFactory)
        {
            Factory = factory;
            ScopeFactory = scopeFactory;
        }

        internal ScriptedVisionDeviceFactory Factory { get; }

        internal VisionRuntimeScopeFactory ScopeFactory { get; }
    }

    private sealed class VisionTestClock : IMonotonicClock
    {
        internal TimeSpan Current { get; private set; }

        public TimeSpan Now => Current;

        internal void Advance(TimeSpan amount)
        {
            Current += amount;
        }
    }

    private sealed class VisionRuntimeScopeFactory : ICameraRuntimeScopeFactory
    {
        private readonly List<string> _calls;

        internal VisionRuntimeScopeFactory(List<string> calls)
        {
            _calls = calls;
        }

        public IDisposable Acquire()
        {
            _calls.Add("scope:acquire");
            return new VisionRuntimeScope(_calls);
        }
    }

    private sealed class VisionRuntimeScope : IDisposable
    {
        private readonly List<string> _calls;

        internal VisionRuntimeScope(List<string> calls)
        {
            _calls = calls;
        }

        public void Dispose()
        {
            _calls.Add("scope:dispose");
        }
    }

    private sealed class ScriptedVisionDeviceFactory : IVisionCameraDeviceFactory
    {
        private readonly List<string> _calls;
        private readonly ScriptedVisionDevice _device;

        internal ScriptedVisionDeviceFactory(List<string> calls, ScriptedVisionDevice device)
        {
            _calls = calls;
            _device = device;
        }

        public int Discover()
        {
            _calls.Add("discover");
            return 1;
        }

        public IVisionCameraDevice OpenByIp(string ipAddress)
        {
            _calls.Add("open:" + ipAddress);
            return _device;
        }
    }

    private sealed class ScriptedVisionDevice : IVisionCameraDevice
    {
        private readonly List<string> _calls;

        internal ScriptedVisionDevice(List<string> calls)
        {
            _calls = calls;
            Frames = new Queue<VisionRawFrame>();
        }

        internal Queue<VisionRawFrame> Frames { get; }

        internal Func<VisionRawFrame> OnTryGetFrame { get; set; }

        internal Exception ConnectException { get; set; }

        public void Connect()
        {
            _calls.Add("connect");
            if (ConnectException != null)
            {
                throw ConnectException;
            }
        }

        public void StartGrabbing()
        {
            _calls.Add("trigger:off");
            _calls.Add("start");
        }

        public VisionRawFrame TryGetFrame(uint timeoutMilliseconds)
        {
            _calls.Add("poll");
            if (OnTryGetFrame != null)
            {
                return OnTryGetFrame();
            }

            return Frames.Count > 0 ? Frames.Dequeue() : null;
        }

        public void StopGrabbing() => _calls.Add("stop");

        public void Disconnect() => _calls.Add("disconnect");

        public void Dispose() => _calls.Add("device:dispose");
    }
}
