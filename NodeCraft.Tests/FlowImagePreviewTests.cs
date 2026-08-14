using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NodeCraft.Flow;
using NodeCraft.Vision.Nodes;
using NodeCraft.Vision.Preview;
using NodeCraft.Vision.Views;

internal static partial class Program
{
    private static async Task RunFlowImagePreviewTestsAsync()
    {
        Run("Depth16 preview normalizes only nonzero current-frame values", () =>
            RunOnSta(() =>
            {
                var image = CreatePreviewImage(
                    3,
                    1,
                    6,
                    FlowPixelFormat.Depth16,
                    FlowImageKind.Depth,
                    new byte[] { 0, 0, 100, 0, 200, 0 });
                var result = FlowImageBitmapConverter.Convert(image);
                var pixels = CopyGray8Pixels(result.Bitmap);
                return pixels.SequenceEqual(new byte[] { 0, 0, 255 })
                    && result.Bitmap.IsFrozen
                    && result.StatusText.Contains("Depth16", StringComparison.Ordinal);
            }));

        Run("Depth16 preview renders all-zero frames black with an explicit status", () =>
            RunOnSta(() =>
            {
                var image = CreatePreviewImage(
                    2,
                    1,
                    4,
                    FlowPixelFormat.Depth16,
                    FlowImageKind.Depth,
                    new byte[] { 0, 0, 0, 0 });
                var result = FlowImageBitmapConverter.Convert(image);
                return CopyGray8Pixels(result.Bitmap).SequenceEqual(new byte[] { 0, 0 })
                    && result.StatusText.Contains("black", StringComparison.OrdinalIgnoreCase);
            }));

        Run("FlowImage preview maps BGR RGB and Mono8 formats", () =>
            RunOnSta(() =>
            {
                var bgr = FlowImageBitmapConverter.Convert(CreatePreviewImage(
                    1, 1, 3, FlowPixelFormat.Bgr24, FlowImageKind.Color, new byte[] { 1, 2, 3 }));
                var rgb = FlowImageBitmapConverter.Convert(CreatePreviewImage(
                    1, 1, 3, FlowPixelFormat.Rgb24, FlowImageKind.Color, new byte[] { 1, 2, 3 }));
                var mono = FlowImageBitmapConverter.Convert(CreatePreviewImage(
                    1, 1, 1, FlowPixelFormat.Mono8, FlowImageKind.Color, new byte[] { 4 }));
                return bgr.Bitmap.Format.Equals(PixelFormats.Bgr24)
                    && rgb.Bitmap.Format.Equals(PixelFormats.Rgb24)
                    && mono.Bitmap.Format.Equals(PixelFormats.Gray8)
                    && bgr.Bitmap.IsFrozen
                    && rgb.Bitmap.IsFrozen
                    && mono.Bitmap.IsFrozen;
            }));

        await RunAsync("latest preview render queue keeps one pending item and rejects stale completion", async () =>
        {
            var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var applied = new List<long>();
            var renderCount = 0;
            BitmapSource bitmap = null;
            RunOnSta(() =>
            {
                bitmap = BitmapSource.Create(
                    1,
                    1,
                    96,
                    96,
                    PixelFormats.Gray8,
                    null,
                    new byte[] { 0 },
                    1);
                bitmap.Freeze();
                return true;
            });

            using var queue = new LatestPreviewRenderQueue(
                image =>
                {
                    var current = Interlocked.Increment(ref renderCount);
                    if (current == 1)
                    {
                        firstStarted.TrySetResult(true);
                        releaseFirst.Task.GetAwaiter().GetResult();
                    }

                    return new PreviewRenderResult(bitmap, image.FrameId.ToString());
                },
                (version, result) =>
                {
                    applied.Add(long.Parse(result.StatusText));
                    return Task.CompletedTask;
                });

            var first = CreatePreviewImage(1, 1, 1, FlowPixelFormat.Mono8, FlowImageKind.Color, new byte[] { 1 }, 1);
            var second = CreatePreviewImage(1, 1, 1, FlowPixelFormat.Mono8, FlowImageKind.Color, new byte[] { 2 }, 2);
            queue.Submit(first);
            await firstStarted.Task;
            queue.Submit(second);
            releaseFirst.TrySetResult(true);
            await queue.DrainAsync();
            return renderCount == 2 && applied.SequenceEqual(new[] { 2L });
        });

        await RunAsync("latest preview render queue keeps the new worker during an idle handoff", async () =>
        {
            var idleEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var applied = new List<long>();
            var renderCount = 0;
            var first = CreatePreviewImage(1, 1, 1, FlowPixelFormat.Mono8, FlowImageKind.Color, new byte[] { 1 }, 1);
            var second = CreatePreviewImage(1, 1, 1, FlowPixelFormat.Mono8, FlowImageKind.Color, new byte[] { 2 }, 2);

            using var queue = new LatestPreviewRenderQueue(
                image =>
                {
                    var count = Interlocked.Increment(ref renderCount);
                    if (count == 2)
                    {
                        secondStarted.TrySetResult(true);
                        releaseSecond.Task.GetAwaiter().GetResult();
                    }

                    return new PreviewRenderResult(CreatePreviewBitmap(), image.FrameId.ToString());
                },
                (version, result) =>
                {
                    applied.Add(long.Parse(result.StatusText));
                    return Task.CompletedTask;
                },
                generation =>
                {
                    idleEntered.TrySetResult(true);
                    releaseIdle.Task.GetAwaiter().GetResult();
                });

            queue.Submit(first);
            await idleEntered.Task;

            queue.Submit(second);
            await secondStarted.Task;
            releaseIdle.TrySetResult(true);
            await Task.Delay(20);
            var activeWorkerIsTracked = !queue.DrainAsync().IsCompleted;
            releaseSecond.TrySetResult(true);
            await queue.DrainAsync();

            return activeWorkerIsTracked && applied.SequenceEqual(new[] { 1L, 2L });
        });

        Run("FlowImage preview view uses DynamicResource theme keys and unload cleanup", () =>
        {
            var xaml = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Vision",
                "Views",
                "FlowImagePreviewView.xaml"));
            var code = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Vision",
                "Views",
                "FlowImagePreviewView.xaml.cs"));
            return xaml.Contains("DynamicResource", StringComparison.Ordinal)
                && code.Contains("Unloaded += FlowImagePreviewView_Unloaded", StringComparison.Ordinal)
                && code.Contains("LatestPreviewRenderQueue", StringComparison.Ordinal)
                && code.Contains("_renderQueue.Dispose()", StringComparison.Ordinal);
        });

        Run("Vision content factories detach parsed WPF roots", () =>
            RunOnSta(() =>
            {
                var canvas = new FlowCanvas();
                object cameraContent = VisionCameraEditor.CreateContent(
                    canvas,
                    new VisionCameraNodeModel { IpAddress = "192.168.1.10" });
                object previewContent = FlowImagePreviewView.CreateContent(
                    canvas,
                    new FlowImagePreviewNodeModel());

                return cameraContent is FrameworkElement
                    && previewContent is FrameworkElement;
            }));

        Run("preview dispatcher drops a stale result before UI mutation", () =>
        {
            return RunOnSta(() =>
            {
                var node = new FlowImagePreviewNodeModel();
                var view = (FlowImagePreviewView)FlowImagePreviewView.CreateContent(
                    new FlowCanvas(),
                    node);
                var queueField = typeof(FlowImagePreviewView).GetField(
                    "_renderQueue",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (queueField == null)
                {
                    throw new MissingFieldException(nameof(FlowImagePreviewView), "_renderQueue");
                }

                var queue = (LatestPreviewRenderQueue)queueField.GetValue(view);
                var first = CreatePreviewImage(
                    1,
                    1,
                    1,
                    FlowPixelFormat.Mono8,
                    FlowImageKind.Color,
                    new byte[] { 1 },
                    1);
                var second = CreatePreviewImage(
                    1,
                    1,
                    1,
                    FlowPixelFormat.Mono8,
                    FlowImageKind.Color,
                    new byte[] { 2 },
                    2);

                queue.Submit(first);
                var applyMethod = typeof(FlowImagePreviewView).GetMethod(
                    "ApplyRenderResultAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (applyMethod == null)
                {
                    throw new MissingMethodException(nameof(FlowImagePreviewView), "ApplyRenderResultAsync");
                }

                var oldApply = (Task)applyMethod.Invoke(
                    view,
                    new object[]
                    {
                        1L,
                        new PreviewRenderResult(CreatePreviewBitmap(), "old"),
                    });

                queue.Submit(second);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (DateTime.UtcNow < deadline
                    && (!oldApply.IsCompleted || !queue.DrainAsync().IsCompleted || node.StatusText.IndexOf("frame 2", StringComparison.Ordinal) < 0))
                {
                    var frame = new DispatcherFrame();
                    view.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }

                var passed = oldApply.IsCompleted
                    && queue.DrainAsync().IsCompleted
                    && node.StatusText.IndexOf("frame 2", StringComparison.Ordinal) >= 0
                    && node.StatusText.IndexOf("old", StringComparison.Ordinal) < 0;
                queue.Dispose();
                return passed;
            });
        });
    }

    private static BitmapSource CreatePreviewBitmap()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Gray8,
            null,
            new byte[] { 0 },
            1);
        bitmap.Freeze();
        return bitmap;
    }

    private static FlowImage CreatePreviewImage(
        int width,
        int height,
        int stride,
        FlowPixelFormat pixelFormat,
        FlowImageKind kind,
        byte[] buffer,
        ulong frameId = 1)
    {
        return FlowImage.CopyFrom(
            width,
            height,
            stride,
            pixelFormat,
            kind,
            buffer,
            frameId,
            frameId,
            DateTimeOffset.UtcNow);
    }

    private static byte[] CopyGray8Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth, 0);
        return pixels;
    }
}
