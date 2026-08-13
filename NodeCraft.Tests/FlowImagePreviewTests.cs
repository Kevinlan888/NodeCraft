using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Preview;

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

        Run("FlowImage preview view uses DynamicResource theme keys and unload cleanup", () =>
        {
            var xaml = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Views",
                "FlowImagePreviewView.xaml"));
            var code = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Views",
                "FlowImagePreviewView.xaml.cs"));
            return xaml.Contains("DynamicResource", StringComparison.Ordinal)
                && code.Contains("Unloaded += FlowImagePreviewView_Unloaded", StringComparison.Ordinal)
                && code.Contains("LatestPreviewRenderQueue", StringComparison.Ordinal)
                && code.Contains("_renderQueue.Dispose()", StringComparison.Ordinal);
        });
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
        var calibration = new CameraCalibration(
            width,
            height,
            new double[9],
            new double[12],
            new double[16],
            false);
        return FlowImage.CopyFrom(
            width,
            height,
            stride,
            pixelFormat,
            kind,
            buffer,
            frameId,
            frameId,
            DateTimeOffset.UtcNow,
            calibration);
    }

    private static byte[] CopyGray8Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth, 0);
        return pixels;
    }
}
