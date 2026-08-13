using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal sealed class LatestFrameMailbox<T>
    {
        private readonly object _gate = new object();
        private readonly List<Waiter> _waiters = new List<Waiter>();
        private LatestFrame<T> _pending;
        private Exception _terminalFault;
        private bool _completed;

        internal void Publish(long sequence, T value)
        {
            List<Waiter> readyWaiters;
            LatestFrame<T> published;
            lock (_gate)
            {
                if (_completed || _terminalFault != null)
                {
                    return;
                }

                published = new LatestFrame<T>(sequence, value);
                _pending = published;
                readyWaiters = TakeReadyWaitersNoLock(sequence);
                if (readyWaiters.Count > 0)
                {
                    _pending = null;
                }
            }

            CompleteWaitersWithResult(readyWaiters, published);
        }

        internal bool TryTakeAfter(long afterSequence, out LatestFrame<T> item)
        {
            lock (_gate)
            {
                if (_pending != null && _pending.Sequence > afterSequence)
                {
                    item = _pending;
                    _pending = null;
                    return true;
                }
            }

            item = null;
            return false;
        }

        internal Task<LatestFrame<T>> WaitForNextAsync(
            long afterSequence,
            CancellationToken cancellationToken)
        {
            Waiter waiter = null;
            LatestFrame<T> readyItem = null;
            Exception terminalFault = null;
            var completed = false;

            lock (_gate)
            {
                if (_pending != null && _pending.Sequence > afterSequence)
                {
                    readyItem = _pending;
                    _pending = null;
                }
                else if (_terminalFault != null)
                {
                    terminalFault = _terminalFault;
                }
                else if (_completed)
                {
                    completed = true;
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    completed = true;
                }
                else
                {
                    waiter = new Waiter(afterSequence);
                    _waiters.Add(waiter);
                }
            }

            if (readyItem != null)
            {
                return Task.FromResult(readyItem);
            }

            if (terminalFault != null)
            {
                return Task.FromException<LatestFrame<T>>(terminalFault);
            }

            if (completed)
            {
                return Task.FromCanceled<LatestFrame<T>>(new CancellationToken(true));
            }

            waiter.Registration = cancellationToken.Register(
                () => CancelWaiter(waiter, cancellationToken));
            return waiter.Completion.Task;
        }

        internal void Fault(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            List<Waiter> waiters;
            lock (_gate)
            {
                if (_completed || _terminalFault != null)
                {
                    return;
                }

                _pending = null;
                _terminalFault = exception;
                waiters = TakeAllWaitersNoLock();
            }

            CompleteWaitersWithException(waiters, exception);
        }

        internal void Complete()
        {
            List<Waiter> waiters;
            lock (_gate)
            {
                if (_completed || _terminalFault != null)
                {
                    return;
                }

                _pending = null;
                _completed = true;
                waiters = TakeAllWaitersNoLock();
            }

            CompleteWaitersAsCanceled(waiters);
        }

        private List<Waiter> TakeReadyWaitersNoLock(long sequence)
        {
            var ready = _waiters
                .Where(waiter => waiter.AfterSequence < sequence)
                .ToList();
            foreach (var waiter in ready)
            {
                _waiters.Remove(waiter);
            }

            return ready;
        }

        private List<Waiter> TakeAllWaitersNoLock()
        {
            var waiters = _waiters.ToList();
            _waiters.Clear();
            return waiters;
        }

        private void CancelWaiter(Waiter waiter, CancellationToken cancellationToken)
        {
            var removed = false;
            lock (_gate)
            {
                removed = _waiters.Remove(waiter);
            }

            if (removed)
            {
                waiter.Completion.TrySetCanceled(cancellationToken);
            }
        }

        private static void CompleteWaitersWithResult(
            IEnumerable<Waiter> waiters,
            LatestFrame<T> item)
        {
            foreach (var waiter in waiters)
            {
                waiter.Registration.Dispose();
                waiter.Completion.TrySetResult(item);
            }
        }

        private static void CompleteWaitersWithException(
            IEnumerable<Waiter> waiters,
            Exception exception)
        {
            foreach (var waiter in waiters)
            {
                waiter.Registration.Dispose();
                waiter.Completion.TrySetException(exception);
            }
        }

        private static void CompleteWaitersAsCanceled(IEnumerable<Waiter> waiters)
        {
            foreach (var waiter in waiters)
            {
                waiter.Registration.Dispose();
                waiter.Completion.TrySetCanceled();
            }
        }

        internal sealed class LatestFrame<TValue>
        {
            internal LatestFrame(long sequence, TValue value)
            {
                Sequence = sequence;
                Value = value;
            }

            internal long Sequence { get; }

            internal TValue Value { get; }
        }

        private sealed class Waiter
        {
            internal Waiter(long afterSequence)
            {
                AfterSequence = afterSequence;
                Completion = new TaskCompletionSource<LatestFrame<T>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal long AfterSequence { get; }

            internal TaskCompletionSource<LatestFrame<T>> Completion { get; }

            internal CancellationTokenRegistration Registration { get; set; }
        }
    }
}
