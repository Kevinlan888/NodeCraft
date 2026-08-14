using System;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Camera
{
    internal interface IVisionCameraDeviceFactory
    {
        int Discover();

        IVisionCameraDevice OpenByIp(string ipAddress);
    }

    internal interface IVisionCameraDevice : IDisposable
    {
        void Connect();

        void StartGrabbing();

        VisionRawFrame TryGetFrame(uint timeoutMilliseconds);

        void StopGrabbing();

        void Disconnect();
    }

    internal sealed class VisionRawFrame
    {
        internal VisionRawFrame(ulong frameId, ulong deviceTimestamp, VisionRawImage image)
        {
            FrameId = frameId;
            DeviceTimestamp = deviceTimestamp;
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }

        internal ulong FrameId { get; }

        internal ulong DeviceTimestamp { get; }

        internal VisionRawImage Image { get; }
    }

    internal sealed class VisionRawImage
    {
        internal VisionRawImage(
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
}
