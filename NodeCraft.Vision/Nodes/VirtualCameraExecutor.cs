using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VirtualCameraExecutor :
        IFlowNodeExecutor,
        IFlowNodeSessionLifecycle,
        IFlowNodeSessionInitializer,
        IFlowIterationSource
    {
        private readonly IVirtualCameraImageLoader _imageLoader;
        private List<VirtualCameraEntry> _entries
            = new List<VirtualCameraEntry>();
        private string _imageDirectory;
        private int _index = -1;
        private FlowImage _current;
        private VirtualCameraEntry _currentEntry;
        private bool _skipErrorImages;
        private VirtualCameraLoadMode _loadMode;
        private bool _starting;
        private bool _started;

        internal VirtualCameraExecutor(IVirtualCameraImageLoader imageLoader = null)
        {
            _imageLoader = imageLoader ?? new VirtualCameraImageLoader();
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
                _entries = preparedEntries;
                _index = -1;
                _current = null;
                _currentEntry = null;
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

        public Task PrepareIterationAsync(
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
            if (_loadMode == VirtualCameraLoadMode.Dynamic)
            {
                return PrepareDynamicIteration(context, cancellationToken);
            }

            _index = (_index + 1) % _entries.Count;
            var entry = _entries[_index];
            _currentEntry = entry;
            _current = entry.PreloadedTemplate.CreateFrame(
                (ulong)entry.Ordinal,
                0,
                DateTimeOffset.UtcNow);
            return Task.CompletedTask;
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

        private Task PrepareDynamicIteration(
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
                    _current = template.CreateFrame(
                        (ulong)entry.Ordinal,
                        0,
                        DateTimeOffset.UtcNow);
                    _currentEntry = entry;
                    _index = nextIndex;
                    return Task.CompletedTask;
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
            _started = false;
        }
    }
}
