using System;
using System.Linq;
using System.Runtime.InteropServices;
using NodeCraft.Flow;

internal static partial class Program
{
    private static void RunVisualContractTests()
    {
        Run("camera calibration defensively copies its matrices", () =>
        {
            var intrinsic = Enumerable.Range(1, 9).Select(value => (double)value).ToArray();
            var calibration = new CameraCalibration(
                640,
                480,
                intrinsic,
                new double[12],
                new double[16],
                isLeftReference: false);

            intrinsic[0] = 999;
            return calibration.Intrinsic.Span[0] == 1
                && calibration.ImageWidth == 640
                && calibration.ImageHeight == 480
                && !calibration.IsLeftReference;
        });

        Run("FlowImage does not own camera calibration", () =>
        {
            var image = FlowImage.CopyFrom(
                2,
                1,
                2,
                FlowPixelFormat.Mono8,
                FlowImageKind.Color,
                new byte[] { 7, 8 },
                1,
                2,
                DateTimeOffset.UtcNow);
            return typeof(FlowImage).GetProperty("Calibration") == null
                && image.Width == 2
                && image.Buffer.Span.SequenceEqual(new byte[] { 7, 8 });
        });

        Run("FlowImage copy and ownership factories have distinct copy behavior", () =>
        {
            var copiedSource = new byte[] { 1, 2, 3, 4, 5, 6 };
            var copied = FlowImage.CopyFrom(
                2,
                1,
                6,
                FlowPixelFormat.Bgr24,
                FlowImageKind.Color,
                copiedSource,
                7,
                8,
                DateTimeOffset.UnixEpoch);
            copiedSource[0] = 42;

            var ownedSource = new byte[] { 7, 8, 9, 10 };
            var owned = FlowImage.FromOwnedBuffer(
                2,
                1,
                4,
                FlowPixelFormat.Depth16,
                FlowImageKind.Depth,
                ownedSource,
                9,
                10,
                DateTimeOffset.UnixEpoch);
            MemoryMarshal.TryGetArray(owned.Buffer, out var ownedSegment);

            return copied.Buffer.Span[0] == 1
                && ReferenceEquals(ownedSource, ownedSegment.Array)
                && FlowDataType.Image.Key == "image"
                && FlowDataType.CameraCalibration.Key == "camera-calibration";
        });

        Run("FlowImage rejects an invalid stride and buffer length", () =>
        {
            try
            {
                FlowImage.CopyFrom(
                    2,
                    1,
                    3,
                    FlowPixelFormat.Bgr24,
                    FlowImageKind.Color,
                    new byte[3],
                    1,
                    2,
                    DateTimeOffset.UnixEpoch);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        });

        Run("CameraCalibration rejects matrix lengths other than the SDK contract", () =>
        {
            try
            {
                new CameraCalibration(
                    2,
                    1,
                    new double[8],
                    new double[12],
                    new double[16],
                    isLeftReference: false);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        });
    }

    private static CameraCalibration CreateTestCalibration(int width, int height)
    {
        return new CameraCalibration(
            width,
            height,
            new double[9],
            new double[12],
            new double[16],
            isLeftReference: false);
    }
}
