using System;
using System.IO;
using System.Runtime.InteropServices;
using NodeCraft.Flow;

namespace Node.Algorithm.Interop
{
    internal sealed class WaybillImageBuffer : IDisposable
    {
        private GCHandle _pin;
        private bool _disposed;

        private WaybillImageBuffer(
            int width,
            int height,
            int inputFormat,
            GCHandle pin,
            IntPtr pointer)
        {
            Width = width;
            Height = height;
            InputFormat = inputFormat;
            _pin = pin;
            Pointer = pointer;
        }

        public int Width { get; }

        public int Height { get; }

        public int InputFormat { get; }

        public IntPtr Pointer { get; private set; }

        internal static WaybillImageBuffer Create(FlowImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            var bytesPerPixel = GetBytesPerPixel(image.PixelFormat, out var inputFormat);
            var rowBytes = checked(image.Width * bytesPerPixel);

            if (image.Stride == rowBytes
                && MemoryMarshal.TryGetArray(image.Buffer, out var segment)
                && segment.Array != null)
            {
                var pin = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
                var pointer = IntPtr.Add(pin.AddrOfPinnedObject(), segment.Offset);
                return new WaybillImageBuffer(
                    image.Width,
                    image.Height,
                    inputFormat,
                    pin,
                    pointer);
            }

            var packedBuffer = new byte[checked(rowBytes * image.Height)];
            var source = image.Buffer.Span;
            for (var row = 0; row < image.Height; row++)
            {
                source
                    .Slice(row * image.Stride, rowBytes)
                    .CopyTo(packedBuffer.AsSpan(row * rowBytes, rowBytes));
            }

            var packedPin = GCHandle.Alloc(packedBuffer, GCHandleType.Pinned);
            return new WaybillImageBuffer(
                image.Width,
                image.Height,
                inputFormat,
                packedPin,
                packedPin.AddrOfPinnedObject());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Pointer = IntPtr.Zero;
            if (_pin.IsAllocated)
            {
                _pin.Free();
            }
        }

        private static int GetBytesPerPixel(FlowPixelFormat pixelFormat, out int inputFormat)
        {
            switch (pixelFormat)
            {
                case FlowPixelFormat.Bgr24:
                    inputFormat = 0;
                    return 3;
                case FlowPixelFormat.Rgb24:
                    inputFormat = 1;
                    return 3;
                case FlowPixelFormat.Mono8:
                    inputFormat = 2;
                    return 1;
                case FlowPixelFormat.Depth16:
                    throw new InvalidDataException("Waybill inference does not support Depth16 images.");
                default:
                    throw new InvalidDataException($"Unsupported FlowImage pixel format: {pixelFormat}.");
            }
        }
    }
}
