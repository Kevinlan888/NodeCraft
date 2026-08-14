using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NodeCraft.Vision.VendorInterop;

namespace NodeCraft.Vision.Camera
{
    internal sealed class VisionCameraDeviceFactory : IVisionCameraDeviceFactory
    {
        private readonly IImvNativeApi _nativeApi;

        internal VisionCameraDeviceFactory(IImvNativeApi nativeApi = null)
        {
            _nativeApi = nativeApi ?? ImvNativeApi.Instance;
        }

        public int Discover()
        {
            ImvDeviceList deviceList;
            VisionNativeException.ThrowIfError(
                "IMV_EnumDevices",
                _nativeApi.EnumDevices(out deviceList, ImvInterfaceType.All));
            if (deviceList.DeviceCount > int.MaxValue)
            {
                throw new InvalidOperationException("IMV discovery returned too many devices.");
            }

            return (int)deviceList.DeviceCount;
        }

        public IVisionCameraDevice OpenByIp(string ipAddress)
        {
            ValidateIpv4(ipAddress);
            var identifier = Marshal.StringToCoTaskMemAnsi(ipAddress);
            try
            {
                IntPtr handle;
                VisionNativeException.ThrowIfError(
                    "IMV_CreateHandle",
                    _nativeApi.CreateHandle(
                        out handle,
                        ImvCreateHandleMode.ByIpAddress,
                        identifier));
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"IMV_CreateHandle returned a null handle for IP address '{ipAddress}'.");
                }

                return new VisionCameraDevice(handle, _nativeApi);
            }
            finally
            {
                Marshal.FreeCoTaskMem(identifier);
            }
        }

        internal static bool IsValidIpv4(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)
                || !string.Equals(ipAddress, ipAddress.Trim(), StringComparison.Ordinal)
                || ipAddress.Any(character => character > 0x7f))
            {
                return false;
            }

            var components = ipAddress.Split('.');
            if (components.Length != 4)
            {
                return false;
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
                    return false;
                }
            }

            return true;
        }

        private static void ValidateIpv4(string ipAddress)
        {
            if (!IsValidIpv4(ipAddress))
            {
                throw new ArgumentException(
                    "The camera IP address must be a four-component dotted-decimal IPv4 literal.",
                    nameof(ipAddress));
            }
        }
    }

    internal sealed class VisionCameraDevice : IVisionCameraDevice
    {
        private readonly VisionCameraSafeHandle _camera;
        private readonly IImvNativeApi _nativeApi;
        private bool _connected;
        private bool _grabbing;
        private bool _disposed;

        internal VisionCameraDevice(IntPtr handle, IImvNativeApi nativeApi)
            : this(
                new VisionCameraSafeHandle(
                    handle,
                    (nativeApi ?? throw new ArgumentNullException(nameof(nativeApi))).DestroyHandle),
                nativeApi)
        {
        }

        internal VisionCameraDevice(VisionCameraSafeHandle camera, IImvNativeApi nativeApi)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
            if (_camera.IsInvalid)
            {
                throw new ArgumentException("The IMV camera handle is invalid.", nameof(camera));
            }
        }

        public void Connect()
        {
            ThrowIfDisposed();
            if (_connected)
            {
                return;
            }

            VisionNativeException.ThrowIfError(
                "IMV_Open",
                _nativeApi.Open(_camera.DangerousGetHandle()));
            _connected = true;
        }

        public void StartGrabbing()
        {
            ThrowIfDisposed();
            if (!_connected)
            {
                throw new InvalidOperationException("The IMV camera must be open before grabbing starts.");
            }

            if (_grabbing)
            {
                return;
            }

            VisionNativeException.ThrowIfError(
                "IMV_SetEnumFeatureSymbol",
                _nativeApi.SetEnumFeatureSymbol(
                    _camera.DangerousGetHandle(),
                    "TriggerMode",
                    "Off"));
            VisionNativeException.ThrowIfError(
                "IMV_StartGrabbing",
                _nativeApi.StartGrabbing(_camera.DangerousGetHandle()));
            _grabbing = true;
        }

        public VisionRawFrame TryGetFrame(uint timeoutMilliseconds)
        {
            ThrowIfDisposed();
            if (!_grabbing)
            {
                throw new InvalidOperationException("The IMV camera must be grabbing before a frame is read.");
            }

            ImvFrame frame;
            var result = _nativeApi.GetFrame(
                _camera.DangerousGetHandle(),
                out frame,
                timeoutMilliseconds);
            if (result == (int)ImvError.Timeout)
            {
                return null;
            }

            VisionNativeException.ThrowIfError("IMV_GetFrame", result);
            Exception primaryException = null;
            try
            {
                var image = VisionImageConverter.ConvertFrame(
                    _camera.DangerousGetHandle(),
                    frame,
                    _nativeApi);
                return new VisionRawFrame(
                    frame.FrameInfo.BlockId,
                    frame.FrameInfo.TimeStamp,
                    image);
            }
            catch (Exception exception)
            {
                primaryException = exception;
                throw;
            }
            finally
            {
                try
                {
                    var releaseResult = _nativeApi.ReleaseFrame(
                        _camera.DangerousGetHandle(),
                        ref frame);
                    if (primaryException == null)
                    {
                        VisionNativeException.ThrowIfError("IMV_ReleaseFrame", releaseResult);
                    }
                }
                catch when (primaryException != null)
                {
                }
            }
        }

        public void StopGrabbing()
        {
            ThrowIfDisposed();
            if (!_grabbing)
            {
                return;
            }

            _grabbing = false;
            VisionNativeException.ThrowIfError(
                "IMV_StopGrabbing",
                _nativeApi.StopGrabbing(_camera.DangerousGetHandle()));
        }

        public void Disconnect()
        {
            ThrowIfDisposed();
            if (_grabbing)
            {
                StopGrabbing();
            }

            if (!_connected)
            {
                return;
            }

            _connected = false;
            VisionNativeException.ThrowIfError(
                "IMV_Close",
                _nativeApi.Close(_camera.DangerousGetHandle()));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Exception cleanupException = null;
            try
            {
                if (_grabbing)
                {
                    StopGrabbing();
                }

                if (_connected)
                {
                    Disconnect();
                }
            }
            catch (Exception exception)
            {
                cleanupException = exception;
            }

            _disposed = true;
            _camera.Dispose();
            if (cleanupException != null)
            {
                throw cleanupException;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VisionCameraDevice));
            }
        }
    }
}
