using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Node.Algorithm.Imaging;
using Node.Algorithm.Models;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RunAlgorithmOverlayTests()
    {
        Run("Waybill overlay renders BGR quadrilaterals and preserves FlowImage metadata", () =>
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var image = FlowImage.CopyFrom(
                8,
                6,
                24,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[24 * 6],
                12,
                34,
                capturedAt);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(1, 1, 6, 4));
            var pixel = Pixel(output, 1, 1, 3);

            return pixel.SequenceEqual(new byte[] { 0, 255, 255 })
                && output.Width == image.Width
                && output.Height == image.Height
                && output.Stride == image.Stride
                && output.PixelFormat == image.PixelFormat
                && output.Kind == image.Kind
                && output.FrameId == image.FrameId
                && output.DeviceTimestamp == image.DeviceTimestamp
                && output.CapturedAtUtc == capturedAt;
        });

        Run("Waybill overlay uses a black outline and bright yellow core", () =>
        {
            var image = FlowImage.CopyFrom(
                12,
                12,
                36,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                Enumerable.Repeat((byte)255, 36 * 12).ToArray(),
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(0, 4, 11, 4));

            return Pixel(output, 5, 1, 3).SequenceEqual(new byte[] { 0, 0, 0 })
                && Pixel(output, 5, 4, 3).SequenceEqual(new byte[] { 0, 255, 255 });
        });

        Run("Waybill overlay maps bright yellow to RGB channel order", () =>
        {
            var image = FlowImage.CopyFrom(
                4,
                4,
                12,
                FlowPixelFormat.Rgb24,
                FlowImageKind.Color,
                new byte[48],
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(0, 0, 3, 3));
            return Pixel(output, 0, 0, 3).SequenceEqual(new byte[] { 255, 255, 0 });
        });

        Run("Waybill overlay renders white lines for Mono8", () =>
        {
            var image = FlowImage.CopyFrom(
                4,
                4,
                4,
                FlowPixelFormat.Mono8,
                FlowImageKind.Color,
                new byte[16],
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(0, 0, 3, 3));
            return output.Buffer.Span[0] == 255
                && output.Buffer.Span[1] == 255
                && output.Buffer.Span[4] == 255;
        });

        Run("Waybill overlay uses a black outline and white core for Mono8", () =>
        {
            var image = FlowImage.CopyFrom(
                12,
                12,
                12,
                FlowPixelFormat.Mono8,
                FlowImageKind.Color,
                Enumerable.Repeat((byte)255, 12 * 12).ToArray(),
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(0, 4, 11, 4));

            return output.Buffer.Span[1 * output.Stride + 5] == 0
                && output.Buffer.Span[4 * output.Stride + 5] == 255;
        });

        Run("Waybill overlay preserves padded row bytes", () =>
        {
            var bytes = Enumerable.Repeat((byte)7, 12 * 2).ToArray();
            var image = FlowImage.CopyFrom(
                3,
                2,
                12,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                bytes,
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, CreateDetection(0, 0, 2, 1));
            return output.Buffer.Span[9] == 7
                && output.Buffer.Span[10] == 7
                && output.Buffer.Span[11] == 7
                && output.Buffer.Span[21] == 7
                && output.Buffer.Span[22] == 7
                && output.Buffer.Span[23] == 7;
        });

        Run("Waybill overlay clips out-of-bounds geometry", () =>
        {
            var image = FlowImage.CopyFrom(
                12,
                12,
                36,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                new byte[36 * 12],
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(
                image,
                new[]
                {
                    new WaybillDetection(
                        0.5f,
                        new[]
                        {
                            new WaybillPoint(-10, -10),
                            new WaybillPoint(20, -20),
                            new WaybillPoint(20, 20),
                            new WaybillPoint(-20, 20),
                        },
                        WaybillGeometryMethod.RotatedRectFallback,
                        0.2f),
                });
            return Pixel(output, 0, 0, 3).SequenceEqual(new byte[] { 0, 255, 255 })
                && Pixel(output, 11, 11, 3).SequenceEqual(new byte[] { 0, 255, 255 });
        });

        Run("Waybill overlay returns an unchanged copy for empty detections", () =>
        {
            var bytes = Enumerable.Range(0, 48).Select(value => (byte)value).ToArray();
            var image = FlowImage.CopyFrom(
                4,
                4,
                12,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                bytes,
                1,
                2,
                DateTimeOffset.UtcNow);
            var output = WaybillOverlayRenderer.Render(image, Array.Empty<WaybillDetection>());
            return !ReferenceEquals(image, output)
                && output.Buffer.Span.SequenceEqual(image.Buffer.Span);
        });

        Run("Waybill overlay rejects Depth16 images", () =>
        {
            var image = FlowImage.CopyFrom(
                2,
                2,
                4,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                new byte[8],
                1,
                2,
                DateTimeOffset.UtcNow);
            return ThrowsAlgorithm<InvalidDataException>(
                () => WaybillOverlayRenderer.Render(image, Array.Empty<WaybillDetection>()));
        });
    }

    private static IReadOnlyList<WaybillDetection> CreateDetection(int left, int top, int right, int bottom)
    {
        return new[]
        {
            new WaybillDetection(
                0.9f,
                new[]
                {
                    new WaybillPoint(left, top),
                    new WaybillPoint(right, top),
                    new WaybillPoint(right, bottom),
                    new WaybillPoint(left, bottom),
                },
                WaybillGeometryMethod.ContourQuad,
                0.8f),
        };
    }

    private static byte[] Pixel(FlowImage image, int x, int y, int bytesPerPixel)
    {
        return image.Buffer.Span
            .Slice(y * image.Stride + x * bytesPerPixel, bytesPerPixel)
            .ToArray();
    }
}
