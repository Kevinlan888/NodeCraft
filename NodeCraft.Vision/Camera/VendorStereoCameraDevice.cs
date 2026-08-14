using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using NodeCraft.Flow;
using NodeCraft.Vision.StereoCamera.VendorInterop;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal sealed class VendorStereoCameraDeviceFactory : IStereoCameraDeviceFactory
    {
        private const int DiscoveryCapacity = 64;
        private bool _discovered;

        public int Discover()
        {
            var handles = new IntPtr[DiscoveryCapacity];
            var size = handles.Length;
            try
            {
                try
                {
                    StereoCameraNativeException.ThrowIfError(
                        "scDiscovery",
                        NativeMethods.scDiscovery(ScInterfaceType.All, handles, ref size));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("StereoCamera discovery failed.", exception);
                }

                if (size < 0 || size > handles.Length)
                {
                    throw new InvalidOperationException(
                        $"StereoCamera discovery returned {size} handles, exceeding the supported capacity {handles.Length}.");
                }

                _discovered = true;
                return size;
            }
            finally
            {
                foreach (var handle in handles)
                {
                    if (handle != IntPtr.Zero)
                    {
                        NativeMethods.scReleaseHandle(handle);
                    }
                }
            }
        }

        public IStereoCameraDevice OpenByIp(string ipAddress)
        {
            ValidateIpv4(ipAddress);
            if (!_discovered)
            {
                throw new InvalidOperationException(
                    "StereoCamera discovery must complete before opening a camera by IP.");
            }

            IntPtr handle;
            try
            {
                handle = NativeMethods.scGetCamera(ipAddress, ScCameraDataType.IP);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("StereoCamera camera lookup failed.", exception);
            }
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"No StereoCamera device was found at IP address '{ipAddress}'.");
            }

            return new VendorStereoCameraDevice(new StereoCameraCameraHandle(handle));
        }

        internal static void ValidateIpv4(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)
                || !string.Equals(ipAddress, ipAddress.Trim(), StringComparison.Ordinal)
                || ipAddress.Any(character => character > 0x7f))
            {
                throw new ArgumentException(
                    "The camera IP address must be a four-component dotted-decimal IPv4 literal.",
                    nameof(ipAddress));
            }

            var components = ipAddress.Split('.');
            if (components.Length != 4)
            {
                throw new ArgumentException(
                    "The camera IP address must be a four-component dotted-decimal IPv4 literal.",
                    nameof(ipAddress));
            }

            foreach (var component in components)
            {
                if (component.Length == 0
                    || component.Length > 3
                    || (component.Length > 1 && component[0] == '0')
                    || component.Any(character => character < '0' || character > '9')
                    || !int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    || value > 255)
                {
                    throw new ArgumentException(
                        "The camera IP address must be a four-component dotted-decimal IPv4 literal.",
                        nameof(ipAddress));
                }
            }
        }
    }

    internal sealed class VendorStereoCameraDevice : IStereoCameraDevice
    {
        private readonly StereoCameraCameraHandle _camera;
        private readonly IStereoCameraFrameApi _frameApi;
        private StereoCameraCalibrationManagerHandle _calibrationManager;
        private ScFnConnectEvent _disconnectDelegate;
        private Action<Exception> _disconnectCallback;
        private uint _disconnectEventId;
        private bool _connected;
        private bool _grabbing;
        private bool _disposed;

        internal VendorStereoCameraDevice(StereoCameraCameraHandle camera)
            : this(camera, NativeFrameApi.Instance)
        {
        }

        internal VendorStereoCameraDevice(
            StereoCameraCameraHandle camera,
            IStereoCameraFrameApi frameApi)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _frameApi = frameApi ?? throw new ArgumentNullException(nameof(frameApi));
            if (_camera.IsInvalid)
            {
                throw new ArgumentException("The camera handle is invalid.", nameof(camera));
            }
        }

        public void Connect()
        {
            ThrowIfDisposed();
            if (_connected)
            {
                return;
            }

            StereoCameraNativeException.ThrowIfError("scConnect", NativeMethods.scConnect(_camera.DangerousGetHandle()));
            _connected = true;
        }

        public void RegisterDisconnectCallback(Action<Exception> callback)
        {
            ThrowIfDisposed();
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (_disconnectEventId != 0)
            {
                return;
            }

            _disconnectCallback = callback;
            _disconnectDelegate = OnNativeDisconnect;
            var eventId = NativeMethods.scRegisterConnectEvent(
                _camera.DangerousGetHandle(),
                _disconnectDelegate,
                IntPtr.Zero);
            if (eventId == 0)
            {
                _disconnectDelegate = null;
                _disconnectCallback = null;
                throw new InvalidOperationException("scRegisterConnectEvent returned event ID 0.");
            }

            _disconnectEventId = eventId;
        }

        public void UnregisterDisconnectCallback()
        {
            if (_disconnectEventId == 0 || _camera.IsClosed)
            {
                _disconnectEventId = 0;
                _disconnectDelegate = null;
                _disconnectCallback = null;
                return;
            }

            NativeMethods.scUnregisterConnectEvent(_camera.DangerousGetHandle(), _disconnectEventId);
            _disconnectEventId = 0;
            _disconnectDelegate = null;
            _disconnectCallback = null;
        }

        public CameraCalibration ReadCalibration(CameraStream stream, bool isLeftReference)
        {
            ThrowIfDisposed();
            if (!_connected)
            {
                throw new InvalidOperationException("The camera must be connected before reading calibration.");
            }

            EnsureCalibrationManager();
            var imageType = stream == CameraStream.Color
                ? ScImageType.Color
                : ScImageType.Depth;
            StereoCameraNativeException.ThrowIfError(
                "scGetCameraCalibInfo",
                NativeMethods.scGetCameraCalibInfo(
                    _calibrationManager.DangerousGetHandle(),
                    imageType,
                    out var data,
                    isLeftReference));
            return VendorStereoCameraImageHelpers.ToCalibration(data, isLeftReference);
        }

        public void StartGrabbing()
        {
            ThrowIfDisposed();
            if (_grabbing)
            {
                return;
            }

            StereoCameraNativeException.ThrowIfError(
                "scStartGrabbing",
                NativeMethods.scStartGrabbing(_camera.DangerousGetHandle(), 0));
            _grabbing = true;
        }

        public RawStereoFrame TryGetFrame(uint timeoutMilliseconds)
        {
            ThrowIfDisposed();
            if (!_grabbing)
            {
                throw new InvalidOperationException("The camera must be grabbing before reading frames.");
            }

            var framePointer = _frameApi.GetFrame(_camera.DangerousGetHandle(), timeoutMilliseconds);
            if (framePointer == IntPtr.Zero)
            {
                return null;
            }

            using var frame = new StereoCameraFrameHandle(framePointer, _frameApi.ReleaseHandle);
            var frameId = _frameApi.GetFrameId(frame.DangerousGetHandle());
            var timestamp = _frameApi.GetFrameTimestamp(frame.DangerousGetHandle());
            var colorPointer = _frameApi.GetFrameImage(frame.DangerousGetHandle(), ScImageType.Color);
            using var colorImage = colorPointer == IntPtr.Zero
                ? null
                : new StereoCameraImageHandle(colorPointer, _frameApi.ReleaseHandle);
            var depthPointer = _frameApi.GetFrameImage(frame.DangerousGetHandle(), ScImageType.Depth);
            using var depthImage = depthPointer == IntPtr.Zero
                ? null
                : new StereoCameraImageHandle(depthPointer, _frameApi.ReleaseHandle);

            var color = colorImage == null
                ? null
                : ReadRawImage(colorImage.DangerousGetHandle(), FlowImageKind.Color, _frameApi);
            var depth = depthImage == null
                ? null
                : ReadRawImage(depthImage.DangerousGetHandle(), FlowImageKind.Depth, _frameApi);

            return new RawStereoFrame(frameId, timestamp, color, depth);
        }

        public void StopGrabbing()
        {
            if (!_grabbing || _camera.IsClosed)
            {
                _grabbing = false;
                return;
            }

            NativeMethods.scStopGrabbing(_camera.DangerousGetHandle());
            _grabbing = false;
        }

        public void Disconnect()
        {
            if (!_connected || _camera.IsClosed)
            {
                _connected = false;
                return;
            }

            StereoCameraNativeException.ThrowIfError(
                "scDisconnect",
                NativeMethods.scDisconnect(_camera.DangerousGetHandle()));
            _connected = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                UnregisterDisconnectCallback();
            }
            finally
            {
                _calibrationManager?.Dispose();
                _calibrationManager = null;
                _camera.Dispose();
            }
        }

        private void EnsureCalibrationManager()
        {
            if (_calibrationManager != null && !_calibrationManager.IsInvalid)
            {
                return;
            }

            var handle = NativeMethods.scCreateCalibDataManager(_camera.DangerousGetHandle());
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("scCreateCalibDataManager returned a null handle.");
            }

            _calibrationManager = new StereoCameraCalibrationManagerHandle(handle);
            StereoCameraNativeException.ThrowIfError(
                "scDownloadCalibData",
                NativeMethods.scDownloadCalibData(_calibrationManager.DangerousGetHandle()));
        }

        private static RawCameraImage ReadRawImage(
            IntPtr image,
            FlowImageKind expectedKind,
            IStereoCameraFrameApi frameApi)
        {
            var width = frameApi.GetImageWidth(image);
            var height = frameApi.GetImageHeight(image);
            var pixelFormat = frameApi.GetImagePixelFormat(image);
            var flowPixelFormat = VendorStereoCameraImageHelpers.MapPixelFormat(pixelFormat, expectedKind);
            var size = frameApi.GetImageDataSize(image);
            var stride = VendorStereoCameraImageHelpers.DeriveStride(width, height, flowPixelFormat, size);
            var data = frameApi.GetImageData(image);
            if (data == IntPtr.Zero)
            {
                throw new InvalidDataException("StereoCamera image data pointer was null.");
            }

            var buffer = new byte[checked((int)size)];
            Marshal.Copy(data, buffer, 0, buffer.Length);
            return new RawCameraImage(
                width,
                height,
                stride,
                flowPixelFormat,
                expectedKind,
                buffer);
        }

        private void OnNativeDisconnect(IntPtr arg, IntPtr data)
        {
            var callback = _disconnectCallback;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(new IOException("StereoCamera reported that the camera went offline."));
            }
            catch
            {
                // Native callback threads must not receive managed callback exceptions.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VendorStereoCameraDevice));
            }
        }
    }

    internal static class VendorStereoCameraImageHelpers
    {
        internal static FlowPixelFormat MapPixelFormat(
            ScPixelFormat pixelFormat,
            FlowImageKind expectedKind = FlowImageKind.Unknown)
        {
            var mapped = pixelFormat switch
            {
                ScPixelFormat.BGR => FlowPixelFormat.Bgr24,
                ScPixelFormat.RGB => FlowPixelFormat.Rgb24,
                ScPixelFormat.Mono8 => FlowPixelFormat.Mono8,
                ScPixelFormat.Depth16 => FlowPixelFormat.Depth16,
                _ => throw new InvalidDataException($"Unsupported StereoCamera pixel format '{pixelFormat}'."),
            };

            if (expectedKind == FlowImageKind.Depth && mapped != FlowPixelFormat.Depth16)
            {
                throw new InvalidDataException("The StereoCamera depth image was not Depth16.");
            }

            if (expectedKind == FlowImageKind.Color && mapped == FlowPixelFormat.Depth16)
            {
                throw new InvalidDataException("The StereoCamera color image was Depth16.");
            }

            return mapped;
        }

        internal static int DeriveStride(
            int width,
            int height,
            FlowPixelFormat pixelFormat,
            uint dataSize)
        {
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("StereoCamera image dimensions must be positive.");
            }

            if (dataSize == 0 || dataSize > int.MaxValue || dataSize % (uint)height != 0)
            {
                throw new InvalidDataException("StereoCamera image data size does not describe whole rows.");
            }

            var stride = checked((int)(dataSize / (uint)height));
            var minimumRowBytes = checked(width * BytesPerPixel(pixelFormat));
            if (stride < minimumRowBytes)
            {
                throw new InvalidDataException(
                    $"StereoCamera image stride {stride} is smaller than the required row size {minimumRowBytes}.");
            }

            return stride;
        }

        internal static CameraCalibration ToCalibration(
            ScCameraCalibInfo data,
            bool isLeftReference)
        {
            if (data.IntrinsicImgWidth <= 0 || data.IntrinsicImgHeight <= 0)
            {
                throw new InvalidDataException("StereoCamera calibration dimensions must be positive.");
            }

            if (data.Intrinsic == null || data.Intrinsic.Length != 9
                || data.Distortion == null || data.Distortion.Length != 12
                || data.Extrinsic == null || data.Extrinsic.Length != 16)
            {
                throw new InvalidDataException("StereoCamera calibration arrays have an invalid layout.");
            }

            return new CameraCalibration(
                data.IntrinsicImgWidth,
                data.IntrinsicImgHeight,
                data.Intrinsic,
                data.Distortion,
                data.Extrinsic,
                isLeftReference);
        }

        private static int BytesPerPixel(FlowPixelFormat pixelFormat)
        {
            return pixelFormat switch
            {
                FlowPixelFormat.Bgr24 => 3,
                FlowPixelFormat.Rgb24 => 3,
                FlowPixelFormat.Mono8 => 1,
                FlowPixelFormat.Depth16 => 2,
                _ => throw new InvalidDataException($"Unsupported pixel format '{pixelFormat}'."),
            };
        }
    }
}
