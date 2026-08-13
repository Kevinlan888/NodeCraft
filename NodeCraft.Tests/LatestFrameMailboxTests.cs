using System;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft.Vision.StereoCamera.Camera;

internal static partial class Program
{
    private static async Task RunLatestFrameMailboxTestsAsync()
    {
        await RunAsync("latest-frame mailbox drops an unconsumed older frame", async () =>
        {
            var mailbox = new LatestFrameMailbox<string>();
            mailbox.Publish(1, "old");
            mailbox.Publish(2, "latest");

            var item = await mailbox.WaitForNextAsync(0, CancellationToken.None);
            var duplicate = mailbox.TryTakeAfter(item.Sequence, out _);
            return item.Sequence == 2 && item.Value == "latest" && !duplicate;
        });

        await RunAsync("latest-frame mailbox never returns a consumed sequence twice", async () =>
        {
            var mailbox = new LatestFrameMailbox<string>();
            mailbox.Publish(7, "frame");
            var first = await mailbox.WaitForNextAsync(0, CancellationToken.None);
            var secondTask = mailbox.WaitForNextAsync(first.Sequence, CancellationToken.None);
            mailbox.Publish(8, "next");
            var second = await secondTask;
            return first.Sequence == 7 && second.Sequence == 8;
        });

        await RunAsync("latest-frame mailbox wakes a waiter for a later sequence", async () =>
        {
            var mailbox = new LatestFrameMailbox<int>();
            var waiter = mailbox.WaitForNextAsync(10, CancellationToken.None);
            mailbox.Publish(10, 10);
            var stillWaiting = !waiter.IsCompleted;
            mailbox.Publish(11, 11);
            var item = await waiter;
            return stillWaiting && item.Sequence == 11 && item.Value == 11;
        });

        await RunAsync("latest-frame mailbox fault clears values and faults current and future waiters", async () =>
        {
            var mailbox = new LatestFrameMailbox<int>();
            var waiter = mailbox.WaitForNextAsync(1, CancellationToken.None);
            var failure = new InvalidOperationException("camera disconnected");
            mailbox.Publish(1, 1);
            mailbox.Fault(failure);

            var currentFaulted = false;
            try
            {
                await waiter;
            }
            catch (InvalidOperationException exception)
            {
                currentFaulted = ReferenceEquals(exception, failure);
            }

            var futureFaulted = false;
            try
            {
                await mailbox.WaitForNextAsync(0, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                futureFaulted = ReferenceEquals(exception, failure);
            }

            return currentFaulted && futureFaulted && !mailbox.TryTakeAfter(0, out _);
        });

        await RunAsync("latest-frame mailbox completion and cancellation wake waiters", async () =>
        {
            var completed = new LatestFrameMailbox<int>();
            var completedWaiter = completed.WaitForNextAsync(0, CancellationToken.None);
            completed.Complete();
            var completionCancelled = false;
            try
            {
                await completedWaiter;
            }
            catch (OperationCanceledException)
            {
                completionCancelled = true;
            }

            var cancelled = new LatestFrameMailbox<int>();
            using var cancellation = new CancellationTokenSource();
            var cancelledWaiter = cancelled.WaitForNextAsync(0, cancellation.Token);
            cancellation.Cancel();
            var waiterCancelled = false;
            try
            {
                await cancelledWaiter;
            }
            catch (OperationCanceledException)
            {
                waiterCancelled = true;
            }

            return completionCancelled && waiterCancelled;
        });
    }
}
