using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VirtualCameraExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowNodeSessionInitializer,
        IFlowIterationSource
    {
        private readonly IVirtualCameraImageLoader _imageLoader;
        private readonly IMonotonicClock _clock;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly Func<DateTimeOffset> _utcNow;
        private List<VirtualCameraEntry> _entries
            = new List<VirtualCameraEntry>();
        private string _imageDirectory;
        private int _index = -1;
        private FlowImage _current;
        private VirtualCameraEntry _currentEntry;
        private bool _skipErrorImages;
        private VirtualCameraLoadMode _loadMode;
        private TimeSpan _framePeriod;
        private TimeSpan _sessionClockOrigin;
        private TimeSpan _nextFrameDue;
        private ulong _nextFrameId;
        private bool _starting;
        private bool _started;

        internal VirtualCameraExecutor(
            IVirtualCameraImageLoader imageLoader = null,
            IMonotonicClock clock = null,
            Func<TimeSpan, CancellationToken, Task> delayAsync = null,
            Func<DateTimeOffset> utcNow = null)
        {
            _imageLoader = imageLoader ?? new VirtualCameraImageLoader();
            _clock = clock ?? new SystemMonotonicClock();
            _delayAsync = delayAsync ?? Task.Delay;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task StartSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            if (_started)
            {
                return;
            }

            if (_starting)
            {
                throw new InvalidOperationException(
                    "VirtualCamera session already has a start operation in progress.");
            }

            _starting = true;
            try
            {
                var sourcePath = ReadInput<string>(context, "sourcePath", "<empty>");
                var sourceLabel = GetSourceLabel(sourcePath);
                var loadMode = ReadInput<VirtualCameraLoadMode>(context, "loadMode", sourceLabel);
                var maxPreloadedImages = ReadInput<int>(context, "maxPreloadedImages", sourceLabel);
                var maxPreloadedBytes = ReadInput<long>(context, "maxPreloadedBytes", sourceLabel);
                var skipErrorImages = ReadInput<bool>(context, "skipErrorImages", sourceLabel);
                var frameRate = ReadFrameRateOrDefault(context, sourceLabel);
                var framePeriod = TimeSpan.FromSeconds(1.0 / frameRate);

                if (!Enum.IsDefined(typeof(VirtualCameraLoadMode), loadMode))
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{sourceLabel}' has unsupported load mode value '{(int)loadMode}'.");
                }

                if (loadMode == VirtualCameraLoadMode.Dynamic
                    && VirtualCameraSourceResolver.IsBuiltinUri(sourcePath))
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{sourceLabel}' cannot use Dynamic load mode.");
                }

                if (loadMode == VirtualCameraLoadMode.Preload
                    && maxPreloadedImages <= 0)
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{sourceLabel}' requires MaxPreloadedImages > 0.");
                }

                if (loadMode == VirtualCameraLoadMode.Preload
                    && maxPreloadedBytes <= 0)
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{sourceLabel}' requires MaxPreloadedBytes > 0.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var source = VirtualCameraSourceResolver.Resolve(sourcePath);
                cancellationToken.ThrowIfCancellationRequested();

                _imageDirectory = source.ImageDirectory;
                _skipErrorImages = skipErrorImages;
                _loadMode = loadMode;

                List<VirtualCameraEntry> preparedEntries;
                if (loadMode == VirtualCameraLoadMode.Preload)
                {
                    preparedEntries = PreloadEntries(
                        source,
                        maxPreloadedImages,
                        maxPreloadedBytes,
                        cancellationToken);
                }
                else
                {
                    preparedEntries = source.Entries.ToList();
                }

                cancellationToken.ThrowIfCancellationRequested();
                var clockOrigin = _clock.Now;
                var firstFrameDue = AddFramePeriodChecked(
                    clockOrigin,
                    framePeriod,
                    sourceLabel);
                _entries = preparedEntries;
                _index = -1;
                _current = null;
                _currentEntry = null;
                _framePeriod = framePeriod;
                _sessionClockOrigin = clockOrigin;
                _nextFrameDue = firstFrameDue;
                _nextFrameId = 0;
                _started = true;
            }
            catch
            {
                ClearSessionState();
                throw;
            }
            finally
            {
                _starting = false;
            }
        }

        public Task StopSessionAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            ClearSessionState();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, object>> InitializeSessionAsync(
            FlowNodeSessionContext context,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted(context);

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["imageDirectory"] = _imageDirectory,
            };
            return Task.FromResult(outputs);
        }

        public async Task PrepareIterationAsync(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted(context);
            if (_entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no readable images.");
            }

            _current = null;
            _currentEntry = null;
            var frameStart = await WaitForFrameStartAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            VirtualCameraEntry entry;
            VirtualCameraImageTemplate template;
            int nextIndex;
            if (_loadMode == VirtualCameraLoadMode.Dynamic)
            {
                var candidate = PrepareDynamicCandidate(context, cancellationToken);
                entry = candidate.Entry;
                template = candidate.Template;
                nextIndex = candidate.Index;
            }
            else
            {
                nextIndex = (_index + 1) % _entries.Count;
                entry = _entries[nextIndex];
                template = entry.PreloadedTemplate
                    ?? throw new InvalidOperationException(
                        $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no preloaded image for '{entry.Path}'.");
            }

            var sourceLabel = GetSourceLabel(context.Node);
            var frameId = _nextFrameId;
            var followingFrameId = IncrementFrameIdChecked(
                frameId,
                sourceLabel);
            var followingFrameDue = AddFramePeriodChecked(
                frameStart,
                _framePeriod,
                sourceLabel);
            var deviceTimestamp = GetDeviceTimestampMicroseconds(frameStart, context);
            var capturedAtUtc = _utcNow();
            var image = template.CreateFrame(frameId, deviceTimestamp, capturedAtUtc);
            cancellationToken.ThrowIfCancellationRequested();

            _current = image;
            _currentEntry = entry;
            _index = nextIndex;
            _nextFrameId = followingFrameId;
            _nextFrameDue = followingFrameDue;
        }

        public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
            FlowExecutionContext context,
            WorkflowNode node,
            FlowNodeDefinition definition,
            IReadOnlyDictionary<string, object> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_current == null || _currentEntry == null)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{GetSourceLabel(node)}' has no prepared image for this iteration.");
            }

            IReadOnlyDictionary<string, object> outputs = new Dictionary<string, object>
            {
                ["image"] = _current,
                ["imagePath"] = _currentEntry.Path,
            };
            return Task.FromResult(outputs);
        }

        internal static long AddPreloadedBytesChecked(
            long totalBytes,
            int bufferLength,
            string sourcePath,
            string imagePath)
        {
            try
            {
                return checked(totalBytes + bufferLength);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{sourcePath}' overflowed decoded byte accounting near '{imagePath}'.",
                    exception);
            }
        }

        internal static ulong IncrementFrameIdChecked(
            ulong frameId,
            string sourcePath)
        {
            try
            {
                return checked(frameId + 1);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{sourcePath}' exhausted frame IDs.",
                    exception);
            }
        }

        internal static TimeSpan AddFramePeriodChecked(
            TimeSpan frameStart,
            TimeSpan framePeriod,
            string sourcePath)
        {
            try
            {
                return frameStart + framePeriod;
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{sourcePath}' overflowed its frame deadline.",
                    exception);
            }
        }

        private List<VirtualCameraEntry> PreloadEntries(
            VirtualCameraSource source,
            int maxPreloadedImages,
            long maxPreloadedBytes,
            CancellationToken cancellationToken)
        {
            var validEntries = new List<VirtualCameraEntry>();
            long totalBytes = 0;
            VirtualCameraImageLoadException lastSkippedError = null;
            foreach (var entry in source.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VirtualCameraImageTemplate template;
                try
                {
                    template = entry.PreloadedTemplate ?? _imageLoader.Load(entry.Path);
                    if (template == null)
                    {
                        throw new InvalidOperationException(
                            $"VirtualCamera source '{source.ImageDirectory}' loader returned no image for '{entry.Path}'.");
                    }
                }
                catch (Exception exception) when (
                    _skipErrorImages
                    && VirtualCameraImageLoader.IsSkippableImageLoadError(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastSkippedError = (VirtualCameraImageLoadException)exception;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (validEntries.Count >= maxPreloadedImages)
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{source.ImageDirectory}' exceeds MaxPreloadedImages at '{entry.Path}'.");
                }

                var nextTotalBytes = AddPreloadedBytesChecked(
                    totalBytes,
                    template.BufferLength,
                    source.ImageDirectory,
                    entry.Path);
                if (nextTotalBytes > maxPreloadedBytes)
                {
                    throw new InvalidOperationException(
                        $"VirtualCamera source '{source.ImageDirectory}' exceeds MaxPreloadedBytes at '{entry.Path}'.");
                }

                totalBytes = nextTotalBytes;
                validEntries.Add(new VirtualCameraEntry(entry.Ordinal, entry.Path, template));
            }

            if (validEntries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{source.ImageDirectory}' has no readable images after skipping image load errors.",
                    lastSkippedError);
            }

            return validEntries;
        }

        private (
            VirtualCameraEntry Entry,
            VirtualCameraImageTemplate Template,
            int Index) PrepareDynamicCandidate(
            FlowNodeSessionContext context,
            CancellationToken cancellationToken)
        {
            if (_entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{GetSourceLabel(context.Node)}' has no readable images.");
            }

            while (_entries.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextIndex = (_index + 1) % _entries.Count;
                var entry = _entries[nextIndex];
                try
                {
                    var template = _imageLoader.Load(entry.Path);
                    if (template == null)
                    {
                        throw new InvalidOperationException(
                            $"VirtualCamera source '{_imageDirectory}' loader returned no image for '{entry.Path}'.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return (entry, template, nextIndex);
                }
                catch (Exception exception) when (
                    _skipErrorImages
                    && VirtualCameraImageLoader.IsSkippableImageLoadError(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _entries.RemoveAt(nextIndex);
                    if (_entries.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"VirtualCamera source '{_imageDirectory}' has no readable images after '{entry.Path}'.",
                            exception);
                    }

                    _index = nextIndex - 1;
                }
            }

            throw new InvalidOperationException(
                $"VirtualCamera source '{_imageDirectory}' has no readable images.");
        }

        private void EnsureStarted(FlowNodeSessionContext context)
        {
            if (!_started)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{GetSourceLabel(context?.Node)}' session is not started.");
            }
        }

        private async Task<TimeSpan> WaitForFrameStartAsync(
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = _clock.Now;
                var remaining = _nextFrameDue - now;
                if (remaining <= TimeSpan.Zero)
                {
                    return now;
                }

                await _delayAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
        }

        private ulong GetDeviceTimestampMicroseconds(
            TimeSpan frameStart,
            FlowNodeSessionContext context)
        {
            var elapsed = frameStart - _sessionClockOrigin;
            if (elapsed < TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{GetSourceLabel(context.Node)}' monotonic clock moved backwards.");
            }

            return checked((ulong)(elapsed.Ticks / 10L));
        }

        private static double ReadFrameRateOrDefault(
            FlowNodeSessionContext context,
            string sourceLabel)
        {
            if (context?.Node?.Inputs == null
                || !context.Node.Inputs.TryGetValue("frameRate", out var value))
            {
                return VirtualCameraNodeModel.DefaultFrameRate;
            }

            if (!(value is double frameRate)
                || !VirtualCameraNodeModel.IsValidFrameRate(frameRate))
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{sourceLabel}' has invalid runtime input 'frameRate'.");
            }

            return frameRate;
        }

        private static T ReadInput<T>(
            FlowNodeSessionContext context,
            string key,
            string sourceLabel)
        {
            if (context?.Node?.Inputs == null
                || !context.Node.Inputs.TryGetValue(key, out var value)
                || !(value is T typedValue))
            {
                throw new InvalidOperationException(
                    $"VirtualCamera source '{sourceLabel}' requires runtime input '{key}' of type '{typeof(T).Name}'.");
            }

            return typedValue;
        }

        private static string GetSourceLabel(string sourcePath)
        {
            return string.IsNullOrWhiteSpace(sourcePath) ? "<empty>" : sourcePath;
        }

        private string GetSourceLabel(WorkflowNode node)
        {
            if (!string.IsNullOrWhiteSpace(_imageDirectory))
            {
                return _imageDirectory;
            }

            if (node?.Inputs != null
                && node.Inputs.TryGetValue("sourcePath", out var value)
                && value is string sourcePath)
            {
                return GetSourceLabel(sourcePath);
            }

            return "<empty>";
        }

        private void ClearSessionState()
        {
            _entries = new List<VirtualCameraEntry>();
            _imageDirectory = null;
            _index = -1;
            _current = null;
            _currentEntry = null;
            _skipErrorImages = false;
            _loadMode = VirtualCameraLoadMode.Preload;
            _framePeriod = TimeSpan.Zero;
            _sessionClockOrigin = TimeSpan.Zero;
            _nextFrameDue = TimeSpan.Zero;
            _nextFrameId = 0;
            _started = false;
        }
    }
}
