using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Node.Algorithm.Imaging;
using Node.Algorithm.Interop;
using NodeCraft.Flow;

internal static partial class Program
{
    private static async Task RunAlgorithmNativeSmokeTestAsync()
    {
        await RunAsync("Waybill native smoke test is opt-in", async () =>
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("NODECRAFT_WAYBILL_NATIVE_SMOKE"),
                    "1",
                    StringComparison.Ordinal))
            {
                return true;
            }

            var repositoryRoot = FindRepositoryRoot();
            var algorithmRoot = Path.GetFullPath(
                Path.Combine(repositoryRoot, "..", "waybill-recongize"));
            var packageRoot = GetEnvironmentPath(
                "WAYBILL_PLUGIN_PACKAGE_ROOT",
                Path.Combine(repositoryRoot, "artifacts", "Plugins", "Node.Algorithm"));
            var pluginAssemblyPath = Path.Combine(packageRoot, "Node.Algorithm.dll");
            var modelPath = GetEnvironmentPath(
                "WAYBILL_MODEL_PATH",
                Path.Combine(packageRoot, "models", "baseline-2-960.onnx"));
            var imagePath = GetEnvironmentPath(
                "WAYBILL_IMAGE_PATH",
                Path.Combine(
                    algorithmRoot,
                    "tests",
                    "fixtures",
                    "images",
                    "positive.jpg"));

            if (!File.Exists(pluginAssemblyPath)
                || !File.Exists(modelPath)
                || !File.Exists(imagePath))
            {
                return false;
            }

            var image = await RunOnStaValueAsync(() => LoadFlowImage(imagePath))
                .ConfigureAwait(false);
            var options = new WaybillInferenceOptions
            {
                Confidence = 0.35f,
                Iou = 0.50f,
                MinMaskAreaRatio = 0.0001f,
                MaxDetections = 100,
                NumThreads = 0,
            };

            using var session = new WaybillNativeSessionFactory().Create(
                pluginAssemblyPath,
                modelPath,
                options);
            var result = session.Process(image, CancellationToken.None);
            var annotated = WaybillOverlayRenderer.Render(image, result.Detections);

            return result.Width == image.Width
                && result.Height == image.Height
                && result.Detections.Count > 0
                && result.Detections.Count <= options.MaxDetections
                && annotated.Width == image.Width
                && annotated.Height == image.Height
                && annotated.PixelFormat == image.PixelFormat
                && annotated.Buffer.Length == image.Buffer.Length;
        });
    }

    private static FlowImage LoadFlowImage(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames == null || decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("Image does not contain a frame.");
        }

        var frame = decoder.Frames[0];
        BitmapSource bitmap;
        FlowPixelFormat pixelFormat;
        var bytesPerPixel = 3;
        if (frame.Format == PixelFormats.Gray8)
        {
            bitmap = frame;
            pixelFormat = FlowPixelFormat.Mono8;
            bytesPerPixel = 1;
        }
        else
        {
            var converted = new FormatConvertedBitmap(
                frame,
                PixelFormats.Bgr24,
                null,
                0);
            converted.Freeze();
            bitmap = converted;
            pixelFormat = FlowPixelFormat.Bgr24;
        }

        bitmap.Freeze();
        var stride = checked(bitmap.PixelWidth * bytesPerPixel);
        var buffer = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(buffer, stride, 0);
        return FlowImage.CopyFrom(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            stride,
            pixelFormat,
            FlowImageKind.Color,
            buffer,
            1,
            0,
            DateTimeOffset.UtcNow);
    }

    private static string GetEnvironmentPath(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : Path.GetFullPath(value);
    }
}
