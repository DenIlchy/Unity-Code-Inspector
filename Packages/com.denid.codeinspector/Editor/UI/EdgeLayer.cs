using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Draws all graph edges as cubic beziers with arrowheads using Painter2D.
    /// Lives inside the transformed content layer, so coordinates are content space.
    /// Also owns edge hit-testing for hover tooltips.
    /// </summary>
    public sealed class EdgeLayer : VisualElement
    {
        public sealed class EdgeVisual
        {
            public GraphEdge Edge;
            public Color Color;
            public Vector2[] Samples;
            public Rect Bounds;
        }

        const int SampleCount = 24;
        const float HitDistance = 6f;

        public sealed class FlashInfo
        {
            public float Start;
            public int Count;
        }

        const float FlashDuration = 0.6f;

        readonly List<EdgeVisual> edges = new List<EdgeVisual>();
        readonly Dictionary<GraphEdge, EdgeVisual> edgeByRef = new Dictionary<GraphEdge, EdgeVisual>();
        readonly Dictionary<GraphEdge, FlashInfo> flashes = new Dictionary<GraphEdge, FlashInfo>();

        /// <summary>All flashed edges with their fire info (for label updates).</summary>
        public IEnumerable<KeyValuePair<GraphEdge, FlashInfo>> Flashes => flashes;
        public string HighlightNodeId;
        public GraphEdge HoveredEdge;
        public Rect ViewportContentRect = new Rect(0, 0, float.MaxValue, float.MaxValue);

        public EdgeLayer()
        {
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Records a fire; the edge glows briefly. Count accumulates until cleared.</summary>
        public void Flash(GraphEdge e)
        {
            Flash(e, (flashes.TryGetValue(e, out var f0) ? f0.Count : 0) + 1);
        }

        /// <summary>Records a fire with an explicit total (restored after rebuilds).</summary>
        public void Flash(GraphEdge e, int count)
        {
            if (!flashes.TryGetValue(e, out var f))
                flashes[e] = f = new FlashInfo();
            f.Start = Time.realtimeSinceStartup;
            f.Count = count;
            MarkDirtyRepaint();
        }

        public int FlashCountFor(GraphEdge e) =>
            flashes.TryGetValue(e, out var f) ? f.Count : 0;

        public bool HasActiveFlashes
        {
            get
            {
                var now = Time.realtimeSinceStartup;
                foreach (var f in flashes.Values)
                    if (now - f.Start < FlashDuration)
                        return true;
                return false;
            }
        }

        /// <summary>Fire counts persist until cleared (graph rebuild / play exit).</summary>
        public void ClearFlashes()
        {
            flashes.Clear();
            MarkDirtyRepaint();
        }

        public bool TryGetMidpoint(GraphEdge e, out Vector2 pos)
        {
            if (edgeByRef.TryGetValue(e, out var ev))
            {
                pos = ev.Samples[ev.Samples.Length / 2];
                return true;
            }
            pos = default;
            return false;
        }

        public void UpdateGeometry(Dictionary<string, Rect> nodeRects,
            List<GraphEdge> edgeList, Func<EdgeKind, Color> colorOf)
        {
            edges.Clear();
            edgeByRef.Clear();
            foreach (var e in edgeList)
            {
                if (!nodeRects.TryGetValue(e.FromId, out var a) ||
                    !nodeRects.TryGetValue(e.ToId, out var b))
                    continue;
                var from = new Vector2(a.xMax, a.center.y);
                var to = new Vector2(b.xMin, b.center.y);
                var ev = new EdgeVisual { Edge = e, Color = colorOf(e.Kind) };
                ev.Samples = Sample(from, to, SampleCount);
                ev.Bounds = ComputeBounds(ev.Samples);
                edges.Add(ev);
                edgeByRef[e] = ev;
            }
            ResizeToFit();
            MarkDirtyRepaint();
        }

        void ResizeToFit()
        {
            var max = new Vector2(4000f, 4000f);
            foreach (var e in edges)
            {
                max.x = Mathf.Max(max.x, e.Bounds.xMax + 40f);
                max.y = Mathf.Max(max.y, e.Bounds.yMax + 40f);
            }
            style.width = max.x;
            style.height = max.y;
        }

        static Vector2[] Sample(Vector2 from, Vector2 to, int count)
        {
            var dx = Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.5f);
            var p0 = from;
            var p1 = from + new Vector2(dx, 0f);
            var p2 = to - new Vector2(dx, 0f);
            var p3 = to;
            var pts = new Vector2[count + 1];
            for (var i = 0; i <= count; i++)
            {
                var t = i / (float)count;
                var u = 1f - t;
                pts[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            }
            return pts;
        }

        static Rect ComputeBounds(Vector2[] pts)
        {
            var min = pts[0];
            var max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var pad = new Vector2(12f, 12f);
            return Rect.MinMaxRect(min.x - pad.x, min.y - pad.y, max.x + pad.x, max.y + pad.y);
        }

        /// <summary>Returns the edge whose polyline is within hit range of a content-space point.</summary>
        public GraphEdge HitTest(Vector2 contentPos, float zoom)
        {
            var maxDist = HitDistance / Mathf.Max(0.01f, zoom);
            var best = maxDist;
            GraphEdge hit = null;
            foreach (var e in edges)
            {
                if (!e.Bounds.Contains(contentPos))
                    continue;
                var d = DistanceToPolyline(e.Samples, contentPos);
                if (d < best)
                {
                    best = d;
                    hit = e.Edge;
                }
            }
            return hit;
        }

        static float DistanceToPolyline(Vector2[] pts, Vector2 p)
        {
            var best = float.MaxValue;
            for (var i = 0; i < pts.Length - 1; i++)
            {
                var d = DistanceToSegment(pts[i], pts[i + 1], p);
                if (d < best)
                    best = d;
            }
            return best;
        }

        static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            var len = ab.sqrMagnitude;
            var t = len < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var painter = ctx.painter2D;
            foreach (var e in edges)
            {
                if (!e.Bounds.Overlaps(ViewportContentRect))
                    continue;

                var color = e.Color;
                var alpha = 1f;
                var lineWidth = 1.5f;
                if (!string.IsNullOrEmpty(HighlightNodeId))
                {
                    var connected = e.Edge.FromId == HighlightNodeId || e.Edge.ToId == HighlightNodeId;
                    if (!connected)
                        alpha = 0.15f;
                    else
                        lineWidth = 2.5f;
                }
                if (HoveredEdge == e.Edge)
                {
                    lineWidth = 3f;
                    alpha = 1f;
                }
                color.a = alpha;

                if (e.Edge.Approximate)
                    StrokeDashed(painter, e, color, lineWidth);
                else
                    StrokeSolid(painter, e, color, lineWidth);

                DrawArrowhead(painter, e, color);

                if (flashes.TryGetValue(e.Edge, out var f))
                {
                    var t = (Time.realtimeSinceStartup - f.Start) / FlashDuration;
                    if (t < 1f)
                    {
                        var glow = new Color(1f, 0.95f, 0.5f, (1f - t) * 0.3f);
                        StrokeSolid(painter, e, glow, lineWidth + 7f * (1f - t));
                        var bright = new Color(1f, 0.95f, 0.5f, 1f - t);
                        StrokeSolid(painter, e, bright, lineWidth + 2f);
                    }
                }
            }
        }

        static void StrokeSolid(Painter2D painter, EdgeVisual e, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(e.Samples[0]);
            for (var i = 1; i < e.Samples.Length; i++)
                painter.LineTo(e.Samples[i]);
            painter.Stroke();
        }

        static void StrokeDashed(Painter2D painter, EdgeVisual e, Color color, float width)
        {
            const float dashLen = 6f;
            const float gapLen = 4f;
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.fillColor = color;

            var drawing = true;
            var phaseLeft = dashLen;
            painter.BeginPath();
            painter.MoveTo(e.Samples[0]);
            var current = e.Samples[0];
            for (var i = 1; i < e.Samples.Length; i++)
            {
                var seg = e.Samples[i] - current;
                var segLen = seg.magnitude;
                var pos = current;
                var remaining = segLen;
                while (remaining > 0.001f)
                {
                    var step = Mathf.Min(phaseLeft, remaining);
                    var next = pos + seg.normalized * step;
                    if (drawing)
                        painter.LineTo(next);
                    else
                        painter.MoveTo(next);
                    phaseLeft -= step;
                    remaining -= step;
                    pos = next;
                    if (phaseLeft <= 0.001f)
                    {
                        drawing = !drawing;
                        phaseLeft = drawing ? dashLen : gapLen;
                    }
                }
                current = e.Samples[i];
            }
            painter.Stroke();
        }

        static void DrawArrowhead(Painter2D painter, EdgeVisual e, Color color)
        {
            var pts = e.Samples;
            var tip = pts[pts.Length - 1];
            var dir = (pts[pts.Length - 1] - pts[pts.Length - 4]);
            if (dir.sqrMagnitude < 1e-6f)
                dir = Vector2.right;
            dir.Normalize();
            var n = new Vector2(-dir.y, dir.x);
            var size = 7f;
            var b1 = tip - dir * size + n * (size * 0.5f);
            var b2 = tip - dir * size - n * (size * 0.5f);

            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(b1);
            painter.LineTo(b2);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
