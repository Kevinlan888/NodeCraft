using System;

namespace NodeCraft.Flow
{
    public sealed class FlowImage
    {
        private FlowImage(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            byte[] buffer,
            ulong frameId,
            ulong deviceTimestamp,
            DateTimeOffset capturedAtUtc)
        {
            Validate(width, height, stride, pixelFormat, buffer);
            Width = width;
            Height = height;
            Stride = stride;
            PixelFormat = pixelFormat;
            Kind = kind;
            Buffer = new ReadOnlyMemory<byte>(buffer);
            FrameId = frameId;
            DeviceTimestamp = deviceTimestamp;
            CapturedAtUtc = capturedAtUtc;
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public FlowPixelFormat PixelFormat { get; }

        public FlowImageKind Kind { get; }

        public ReadOnlyMemory<byte> Buffer { get; }

        public ulong FrameId { get; }

        public ulong DeviceTimestamp { get; }

        public DateTimeOffset CapturedAtUtc { get; }

        public static FlowImage CopyFrom(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            ReadOnlySpan<byte> buffer,
            ulong frameId,
            ulong deviceTimestamp,
            DateTimeOffset capturedAtUtc)
        {
            return new FlowImage(
                width,
                height,
                stride,
                pixelFormat,
                kind,
                buffer.ToArray(),
                frameId,
                deviceTimestamp,
                capturedAtUtc);
        }

        public static FlowImage FromOwnedBuffer(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            FlowImageKind kind,
            byte[] buffer,
            ulong frameId,
            ulong deviceTimestamp,
            DateTimeOffset capturedAtUtc)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return new FlowImage(
                width,
                height,
                stride,
                pixelFormat,
                kind,
                buffer,
                frameId,
                deviceTimestamp,
                capturedAtUtc);
        }

        private static void Validate(
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            byte[] buffer)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Image height must be positive.");
            }

            if (stride <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stride), "Image stride must be positive.");
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var bytesPerPixel = GetBytesPerPixel(pixelFormat);
            int expectedLength;
            try
            {
                expectedLength = checked(stride * height);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException("Image stride and height overflow the supported buffer size.", nameof(stride), ex);
            }

            if (buffer.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"Image buffer length must equal stride * height ({expectedLength}).",
                    nameof(buffer));
            }

            int minimumRowBytes;
            try
            {
                minimumRowBytes = checked(width * bytesPerPixel);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException("Image width overflows the supported row size.", nameof(width), ex);
            }

            if (stride < minimumRowBytes)
            {
                throw new ArgumentException(
                    $"Image stride must be at least {minimumRowBytes} bytes for {pixelFormat}.",
                    nameof(stride));
            }
        }

        private static int GetBytesPerPixel(FlowPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case FlowPixelFormat.Bgr24:
                case FlowPixelFormat.Rgb24:
                    return 3;
                case FlowPixelFormat.Mono8:
                    return 1;
                case FlowPixelFormat.Depth16:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported image pixel format.");
            }
        }
    }
}
