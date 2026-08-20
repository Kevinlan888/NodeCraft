using System;
using System.Collections.Generic;
using System.Linq;
using Node.Algorithm.Models;

internal static partial class Program
{
    private static void RunAlgorithmResultTests()
    {
        Run("Waybill recognition result copies ordered detection geometry", () =>
        {
            var sourcePoints = new List<WaybillPoint>
            {
                new WaybillPoint(1, 2),
                new WaybillPoint(7, 2),
                new WaybillPoint(7, 5),
                new WaybillPoint(1, 5),
            };
            var sourceDetections = new List<WaybillDetection>
            {
                new WaybillDetection(
                    0.91f,
                    sourcePoints,
                    WaybillGeometryMethod.ContourQuad,
                    0.73f),
            };

            var result = new WaybillRecognitionResult(8, 6, sourceDetections);
            sourcePoints[0] = new WaybillPoint(99, 99);
            sourceDetections.Clear();

            return result.Width == 8
                && result.Height == 6
                && result.Detections.Count == 1
                && result.Detections[0].Score == 0.91f
                && result.Detections[0].MaskIou == 0.73f
                && result.Detections[0].GeometryMethod == WaybillGeometryMethod.ContourQuad
                && result.Detections[0].Points.Count == 4
                && result.Detections[0].Points[0].X == 1
                && result.Detections[0].Points[0].Y == 2
                && result.Detections[0].Points[3].X == 1
                && result.Detections[0].Points[3].Y == 5;
        });

        Run("Waybill result models reject invalid dimensions and geometry", () =>
        {
            var validPoints = new[]
            {
                new WaybillPoint(0, 0),
                new WaybillPoint(1, 0),
                new WaybillPoint(1, 1),
                new WaybillPoint(0, 1),
            };

            return ThrowsAlgorithm<ArgumentOutOfRangeException>(
                       () => new WaybillRecognitionResult(0, 1, Array.Empty<WaybillDetection>()))
                && ThrowsAlgorithm<ArgumentException>(
                       () => new WaybillDetection(
                           0.5f,
                           validPoints.Take(3).ToArray(),
                           WaybillGeometryMethod.ContourQuad,
                           0.1f))
                && ThrowsAlgorithm<ArgumentNullException>(
                       () => new WaybillRecognitionResult(1, 1, null));
        });

        Run("Waybill geometry methods retain the native enum values", () =>
            (int)WaybillGeometryMethod.ContourQuad == 0
                && (int)WaybillGeometryMethod.RotatedRectFallback == 1);
    }

    private static bool ThrowsAlgorithm<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
