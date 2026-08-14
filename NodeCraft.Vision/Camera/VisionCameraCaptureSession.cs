using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Camera
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

    internal interface ICameraRuntimeScopeFactory
    {
        IDisposable Acquire();
    }

    internal sealed class VisionCameraCaptureOptions
    {
        internal VisionCameraCaptureOptions(
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

    internal sealed class VisionCameraCaptureSession
    {
        private readonly object _gate = new object();
        private readonly string _ipAddress;
        private readonly IVisionCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly VisionCameraCaptureOptions _options;
        private readonly ILogger _logger;
        private readonly LatestFrameMailbox<FlowImage> _mailbox = new LatestFrameMailbox<FlowImage>();

        private Task _startTask;
        private Task _stopTask;
        private Task _captureTask;
        private CancellationTokenSource _captureCancellation;
        private CancellationTokenRegistration _callerCancellationRegistration;
        private IDisposable _runtimeScope;
        private IVisionCameraDevice _device;
        private bool _deviceConnected;
        private bool _grabbing;
        private bool _started;
        private bool _stopped;
        private long _sequence;

        internal VisionCameraCaptureSession(
            string ipAddress,
            IVisionCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            VisionCameraCaptureOptions options = null,
            ILogger logger = null)
        {
            _ipAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _runtimeScopeFactory = runtimeScopeFactory ?? throw new ArgumentNullException(nameof(runtimeScopeFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? new VisionCameraCaptureOptions();
            _logger = logger ?? NullLogger.Instance;
        }

        internal Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    throw new InvalidOperationException("The Vision capture session has stopped.");
                }

                if (_startTask == null)
                {
                    _startTask = StartCoreAsync(cancellationToken);
                }

                return _startTask;
            }
        }

        internal Task<LatestFrameMailbox<FlowImage>.LatestFrame<FlowImage>> WaitForNextAsync(
            long afterSequence,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_started && _startTask == null)
                {
                    throw new InvalidOperationException("The Vision capture session has not started.");
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
                if (!VisionCameraDeviceFactory.IsValidIpv4(_ipAddress))
                {
                    throw new ArgumentException(
                        "The camera IP address must be a four-component dotted-decimal IPv4 literal.",
                        nameof(_ipAddress));
                }

                cancellationToken.ThrowIfCancellationRequested();
                _runtimeScope = _runtimeScopeFactory.Acquire();
                cancellationToken.ThrowIfCancellationRequested();

                var discovered = _deviceFactory.Discover();
                if (discovered < 0)
                {
                    throw new InvalidOperationException("Vision discovery returned a negative device count.");
                }

                _device = _deviceFactory.OpenByIp(_ipAddress);
                if (_device == null)
                {
                    throw new InvalidOperationException("Vision device factory returned null.");
                }

                _device.Connect();
                _deviceConnected = true;
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
                try
                {
                    await CleanupAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "Vision startup cleanup failed.");
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
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
                        _logger.LogError(exception, "Vision capture loop stopped after a fault.");
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
                    if (rawFrame == null)
                    {
                        if (now - lastCompleteFrameAt >= _options.NoValidFrameTimeout)
                        {
                            throw new TimeoutException(
                                $"No valid Vision frame was received within {_options.NoValidFrameTimeout}.");
                        }

                        continue;
                    }

                    var capturedAtUtc = DateTimeOffset.UtcNow;
                    var image = FlowImage.FromOwnedBuffer(
                        rawFrame.Image.Width,
                        rawFrame.Image.Height,
                        rawFrame.Image.Stride,
                        rawFrame.Image.PixelFormat,
                        rawFrame.Image.Kind,
                        rawFrame.Image.Buffer,
                        rawFrame.FrameId,
                        rawFrame.DeviceTimestamp,
                        capturedAtUtc);
                    var sequence = checked(++_sequence);
                    _mailbox.Publish(sequence, image);
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

        private Task CleanupAsync()
        {
            var cleanupErrors = new List<Exception>();
            _callerCancellationRegistration.Dispose();
            _callerCancellationRegistration = default;

            if (_grabbing && _device != null)
            {
                try
                {
                    _device.StopGrabbing();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                    _logger.LogError(exception, "Vision grabbing cleanup failed.");
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
                    _logger.LogError(exception, "Vision disconnect cleanup failed.");
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
                    _logger.LogError(exception, "Vision device disposal failed.");
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
                    _logger.LogError(exception, "Vision native runtime cleanup failed.");
                }

                _runtimeScope = null;
            }

            if (cleanupErrors.Count > 0)
            {
                throw new AggregateException(
                    "One or more Vision capture cleanup operations failed.",
                    cleanupErrors);
            }

            return Task.CompletedTask;
        }
    }
}
