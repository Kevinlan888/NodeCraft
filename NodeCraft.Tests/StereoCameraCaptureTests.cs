using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;
using NodeCraft.Vision.StereoCamera.Nodes;

internal static partial class Program
{
    private static async Task RunStereoCameraCaptureTestsAsync()
    {
        await RunAsync("stereo camera capture starts in the required order and stops in reverse", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedStereoCameraDevice(calls);
            var fixture = CreateCameraFixture(calls, device);
            using var cancellation = new CancellationTokenSource();
            var session = new StereoCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new TestMonotonicClock(),
                new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));

            await session.StartAsync(cancellation.Token);
            await session.StopAsync();

            var requiredPrefix = new[]
            {
                "scope:acquire",
                "discover",
                "open:192.168.1.10",
                "connect",
                "register",
                "calibration:Color",
                "calibration:Depth",
                "start",
            };
            var prefix = calls.Take(requiredPrefix.Length).SequenceEqual(requiredPrefix);
            var stopIndex = calls.IndexOf("stop");
            var disconnectIndex = calls.IndexOf("disconnect");
            var disposeIndex = calls.IndexOf("device:dispose");
            var scopeDisposeIndex = calls.IndexOf("scope:dispose");
            return prefix
                && calls.Contains("unregister")
                && stopIndex >= 0
                && disconnectIndex > stopIndex
                && disposeIndex > disconnectIndex
                && scopeDisposeIndex > disposeIndex;
        });

        await RunAsync("stereo camera capture preserves startup failure when cleanup fails", async () =>
        {
            var calls = new List<string>();
            var connectException = new InvalidOperationException("connect failed first");
            var cleanupException = new InvalidOperationException("runtime cleanup failed second");
            var device = new ScriptedStereoCameraDevice(calls)
            {
                ConnectException = connectException,
            };
            var fixture = new CameraFixture(
                new ScriptedStereoCameraDeviceFactory(calls, device),
                new RecordingRuntimeScopeFactory(calls, cleanupException));
            var logger = new RecordingLogger();
            var session = new StereoCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new TestMonotonicClock(),
                new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)),
                logger);

            Exception observed = null;
            try
            {
                await session.StartAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            return ReferenceEquals(observed, connectException)
                && logger.Exceptions.Contains(cleanupException)
                && logger.Messages.Any(message =>
                    message.Contains("StereoCamera native runtime cleanup failed.", StringComparison.Ordinal))
                && calls.Contains("scope:dispose");
        });

        await RunAsync("stereo camera capture publishes only complete same-frame color/depth bundles", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedStereoCameraDevice(calls);
            device.Frames.Enqueue(new RawStereoFrame(1, 10, CreateRawColor(1), null));
            device.Frames.Enqueue(new RawStereoFrame(2, 20, CreateRawColor(2), CreateRawDepth(2)));
            var fixture = CreateCameraFixture(calls, device);
            var session = new StereoCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new TestMonotonicClock(),
                new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));

            await session.StartAsync(CancellationToken.None);
            var item = await session.WaitForNextAsync(0, CancellationToken.None);
            await session.StopAsync();
            return item.Value.ColorImage.FrameId == 2
                && item.Value.DepthImage.FrameId == 2
                && item.Value.ColorCalibration.ImageWidth == 640
                && item.Value.DepthCalibration.ImageWidth == 320;
        });

        await RunAsync("stereo camera capture faults on malformed image and clears mailbox", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedStereoCameraDevice(calls);
            device.Frames.Enqueue(new RawStereoFrame(
                1,
                10,
                new RawCameraImage(4, 2, 1, FlowPixelFormat.Bgr24, FlowImageKind.Color, new byte[2]),
                CreateRawDepth(1)));
            var fixture = CreateCameraFixture(calls, device);
            var session = new StereoCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                new TestMonotonicClock(),
                new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
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

        await RunAsync("stereo camera capture faults after no valid frame timeout", async () =>
        {
            var calls = new List<string>();
            var clock = new TestMonotonicClock();
            var device = new ScriptedStereoCameraDevice(calls)
            {
                OnTryGetFrame = () =>
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                    return null;
                },
            };
            var fixture = CreateCameraFixture(calls, device);
            var session = new StereoCameraCaptureSession(
                "192.168.1.10",
                fixture.Factory,
                fixture.ScopeFactory,
                clock,
                new StereoCameraCaptureOptions(100, TimeSpan.FromSeconds(5)));
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

        await RunAsync("stereo camera executor exposes four synchronized output slots", async () =>
        {
            var calls = new List<string>();
            var device = new ScriptedStereoCameraDevice(calls);
            device.Frames.Enqueue(new RawStereoFrame(4, 40, CreateRawColor(4), CreateRawDepth(4)));
            var fixture = CreateCameraFixture(calls, device);
            var executor = new StereoCameraExecutor(
                fixture.Factory,
                fixture.ScopeFactory,
                new TestMonotonicClock(),
                new StereoCameraCaptureOptions());
            var definition = new FlowNodeDefinition { TypeKey = "camera" };
            var node = new WorkflowNode { Id = "camera", TypeKey = "camera" };
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
            var color = (FlowImage)outputs["colorImage"];
            var depth = (FlowImage)outputs["depthImage"];
            var colorCalibration = (CameraCalibration)outputs["colorCalibration"];
            var depthCalibration = (CameraCalibration)outputs["depthCalibration"];
            return outputs.Keys.SequenceEqual(new[] { "colorImage", "depthImage", "colorCalibration", "depthCalibration" })
                && colorCalibration.ImageWidth == 640
                && depthCalibration.ImageWidth == 320
                && !ReferenceEquals(color, (object)colorCalibration)
                && !ReferenceEquals(depth, (object)depthCalibration);
        });
    }

    private static CameraFixture CreateCameraFixture(
        List<string> calls,
        ScriptedStereoCameraDevice device)
    {
        return new CameraFixture(
            new ScriptedStereoCameraDeviceFactory(calls, device),
            new RecordingRuntimeScopeFactory(calls));
    }

    private static RawCameraImage CreateRawColor(ulong frameId)
    {
        return new RawCameraImage(
            2,
            1,
            6,
            FlowPixelFormat.Bgr24,
            FlowImageKind.Color,
            new byte[] { (byte)frameId, 2, 3, 4, 5, 6 });
    }

    private static RawCameraImage CreateRawDepth(ulong frameId)
    {
        return new RawCameraImage(
            2,
            1,
            4,
            FlowPixelFormat.Depth16,
            FlowImageKind.Depth,
            new byte[] { (byte)frameId, 0, 4, 0 });
    }

    private sealed class CameraFixture
    {
        internal CameraFixture(
            ScriptedStereoCameraDeviceFactory factory,
            RecordingRuntimeScopeFactory scopeFactory)
        {
            Factory = factory;
            ScopeFactory = scopeFactory;
        }

        internal ScriptedStereoCameraDeviceFactory Factory { get; }

        internal RecordingRuntimeScopeFactory ScopeFactory { get; }
    }

    private sealed class TestMonotonicClock : IMonotonicClock
    {
        internal TimeSpan Current { get; private set; }

        public TimeSpan Now => Current;

        internal void Advance(TimeSpan amount)
        {
            Current += amount;
        }
    }

    private sealed class RecordingRuntimeScopeFactory : ICameraRuntimeScopeFactory
    {
        private readonly List<string> _calls;
        private readonly Exception _disposeException;

        internal RecordingRuntimeScopeFactory(List<string> calls, Exception disposeException = null)
        {
            _calls = calls;
            _disposeException = disposeException;
        }

        public IDisposable Acquire()
        {
            _calls.Add("scope:acquire");
            return new RecordingScope(_calls, _disposeException);
        }
    }

    private sealed class RecordingScope : IDisposable
    {
        private readonly List<string> _calls;
        private readonly Exception _disposeException;

        internal RecordingScope(List<string> calls, Exception disposeException)
        {
            _calls = calls;
            _disposeException = disposeException;
        }

        public void Dispose()
        {
            _calls.Add("scope:dispose");
            if (_disposeException != null)
            {
                throw _disposeException;
            }
        }
    }

    private sealed class ScriptedStereoCameraDeviceFactory : IStereoCameraDeviceFactory
    {
        private readonly List<string> _calls;
        private readonly ScriptedStereoCameraDevice _device;

        internal ScriptedStereoCameraDeviceFactory(List<string> calls, ScriptedStereoCameraDevice device)
        {
            _calls = calls;
            _device = device;
        }

        public int Discover()
        {
            _calls.Add("discover");
            return 1;
        }

        public IStereoCameraDevice OpenByIp(string ipAddress)
        {
            _calls.Add("open:" + ipAddress);
            return _device;
        }
    }

    private sealed class ScriptedStereoCameraDevice : IStereoCameraDevice
    {
        private readonly List<string> _calls;
        private Action<Exception> _disconnectCallback;

        internal ScriptedStereoCameraDevice(List<string> calls)
        {
            _calls = calls;
            Frames = new Queue<RawStereoFrame>();
        }

        internal Queue<RawStereoFrame> Frames { get; }

        internal Func<RawStereoFrame> OnTryGetFrame { get; set; }

        internal Exception ConnectException { get; set; }

        public void Connect()
        {
            _calls.Add("connect");
            if (ConnectException != null)
            {
                throw ConnectException;
            }
        }

        public void RegisterDisconnectCallback(Action<Exception> callback)
        {
            _calls.Add("register");
            _disconnectCallback = callback;
        }

        public void UnregisterDisconnectCallback() => _calls.Add("unregister");

        public CameraCalibration ReadCalibration(CameraStream stream, bool isLeftReference)
        {
            _calls.Add("calibration:" + stream);
            return CreateCalibration(stream == CameraStream.Color ? 640 : 320);
        }

        public void StartGrabbing() => _calls.Add("start");

        public RawStereoFrame TryGetFrame(uint timeoutMilliseconds)
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

        internal void DisconnectNow(Exception exception)
        {
            _disconnectCallback?.Invoke(exception);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        internal RecordingLogger()
        {
            Exceptions = new List<Exception>();
            Messages = new List<string>();
        }

        internal List<Exception> Exceptions { get; }

        internal List<string> Messages { get; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                Exceptions.Add(exception);
            }

            if (formatter != null)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }

    private static CameraCalibration CreateCalibration(int width)
    {
        return new CameraCalibration(
            width,
            480,
            new double[9],
            new double[12],
            new double[16],
            isLeftReference: false);
    }
}
