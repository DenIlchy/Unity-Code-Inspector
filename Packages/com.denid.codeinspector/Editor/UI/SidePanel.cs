using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Right-hand panel: selected node info + events/messages + grouped
    /// incoming/outgoing edge lists. Phase 5 adds parameter tweaking here.
    /// </summary>
    public sealed class SidePanel : VisualElement
    {
        /// <summary>Navigate within the currently visible graph.</summary>
        public event Action<string> NavigateToNode;
        /// <summary>Navigate to a node in the Scene view (switches view).</summary>
        public event Action<string> NavigateToSceneNode;

        readonly Label titleLabel;
        readonly Label subtitleLabel;
        readonly VisualElement badgesBox;
        readonly VisualElement infoBox;
        readonly ScrollView usesList;
        readonly ScrollView usedByList;
        readonly VisualElement contentBox;
        readonly Label emptyLabel;
        readonly TweakSection tweak;
        readonly VisualElement liveBox;
        readonly VisualElement liveLinesBox;
        readonly VisualElement tweaksBox;
        string liveNodeId;
        string tweaksSignature;

        CodeGraph graph;
        GraphFilterResult filtered;
        CodeGraph typeGraph;
        CodeGraph sceneGraph;
        string currentNodeId;

        /// <summary>Raised when an inspected object value changes (for node pulse).</summary>
        public event Action<UnityEngine.Object> InspectedObjectChanged
        {
            add => tweak.InspectedObjectChanged += value;
            remove => tweak.InspectedObjectChanged -= value;
        }

        /// <summary>Play Mode live data providers (set by the window).</summary>
        public Func<GraphNode, List<string>> LiveChangesProvider;
        public Func<GraphNode, List<string>> LiveEventsProvider;
        public Func<GraphNode, List<string>> LiveContactsProvider;
        public Func<GraphNode, List<TweakRecord>> TweaksProvider;
        public Action<TweakRecord, GraphNode> TweakRevertRequested;

        public SidePanel()
        {
            AddToClassList("ci-sidepanel");

            emptyLabel = new Label("Select a node to see its connections.");
            emptyLabel.AddToClassList("ci-sidepanel-sub");
            Add(emptyLabel);

            contentBox = new VisualElement();
            contentBox.style.display = DisplayStyle.None;
            Add(contentBox);

            titleLabel = new Label();
            titleLabel.AddToClassList("ci-sidepanel-title");
            contentBox.Add(titleLabel);

            subtitleLabel = new Label();
            subtitleLabel.AddToClassList("ci-sidepanel-sub");
            contentBox.Add(subtitleLabel);

            badgesBox = new VisualElement();
            badgesBox.AddToClassList("ci-node-badges");
            contentBox.Add(badgesBox);

            tweak = new TweakSection();
            contentBox.Add(tweak);

            liveBox = new VisualElement();
            liveLinesBox = new VisualElement();
            liveBox.Add(liveLinesBox);
            tweaksBox = new VisualElement();
            liveBox.Add(tweaksBox);
            contentBox.Add(liveBox);

            infoBox = new VisualElement();
            contentBox.Add(infoBox);

            var usesHeader = new Label("Uses →");
            usesHeader.AddToClassList("ci-sidepanel-section");
            contentBox.Add(usesHeader);
            usesList = new ScrollView();
            contentBox.Add(usesList);

            var usedByHeader = new Label("Used by ←");
            usedByHeader.AddToClassList("ci-sidepanel-section");
            contentBox.Add(usedByHeader);
            usedByList = new ScrollView();
            contentBox.Add(usedByList);
        }

        public void SetContext(CodeGraph graph, GraphFilterResult filtered,
            CodeGraph typeGraph = null, CodeGraph sceneGraph = null)
        {
            this.graph = graph;
            this.filtered = filtered;
            this.typeGraph = typeGraph;
            this.sceneGraph = sceneGraph;
            ShowNode(currentNodeId);
        }

        public void ShowNode(string nodeId)
        {
            currentNodeId = nodeId;
            liveNodeId = null;
            usesList.Clear();
            usedByList.Clear();
            badgesBox.Clear();
            infoBox.Clear();

            if (graph == null || nodeId == null || !graph.Nodes.TryGetValue(nodeId, out var node))
            {
                tweak.Unbind();
                contentBox.style.display = DisplayStyle.None;
                emptyLabel.style.display = DisplayStyle.Flex;
                return;
            }

            tweak.Bind(node, typeGraph, sceneGraph);

            contentBox.style.display = DisplayStyle.Flex;
            emptyLabel.style.display = DisplayStyle.None;

            titleLabel.text = node.Label + (node.Inactive ? "  (inactive)" : "");
            subtitleLabel.text = BuildSubtitle(node);

            foreach (NodeBadges b in Enum.GetValues(typeof(NodeBadges)))
            {
                if (b == NodeBadges.None || !node.Badges.HasFlag(b))
                    continue;
                var pill = new Label(b.ToString());
                pill.AddToClassList("ci-badge");
                badgesBox.Add(pill);
            }

            switch (node.Kind)
            {
                case NodeKind.GameObject:
                    BuildGameObjectInfo(node);
                    break;
                case NodeKind.ScriptType:
                    BuildTypeInfo(node);
                    break;
            }

            FillEdgeList(usesList, nodeId, outgoing: true);
            FillEdgeList(usedByList, nodeId, outgoing: false);
            RefreshLive();
        }

        /// <summary>Refills the live sections for the current node (called per tick).</summary>
        public void RefreshLive()
        {
            var nodeChanged = liveNodeId != currentNodeId;
            liveNodeId = currentNodeId;

            GraphNode node = null;
            var entries = new List<(bool Header, string Text)>();
            if (graph != null && currentNodeId != null &&
                graph.Nodes.TryGetValue(currentNodeId, out node))
            {
                CollectLiveSection(entries, "Live values", LiveChangesProvider?.Invoke(node));
                CollectLiveSection(entries, "Events fired", LiveEventsProvider?.Invoke(node));
                CollectLiveSection(entries, "Contacts", LiveContactsProvider?.Invoke(node));
            }
            ReconcileLines(entries);

            var tweaks = node != null ? TweaksProvider?.Invoke(node) : null;
            var sig = TweakSignature(tweaks);
            if (nodeChanged || sig != tweaksSignature)
            {
                tweaksSignature = sig;
                RebuildTweaks(node, tweaks);
            }
        }

        void ReconcileLines(List<(bool Header, string Text)> entries)
        {
            var i = 0;
            for (; i < entries.Count; i++)
            {
                var cls = entries[i].Header ? "ci-sidepanel-section" : "ci-edge-entry";
                if (i < liveLinesBox.childCount)
                {
                    var label = (Label)liveLinesBox[i];
                    if (label.text != entries[i].Text)
                        label.text = entries[i].Text;
                    if (!label.ClassListContains(cls))
                    {
                        label.ClearClassList();
                        label.AddToClassList(cls);
                    }
                }
                else
                {
                    var label = new Label(entries[i].Text);
                    label.AddToClassList(cls);
                    liveLinesBox.Add(label);
                }
            }
            while (liveLinesBox.childCount > entries.Count)
                liveLinesBox.RemoveAt(liveLinesBox.childCount - 1);
        }

        static void CollectLiveSection(List<(bool, string)> entries, string title,
            List<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return;
            entries.Add((true, title));
            foreach (var line in lines)
                entries.Add((false, line));
        }

        static string TweakSignature(List<TweakRecord> tweaks)
        {
            if (tweaks == null || tweaks.Count == 0)
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (var r in tweaks)
            {
                sb.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r));
                sb.Append(';').Append(r.OriginalValue);
                sb.Append(';').Append(r.LastValue);
                sb.Append(';').Append(r.Status).Append('|');
            }
            return sb.ToString();
        }

        void RebuildTweaks(GraphNode node, List<TweakRecord> tweaks)
        {
            tweaksBox.Clear();
            if (node == null || tweaks == null || tweaks.Count == 0)
                return;
            var h = new Label("Play Mode changes (" + tweaks.Count + ")");
            h.AddToClassList("ci-sidepanel-section");
            tweaksBox.Add(h);
            foreach (var rec in tweaks)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                var reverted = rec.Status == (int)TweakStatus.Reverted;
                var label = new Label(
                    $"{rec.PropertyDisplay}  {rec.OriginalValue} → {rec.LastValue}" +
                    (reverted ? "  (reverted)" : ""));
                label.AddToClassList("ci-edge-entry");
                label.style.flexGrow = 1f;
                row.Add(label);
                var revert = new Button(() => TweakRevertRequested?.Invoke(rec, node))
                {
                    text = "Revert",
                };
                revert.AddToClassList("ci-tweak-revert");
                if (reverted)
                    revert.SetEnabled(false);
                row.Add(revert);
                tweaksBox.Add(row);
            }
        }

        string BuildSubtitle(GraphNode node)
        {
            if (node.Kind == NodeKind.GameObject || node.Kind == NodeKind.Asset)
                return string.IsNullOrEmpty(node.Caption) ? node.Kind.ToString() : node.Caption;
            var asmName = node.Type != null ? node.Type.Assembly.GetName().Name : "?";
            var s = node.Category + " · " + asmName;
            if (node.ExecutionOrder != 0)
                s += " · order " + node.ExecutionOrder;
            return s;
        }

        void SectionHeader(string text)
        {
            var h = new Label(text);
            h.AddToClassList("ci-sidepanel-section");
            infoBox.Add(h);
        }

        void BuildGameObjectInfo(GraphNode node)
        {
            if (node.Components != null && node.Components.Count > 0)
            {
                SectionHeader("Components");
                foreach (var c in node.Components)
                {
                    var row = new Label(
                        (c.Enabled ? "" : "[disabled] ") +
                        (c.Type != null ? c.Type.Name : "?"));
                    row.AddToClassList("ci-edge-entry");
                    var type = c.Type;
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (type != null)
                            ScriptLocator.Ping(type);
                    });
                    infoBox.Add(row);
                }
            }

            // UnityEvent persistent listeners on this GameObject.
            var eventEdges = new List<GraphEdge>();
            if (filtered != null)
            {
                foreach (var e in filtered.Edges)
                {
                    if (e.Kind == EdgeKind.UnityEventBinding && e.FromId == node.Id)
                        eventEdges.Add(e);
                }
            }
            if (eventEdges.Count > 0)
            {
                SectionHeader("Events");
                foreach (var e in eventEdges)
                {
                    var row = new Label(e.Label);
                    row.AddToClassList("ci-edge-entry");
                    var target = e.ToId;
                    row.RegisterCallback<ClickEvent>(_ => NavigateToNode?.Invoke(target));
                    infoBox.Add(row);
                }
            }

            // C# events declared by user scripts, with subscriber types.
            var csharp = new List<string>();
            if (typeGraph != null && node.Components != null)
            {
                foreach (var c in node.Components)
                {
                    if (!c.IsUserScript || c.Type == null)
                        continue;
                    var typeId = CodeGraph.NodeIdFor(c.Type);
                    foreach (var e in typeGraph.Edges)
                    {
                        if (e.Kind != EdgeKind.CSharpEventSubscription || e.ToId != typeId)
                            continue;
                        var sub = typeGraph.Nodes.TryGetValue(e.FromId, out var n) ? n.Label : "?";
                        csharp.Add(e.Label + "  ←  " + sub);
                    }
                }
            }
            if (csharp.Count > 0)
            {
                SectionHeader("C# events");
                foreach (var s in csharp)
                {
                    var row = new Label(s);
                    row.AddToClassList("ci-edge-entry");
                    infoBox.Add(row);
                }
            }

            // Unity messages handled by its scripts.
            var messages = new List<string>();
            if (node.Components != null)
            {
                foreach (var c in node.Components)
                {
                    if (!c.IsUserScript || c.Type == null)
                        continue;
                    foreach (var m in c.Type.GetMethods(
                                 System.Reflection.BindingFlags.DeclaredOnly |
                                 System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic))
                    {
                        if (m.Name.StartsWith("On", StringComparison.Ordinal) ||
                            m.Name == "Update" || m.Name == "FixedUpdate" ||
                            m.Name == "LateUpdate" || m.Name == "Awake" || m.Name == "Start")
                            messages.Add(m.Name);
                    }
                }
            }
            if (messages.Count > 0)
            {
                SectionHeader("Unity messages");
                messages.Sort(StringComparer.Ordinal);
                var row = new Label(string.Join(", ", messages));
                row.AddToClassList("ci-edge-entry");
                infoBox.Add(row);
            }
        }

        void BuildTypeInfo(GraphNode node)
        {
            if (node.Type == null || !typeof(MonoBehaviour).IsAssignableFrom(node.Type) ||
                sceneGraph == null)
                return;
            var instances = new List<GraphNode>();
            foreach (var n in sceneGraph.Nodes.Values)
            {
                if (n.Kind != NodeKind.GameObject || n.Components == null)
                    continue;
                foreach (var c in n.Components)
                {
                    if (c.Type == node.Type)
                    {
                        instances.Add(n);
                        break;
                    }
                }
            }
            if (instances.Count == 0)
                return;
            SectionHeader("Instances in open scenes");
            foreach (var inst in instances)
            {
                var row = new Label(inst.Label + (string.IsNullOrEmpty(inst.Caption) ? "" : "  (" + inst.Caption + ")"));
                row.AddToClassList("ci-edge-entry");
                var id = inst.Id;
                row.RegisterCallback<ClickEvent>(_ => NavigateToSceneNode?.Invoke(id));
                infoBox.Add(row);
            }
        }

        void FillEdgeList(ScrollView list, string nodeId, bool outgoing)
        {
            if (filtered == null)
                return;
            var grouped = new Dictionary<EdgeKind, List<GraphEdge>>();
            foreach (var e in filtered.Edges)
            {
                var other = outgoing ? e.ToId : e.FromId;
                var self = outgoing ? e.FromId : e.ToId;
                if (self != nodeId)
                    continue;
                if (!grouped.TryGetValue(e.Kind, out var l))
                    grouped[e.Kind] = l = new List<GraphEdge>();
                l.Add(e);
            }

            foreach (var kv in grouped)
            {
                var header = new Label(kv.Key.ToString());
                header.AddToClassList("ci-member-group");
                list.Add(header);
                foreach (var e in kv.Value)
                {
                    var other = outgoing ? e.ToId : e.FromId;
                    var name = graph.Nodes.TryGetValue(other, out var n) ? n.Label : other;
                    var row = new Label(name + "  —  " + e.Label + (e.Approximate ? " (approx)" : ""));
                    row.AddToClassList("ci-edge-entry");
                    var target = other;
                    row.RegisterCallback<ClickEvent>(_ => NavigateToNode?.Invoke(target));
                    list.Add(row);
                }
            }
        }
    }
}
