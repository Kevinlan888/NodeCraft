using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Node.Algorithm.Models
{
    public sealed class WaybillPoint
    {
        public WaybillPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }
    }

    public enum WaybillGeometryMethod
    {
        ContourQuad = 0,
        RotatedRectFallback = 1,
    }

    public sealed class WaybillDetection
    {
        public WaybillDetection(
            float score,
            IReadOnlyList<WaybillPoint> points,
            WaybillGeometryMethod geometryMethod,
            float maskIou)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Count != 4)
            {
                throw new ArgumentException("A waybill detection must contain exactly four points.", nameof(points));
            }

            if (points.Any(point => point == null))
            {
                throw new ArgumentException("Waybill detection points cannot contain null values.", nameof(points));
            }

            Score = score;
            Points = new ReadOnlyCollection<WaybillPoint>(points.ToArray());
            GeometryMethod = geometryMethod;
            MaskIou = maskIou;
        }

        public float Score { get; }

        public IReadOnlyList<WaybillPoint> Points { get; }

        public WaybillGeometryMethod GeometryMethod { get; }

        public float MaskIou { get; }
    }

    public sealed class WaybillRecognitionResult
    {
        public WaybillRecognitionResult(
            int width,
            int height,
            IReadOnlyList<WaybillDetection> detections)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
            }

            if (detections == null)
            {
                throw new ArgumentNullException(nameof(detections));
            }

            if (detections.Any(detection => detection == null))
            {
                throw new ArgumentException("Waybill detections cannot contain null values.", nameof(detections));
            }

            Width = width;
            Height = height;
            Detections = new ReadOnlyCollection<WaybillDetection>(detections.ToArray());
        }

        public int Width { get; }

        public int Height { get; }

        public IReadOnlyList<WaybillDetection> Detections { get; }
    }
}
