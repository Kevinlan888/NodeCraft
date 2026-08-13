using System;
using System.IO;
using System.Linq;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.Camera;
using NodeCraft.Vision.StereoCamera.VendorInterop;

internal static partial class Program
{
    private static void RunVendorStereoCameraDeviceTests()
    {
        Run("vendor camera maps supported pixel formats and rejects kind mismatches", () =>
        {
            return VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.BGR) == FlowPixelFormat.Bgr24
                && VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.RGB) == FlowPixelFormat.Rgb24
                && VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.Mono8) == FlowPixelFormat.Mono8
                && VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.Depth16, FlowImageKind.Depth) == FlowPixelFormat.Depth16
                && Throws<InvalidDataException>(() =>
                    VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.Depth16, FlowImageKind.Color))
                && Throws<InvalidDataException>(() =>
                    VendorStereoCameraImageHelpers.MapPixelFormat(ScPixelFormat.BGR, FlowImageKind.Depth));
        });

        Run("vendor camera derives and validates image stride", () =>
        {
            return VendorStereoCameraImageHelpers.DeriveStride(4, 2, FlowPixelFormat.Bgr24, 28) == 14
                && Throws<InvalidDataException>(() =>
                    VendorStereoCameraImageHelpers.DeriveStride(4, 2, FlowPixelFormat.Bgr24, 22))
                && Throws<InvalidDataException>(() =>
                    VendorStereoCameraImageHelpers.DeriveStride(4, 2, FlowPixelFormat.Bgr24, 8));
        });

        Run("vendor camera converts calibration arrays without exposing native storage", () =>
        {
            var data = new ScCameraCalibInfo
            {
                IntrinsicImgWidth = 640,
                IntrinsicImgHeight = 480,
                Intrinsic = Enumerable.Range(0, 9).Select(value => (double)value).ToArray(),
                Distortion = Enumerable.Range(0, 12).Select(value => (double)value).ToArray(),
                Extrinsic = Enumerable.Range(0, 16).Select(value => (double)value).ToArray(),
                Reserved = new int[28],
            };
            var calibration = VendorStereoCameraImageHelpers.ToCalibration(data, isLeftReference: false);
            data.Intrinsic[0] = 99;
            return calibration.ImageWidth == 640
                && calibration.Intrinsic.Span[0] == 0
                && !calibration.IsLeftReference;
        });

        Run("vendor camera validates strict dotted-decimal IPv4 literals", () =>
        {
            VendorStereoCameraDeviceFactory.ValidateIpv4("192.168.1.10");
            return Throws<ArgumentException>(() => VendorStereoCameraDeviceFactory.ValidateIpv4("192.168.1"))
                && Throws<ArgumentException>(() => VendorStereoCameraDeviceFactory.ValidateIpv4("192.168.001.10"))
                && Throws<ArgumentException>(() => VendorStereoCameraDeviceFactory.ValidateIpv4(" 192.168.1.10"))
                && Throws<ArgumentException>(() => VendorStereoCameraDeviceFactory.ValidateIpv4("::1"))
                && Throws<ArgumentException>(() => VendorStereoCameraDeviceFactory.ValidateIpv4("192.168.1.256"));
        });

        Run("vendor image adapter uses one managed copy and no bitmap conversion", () =>
        {
            var source = File.ReadAllText(FindRepositoryFile(
                "NodeCraft.Vision.StereoCamera",
                "Camera",
                "VendorStereoCameraDevice.cs"));
            return source.Split(new[] { "Marshal.Copy" }, StringSplitOptions.None).Length - 1 == 1
                && !source.Contains("Bitmap", StringComparison.Ordinal);
        });
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
