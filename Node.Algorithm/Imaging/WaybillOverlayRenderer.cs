using System;
using System.Collections.Generic;
using System.IO;
using Node.Algorithm.Models;
using NodeCraft.Flow;

namespace Node.Algorithm.Imaging
{
    public static class WaybillOverlayRenderer
    {
        public static FlowImage Render(
            FlowImage image,
            IReadOnlyList<WaybillDetection> detections)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (detections == null)
            {
                throw new ArgumentNullException(nameof(detections));
            }

            if (image.PixelFormat == FlowPixelFormat.Depth16)
            {
                throw new InvalidDataException("Waybill overlays do not support Depth16 images.");
            }

            var bytes = image.Buffer.ToArray();
            foreach (var detection in detections)
            {
                if (detection == null)
                {
                    throw new ArgumentException("Waybill detections cannot contain null values.", nameof(detections));
                }

                for (var index = 0; index < detection.Points.Count; index++)
                {
                    var start = detection.Points[index];
                    var end = detection.Points[(index + 1) % detection.Points.Count];
                    DrawLine(
                        bytes,
                        image.Width,
                        image.Height,
                        image.Stride,
                        image.PixelFormat,
                        start.X,
                        start.Y,
                        end.X,
                        end.Y);
                }
            }

            return FlowImage.FromOwnedBuffer(
                image.Width,
                image.Height,
                image.Stride,
                image.PixelFormat,
                image.Kind,
                bytes,
                image.FrameId,
                image.DeviceTimestamp,
                image.CapturedAtUtc);
        }

        private static void DrawLine(
            byte[] buffer,
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            int startX,
            int startY,
            int endX,
            int endY)
        {
            startX = Clamp(startX, 0, width - 1);
            startY = Clamp(startY, 0, height - 1);
            endX = Clamp(endX, 0, width - 1);
            endY = Clamp(endY, 0, height - 1);

            var deltaX = Math.Abs(endX - startX);
            var stepX = startX < endX ? 1 : -1;
            var deltaY = -Math.Abs(endY - startY);
            var stepY = startY < endY ? 1 : -1;
            var error = deltaX + deltaY;
            var horizontal = deltaX >= -deltaY;

            while (true)
            {
                DrawThickPixel(buffer, width, height, stride, pixelFormat, startX, startY, horizontal);
                if (startX == endX && startY == endY)
                {
                    break;
                }

                var doubleError = 2 * error;
                if (doubleError >= deltaY)
                {
                    error += deltaY;
                    startX += stepX;
                }

                if (doubleError <= deltaX)
                {
                    error += deltaX;
                    startY += stepY;
                }
            }
        }

        private static void DrawThickPixel(
            byte[] buffer,
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            int x,
            int y,
            bool horizontal)
        {
            SetPixel(buffer, width, height, stride, pixelFormat, x, y);
            if (horizontal)
            {
                SetPixel(buffer, width, height, stride, pixelFormat, x, y - 1);
                SetPixel(buffer, width, height, stride, pixelFormat, x, y + 1);
            }
            else
            {
                SetPixel(buffer, width, height, stride, pixelFormat, x - 1, y);
                SetPixel(buffer, width, height, stride, pixelFormat, x + 1, y);
            }
        }

        private static void SetPixel(
            byte[] buffer,
            int width,
            int height,
            int stride,
            FlowPixelFormat pixelFormat,
            int x,
            int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            var bytesPerPixel = pixelFormat == FlowPixelFormat.Mono8 ? 1 : 3;
            var offset = checked(y * stride + x * bytesPerPixel);
            if (offset < 0 || offset + bytesPerPixel > buffer.Length)
            {
                return;
            }

            if (pixelFormat == FlowPixelFormat.Mono8)
            {
                buffer[offset] = 255;
                return;
            }

            buffer[offset] = pixelFormat == FlowPixelFormat.Bgr24 ? (byte)0 : (byte)255;
            buffer[offset + 1] = 0;
            buffer[offset + 2] = pixelFormat == FlowPixelFormat.Bgr24 ? (byte)255 : (byte)0;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
