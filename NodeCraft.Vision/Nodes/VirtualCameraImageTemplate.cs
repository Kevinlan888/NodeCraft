using System;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    internal sealed class VirtualCameraImageTemplate
    {
        private readonly byte[] _buffer;

        internal VirtualCameraImageTemplate(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var validated = FlowImage.FromOwnedBuffer(
                width,
                height,
                stride,
                pixelFormat,
                kind,
                buffer,
                0,
                0,
                DateTimeOffset.UnixEpoch);
            Width = validated.Width;
            Height = validated.Height;
            Stride = validated.Stride;
            PixelFormat = validated.PixelFormat;
            Kind = validated.Kind;
            _buffer = buffer;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int Stride { get; }

        internal FlowPixelFormat PixelFormat { get; }

        internal FlowImageKind Kind { get; }

        internal int BufferLength => _buffer.Length;

        internal FlowImage CreateFrame(
            ulong frameId,
            ulong deviceTimestamp,
            DateTimeOffset capturedAtUtc)
        {
            return FlowImage.FromOwnedBuffer(
                Width,
                Height,
                Stride,
                PixelFormat,
                Kind,
                _buffer,
                frameId,
                deviceTimestamp,
                capturedAtUtc);
        }
    }
}
