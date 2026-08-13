using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeCraft;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunFlowExecutionControllerTestsAsync()
    {
        await RunAsync("flow execution controller runs one iteration and returns to idle", async () =>
        {
            var fixture = CreateIterationFixture();
            var controller = new FlowExecutionController();
            var states = new List<FlowRunState>();
            controller.StateChanged += (_, _) => states.Add(controller.State);
            var callbackCount = 0;

            await controller.RunOnceAsync(
                fixture.Executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            return callbackCount == 1
                && controller.State == FlowRunState.Idle
                && states.Contains(FlowRunState.Starting)
                && states.Contains(FlowRunState.RunningOnce)
                && states.Contains(FlowRunState.Stopping);
        });

        await RunAsync("flow execution controller serializes continuous callbacks", async () =>
        {
            var fixture = CreateIterationFixture();
            var controller = new FlowExecutionController();
            using var cancellation = new CancellationTokenSource();
            var firstCallbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackCount = 0;
            var activeCallbacks = 0;
            var maxActiveCallbacks = 0;

            var runTask = controller.RunContinuouslyAsync(
                fixture.Executor.CreateSession(),
                async (context, iteration, elapsed) =>
                {
                    var active = Interlocked.Increment(ref activeCallbacks);
                    maxActiveCallbacks = Math.Max(maxActiveCallbacks, active);
                    var count = Interlocked.Increment(ref callbackCount);
                    if (count == 1)
                    {
                        firstCallbackEntered.TrySetResult(true);
                        await releaseFirstCallback.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        cancellation.Cancel();
                    }

                    Interlocked.Decrement(ref activeCallbacks);
                },
                cancellation.Token);

            await firstCallbackEntered.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            var callbackWasQueuedWhileBlocked = callbackCount > 1;
            releaseFirstCallback.TrySetResult(true);
            await runTask;

            return !callbackWasQueuedWhileBlocked
                && maxActiveCallbacks == 1
                && controller.State == FlowRunState.Idle;
        });

        await RunAsync("flow execution controller adds the ordinary graph guard delay", async () =>
        {
            var fixture = CreateLifecycleFixture(new List<string>());
            var delayCount = 0;
            var controller = new FlowExecutionController(
                (delay, cancellationToken) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });
            using var cancellation = new CancellationTokenSource();
            var callbackCount = 0;

            await controller.RunContinuouslyAsync(
                fixture.Executor.CreateSession(),
                (context, iteration, elapsed) =>
                {
                    callbackCount++;
                    if (callbackCount >= 3)
                    {
                        cancellation.Cancel();
                    }

                    return Task.CompletedTask;
                },
                cancellation.Token);

            return callbackCount >= 3
                && delayCount >= 2
                && controller.State == FlowRunState.Idle;
        });

        await RunAsync("flow execution controller rejects a second active run and stops once", async () =>
        {
            var fixture = CreateIterationFixture();
            var controller = new FlowExecutionController();
            using var cancellation = new CancellationTokenSource();
            var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var runTask = controller.RunContinuouslyAsync(
                fixture.Executor.CreateSession(),
                async (context, iteration, elapsed) =>
                {
                    callbackEntered.TrySetResult(true);
                    await releaseCallback.Task.ConfigureAwait(false);
                },
                cancellation.Token);

            await callbackEntered.Task;
            var rejected = false;
            try
            {
                await controller.RunOnceAsync(
                    fixture.Executor.CreateSession(),
                    (context, iteration, elapsed) => Task.CompletedTask,
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            var stopTask = controller.StopAsync();
            releaseCallback.TrySetResult(true);
            await stopTask;
            await runTask;
            return rejected && controller.State == FlowRunState.Idle;
        });
    }
}
