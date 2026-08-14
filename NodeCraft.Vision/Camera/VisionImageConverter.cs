using System;
using System.IO;
using System.Runtime.InteropServices;
using NodeCraft.Flow;
using NodeCraft.Vision.VendorInterop;

namespace NodeCraft.Vision.Camera
{
    internal static class VisionImageConverter
    {
        internal static VisionRawImage ConvertFrame(
            IntPtr cameraHandle,
            ImvFrame frame,
            IImvNativeApi nativeApi)
        {
            if (nativeApi == null)
            {
                throw new ArgumentNullException(nameof(nativeApi));
            }

            var info = frame.FrameInfo;
            if (info.Status != 0)
            {
                throw new InvalidDataException($"IMV frame status was {info.Status}.");
            }

            if (info.Width == 0 || info.Height == 0)
            {
                throw new InvalidDataException("IMV frame dimensions must be positive.");
            }

            if (info.Width > int.MaxValue || info.Height > int.MaxValue)
            {
                throw new InvalidDataException("IMV frame dimensions exceed the supported managed image size.");
            }

            if (info.Size == 0 || info.Size > int.MaxValue)
            {
                throw new InvalidDataException("IMV frame size is outside the supported managed buffer size.");
            }

            if (frame.Data == IntPtr.Zero)
            {
                throw new InvalidDataException("IMV frame data pointer was null.");
            }

            var width = (int)info.Width;
            var height = (int)info.Height;
            if (IsBayer(info.PixelFormat))
            {
                return ConvertBayer(cameraHandle, frame.Data, info, width, height, nativeApi);
            }

            var bytesPerPixel = GetDirectBytesPerPixel(info.PixelFormat);
            if (info.Size % info.Height != 0)
            {
                throw new InvalidDataException("IMV frame size does not describe whole image rows.");
            }

            var stride = checked((int)(info.Size / info.Height));
            var minimumRowBytes = checked(width * bytesPerPixel);
            if (stride < minimumRowBytes)
            {
                throw new InvalidDataException(
                    $"IMV frame stride {stride} is smaller than the required row size {minimumRowBytes}.");
            }

            var buffer = new byte[(int)info.Size];
            Marshal.Copy(frame.Data, buffer, 0, buffer.Length);
            return new VisionRawImage(
                width,
                height,
                stride,
                ToFlowPixelFormat(info.PixelFormat),
                FlowImageKind.Color,
                buffer);
        }

        private static VisionRawImage ConvertBayer(
            IntPtr cameraHandle,
            IntPtr sourceData,
            ImvFrameInfo info,
            int width,
            int height,
            IImvNativeApi nativeApi)
        {
            int outputLength;
            try
            {
                outputLength = checked(width * height * 3);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("Bayer image dimensions overflow the supported output size.", exception);
            }

            var output = new byte[outputLength];
            var pinnedOutput = GCHandle.Alloc(output, GCHandleType.Pinned);
            try
            {
                var parameter = new ImvPixelConvertParam
                {
                    Width = info.Width,
                    Height = info.Height,
                    PixelFormat = info.PixelFormat,
                    SourceData = sourceData,
                    SourceDataLength = info.Size,
                    PaddingX = info.PaddingX,
                    PaddingY = info.PaddingY,
                    BayerDemosaic = ImvBayerDemosaic.Bilinear,
                    DestinationPixelFormat = ImvPixelType.Bgr8,
                    DestinationBuffer = pinnedOutput.AddrOfPinnedObject(),
                    DestinationBufferSize = (uint)output.Length,
                };

                VisionNativeException.ThrowIfError(
                    "IMV_PixelConvert",
                    nativeApi.PixelConvert(cameraHandle, ref parameter));

                if (parameter.DestinationDataLength != outputLength)
                {
                    throw new InvalidDataException(
                        $"IMV_PixelConvert returned {parameter.DestinationDataLength} bytes; expected {outputLength}.");
                }

                return new VisionRawImage(
                    width,
                    height,
                    checked(width * 3),
                    FlowPixelFormat.Bgr24,
                    FlowImageKind.Color,
                    output);
            }
            finally
            {
                pinnedOutput.Free();
            }
        }

        private static bool IsBayer(ImvPixelType pixelType)
        {
            return pixelType == ImvPixelType.BayerGr8
                || pixelType == ImvPixelType.BayerRg8
                || pixelType == ImvPixelType.BayerGb8
                || pixelType == ImvPixelType.BayerBg8;
        }

        private static int GetDirectBytesPerPixel(ImvPixelType pixelType)
        {
            switch (pixelType)
            {
                case ImvPixelType.Mono8:
                    return 1;
                case ImvPixelType.Bgr8:
                case ImvPixelType.Rgb8:
                    return 3;
                default:
                    throw new InvalidDataException($"Unsupported IMV pixel format {(int)pixelType}.");
            }
        }

        private static FlowPixelFormat ToFlowPixelFormat(ImvPixelType pixelType)
        {
            switch (pixelType)
            {
                case ImvPixelType.Mono8:
                    return FlowPixelFormat.Mono8;
                case ImvPixelType.Bgr8:
                    return FlowPixelFormat.Bgr24;
                case ImvPixelType.Rgb8:
                    return FlowPixelFormat.Rgb24;
                default:
                    throw new InvalidDataException($"Unsupported IMV pixel format {(int)pixelType}.");
            }
        }
    }
}
