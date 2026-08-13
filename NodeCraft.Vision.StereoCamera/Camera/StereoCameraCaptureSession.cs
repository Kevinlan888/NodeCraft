using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal interface IMonotonicClock
    {
        TimeSpan Now { get; }
    }

    internal sealed class SystemMonotonicClock : IMonotonicClock
    {
        private readonly long _origin = Stopwatch.GetTimestamp();

        public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);
    }

    internal sealed class StereoCameraCaptureOptions
    {
        internal StereoCameraCaptureOptions(
            uint pollTimeoutMilliseconds = 100,
            TimeSpan? noValidFrameTimeout = null)
        {
            if (pollTimeoutMilliseconds == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pollTimeoutMilliseconds));
            }

            PollTimeoutMilliseconds = pollTimeoutMilliseconds;
            NoValidFrameTimeout = noValidFrameTimeout ?? TimeSpan.FromSeconds(5);
            if (NoValidFrameTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(noValidFrameTimeout));
            }
        }

        internal uint PollTimeoutMilliseconds { get; }

        internal TimeSpan NoValidFrameTimeout { get; }
    }

    internal sealed class StereoCameraCaptureSession
    {
        private readonly object _gate = new object();
        private readonly string _ipAddress;
        private readonly IStereoCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly StereoCameraCaptureOptions _options;
        private readonly ILogger _logger;
        private readonly LatestFrameMailbox<FrameBundle> _mailbox = new LatestFrameMailbox<FrameBundle>();

        private Task _startTask;
        private Task _stopTask;
        private Task _captureTask;
        private CancellationTokenSource _captureCancellation;
        private CancellationTokenRegistration _callerCancellationRegistration;
        private IDisposable _runtimeScope;
        private IStereoCameraDevice _device;
        private bool _deviceConnected;
        private bool _callbackRegistered;
        private bool _grabbing;
        private bool _started;
        private bool _stopped;
        private long _sequence;
        private CameraCalibration _colorCalibration;
        private CameraCalibration _depthCalibration;

        internal StereoCameraCaptureSession(
            string ipAddress,
            IStereoCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            StereoCameraCaptureOptions options = null,
            ILogger logger = null)
        {
            _ipAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _runtimeScopeFactory = runtimeScopeFactory ?? throw new ArgumentNullException(nameof(runtimeScopeFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? new StereoCameraCaptureOptions();
            _logger = logger ?? NullLogger.Instance;
        }

        internal CameraCalibration ColorCalibration => _colorCalibration;

        internal CameraCalibration DepthCalibration => _depthCalibration;

        internal Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    throw new InvalidOperationException("The StereoCamera capture session has stopped.");
                }

                if (_startTask == null)
                {
                    _startTask = StartCoreAsync(cancellationToken);
                }

                return _startTask;
            }
        }

        internal Task<LatestFrameMailbox<FrameBundle>.LatestFrame<FrameBundle>> WaitForNextAsync(
            long afterSequence,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_started && _startTask == null)
                {
                    throw new InvalidOperationException("The StereoCamera capture session has not started.");
                }
            }

            return _mailbox.WaitForNextAsync(afterSequence, cancellationToken);
        }

        internal Task StopAsync()
        {
            lock (_gate)
            {
                if (_stopTask == null)
                {
                    _stopTask = StopCoreAsync();
                }

                return _stopTask;
            }
        }

        private async Task StartCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                VendorStereoCameraDeviceFactory.ValidateIpv4(_ipAddress);
                cancellationToken.ThrowIfCancellationRequested();

                _runtimeScope = _runtimeScopeFactory.Acquire();
                cancellationToken.ThrowIfCancellationRequested();
                var discovered = _deviceFactory.Discover();
                if (discovered < 0)
                {
                    throw new InvalidOperationException("StereoCamera discovery returned a negative device count.");
                }

                _device = _deviceFactory.OpenByIp(_ipAddress);
                if (_device == null)
                {
                    throw new InvalidOperationException("StereoCamera device factory returned null.");
                }

                _device.Connect();
                _deviceConnected = true;
                _device.RegisterDisconnectCallback(OnDeviceDisconnect);
                _callbackRegistered = true;
                _colorCalibration = _device.ReadCalibration(CameraStream.Color, isLeftReference: false);
                _depthCalibration = _device.ReadCalibration(CameraStream.Depth, isLeftReference: false);
                if (_colorCalibration == null || _depthCalibration == null)
                {
                    throw new InvalidOperationException("StereoCamera calibration was not available.");
                }

                _device.StartGrabbing();
                _grabbing = true;
                _captureCancellation = new CancellationTokenSource();
                if (cancellationToken.CanBeCanceled)
                {
                    _callerCancellationRegistration = cancellationToken.Register(
                        () => _captureCancellation.Cancel());
                }

                lock (_gate)
                {
                    _started = true;
                }

                _captureTask = Task.Run(
                    () => CaptureLoopAsync(_captureCancellation.Token),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                _mailbox.Fault(exception);
                await CleanupAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task StopCoreAsync()
        {
            try
            {
                if (_startTask != null && !_startTask.IsCompleted)
                {
                    try
                    {
                        await _startTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                _captureCancellation?.Cancel();
                if (_captureTask != null)
                {
                    try
                    {
                        await _captureTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "StereoCamera capture loop stopped after a fault.");
                    }
                }

                _mailbox.Complete();
                await CleanupAsync().ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _stopped = true;
                }
            }
        }

        private Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            var lastCompleteFrameAt = _clock.Now;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rawFrame = _device.TryGetFrame(_options.PollTimeoutMilliseconds);
                    var now = _clock.Now;
                    if (rawFrame == null || !rawFrame.IsComplete)
                    {
                        if (now - lastCompleteFrameAt >= _options.NoValidFrameTimeout)
                        {
                            throw new TimeoutException(
                                $"No valid stereo frame was received within {_options.NoValidFrameTimeout}.");
                        }

                        continue;
                    }

                    var capturedAtUtc = DateTimeOffset.UtcNow;
                    var colorImage = FlowImage.FromOwnedBuffer(
                        rawFrame.Color.Width,
                        rawFrame.Color.Height,
                        rawFrame.Color.Stride,
                        rawFrame.Color.PixelFormat,
                        rawFrame.Color.Kind,
                        rawFrame.Color.Buffer,
                        rawFrame.FrameId,
                        rawFrame.DeviceTimestamp,
                        capturedAtUtc,
                        _colorCalibration);
                    var depthImage = FlowImage.FromOwnedBuffer(
                        rawFrame.Depth.Width,
                        rawFrame.Depth.Height,
                        rawFrame.Depth.Stride,
                        rawFrame.Depth.PixelFormat,
                        rawFrame.Depth.Kind,
                        rawFrame.Depth.Buffer,
                        rawFrame.FrameId,
                        rawFrame.DeviceTimestamp,
                        capturedAtUtc,
                        _depthCalibration);
                    var sequence = checked(++_sequence);
                    var bundle = new FrameBundle(
                        sequence,
                        colorImage,
                        depthImage,
                        _colorCalibration,
                        _depthCalibration);
                    _mailbox.Publish(sequence, bundle);
                    lastCompleteFrameAt = now;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _mailbox.Complete();
            }
            catch (Exception exception)
            {
                _mailbox.Fault(exception);
                _captureCancellation?.Cancel();
                throw;
            }

            return Task.CompletedTask;
        }

        private void OnDeviceDisconnect(Exception exception)
        {
            var disconnectException = exception
                ?? new InvalidOperationException("StereoCamera disconnected.");
            _mailbox.Fault(disconnectException);
            _captureCancellation?.Cancel();
        }

        private Task CleanupAsync()
        {
            var cleanupErrors = new List<Exception>();
            _callerCancellationRegistration.Dispose();
            _callerCancellationRegistration = default;

            if (_callbackRegistered && _device != null)
            {
                try
                {
                    _device.UnregisterDisconnectCallback();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "StereoCamera disconnect callback cleanup failed.");
                }

                _callbackRegistered = false;
            }

            if (_grabbing && _device != null)
            {
                try
                {
                    _device.StopGrabbing();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "StereoCamera grabbing cleanup failed.");
                }

                _grabbing = false;
            }

            if (_deviceConnected && _device != null)
            {
                try
                {
                    _device.Disconnect();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "StereoCamera disconnect cleanup failed.");
                }

                _deviceConnected = false;
            }

            if (_device != null)
            {
                try
                {
                    _device.Dispose();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "StereoCamera device disposal failed.");
                }

                _device = null;
            }

            _captureCancellation?.Dispose();
            _captureCancellation = null;
            _captureTask = null;

            if (_runtimeScope != null)
            {
                try
                {
                    _runtimeScope.Dispose();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "StereoCamera native runtime cleanup failed.");
                }

                _runtimeScope = null;
            }

            _colorCalibration = null;
            _depthCalibration = null;

            if (cleanupErrors.Count > 0)
            {
                throw new AggregateException(
                    "One or more StereoCamera capture cleanup operations failed.",
                    cleanupErrors);
            }

            return Task.CompletedTask;
        }
    }
}
