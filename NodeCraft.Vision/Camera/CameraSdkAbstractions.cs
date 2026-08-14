using System;
using NodeCraft.Flow;
using NodeCraft.Vision.Runtime;

namespace NodeCraft.Vision.StereoCamera.Camera
{
    internal enum CameraStream
    {
        Color,
        Depth,
    }

    internal interface IStereoCameraDeviceFactory
    {
        int Discover();

        IStereoCameraDevice OpenByIp(string ipAddress);
    }

    internal interface ICameraRuntimeScopeFactory
    {
        IDisposable Acquire();
    }

    internal interface IStereoCameraDevice : IDisposable
    {
        void Connect();

        void RegisterDisconnectCallback(Action<Exception> callback);

        void UnregisterDisconnectCallback();

        CameraCalibration ReadCalibration(CameraStream stream, bool isLeftReference);

        void StartGrabbing();

        RawStereoFrame TryGetFrame(uint timeoutMilliseconds);

        void StopGrabbing();

        void Disconnect();
    }

    internal sealed class RawStereoFrame
    {
        internal RawStereoFrame(
            ulong frameId,
            ulong deviceTimestamp,
            RawCameraImage color,
            RawCameraImage depth)
        {
            FrameId = frameId;
            DeviceTimestamp = deviceTimestamp;
            Color = color;
            Depth = depth;
        }

        internal ulong FrameId { get; }

        internal ulong DeviceTimestamp { get; }

        internal RawCameraImage Color { get; }

        internal RawCameraImage Depth { get; }

        internal bool IsComplete => Color != null && Depth != null;
    }

    internal sealed class RawCameraImage
    {
        internal RawCameraImage(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            byte[] buffer)
        {
            Width = width;
            Height = height;
            Stride = stride;
            PixelFormat = pixelFormat;
            Kind = kind;
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int Stride { get; }

        internal FlowPixelFormat PixelFormat { get; }

        internal FlowImageKind Kind { get; }

        internal byte[] Buffer { get; }
    }

    internal sealed class ProductionCameraRuntimeScopeFactory : ICameraRuntimeScopeFactory
    {
        private readonly string _pluginAssemblyPath;

        internal ProductionCameraRuntimeScopeFactory(string pluginAssemblyPath)
        {
            _pluginAssemblyPath = pluginAssemblyPath ?? throw new ArgumentNullException(nameof(pluginAssemblyPath));
        }

        public IDisposable Acquire()
        {
            return NativeRuntimeScope.Acquire(_pluginAssemblyPath);
        }
    }
}
