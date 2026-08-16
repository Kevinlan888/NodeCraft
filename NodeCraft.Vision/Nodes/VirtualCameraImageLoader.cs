using System;
using System.IO;
using System.Security;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NodeCraft.Flow;

namespace NodeCraft.Vision.Nodes
{
    internal interface IVirtualCameraImageLoader
    {
        VirtualCameraImageTemplate Load(string path);
    }

    internal sealed class VirtualCameraImageLoadException : Exception
    {
        internal VirtualCameraImageLoadException(string path, Exception innerException)
            : base($"VirtualCamera image '{path}' could not be loaded.", innerException)
        {
            Path = path;
        }

        public string Path { get; }
    }

    internal sealed class VirtualCameraImageLoader : IVirtualCameraImageLoader
    {
        public VirtualCameraImageTemplate Load(string path)
        {
            try
            {
                BitmapSource bitmap;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames == null || decoder.Frames.Count == 0)
                    {
                        throw new InvalidDataException("Image does not contain a frame.");
                    }

                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
                    {
                        throw new InvalidDataException("Image dimensions must be positive.");
                    }

                    if (frame.Format == PixelFormats.Gray8)
                    {
                        bitmap = frame;
                    }
                    else
                    {
                        var converted = new FormatConvertedBitmap(
                            frame,
                            PixelFormats.Bgr24,
                            null,
                            0);
                        converted.Freeze();
                        bitmap = converted;
                    }

                    bitmap.Freeze();
                }

                var bytesPerPixel = bitmap.Format == PixelFormats.Gray8 ? 1 : 3;
                var stride = checked(bitmap.PixelWidth * bytesPerPixel);
                var buffer = new byte[checked(stride * bitmap.PixelHeight)];
                bitmap.CopyPixels(buffer, stride, 0);
                return new VirtualCameraImageTemplate(
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    stride,
                    bitmap.Format == PixelFormats.Gray8
                        ? FlowPixelFormat.Mono8
                        : FlowPixelFormat.Bgr24,
                    FlowImageKind.Color,
                    buffer);
            }
            catch (Exception exception) when (IsExpectedImageLoadFailure(exception))
            {
                throw new VirtualCameraImageLoadException(path, exception);
            }
        }

        internal static bool IsSkippableImageLoadError(Exception exception)
        {
            return exception is VirtualCameraImageLoadException;
        }

        private static bool IsExpectedImageLoadFailure(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is InvalidDataException
                || exception is FileFormatException
                || exception is NotSupportedException
                || exception is ArgumentException
                || exception is OverflowException;
        }
    }
}
