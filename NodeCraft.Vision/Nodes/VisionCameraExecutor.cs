using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VisionCameraExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowIterationSource
    {
        private readonly IVisionCameraDeviceFactory _deviceFactory;
        private readonly ICameraRuntimeScopeFactory _runtimeScopeFactory;
        private readonly IMonotonicClock _clock;
        private readonly VisionCameraCaptureOptions _options;
        private readonly ILogger _logger;
        private VisionCameraCaptureSession _captureSession;
        private FlowImage _currentImage;
        private long _lastSequence;

        internal VisionCameraExecutor(
            IVisionCameraDeviceFactory deviceFactory,
            ICameraRuntimeScopeFactory runtimeScopeFactory,
            IMonotonicClock clock,
            VisionCameraCaptureOptions options,
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
                throw new InvalidOperationException("Vision camera node requires an ipAddress input.");
            }

            _captureSession = new VisionCameraCaptureSession(
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
                catch
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
            _currentImage = null;
            _lastSequence = 0;
            if (session != null)
            {
                await session.StopAsync().ConfigureAwait(false);
            }
        }

        public async Task PrepareIterationAsync(FlowNodeSessionContext context, CancellationToken cancellationToken)
        {
            var session = _captureSession
                ?? throw new InvalidOperationException("Vision camera session has not started.");
            _currentImage = null;
            var item = await session.WaitForNextAsync(_lastSequence, cancellationToken)
                .ConfigureAwait(false);
            _lastSequence = item.Sequence;
            _currentImage = item.Value;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            var image = _currentImage
                ?? throw new InvalidOperationException("Vision camera has no prepared image for this iteration.");
            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["image"] = image,
            };
            return Task.FromResult(outputs);
        }
    }
}
