using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;

namespace NodeCraft.Vision.StereoCamera.Nodes
{
    internal sealed class StereoCameraExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowIterationSource
    {
        private readonly IStereoCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly StereoCameraCaptureOptions _options;
        private readonly ILogger _logger;
        private StereoCameraCaptureSession _captureSession;
        private FrameBundle _currentBundle;
        private long _lastSequence;

        internal StereoCameraExecutor(
            IStereoCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            StereoCameraCaptureOptions options,
            ILogger logger = null)
        {
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _runtimeScopeFactory = runtimeScopeFactory ?? throw new ArgumentNullException(nameof(runtimeScopeFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task StartSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            if (_captureSession != null)
            {
                return;
            }

            if (!context.Node.Inputs.TryGetValue("ipAddress", out var value)
                || !(value is string ipAddress))
            {
                throw new InvalidOperationException("StereoCamera node requires an ipAddress input.");
            }

            _captureSession = new StereoCameraCaptureSession(
                ipAddress,
                _deviceFactory,
                _runtimeScopeFactory,
                _clock,
                _options,
                _logger);
            try
            {
                await _captureSession.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await _captureSession.StopAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                _captureSession = null;
                throw;
            }
        }

        public async Task StopSessionAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            var session = _captureSession;
            _captureSession = null;
            _currentBundle = null;
            _lastSequence = 0;
            if (session != null)
            {
                await session.StopAsync().ConfigureAwait(false);
            }
        }

        public async Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            var session = _captureSession
                ?? throw new InvalidOperationException("StereoCamera session has not started.");
            _currentBundle = null;
            var item = await session.WaitForNextAsync(_lastSequence, cancellationToken)
                .ConfigureAwait(false);
            _lastSequence = item.Sequence;
            _currentBundle = item.Value;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            var bundle = _currentBundle
                ?? throw new InvalidOperationException("StereoCamera has no prepared frame for this iteration.");
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["colorImage"] = bundle.ColorImage,
                ["depthImage"] = bundle.DepthImage,
                ["colorCalibration"] = bundle.ColorCalibration,
                ["depthCalibration"] = bundle.DepthCalibration,
            };
            return Task.FromResult(outputs);
        }
    }
}
