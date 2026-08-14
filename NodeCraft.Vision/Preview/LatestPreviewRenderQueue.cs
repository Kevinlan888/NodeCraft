using System;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Preview
{
    internal sealed class LatestPreviewRenderQueue : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<FlowImage, PreviewRenderResult> _render;
        private readonly Func<long, PreviewRenderResult, Task> _applyAsync;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Action<long> _workerIdleCallback;
        private PendingWork _pending;
        private Task _workerTask;
        private long _nextWorkerGeneration;
        private long _workerGeneration;
        private long _latestVersion;
        private bool _disposed;

        internal LatestPreviewRenderQueue(
            Func<FlowImage, PreviewRenderResult> render,
            Func<long, PreviewRenderResult, Task> applyAsync)
            : this(render, applyAsync, null)
        {
        }

        internal LatestPreviewRenderQueue(
            Func<FlowImage, PreviewRenderResult> render,
            Func<long, PreviewRenderResult, Task> applyAsync,
            Action<long> workerIdleCallback)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _applyAsync = applyAsync ?? throw new ArgumentNullException(nameof(applyAsync));
            _workerIdleCallback = workerIdleCallback;
        }

        internal LatestPreviewRenderQueue(
            Func<FlowImage, PreviewRenderResult> render,
            Func<PreviewRenderResult, Task> applyAsync)
            : this(render, (version, result) => applyAsync(result), null)
        {
        }

        internal long LatestVersion
        {
            get
            {
                lock (_gate)
                {
                    return _latestVersion;
                }
            }
        }

        internal void Submit(FlowImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            lock (_gate)
            {
                ThrowIfDisposedNoLock();
                var version = ++_latestVersion;
                _pending = new PendingWork(version, image);
                if (_workerTask == null)
                {
                    var generation = ++_nextWorkerGeneration;
                    _workerGeneration = generation;
                    _workerTask = Task.Run(() => ProcessAsync(generation));
                }
            }
        }

        internal Task SubmitAsync(FlowImage image)
        {
            Submit(image);
            return DrainAsync();
        }

        internal Task DrainAsync()
        {
            lock (_gate)
            {
                return _workerTask ?? Task.CompletedTask;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pending = null;
                _cancellation.Cancel();
            }

            // The worker may still be observing the token. It exits through its finally block;
            // keeping the source alive avoids racing Dispose with token access.
        }

        private async Task ProcessAsync(long generation)
        {
            try
            {
                while (true)
                {
                    PendingWork work = null;
                    var workerBecameIdle = false;
                    lock (_gate)
                    {
                        if (_cancellation.IsCancellationRequested || _pending == null)
                        {
                            workerBecameIdle = OwnsWorkerNoLock(generation)
                                && !_cancellation.IsCancellationRequested
                                && _pending == null;
                            if (OwnsWorkerNoLock(generation))
                            {
                                _workerTask = null;
                                _workerGeneration = 0;
                            }
                        }
                        else
                        {
                            work = _pending;
                            _pending = null;
                        }
                    }

                    if (work == null)
                    {
                        if (workerBecameIdle)
                        {
                            // This callback is used only by deterministic tests to hold the
                            // handoff window after ownership has been released.
                            _workerIdleCallback?.Invoke(generation);
                        }

                        return;
                    }

                    _cancellation.Token.ThrowIfCancellationRequested();
                    var result = await Task.Run(
                        () => _render(work.Image),
                        _cancellation.Token).ConfigureAwait(false);

                    var isLatest = false;
                    lock (_gate)
                    {
                        isLatest = !_cancellation.IsCancellationRequested
                            && work.Version == _latestVersion;
                    }

                    if (isLatest)
                    {
                        await _applyAsync(work.Version, result).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (OwnsWorkerNoLock(generation))
                    {
                        _workerTask = null;
                        _workerGeneration = 0;
                    }
                }
            }
        }

        private bool OwnsWorkerNoLock(long generation)
        {
            return _workerGeneration == generation;
        }

        private void ThrowIfDisposedNoLock()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LatestPreviewRenderQueue));
            }
        }

        private sealed class PendingWork
        {
            internal PendingWork(long version, FlowImage image)
            {
                Version = version;
                Image = image;
            }

            internal long Version { get; }

            internal FlowImage Image { get; }
        }
    }
}
