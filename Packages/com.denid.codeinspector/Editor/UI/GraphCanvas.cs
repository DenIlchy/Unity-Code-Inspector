using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Pan/zoom canvas that renders a filtered CodeGraph: nodes (draggable),
    /// bezier edges, hover tooltips, selection dimming and viewport culling.
    /// </summary>
    public sealed class GraphCanvas : VisualElement
    {
        public const float MinZoom = 0.1f;
        public const float MaxZoom = 2.5f;

        readonly VisualElement contentLayer;
        readonly EdgeLayer edgeLayer;
        readonly Label edgeTooltip;
        readonly Dictionary<string, NodeView> nodeViews = new Dictionary<string, NodeView>();
        readonly Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();
        readonly Dictionary<string, Vector2> sizes = new Dictionary<string, Vector2>();
        readonly Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>();

        CodeGraph graph;
        GraphFilterResult filtered;
        Func<EdgeKind, Color> edgeColorOf = EdgeColors.Get;
        LayoutStore layoutStore;
        string viewName = "Architecture";

        readonly List<GraphEdge> transientEdges = new List<GraphEdge>();
        readonly Dictionary<GraphEdge, Label> flashLabels = new Dictionary<GraphEdge, Label>();

        Vector2 pan;
        float zoom = 1f;
        bool panning;
        Vector2 panStart;
        Vector2 panStartMouse;
        string selectedId;

        public event Action<string> NodeSelected;
        public event Action<Type> NodeOpenRequested;
        /// <summary>Raised on node double-click for any node kind.</summary>
        public event Action<GraphNode> NodeActivated;

        public string SelectedNodeId => selectedId;
        public int VisibleNodeCount => nodeViews.Count;

        public string ViewName
        {
            get => viewName;
            set => viewName = value;
        }

        /// <summary>Finds the visible node whose Target is the given object.</summary>
        public string FindNodeIdForTarget(UnityEngine.Object target)
        {
            if (target == null || graph == null)
                return null;
            foreach (var kv in nodeViews)
            {
                if (kv.Value.Node.Target == target)
                    return kv.Key;
            }
            return null;
        }

        public GraphCanvas(LayoutStore store = null, string viewName = "Architecture")
        {
            layoutStore = store ?? new LayoutStore();
            this.viewName = viewName;
            AddToClassList("ci-canvas");
            style.flexGrow = 1f;
            style.overflow = Overflow.Hidden;

            contentLayer = new VisualElement { name = "content" };
            contentLayer.style.position = Position.Absolute;
            contentLayer.style.transformOrigin = new TransformOrigin(0, 0);
            contentLayer.style.left = 0;
            contentLayer.style.top = 0;
            contentLayer.pickingMode = PickingMode.Ignore;
            Add(contentLayer);

            edgeLayer = new EdgeLayer();
            contentLayer.Add(edgeLayer);

            edgeTooltip = new Label();
            edgeTooltip.AddToClassList("ci-edge-tooltip");
            edgeTooltip.style.position = Position.Absolute;
            edgeTooltip.style.display = DisplayStyle.None;
            edgeTooltip.pickingMode = PickingMode.Ignore;
            Add(edgeTooltip);

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<GeometryChangedEvent>(_ => UpdateCulling());
            focusable = true;
            schedule?.Execute(FlashTick).Every(80);
        }

        public void SetEdgeColors(Func<EdgeKind, Color> colorOf)
        {
            edgeColorOf = colorOf ?? EdgeColors.Get;
        }

        // ---- Binding / layout -------------------------------------------------

        public void Bind(CodeGraph graph, GraphFilterResult filtered)
        {
            var previousPositions = new Dictionary<string, Vector2>(positions);
            this.graph = graph;
            this.filtered = filtered;
            var keepSelection = selectedId != null && filtered != null && filtered.NodeIds.Contains(selectedId);
            if (!keepSelection)
                selectedId = null;
            panning = false;
            edgeTooltip.style.display = DisplayStyle.None;

            foreach (var kv in nodeViews)
                kv.Value.RemoveFromHierarchy();
            nodeViews.Clear();
            transientEdges.Clear();
            foreach (var kv in flashLabels)
                kv.Value.RemoveFromHierarchy();
            flashLabels.Clear();
            edgeLayer.ClearFlashes();
            positions.Clear();
            sizes.Clear();
            nodeRects.Clear();

            if (graph == null || filtered == null)
            {
                edgeLayer.UpdateGeometry(nodeRects, new List<GraphEdge>(), edgeColorOf);
                return;
            }

            foreach (var id in filtered.NodeIds)
                sizes[id] = new Vector2(NodeView.EstimatedWidth, NodeView.EstimatedHeight);

            var edges = new List<(string, string)>(filtered.Edges.Count);
            foreach (var e in filtered.Edges)
                edges.Add((e.FromId, e.ToId));

            var auto = GraphLayout.Layout(filtered.NodeIds, edges, sizes);
            foreach (var id in filtered.NodeIds)
            {
                var pos = auto.TryGetValue(id, out var p) ? p : Vector2.zero;
                var key = StoreKey(id);
                if (previousPositions.TryGetValue(id, out var prev))
                    pos = prev;
                else if (layoutStore != null && layoutStore.TryGet(viewName, key, out var saved))
                    pos = saved;
                positions[id] = pos;
            }

            foreach (var id in filtered.NodeIds)
            {
                var view = CreateNodeView(id, graph.Nodes[id]);
                view.ContentPosition = positions[id];
                view.Dragged += OnNodeDragged;
                view.DragEnded += OnNodeDragEnded;
                view.Clicked += OnNodeClicked;
                view.DoubleClicked += OnNodeDoubleClicked;
                view.RegisterCallback<GeometryChangedEvent>(OnNodeMeasured);
                nodeViews[id] = view;
                contentLayer.Add(view);
            }

            RebuildNodeRects();
            RefreshEdgeGeometry();
            UpdateCulling();
        }

        internal static NodeView CreateNodeView(string id, GraphNode node)
        {
            return new NodeView(id, node);
        }

        void OnNodeMeasured(GeometryChangedEvent e)
        {
            if (!(e.target is NodeView view))
                return;
            var size = e.newRect.size;
            if (size.x < 10f || size.y < 10f)
                return;
            if (sizes.TryGetValue(view.NodeId, out var old) &&
                Mathf.Abs(old.x - size.x) < 1f && Mathf.Abs(old.y - size.y) < 1f)
                return;
            sizes[view.NodeId] = size;
            RebuildNodeRects();
            RefreshEdgeGeometry();
        }

        void RebuildNodeRects()
        {
            nodeRects.Clear();
            foreach (var id in positions.Keys)
            {
                var s = sizes.TryGetValue(id, out var v) ? v : new Vector2(NodeView.EstimatedWidth, NodeView.EstimatedHeight);
                nodeRects[id] = new Rect(positions[id], s);
            }
        }

        void RefreshEdgeGeometry()
        {
            var all = new List<GraphEdge>(filtered?.Edges ?? new List<GraphEdge>());
            all.AddRange(transientEdges);
            edgeLayer.UpdateGeometry(nodeRects, all, edgeColorOf);
            UpdateFlashLabels();
        }

        // ---- Pan / zoom --------------------------------------------------------

        public Vector2 Pan
        {
            get => pan;
            private set
            {
                pan = value;
                ApplyTransform();
            }
        }

        public float Zoom
        {
            get => zoom;
            private set
            {
                zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
                ApplyTransform();
            }
        }

        void ApplyTransform()
        {
            contentLayer.style.translate = new Translate(pan.x, pan.y);
            contentLayer.style.scale = new Scale(new Vector3(zoom, zoom, 1f));
            UpdateCulling();
            edgeLayer.MarkDirtyRepaint();
        }

        public Vector2 LocalToContent(Vector2 local) => (local - pan) / zoom;
        public Vector2 ContentToLocal(Vector2 content) => content * zoom + pan;

        void OnWheel(WheelEvent e)
        {
            var local = (Vector2)e.localMousePosition;
            var before = LocalToContent(local);
            var factor = e.delta.y > 0 ? 0.9f : 1.1f;
            Zoom = zoom * factor;
            Pan = local - before * zoom;
            e.StopPropagation();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            // Only fires for background clicks: NodeViews stop propagation.
            // Left drag on empty space, Alt+left, or middle drag pans the canvas.
            if (e.button == 0)
                ClearSelection();
            if (e.button == 0 || e.button == 2 || (e.button == 0 && e.altKey))
            {
                panning = true;
                panStart = pan;
                panStartMouse = (Vector2)e.position;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
            }
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (panning)
            {
                var delta = (Vector2)e.position - panStartMouse;
                Pan = panStart + delta;
                e.StopPropagation();
            }
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!panning)
                return;
            panning = false;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMouseMove(MouseMoveEvent e)
        {
            var local = (Vector2)e.localMousePosition;
            var content = LocalToContent(local);
            var hit = edgeLayer.HitTest(content, zoom);
            if (hit != edgeLayer.HoveredEdge)
            {
                edgeLayer.HoveredEdge = hit;
                edgeLayer.MarkDirtyRepaint();
            }
            if (hit != null)
            {
                var text = hit.Kind + ": " + hit.Label + (hit.Approximate ? " (approximate)" : "");
                if (hit.Details != null)
                    foreach (var line in hit.Details)
                        text += "\n" + line;
                edgeTooltip.text = text;
                edgeTooltip.style.display = DisplayStyle.Flex;
                edgeTooltip.style.left = local.x + 14f;
                edgeTooltip.style.top = local.y + 14f;
            }
            else
            {
                edgeTooltip.style.display = DisplayStyle.None;
            }
        }

        // ---- Node interaction --------------------------------------------------

        void OnNodeDragged(NodeView view, Vector2 pos)
        {
            positions[view.NodeId] = pos;
            RebuildNodeRects();
            RefreshEdgeGeometry();
            UpdateCulling();
        }

        string StoreKey(string id)
        {
            return graph != null && graph.Nodes.TryGetValue(id, out var n) && n.PersistKey != null
                ? n.PersistKey
                : id;
        }

        void OnNodeDragEnded(NodeView view)
        {
            layoutStore?.Set(viewName, StoreKey(view.NodeId), positions[view.NodeId]);
        }

        void OnNodeClicked(NodeView view)
        {
            Select(view.NodeId);
            ScriptLocator.Ping(view.Node.Type);
        }

        void OnNodeDoubleClicked(NodeView view)
        {
            if (NodeActivated != null)
            {
                NodeActivated.Invoke(view.Node);
                return;
            }
            if (view.Node.Type != null)
            {
                NodeOpenRequested?.Invoke(view.Node.Type);
                if (NodeOpenRequested == null)
                    ScriptLocator.Open(view.Node.Type);
            }
        }

        /// <summary>Node ids currently inside the viewport (all, when unmeasured).</summary>
        public IEnumerable<string> VisibleNodeIds
        {
            get
            {
                var viewport = contentRect;
                if (viewport.width <= 0 || viewport.height <= 0)
                {
                    foreach (var id in nodeRects.Keys)
                        yield return id;
                    yield break;
                }
                var topLeft = LocalToContent(viewport.min);
                var bottomRight = LocalToContent(viewport.max);
                var contentViewport = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
                foreach (var kv in nodeRects)
                    if (kv.Value.Overlaps(contentViewport))
                        yield return kv.Key;
            }
        }

        /// <summary>Re-apply live state (inactive / component enabled) to a node view.</summary>
        public void RefreshNodeLive(string nodeId)
        {
            if (nodeId != null && nodeViews.TryGetValue(nodeId, out var v))
                v.RefreshLive();
        }

        /// <summary>Adds a runtime-only edge; returns null when a node is missing.</summary>
        public GraphEdge AddTransientEdge(string fromId, string toId, EdgeKind kind, string label)
        {
            if (!nodeRects.ContainsKey(fromId) || !nodeRects.ContainsKey(toId))
                return null;
            var edge = new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Kind = kind,
                Label = label,
                Transient = true,
            };
            transientEdges.Add(edge);
            RefreshEdgeGeometry();
            return edge;
        }

        public void RemoveTransientEdge(GraphEdge edge)
        {
            if (edge == null)
                return;
            transientEdges.Remove(edge);
            if (flashLabels.TryGetValue(edge, out var l))
            {
                l.RemoveFromHierarchy();
                flashLabels.Remove(edge);
            }
            RefreshEdgeGeometry();
        }

        /// <summary>Flash one edge (fire counter + glow).</summary>
        public void FlashEdge(GraphEdge edge)
        {
            if (edge == null)
                return;
            edgeLayer.Flash(edge);
            UpdateFlashLabels();
        }

        /// <summary>Flash all edges matching the predicate.</summary>
        public void FlashEdges(Predicate<GraphEdge> match)
        {
            if (filtered != null)
            {
                foreach (var e in filtered.Edges)
                    if (match(e))
                        edgeLayer.Flash(e);
            }
            foreach (var e in transientEdges)
                if (match(e))
                    edgeLayer.Flash(e);
            UpdateFlashLabels();
        }

        /// <summary>Flash an edge with an explicit accumulated fire count.</summary>
        public void SetEdgeFlashCount(GraphEdge edge, int count)
        {
            if (edge == null)
                return;
            edgeLayer.Flash(edge, count);
            UpdateFlashLabels();
        }

        public int FlashCountFor(GraphEdge edge) => edgeLayer.FlashCountFor(edge);

        void FlashTick()
        {
            if (edgeLayer.HasActiveFlashes)
            {
                edgeLayer.MarkDirtyRepaint();
                UpdateFlashLabels();
            }
        }

        void UpdateFlashLabels()
        {
            if (filtered == null && transientEdges.Count == 0)
                return;
            void Sync(GraphEdge e)
            {
                var count = edgeLayer.FlashCountFor(e);
                if (count <= 0)
                    return;
                if (!flashLabels.TryGetValue(e, out var l))
                {
                    l = new Label();
                    l.AddToClassList("ci-edge-firecount");
                    l.style.position = Position.Absolute;
                    l.pickingMode = PickingMode.Ignore;
                    flashLabels[e] = l;
                    contentLayer.Add(l);
                }
                l.text = "×" + count;
                if (edgeLayer.TryGetMidpoint(e, out var mid))
                {
                    l.style.left = mid.x + 6f;
                    l.style.top = mid.y - 8f;
                }
            }
            if (filtered != null)
                foreach (var e in filtered.Edges)
                    Sync(e);
            foreach (var e in transientEdges)
                Sync(e);
        }

        /// <summary>Pulse the node view for a bound value change.</summary>
        public void PulseNode(string nodeId)
        {
            if (nodeId != null && nodeViews.TryGetValue(nodeId, out var v))
                v.Pulse();
        }

        /// <summary>Pulse every node that owns or hosts the given object.</summary>
        public void PulseObject(UnityEngine.Object target)
        {
            if (graph == null || target == null)
                return;
            foreach (var id in GraphPulse.NodeIdsForObjects(new[] { target }, graph))
                PulseNode(id);
        }

        public void Select(string nodeId)
        {
            selectedId = nodeId;
            edgeLayer.HighlightNodeId = nodeId;
            edgeLayer.MarkDirtyRepaint();
            foreach (var kv in nodeViews)
            {
                var sel = kv.Key == nodeId;
                kv.Value.SetSelected(sel);
                var dim = !string.IsNullOrEmpty(nodeId) && !sel && !IsNeighbor(nodeId, kv.Key);
                kv.Value.SetDimmed(dim);
            }
            NodeSelected?.Invoke(nodeId);
        }

        public void ClearSelection()
        {
            selectedId = null;
            edgeLayer.HighlightNodeId = null;
            edgeLayer.MarkDirtyRepaint();
            foreach (var kv in nodeViews)
            {
                kv.Value.SetSelected(false);
                kv.Value.SetDimmed(false);
            }
            NodeSelected?.Invoke(null);
        }

        bool IsNeighbor(string a, string b)
        {
            if (filtered == null)
                return false;
            foreach (var e in filtered.Edges)
            {
                if ((e.FromId == a && e.ToId == b) || (e.FromId == b && e.ToId == a))
                    return true;
            }
            return false;
        }

        // ---- Culling / framing -------------------------------------------------

        void UpdateCulling()
        {
            var viewport = contentRect;
            if (viewport.width <= 0 || viewport.height <= 0)
                return;
            var topLeft = LocalToContent(viewport.min);
            var bottomRight = LocalToContent(viewport.max);
            var contentViewport = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
            edgeLayer.ViewportContentRect = contentViewport;
            foreach (var kv in nodeViews)
            {
                var visible = nodeRects.TryGetValue(kv.Key, out var r) && r.Overlaps(contentViewport);
                var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (kv.Value.style.display.value != display)
                    kv.Value.style.display = display;
            }
            edgeLayer.MarkDirtyRepaint();
        }

        public void FrameAll()
        {
            if (nodeRects.Count == 0)
                return;
            var bounds = new Rect();
            var first = true;
            foreach (var r in nodeRects.Values)
            {
                bounds = first ? r : Encapsulate(bounds, r);
                first = false;
            }
            FrameRect(bounds);
        }

        public void FrameNode(string nodeId)
        {
            if (!nodeRects.TryGetValue(nodeId, out var r))
                return;
            var padded = new Rect(r.x - 80f, r.y - 60f, r.width + 160f, r.height + 120f);
            FrameRect(padded);
        }

        static Rect Encapsulate(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        void FrameRect(Rect bounds)
        {
            var viewport = contentRect;
            if (viewport.width <= 0 || viewport.height <= 0)
                return;
            var zx = viewport.width / Mathf.Max(1f, bounds.width);
            var zy = viewport.height / Mathf.Max(1f, bounds.height);
            Zoom = Mathf.Min(zx, zy, MaxZoom);
            Pan = new Vector2(
                viewport.center.x - bounds.center.x * zoom,
                viewport.center.y - bounds.center.y * zoom);
        }

        public void ResetLayout()
        {
            layoutStore?.ClearView(viewName);
            if (graph != null && filtered != null)
                Bind(graph, filtered);
        }
    }

    /// <summary>Edge-kind color palette (skin-aware).</summary>
    public static class EdgeColors
    {
        static readonly Dictionary<EdgeKind, Color> colors = new Dictionary<EdgeKind, Color>
        {
            { EdgeKind.FieldReference, new Color(0.10f, 0.90f, 0.90f) },
            { EdgeKind.MethodCall, new Color(1.00f, 0.60f, 0.10f) },
            { EdgeKind.GetComponent, new Color(0.95f, 0.85f, 0.20f) },
            { EdgeKind.TypeUsage, new Color(0.40f, 0.40f, 0.40f) },
            { EdgeKind.UnityEventBinding, new Color(1.00f, 0.50f, 0.80f) },
            { EdgeKind.CSharpEventSubscription, new Color(0.90f, 0.20f, 0.90f) },
            { EdgeKind.UnityMessage, new Color(0.95f, 0.25f, 0.25f) },
            { EdgeKind.SendMessage, new Color(0.60f, 0.60f, 0.60f) },
            { EdgeKind.FieldAccess, new Color(0.50f, 0.75f, 1.00f) },
        };

        public static Color Get(EdgeKind kind)
        {
            if (kind == EdgeKind.Inheritance)
                return UnityEditor.EditorGUIUtility.isProSkin ? Color.white : Color.black;
            return colors.TryGetValue(kind, out var c) ? c : Color.gray;
        }

        public static IEnumerable<EdgeKind> AllKinds => (EdgeKind[])Enum.GetValues(typeof(EdgeKind));
    }
}
