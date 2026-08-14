using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
                "NodeCraft.Vision",
                "Camera",
                "VendorStereoCameraDevice.cs"));
            return source.Split(new[] { "Marshal.Copy" }, StringSplitOptions.None).Length - 1 == 1
                && !source.Contains("Bitmap", StringComparison.Ordinal);
        });

        Run("vendor camera releases native frame and images after successful frame read", () =>
        {
            using var api = new FakeStereoCameraFrameApi();
            using var device = CreateGrabbingVendorDevice(api);

            var frame = device.TryGetFrame(123);

            return frame.FrameId == 77
                && frame.DeviceTimestamp == 123456
                && frame.Color.Buffer.SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 })
                && frame.Depth.Buffer.SequenceEqual(new byte[] { 7, 0, 8, 0 })
                && api.ReleaseCount(api.FramePointer) == 1
                && api.ReleaseCount(api.ColorPointer) == 1
                && api.ReleaseCount(api.DepthPointer) == 1;
        });

        Run("vendor camera releases acquired frame and color image when depth acquisition fails", () =>
        {
            using var api = new FakeStereoCameraFrameApi
            {
                DepthAcquisitionException = new InvalidOperationException("depth acquisition failed"),
            };
            using var device = CreateGrabbingVendorDevice(api);

            Exception observed = null;
            try
            {
                device.TryGetFrame(123);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            return ReferenceEquals(observed, api.DepthAcquisitionException)
                && api.ReleaseCount(api.FramePointer) == 1
                && api.ReleaseCount(api.ColorPointer) == 1
                && api.ReleaseCount(api.DepthPointer) == 0;
        });

        Run("vendor camera releases acquired frame and images when color conversion fails", () =>
        {
            using var api = new FakeStereoCameraFrameApi
            {
                ColorConversionException = new InvalidDataException("color conversion failed"),
            };
            using var device = CreateGrabbingVendorDevice(api);

            Exception observed = null;
            try
            {
                device.TryGetFrame(123);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            return ReferenceEquals(observed, api.ColorConversionException)
                && api.ReleaseCount(api.FramePointer) == 1
                && api.ReleaseCount(api.ColorPointer) == 1
                && api.ReleaseCount(api.DepthPointer) == 1;
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

    private static VendorStereoCameraDevice CreateGrabbingVendorDevice(FakeStereoCameraFrameApi api)
    {
        var device = new VendorStereoCameraDevice(
            new StereoCameraCameraHandle(api.CameraPointer, api.ReleaseHandle),
            api);
        var grabbing = typeof(VendorStereoCameraDevice)
            .GetField("_grabbing", BindingFlags.Instance | BindingFlags.NonPublic);
        if (grabbing == null)
        {
            throw new MissingFieldException(nameof(VendorStereoCameraDevice), "_grabbing");
        }

        grabbing.SetValue(device, true);
        return device;
    }

    private sealed class FakeStereoCameraFrameApi : IStereoCameraFrameApi, IDisposable
    {
        private readonly IntPtr _colorData;
        private readonly IntPtr _depthData;
        private readonly List<IntPtr> _releasedHandles = new List<IntPtr>();

        internal FakeStereoCameraFrameApi()
        {
            CameraPointer = new IntPtr(0xCA);
            FramePointer = new IntPtr(0xFA);
            ColorPointer = new IntPtr(0xC010);
            DepthPointer = new IntPtr(0xD00D);
            _colorData = Marshal.AllocHGlobal(6);
            _depthData = Marshal.AllocHGlobal(4);
            Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6 }, 0, _colorData, 6);
            Marshal.Copy(new byte[] { 7, 0, 8, 0 }, 0, _depthData, 4);
        }

        internal IntPtr CameraPointer { get; }

        internal IntPtr FramePointer { get; }

        internal IntPtr ColorPointer { get; }

        internal IntPtr DepthPointer { get; }

        internal Exception DepthAcquisitionException { get; set; }

        internal Exception ColorConversionException { get; set; }

        public IntPtr GetFrame(IntPtr camera, uint timeoutMilliseconds)
        {
            if (camera != CameraPointer)
            {
                throw new InvalidOperationException("Unexpected camera handle.");
            }

            return FramePointer;
        }

        public ulong GetFrameId(IntPtr frame)
        {
            Require(frame, FramePointer);
            return 77;
        }

        public ulong GetFrameTimestamp(IntPtr frame)
        {
            Require(frame, FramePointer);
            return 123456;
        }

        public IntPtr GetFrameImage(IntPtr frame, ScImageType type)
        {
            Require(frame, FramePointer);
            if (type == ScImageType.Depth && DepthAcquisitionException != null)
            {
                throw DepthAcquisitionException;
            }

            return type == ScImageType.Color ? ColorPointer : DepthPointer;
        }

        public int GetImageWidth(IntPtr image)
        {
            RequireImage(image);
            return 2;
        }

        public int GetImageHeight(IntPtr image)
        {
            RequireImage(image);
            return 1;
        }

        public ScPixelFormat GetImagePixelFormat(IntPtr image)
        {
            RequireImage(image);
            return image == ColorPointer ? ScPixelFormat.BGR : ScPixelFormat.Depth16;
        }

        public uint GetImageDataSize(IntPtr image)
        {
            RequireImage(image);
            if (image == ColorPointer && ColorConversionException != null)
            {
                throw ColorConversionException;
            }

            return image == ColorPointer ? 6u : 4u;
        }

        public IntPtr GetImageData(IntPtr image)
        {
            RequireImage(image);
            return image == ColorPointer ? _colorData : _depthData;
        }

        public bool ReleaseHandle(IntPtr handle)
        {
            _releasedHandles.Add(handle);
            return true;
        }

        internal int ReleaseCount(IntPtr handle)
        {
            return _releasedHandles.Count(released => released == handle);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_colorData);
            Marshal.FreeHGlobal(_depthData);
        }

        private void RequireImage(IntPtr image)
        {
            if (image != ColorPointer && image != DepthPointer)
            {
                throw new InvalidOperationException("Unexpected image handle.");
            }
        }

        private static void Require(IntPtr actual, IntPtr expected)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException("Unexpected native handle.");
            }
        }
    }
}
