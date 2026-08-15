using System;
using System.Threading.Tasks;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunSessionNodeInitializationTestsAsync()
    {
        await RunAsync("session value store is write-once and read-only after sealing", async () =>
        {
            var store = new SessionValueStore();
            var view = store.CreateReadOnlyView();
            var value = new object();

            store.SetPortValue("camera", 0, value);
            var firstRead = view.TryGetPortValue("camera", 0, out var first)
                && ReferenceEquals(first, value);
            var duplicateRejected = Throws<InvalidOperationException>(
                () => store.SetPortValue("camera", 0, new object()));

            store.Seal();
            var sealedRejected = Throws<InvalidOperationException>(
                () => store.SetPortValue("camera", 1, new object()));
            store.Clear();

            await Task.CompletedTask;
            return firstRead
                && duplicateRejected
                && sealedRejected
                && !view.TryGetPortValue("camera", 0, out _);
        });
    }
}
