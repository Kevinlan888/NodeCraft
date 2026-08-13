using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace NodeCraft.Flow
{
    public static class OrthogonalRouter
    {
        // 用于替代 tuple 的结构
        private struct GridPoint : IEquatable<GridPoint>
        {
            public int X;
            public int Y;
            public GridPoint(int x, int y) { X = x; Y = y; }
            public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is GridPoint gp && Equals(gp);
            public override int GetHashCode() => (X * 397) ^ Y;
            public static implicit operator Point(GridPoint g) => new Point(g.X, g.Y);
        }

        public class RouteResult
        {
            public List<Point> Points; // 画布坐标系下的折线点（顺序）
            public bool Success;
        }

        public static RouteResult Route(Point start, Point end, List<Rect> obstacles, Rect canvasBounds, double cellSize = 16, double padding = 6)
        {
            const int turnPenalty = 6;

            int cols = Math.Max(3, (int)Math.Ceiling(canvasBounds.Width / cellSize));
            int rows = Math.Max(3, (int)Math.Ceiling(canvasBounds.Height / cellSize));

            int ix(Point p) => Math.Min(cols - 1, Math.Max(0, (int)((p.X - canvasBounds.X) / cellSize)));
            int iy(Point p) => Math.Min(rows - 1, Math.Max(0, (int)((p.Y - canvasBounds.Y) / cellSize)));
            Point centerOf(int gx, int gy) => new Point(canvasBounds.X + gx * cellSize + cellSize / 2.0, canvasBounds.Y + gy * cellSize + cellSize / 2.0);

            bool[,] blocked = new bool[cols, rows];

            foreach (var r in obstacles)
            {
                Rect er = new Rect(r.X - padding, r.Y - padding, r.Width + padding * 2, r.Height + padding * 2);
                int x0 = Math.Max(0, (int)((er.X - canvasBounds.X) / cellSize));
                int y0 = Math.Max(0, (int)((er.Y - canvasBounds.Y) / cellSize));
                int x1 = Math.Min(cols - 1, (int)Math.Floor((er.X + er.Width - canvasBounds.X) / cellSize));
                int y1 = Math.Min(rows - 1, (int)Math.Floor((er.Y + er.Height - canvasBounds.Y) / cellSize));
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        if (x >= 0 && x < cols && y >= 0 && y < rows)
                            blocked[x, y] = true;
            }

            GridPoint startIdx = new GridPoint(ix(start), iy(start));
            GridPoint endIdx = new GridPoint(ix(end), iy(end));
            GridPoint s = startIdx, e = endIdx;

            if (blocked[s.X, s.Y])
            {
                var t = FindNearestFree(s, blocked, cols, rows);
                if (t == null) return new RouteResult { Success = false, Points = null };
                s = t.Value;
            }
            if (blocked[e.X, e.Y])
            {
                var t = FindNearestFree(e, blocked, cols, rows);
                if (t == null) return new RouteResult { Success = false, Points = null };
                e = t.Value;
            }

            var cameFrom = new Dictionary<GridPoint, GridPoint>();
            var gScore = new Dictionary<GridPoint, int>();
            var open = new List<GridPoint>();
            Func<GridPoint, int> Heuristic = node => Math.Abs(node.X - e.X) + Math.Abs(node.Y - e.Y);

            gScore[s] = 0;
            open.Add(s);

            GridPoint[] dirs = new[] { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1), new GridPoint(0, -1) };

            bool found = false;
            while (open.Count > 0)
            {
                int bestIdx = 0;
                int bestF = int.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    var n = open[i];
                    int g = gScore.ContainsKey(n) ? gScore[n] : int.MaxValue;
                    int f = g + Heuristic(n);
                    if (f < bestF) { bestF = f; bestIdx = i; }
                }
                var current = open[bestIdx];
                open.RemoveAt(bestIdx);

                if (current.Equals(e)) { found = true; break; }

                foreach (var d in dirs)
                {
                    int nx = current.X + d.X;
                    int ny = current.Y + d.Y;
                    if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                    if (blocked[nx, ny]) continue;
                    var neigh = new GridPoint(nx, ny);
                    int tentativeG = gScore[current] + 1;
                    if (cameFrom.TryGetValue(current, out var previous))
                    {
                        var currentDirection = new GridPoint(current.X - previous.X, current.Y - previous.Y);
                        if (currentDirection.X != d.X || currentDirection.Y != d.Y)
                        {
                            tentativeG += turnPenalty;
                        }
                    }

                    if (!gScore.ContainsKey(neigh) || tentativeG < gScore[neigh])
                    {
                        gScore[neigh] = tentativeG;
                        cameFrom[neigh] = current;
                        if (!open.Contains(neigh)) open.Add(neigh);
                    }
                }
            }

            if (!found) return new RouteResult { Success = false, Points = null };

            var path = new List<GridPoint>();
            var curNode = e;
            path.Add(curNode);
            while (!curNode.Equals(s))
            {
                curNode = cameFrom[curNode];
                path.Add(curNode);
            }
            path.Reverse();

            var pts = path.Select(p => centerOf(p.X, p.Y)).ToList();
            pts.Insert(0, start);
            pts.Add(end);
            var reduced = MergeCollinear(pts);
            return new RouteResult { Success = true, Points = reduced };
        }

        private static GridPoint? FindNearestFree(GridPoint start, bool[,] blocked, int cols, int rows)
        {
            var q = new Queue<GridPoint>();
            var seen = new bool[cols, rows];
            q.Enqueue(start);
            seen[start.X, start.Y] = true;
            GridPoint[] dirs = new[] { new GridPoint(1, 0), new GridPoint(-1, 0), new GridPoint(0, 1), new GridPoint(0, -1) };
            while (q.Count > 0)
            {
                var t = q.Dequeue();
                if (!blocked[t.X, t.Y]) return t;
                foreach (var d in dirs)
                {
                    int nx = t.X + d.X, ny = t.Y + d.Y;
                    if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) continue;
                    if (seen[nx, ny]) continue;
                    seen[nx, ny] = true;
                    q.Enqueue(new GridPoint(nx, ny));
                }
            }
            return null;
        }

        private static List<Point> MergeCollinear(List<Point> pts)
        {
            if (pts == null || pts.Count <= 2) return pts;
            var res = new List<Point> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var prev = res.Last();
                var cur = pts[i];
                var next = pts[i + 1];
                var v1 = new Vector(cur.X - prev.X, cur.Y - prev.Y);
                var v2 = new Vector(next.X - cur.X, next.Y - cur.Y);
                if (v1.Length > 0) v1.Normalize();
                if (v2.Length > 0) v2.Normalize();
                if (Math.Abs(Vector.CrossProduct(v1, v2)) < 1e-6 && Vector.Multiply(v1, v2) > 0.999)
                {
                    continue;
                }
                res.Add(cur);
            }
            res.Add(pts.Last());
            return res.Where((p, idx) => idx == 0 || p != res[idx - 1]).ToList();
        }
    }
}
