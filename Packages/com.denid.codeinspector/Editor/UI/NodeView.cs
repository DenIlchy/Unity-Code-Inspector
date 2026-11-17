using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>A single node on the graph canvas: color strip, title, badges, members.</summary>
    public sealed class NodeView : VisualElement
    {
        public const float EstimatedWidth = 220f;
        public const float EstimatedHeight = 80f;

        public readonly string NodeId;
        public readonly GraphNode Node;

        public event Action<NodeView, Vector2> Dragged;
        public event Action<NodeView> DragEnded;
        public event Action<NodeView> Clicked;
        public event Action<NodeView> DoubleClicked;

        readonly VisualElement header;
        readonly Label titleLabel;
        readonly VisualElement badgesRow;
        readonly List<(ComponentInfo info, Label name)> compRows =
            new List<(ComponentInfo, Label)>();
        bool pointerDown;
        bool dragging;
        Vector2 pointerStartContent;
        Vector2 nodeStartPos;

        public NodeView(string nodeId, GraphNode node)
        {
            NodeId = nodeId;
            Node = node;
            AddToClassList("ci-node");
            AddToClassList("ci-node-" + node.Category.ToString().ToLowerInvariant());
            if (node.Kind == NodeKind.GameObject)
                AddToClassList("ci-node-gameobject");
            else if (node.Kind == NodeKind.Asset)
                AddToClassList("ci-node-asset");
            style.position = Position.Absolute;
            style.width = EstimatedWidth;
            if (node.Inactive)
                EnableInClassList("ci-node-inactive", true);

            header = new VisualElement { name = "header" };
            header.AddToClassList("ci-node-header");

            var strip = new VisualElement();
            strip.AddToClassList("ci-node-strip");
            header.Add(strip);

            var headerText = new VisualElement();
            headerText.AddToClassList("ci-node-headertext");
            titleLabel = new Label(node.Label + (node.Inactive ? "  (inactive)" : ""));
            titleLabel.AddToClassList("ci-node-title");
            headerText.Add(titleLabel);
            var captionText = !string.IsNullOrEmpty(node.Caption) ? node.Caption : node.Category.ToString();
            var caption = new Label(captionText);
            caption.AddToClassList("ci-node-caption");
            headerText.Add(caption);
            header.Add(headerText);
            Add(header);

            badgesRow = new VisualElement();
            badgesRow.AddToClassList("ci-node-badges");
            AddBadges(node);
            if (badgesRow.childCount > 0)
                Add(badgesRow);

            if (node.Components != null && node.Components.Count > 0)
            {
                var compsBox = new VisualElement();
                compsBox.AddToClassList("ci-node-comps");
                foreach (var c in node.Components)
                    compsBox.Add(ComponentRow(c));
                Add(compsBox);
            }

            if (node.Kind == NodeKind.ScriptType)
            {
                var foldout = new Foldout { text = "Members", value = false };
                foldout.AddToClassList("ci-node-members");
                var populated = false;
                foldout.RegisterValueChangedCallback(_ =>
                {
                    if (populated || !foldout.value)
                        return;
                    populated = true;
                    PopulateMembers(foldout, node.Members);
                });
                Add(foldout);
            }

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        VisualElement ComponentRow(ComponentInfo c)
        {
            var row = new VisualElement();
            row.AddToClassList("ci-comp-row");
            if (c.Type != null)
            {
                var img = new Image
                {
                    image = UnityEditor.EditorGUIUtility.ObjectContent(null, c.Type).image,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                img.AddToClassList("ci-comp-icon");
                row.Add(img);
            }
            var name = new Label(c.Type != null ? c.Type.Name : "?");
            name.AddToClassList("ci-comp-name");
            if (c.IsUserScript)
                name.AddToClassList("ci-comp-user");
            if (!c.Enabled)
                name.AddToClassList("ci-comp-disabled");
            compRows.Add((c, name));
            row.Add(name);
            if (c.Badges != NodeBadges.None)
            {
                var badges = new VisualElement();
                badges.AddToClassList("ci-comp-badges");
                void Pill(string text, string cls)
                {
                    var l = new Label(text);
                    l.AddToClassList("ci-badge");
                    l.AddToClassList(cls);
                    badges.Add(l);
                }
                if (c.Badges.HasFlag(NodeBadges.Update)) Pill("U", "ci-badge-update");
                if (c.Badges.HasFlag(NodeBadges.Trigger)) Pill("T", "ci-badge-trigger");
                if (c.Badges.HasFlag(NodeBadges.Collision)) Pill("C", "ci-badge-collision");
                if (c.Badges.HasFlag(NodeBadges.Mouse)) Pill("M", "ci-badge-mouse");
                if (c.Badges.HasFlag(NodeBadges.HasEvents)) Pill("E", "ci-badge-events");
                row.Add(badges);
            }
            return row;
        }

        void AddBadges(GraphNode node)
        {
            void Pill(string text, string cls, string tooltip)
            {
                var l = new Label(text);
                l.AddToClassList("ci-badge");
                l.AddToClassList(cls);
                l.tooltip = tooltip;
                badgesRow.Add(l);
            }

            var b = node.Badges;
            if (b.HasFlag(NodeBadges.Update)) Pill("Update", "ci-badge-update", "Has an Update() message");
            if (b.HasFlag(NodeBadges.FixedUpdate)) Pill("Fixed", "ci-badge-update", "Has a FixedUpdate() message");
            if (b.HasFlag(NodeBadges.LateUpdate)) Pill("Late", "ci-badge-update", "Has a LateUpdate() message");
            if (b.HasFlag(NodeBadges.Trigger)) Pill("Trigger", "ci-badge-trigger", "Has OnTrigger* messages");
            if (b.HasFlag(NodeBadges.Collision)) Pill("Collision", "ci-badge-collision", "Has OnCollision* messages");
            if (b.HasFlag(NodeBadges.Mouse)) Pill("Mouse", "ci-badge-mouse", "Has OnMouse* messages");
            if (b.HasFlag(NodeBadges.HasEvents)) Pill("Events", "ci-badge-events", "Declares C# events or UnityEvents");
            if (b.HasFlag(NodeBadges.BrokenEvent)) Pill("Broken event", "ci-badge-broken", "Has a broken UnityEvent persistent listener");
            if (b.HasFlag(NodeBadges.TriggerCollider)) Pill("Trigger collider", "ci-badge-trigger", "Has a trigger collider");
            if (b.HasFlag(NodeBadges.Rigidbody)) Pill("Rigidbody", "ci-badge-collision", "Has a Rigidbody");
            if (node.ExecutionOrder != 0)
                Pill("Order " + node.ExecutionOrder, "ci-badge-order", "Script execution order");
        }

        static void PopulateMembers(VisualElement container, List<GraphMember> members)
        {
            var groups = new (MemberKind Kind, string Title)[]
            {
                (MemberKind.Field, "Fields"),
                (MemberKind.Property, "Properties"),
                (MemberKind.Method, "Methods"),
                (MemberKind.Event, "Events"),
            };
            foreach (var (kind, title) in groups)
            {
                var any = false;
                foreach (var m in members)
                {
                    if (m.Kind != kind)
                        continue;
                    if (!any)
                    {
                        any = true;
                        var h = new Label(title);
                        h.AddToClassList("ci-member-group");
                        container.Add(h);
                    }
                    var row = new Label(m.Name + " : " + m.TypeName);
                    row.AddToClassList("ci-member");
                    container.Add(row);
                }
            }
        }

        public Vector2 ContentPosition
        {
            get => new Vector2(resolvedStyle.left, resolvedStyle.top);
            set
            {
                style.left = value.x;
                style.top = value.y;
            }
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0)
                return;
            pointerDown = true;
            dragging = false;
            nodeStartPos = new Vector2(resolvedStyle.left, resolvedStyle.top);
            // Convert pointer position into content (untransformed) space.
            pointerStartContent = parent.WorldToLocal(e.position);
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!pointerDown)
                return;
            var content = parent.WorldToLocal(e.position);
            var delta = content - pointerStartContent;
            if (!dragging && delta.sqrMagnitude < 9f)
                return;
            dragging = true;
            ContentPosition = nodeStartPos + delta;
            Dragged?.Invoke(this, ContentPosition);
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (e.button != 0 || !pointerDown)
                return;
            pointerDown = false;
            this.ReleasePointer(e.pointerId);
            if (dragging)
            {
                DragEnded?.Invoke(this);
            }
            else
            {
                var time = Time.realtimeSinceStartup;
                if (time - lastClickTime < 0.35f)
                    DoubleClicked?.Invoke(this);
                else
                    Clicked?.Invoke(this);
                lastClickTime = time;
            }
            dragging = false;
            e.StopPropagation();
        }

        float lastClickTime;

        public void SetSelected(bool selected)
        {
            EnableInClassList("ci-node-selected", selected);
        }

        public void SetDimmed(bool dimmed)
        {
            EnableInClassList("ci-node-dimmed", dimmed);
        }

        /// <summary>Re-apply live state (active / component enabled) after a monitor tick.</summary>
        public void RefreshLive()
        {
            EnableInClassList("ci-node-inactive", Node.Inactive);
            titleLabel.text = Node.Label + (Node.Inactive ? "  (inactive)" : "");
            foreach (var (info, name) in compRows)
                name.EnableInClassList("ci-comp-disabled", !info.Enabled);
        }

        public int PulseCount { get; private set; }

        /// <summary>Brief highlight used when a bound value changes.</summary>
        public void Pulse()
        {
            PulseCount++;
            AddToClassList("ci-node-pulse");
            schedule?.Execute(() => RemoveFromClassList("ci-node-pulse")).StartingIn(600);
        }
    }
}
