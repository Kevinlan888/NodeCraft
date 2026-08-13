using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NodeCraft.Flow
{
    public class ConnectionLine : Shape
    {
        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(
                nameof(Points),
                typeof(PointCollection),
                typeof(ConnectionLine),
                new FrameworkPropertyMetadata(new PointCollection(), FrameworkPropertyMetadataOptions.AffectsRender)
            );


        public PointCollection Points
        {
            get => (PointCollection)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        public static readonly DependencyProperty ArrowLengthProperty =
            DependencyProperty.Register(
                nameof(ArrowLength),
                typeof(double),
                typeof(ConnectionLine),
                new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender)
            );

        public double ArrowLength
        {
            get => (double)GetValue(ArrowLengthProperty);
            set => SetValue(ArrowLengthProperty, value);
        }

        public static readonly DependencyProperty ArrowWidthProperty =
            DependencyProperty.Register(
                nameof(ArrowWidth),
                typeof(double),
                typeof(ConnectionLine),
                new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender)
            );

        public double ArrowWidth
        {
            get => (double)GetValue(ArrowWidthProperty);
            set => SetValue(ArrowWidthProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(double),
                typeof(ConnectionLine),
                new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender)
            );

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        protected override Geometry DefiningGeometry
        {
            get
            {
                if (Points?.Count < 2) return Geometry.Empty;

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    BuildRoundedPolyline(ctx, Points.ToList(), Math.Max(0, CornerRadius));

                    if (Fill != null && Points.Count >= 2)
                    {
                        BuildArrowHead(ctx, Points[Points.Count - 2], Points[Points.Count - 1]);
                    }
                }

                geometry.Freeze();
                return geometry;
            }
        }

        private void BuildRoundedPolyline(StreamGeometryContext ctx, IList<Point> points, double cornerRadius)
        {
            ctx.BeginFigure(points[0], false, false);

            if (points.Count == 2 || cornerRadius <= 0)
            {
                ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
                return;
            }

            for (int i = 1; i < points.Count - 1; i++)
            {
                var previous = points[i - 1];
                var current = points[i];
                var next = points[i + 1];

                var inVector = previous - current;
                var outVector = next - current;

                if (inVector.Length < double.Epsilon || outVector.Length < double.Epsilon)
                {
                    ctx.LineTo(current, true, true);
                    continue;
                }

                var normalizedIn = inVector;
                normalizedIn.Normalize();
                var normalizedOut = outVector;
                normalizedOut.Normalize();

                var segmentLength = Math.Min(cornerRadius, Math.Min((previous - current).Length, (next - current).Length) / 2.0);
                var cornerStart = current + normalizedIn * segmentLength;
                var cornerEnd = current + normalizedOut * segmentLength;

                ctx.LineTo(cornerStart, true, true);
                ctx.QuadraticBezierTo(current, cornerEnd, true, true);
            }

            ctx.LineTo(points[points.Count - 1], true, true);
        }

        private void BuildArrowHead(StreamGeometryContext ctx, Point previous, Point end)
        {
            var direction = previous - end;
            if (direction.Length < double.Epsilon)
            {
                return;
            }

            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var arrowBase = end + direction * ArrowLength;
            var left = arrowBase + normal * ArrowWidth;
            var right = arrowBase - normal * ArrowWidth;

            ctx.BeginFigure(end, true, true);
            ctx.LineTo(left, true, true);
            ctx.LineTo(right, true, true);
        }
    }
}
