using System;
using System.Buffers.Binary;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NodeCraft.Flow;

namespace NodeCraft.Vision.StereoCamera.Preview
{
    internal static class FlowImageBitmapConverter
    {
        internal static PreviewRenderResult Convert(FlowImage image)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            var buffer = image.Buffer.ToArray();
            switch (image.PixelFormat)
            {
                case FlowPixelFormat.Bgr24:
                    return CreatePackedResult(image, PixelFormats.Bgr24, buffer, image.Stride, "Bgr24");
                case FlowPixelFormat.Rgb24:
                    return CreatePackedResult(image, PixelFormats.Rgb24, buffer, image.Stride, "Rgb24");
                case FlowPixelFormat.Mono8:
                    return CreatePackedResult(image, PixelFormats.Gray8, buffer, image.Stride, "Mono8");
                case FlowPixelFormat.Depth16:
                    return ConvertDepth16(image, buffer);
                default:
                    throw new ArgumentOutOfRangeException(nameof(image), "Unsupported FlowImage pixel format.");
            }
        }

        private static PreviewRenderResult CreatePackedResult(
            FlowImage image,
            PixelFormat pixelFormat,
            byte[] buffer,
            int stride,
            string formatName)
        {
            var bitmap = BitmapSource.Create(
                image.Width,
                image.Height,
                96,
                96,
                pixelFormat,
                null,
                buffer,
                stride);
            bitmap.Freeze();
            return new PreviewRenderResult(
                bitmap,
                $"{formatName} {image.Width}x{image.Height}, frame {image.FrameId}");
        }

        private static PreviewRenderResult ConvertDepth16(FlowImage image, byte[] source)
        {
            ushort minimum = ushort.MaxValue;
            ushort maximum = ushort.MinValue;
            var hasNonZero = false;
            for (var y = 0; y < image.Height; y++)
            {
                var rowOffset = checked(y * image.Stride);
                for (var x = 0; x < image.Width; x++)
                {
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(
                        source.AsSpan(rowOffset + (x * 2), 2));
                    if (value == 0)
                    {
                        continue;
                    }

                    hasNonZero = true;
                    if (value < minimum)
                    {
                        minimum = value;
                    }

                    if (value > maximum)
                    {
                        maximum = value;
                    }
                }
            }

            var normalized = new byte[checked(image.Width * image.Height)];
            var hasRange = hasNonZero && maximum > minimum;
            if (hasRange)
            {
                var range = maximum - minimum;
                for (var y = 0; y < image.Height; y++)
                {
                    var sourceRowOffset = checked(y * image.Stride);
                    var destinationRowOffset = checked(y * image.Width);
                    for (var x = 0; x < image.Width; x++)
                    {
                        var value = BinaryPrimitives.ReadUInt16LittleEndian(
                            source.AsSpan(sourceRowOffset + (x * 2), 2));
                        normalized[destinationRowOffset + x] = value == 0
                            ? (byte)0
                            : (byte)(((value - minimum) * 255u) / range);
                    }
                }
            }

            var bitmap = BitmapSource.Create(
                image.Width,
                image.Height,
                96,
                96,
                PixelFormats.Gray8,
                null,
                normalized,
                image.Width);
            bitmap.Freeze();

            var status = new StringBuilder()
                .Append("Depth16 ")
                .Append(image.Width)
                .Append('x')
                .Append(image.Height)
                .Append(", frame ")
                .Append(image.FrameId);
            if (!hasNonZero)
            {
                status.Append(", no nonzero depth; black");
            }
            else if (!hasRange)
            {
                status.Append(", no depth range; black");
            }
            else
            {
                status.Append(" [").Append(minimum).Append('-').Append(maximum).Append(']');
            }

            return new PreviewRenderResult(bitmap, status.ToString());
        }
    }
}
