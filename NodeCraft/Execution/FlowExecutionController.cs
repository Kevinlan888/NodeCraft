using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Flow;

namespace NodeCraft
{
    internal sealed class FlowExecutionController
    {
        private static readonly TimeSpan OrdinaryGraphGuardDelay = TimeSpan.FromMilliseconds(10);

        private readonly object _gate = new object();
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private FlowRunState _state = FlowRunState.Idle;
        private Exception _lastError;
        private Task _activeTask;
        private CancellationTokenSource _runCancellation;

        public FlowExecutionController(
            Func<TimeSpan, CancellationToken, Task> delayAsync = null)
        {
            _delayAsync = delayAsync ?? Task.Delay;
        }

        public event EventHandler StateChanged;

        public FlowRunState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public Exception LastError
        {
            get
            {
                lock (_gate)
                {
                    return _lastError;
                }
            }
        }

        public Task RunOnceAsync(
            GraphExecutionSession session,
            Func<FlowExecutionContext, long, TimeSpan, Task> resultCallback,
            CancellationToken cancellationToken = default)
        {
            return StartRun(session, resultCallback, continuous: false, cancellationToken);
        }

        public Task RunContinuouslyAsync(
            GraphExecutionSession session,
            Func<FlowExecutionContext, long, TimeSpan, Task> resultCallback,
            CancellationToken cancellationToken = default)
        {
            return StartRun(session, resultCallback, continuous: true, cancellationToken);
        }

        public Task StopAsync()
        {
            Task activeTask;
            CancellationTokenSource runCancellation;
            var raiseStopping = false;

            lock (_gate)
            {
                activeTask = _activeTask;
                runCancellation = _runCancellation;
                if (activeTask == null)
                {
                    return Task.CompletedTask;
                }

                if (_state != FlowRunState.Stopping)
                {
                    _state = FlowRunState.Stopping;
                    raiseStopping = true;
                }
            }

            if (raiseStopping)
            {
                RaiseStateChanged();
            }

            runCancellation?.Cancel();
            return activeTask;
        }

        private Task StartRun(
            GraphExecutionSession session,
            Func<FlowExecutionContext, long, TimeSpan, Task> resultCallback,
            bool continuous,
            CancellationToken cancellationToken)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (resultCallback == null)
            {
                throw new ArgumentNullException(nameof(resultCallback));
            }

            CancellationTokenSource linkedCancellation;
            TaskCompletionSource<object> completion;
            lock (_gate)
            {
                if (_activeTask != null)
                {
                    throw new InvalidOperationException(
                        "A flow execution is already active. Stop it before starting another run.");
                }

                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                completion = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _activeTask = completion.Task;
                _runCancellation = linkedCancellation;
                _lastError = null;
                _state = FlowRunState.Starting;
            }

            RaiseStateChanged();
            _ = Task.Run(
                () => RunCoreAsync(
                    session,
                    resultCallback,
                    continuous,
                    linkedCancellation,
                    completion));
            return completion.Task;
        }

        private async Task RunCoreAsync(
            GraphExecutionSession session,
            Func<FlowExecutionContext, long, TimeSpan, Task> resultCallback,
            bool continuous,
            CancellationTokenSource runCancellation,
            TaskCompletionSource<object> completion)
        {
            Exception primaryException = null;
            Exception cleanupException = null;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                runCancellation.Token.ThrowIfCancellationRequested();
                if (!TryTransitionToRunning(
                        continuous ? FlowRunState.RunningContinuous : FlowRunState.RunningOnce,
                        runCancellation.Token))
                {
                    throw new OperationCanceledException(runCancellation.Token);
                }
                await session.StartAsync(runCancellation.Token).ConfigureAwait(false);

                long iteration = 0;
                while (true)
                {
                    runCancellation.Token.ThrowIfCancellationRequested();
                    var context = await session.ExecuteIterationAsync(runCancellation.Token)
                        .ConfigureAwait(false);
                    iteration++;

                    await resultCallback(context, iteration, stopwatch.Elapsed)
                        .ConfigureAwait(false);

                    if (!continuous)
                    {
                        break;
                    }

                    if (!session.HasIterationSources)
                    {
                        await _delayAsync(OrdinaryGraphGuardDelay, runCancellation.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
            {
                // Cancellation requested by the caller or StopAsync is a normal end of a run.
            }
            catch (Exception exception)
            {
                primaryException = exception;
                SetLastError(exception);
            }
            finally
            {
                TransitionTo(FlowRunState.Stopping);

                try
                {
                    await session.StopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }

                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupException = cleanupException ?? exception;
                }

                if (primaryException == null && cleanupException != null)
                {
                    primaryException = cleanupException;
                    SetLastError(cleanupException);
                }

                TransitionTo(FlowRunState.Idle);
                lock (_gate)
                {
                    _activeTask = null;
                    _runCancellation = null;
                }

                runCancellation.Dispose();

                if (primaryException != null)
                {
                    completion.TrySetException(primaryException);
                }
                else
                {
                    completion.TrySetResult(null);
                }
            }
        }

        private void TransitionTo(FlowRunState state)
        {
            var changed = false;
            lock (_gate)
            {
                if (_state != state)
                {
                    _state = state;
                    changed = true;
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }

        private bool TryTransitionToRunning(FlowRunState state, CancellationToken cancellationToken)
        {
            var changed = false;
            lock (_gate)
            {
                if (cancellationToken.IsCancellationRequested || _state == FlowRunState.Stopping)
                {
                    return false;
                }

                if (_state != state)
                {
                    _state = state;
                    changed = true;
                }
            }

            if (changed)
            {
                RaiseStateChanged();
            }

            return true;
        }

        private void SetLastError(Exception exception)
        {
            lock (_gate)
            {
                _lastError = exception;
            }
        }

        private void RaiseStateChanged()
        {
            var handler = StateChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // A UI observer must not turn a completed graph run into a failed run.
            }
        }
    }
}
