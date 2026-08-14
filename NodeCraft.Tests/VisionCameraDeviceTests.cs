using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NodeCraft.Flow;
using NodeCraft.Vision.Camera;
using NodeCraft.Vision.VendorInterop;

internal static partial class Program
{
    private static void RunVisionCameraDeviceTests()
    {
        Run("Vision converter maps IMV direct pixel formats", () =>
        {
            var mono = ConvertFrame(ImvPixelType.Mono8, 2, 1, new byte[] { 1, 2 });
            var bgr = ConvertFrame(ImvPixelType.Bgr8, 1, 1, new byte[] { 3, 4, 5 });
            var rgb = ConvertFrame(ImvPixelType.Rgb8, 1, 1, new byte[] { 6, 7, 8 });
            return mono.PixelFormat == FlowPixelFormat.Mono8
                && mono.Stride == 2
                && mono.Kind == FlowImageKind.Color
                && bgr.PixelFormat == FlowPixelFormat.Bgr24
                && bgr.Stride == 3
                && rgb.PixelFormat == FlowPixelFormat.Rgb24
                && rgb.Buffer.SequenceEqual(new byte[] { 6, 7, 8 });
        });

        Run("Vision converter validates direct frame layout", () =>
        {
            try
            {
                ConvertFrame(ImvPixelType.Bgr8, 2, 1, new byte[] { 1, 2, 3, 4, 5 });
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        });

        Run("Vision converter rejects invalid status dimensions and pointer", () =>
        {
            var cases = new[]
            {
                CreateMalformedFrame(status: 1, width: 1, height: 1, size: 3, data: new IntPtr(1)),
                CreateMalformedFrame(status: 0, width: 0, height: 1, size: 3, data: new IntPtr(1)),
                CreateMalformedFrame(status: 0, width: 1, height: 0, size: 3, data: new IntPtr(1)),
                CreateMalformedFrame(status: 0, width: 1, height: 1, size: 3, data: IntPtr.Zero),
            };
            return cases.All(frame => ThrowsInvalidData(() =>
                VisionImageConverter.ConvertFrame(IntPtr.Zero, frame, new RecordingImvNativeApi())));
        });

        Run("Vision converter converts Bayer through IMV_PixelConvert", () =>
        {
            var api = new RecordingImvNativeApi
            {
                PixelConvertDataLength = 6,
                PixelConvertCallback = parameter =>
                {
                    Marshal.Copy(new byte[] { 9, 8, 7, 6, 5, 4 }, 0, parameter.DestinationBuffer, 6);
                    parameter.DestinationDataLength = 6;
                },
            };
            var frame = CreateFrame(ImvPixelType.BayerRg8, 2, 1, new byte[] { 1, 2 });
            try
            {
                var image = VisionImageConverter.ConvertFrame(new IntPtr(7), frame, api);
                return image.PixelFormat == FlowPixelFormat.Bgr24
                    && image.Stride == 6
                    && image.Buffer.SequenceEqual(new byte[] { 9, 8, 7, 6, 5, 4 })
                    && api.LastPixelConvert.PixelFormat == ImvPixelType.BayerRg8
                    && api.LastPixelConvert.DestinationPixelFormat == ImvPixelType.Bgr8
                    && api.LastPixelConvert.BayerDemosaic == ImvBayerDemosaic.Bilinear;
            }
            finally
            {
                FreeFrameData(frame);
            }
        });

        Run("Vision device releases a successful native frame", () =>
        {
            var api = new RecordingImvNativeApi
            {
                NextFrame = CreateFrame(ImvPixelType.Mono8, 1, 1, new byte[] { 42 }),
            };
            using (var device = CreateStartedDevice(api))
            {
                var image = device.TryGetFrame(100);
                return image != null && image.Image.Buffer.SequenceEqual(new byte[] { 42 });
            }
        });

        Run("Vision device releases a frame when conversion fails", () =>
        {
            var api = new RecordingImvNativeApi
            {
                NextFrame = CreateFrame(ImvPixelType.BayerRg8, 1, 1, new byte[] { 42 }),
                PixelConvertCallback = _ => throw new InvalidOperationException("conversion failed"),
            };
            using (var device = CreateStartedDevice(api))
            {
                try
                {
                    device.TryGetFrame(100);
                    return false;
                }
                catch (InvalidOperationException)
                {
                    return api.ReleaseFrameCount == 1;
                }
            }
        });

        Run("Vision factory validates strict dotted-decimal IPv4", () =>
        {
            var valid = new[] { "192.168.1.10", "0.0.0.0", "255.255.255.255" };
            var invalid = new[] { " 192.168.1.10", "192.168.1.010", "192.168.1", "192.168.1.256", "::1" };
            return valid.All(VisionCameraDeviceFactory.IsValidIpv4)
                && invalid.All(ip => !VisionCameraDeviceFactory.IsValidIpv4(ip));
        });

        Run("Vision device lifecycle is idempotent and ordered", () =>
        {
            var calls = new List<string>();
            var api = new RecordingImvNativeApi(calls);
            using (var device = new VisionCameraDevice(new IntPtr(9), api))
            {
                device.Connect();
                device.StartGrabbing();
                device.StopGrabbing();
                device.StopGrabbing();
                device.Disconnect();
                device.Disconnect();
            }

            return calls.SequenceEqual(new[]
            {
                "open",
                "trigger:off",
                "start",
                "stop",
                "close",
                "destroy",
            });
        });
    }

    private static VisionCameraDevice CreateStartedDevice(RecordingImvNativeApi api)
    {
        var device = new VisionCameraDevice(new IntPtr(8), api);
        device.Connect();
        device.StartGrabbing();
        return device;
    }

    private static VisionRawImage ConvertFrame(
        ImvPixelType pixelType,
        uint width,
        uint height,
        byte[] data)
    {
        var frame = CreateFrame(pixelType, width, height, data);
        try
        {
            return VisionImageConverter.ConvertFrame(IntPtr.Zero, frame, new RecordingImvNativeApi());
        }
        finally
        {
            FreeFrameData(frame);
        }
    }

    private static ImvFrame CreateFrame(
        ImvPixelType pixelType,
        uint width,
        uint height,
        byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new ImvFrame
        {
            Data = pointer,
            FrameInfo = new ImvFrameInfo
            {
                Status = 0,
                Width = width,
                Height = height,
                Size = (uint)data.Length,
                PixelFormat = pixelType,
                PaddingX = 0,
                PaddingY = 0,
            },
        };
    }

    private static ImvFrame CreateMalformedFrame(
        uint status,
        uint width,
        uint height,
        uint size,
        IntPtr data)
    {
        return new ImvFrame
        {
            Data = data,
            FrameInfo = new ImvFrameInfo
            {
                Status = status,
                Width = width,
                Height = height,
                Size = size,
                PixelFormat = ImvPixelType.Bgr8,
            },
        };
    }

    private static bool ThrowsInvalidData(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static void FreeFrameData(ImvFrame frame)
    {
        if (frame.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(frame.Data);
        }
    }

    private sealed class RecordingImvNativeApi : IImvNativeApi
    {
        private readonly List<string> _calls;

        internal RecordingImvNativeApi(List<string> calls = null)
        {
            _calls = calls ?? new List<string>();
        }

        internal ImvFrame NextFrame { get; set; }

        internal Action<ImvPixelConvertParam> PixelConvertCallback { get; set; }

        internal uint PixelConvertDataLength { get; set; }

        internal ImvPixelConvertParam LastPixelConvert { get; private set; }

        internal int ReleaseFrameCount { get; private set; }

        public int EnumDevices(out ImvDeviceList deviceList, ImvInterfaceType interfaceType)
        {
            deviceList = new ImvDeviceList { DeviceCount = 1 };
            return (int)ImvError.Ok;
        }

        public int CreateHandle(out IntPtr handle, ImvCreateHandleMode mode, IntPtr identifier)
        {
            handle = new IntPtr(10);
            return (int)ImvError.Ok;
        }

        public int DestroyHandle(IntPtr handle)
        {
            _calls.Add("destroy");
            return (int)ImvError.Ok;
        }

        public int Open(IntPtr handle)
        {
            _calls.Add("open");
            return (int)ImvError.Ok;
        }

        public int Close(IntPtr handle)
        {
            _calls.Add("close");
            return (int)ImvError.Ok;
        }

        public int SetEnumFeatureSymbol(IntPtr handle, string featureName, string enumSymbol)
        {
            _calls.Add("trigger:off");
            return (int)ImvError.Ok;
        }

        public int StartGrabbing(IntPtr handle)
        {
            _calls.Add("start");
            return (int)ImvError.Ok;
        }

        public int StopGrabbing(IntPtr handle)
        {
            _calls.Add("stop");
            return (int)ImvError.Ok;
        }

        public int GetFrame(IntPtr handle, out ImvFrame frame, uint timeoutMilliseconds)
        {
            frame = NextFrame;
            return NextFrame.Data == IntPtr.Zero ? (int)ImvError.Timeout : (int)ImvError.Ok;
        }

        public int ReleaseFrame(IntPtr handle, ref ImvFrame frame)
        {
            ReleaseFrameCount++;
            return (int)ImvError.Ok;
        }

        public int PixelConvert(IntPtr handle, ref ImvPixelConvertParam parameter)
        {
            LastPixelConvert = parameter;
            PixelConvertCallback?.Invoke(parameter);
            parameter.DestinationDataLength = PixelConvertDataLength;
            return (int)ImvError.Ok;
        }
    }
}
