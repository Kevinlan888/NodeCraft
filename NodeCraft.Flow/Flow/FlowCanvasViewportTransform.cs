using System;
using System.Windows;

namespace NodeCraft.Flow
{
    internal sealed class FlowCanvasViewportTransform
    {
        public const double MinZoom = 0.25;
        public const double MaxZoom = 2.0;

        public double Zoom { get; private set; } = 1.0;
        public Vector PanOffset { get; private set; }

        public Point ToViewport(Point world)
        {
            return new Point(
                world.X * Zoom + PanOffset.X,
                world.Y * Zoom + PanOffset.Y);
        }

        public Point ToWorld(Point viewport)
        {
            return new Point(
                (viewport.X - PanOffset.X) / Zoom,
                (viewport.Y - PanOffset.Y) / Zoom);
        }

        public void SetZoom(double zoom)
        {
            if (double.IsNaN(zoom))
            {
                return;
            }

            Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
        }

        public void ZoomAt(Point viewportPoint, double factor)
        {
            if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            {
                return;
            }

            var worldPoint = ToWorld(viewportPoint);
            SetZoom(Zoom * factor);
            PanOffset = new Vector(
                viewportPoint.X - worldPoint.X * Zoom,
                viewportPoint.Y - worldPoint.Y * Zoom);
        }

        public void PanBy(Vector delta)
        {
            PanOffset += delta;
        }
    }
}
