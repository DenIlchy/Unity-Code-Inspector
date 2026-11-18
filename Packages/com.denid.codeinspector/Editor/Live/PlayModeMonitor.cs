using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Play Mode live view: live enabled/active state, serialized-field change
    /// detection (round-robin fingerprinting), UnityEvent flashing relays,
    /// and trigger/collision probes. Owned by the window; ticked ~200 ms.
    /// </summary>
    public sealed class PlayModeMonitor
    {
        const int WatchBudgetPerTick = 40;
        const int MaxWatchedProperties = 64;
        const int RingSize = 20;
        const int ContactRingSize = 200;
        const float PulseCooldown = 0.5f;
        const double SlowTickMs = 5.0;
        const float TransientEdgeTtl = 2f;

        class Watcher
        {
            public string NodeId;
            public Component Component;
            public SerializedObject So;
            public List<ValueSnapshot.FieldValue> Snapshot;
        }

        /// <summary>A recorded trigger/collision contact.</summary>
        public struct ContactEntry
        {
            /// <summary>"go:<instanceId>" of the other object, or null.</summary>
            public string OtherId;
            public string OtherName;
            public string Message;
            public float Time;
        }

        class TransientEdge
        {
            public GraphEdge Edge;
            public float LastContact;
        }

        readonly List<Watcher> watchers = new List<Watcher>();
        readonly Dictionary<(int, string), UnityEventRelay.Registration> relays =
            new Dictionary<(int, string), UnityEventRelay.Registration>();
        readonly Dictionary<(int, string), List<GraphEdge>> relayEdges =
            new Dictionary<(int, string), List<GraphEdge>>();
        readonly HashSet<int> probedGos = new HashSet<int>();
        readonly List<TransientEdge> transientEdges = new List<TransientEdge>();
        readonly Dictionary<string, float> lastPulseAt = new Dictionary<string, float>();
        readonly Dictionary<string, Queue<string>> eventsLog = new Dictionary<string, Queue<string>>();
        readonly Dictionary<(int, string), int> eventFireCounts =
            new Dictionary<(int, string), int>();
        readonly Dictionary<string, Queue<ContactEntry>> contactsLog =
            new Dictionary<string, Queue<ContactEntry>>();
        readonly Dictionary<string, int> contactCounts = new Dictionary<string, int>();
        readonly Dictionary<Component, Queue<string>> changesLog =
            new Dictionary<Component, Queue<string>>();

        int watchCursor;
        bool slowTickWarned;
        bool contactHooked;

        /// <summary>A UnityEvent edge fired (edge, total count).</summary>
        public event Action<GraphEdge, int> UnityEventFired;
        /// <summary>A node's serialized values changed (pulse + panel update).</summary>
        public event Action<string> NodePulse;
        /// <summary>Probe contact: (self node id, other node id or null, message).</summary>
        public event Action<string, string, string> Contacted;
        /// <summary>One-off diagnostics.</summary>
        public event Action<string> Diagnostic;

        /// <summary>(Re)attach watchers, relays, and probes after a graph (re)build.</summary>
        public void Attach(CodeGraph graph, GraphCanvas canvas)
        {
            if (graph == null)
                return;
            BuildWatchers(graph);
            SyncRelays(graph);
            AttachProbes(graph);
            HookContacts();
            // Re-apply accumulated fire counts to the new edge objects.
            foreach (var e in graph.Edges)
            {
                if (e.Kind == EdgeKind.UnityEventBinding && e.SourceObject != null &&
                    e.EventPath != null &&
                    eventFireCounts.TryGetValue(
                        (e.SourceObject.GetInstanceID(), e.EventPath), out var n) && n > 0)
                    canvas?.SetEdgeFlashCount(e, n);
            }
            foreach (var t in transientEdges)
                canvas?.RemoveTransientEdge(t.Edge);
            transientEdges.Clear();
        }

        public void Clear()
        {
            watchers.Clear();
            changesLog.Clear();
            watchCursor = 0;
            lastPulseAt.Clear();
            foreach (var kv in relays)
                UnityEventRelay.Unregister(kv.Value);
            relays.Clear();
            relayEdges.Clear();
            probedGos.Clear();
            transientEdges.Clear();
            eventsLog.Clear();
            contactsLog.Clear();
            contactCounts.Clear();
            eventFireCounts.Clear();
            if (contactHooked)
            {
                CodeInspectorProbes.Contact -= OnProbeContact;
                contactHooked = false;
            }
        }

        // ---- Watchers (value fingerprinting) ----------------------------------

        void BuildWatchers(CodeGraph graph)
        {
            watchers.Clear();
            watchCursor = 0;
            foreach (var node in graph.Nodes.Values)
            {
                if (node.Components == null)
                    continue;
                foreach (var c in node.Components)
                {
                    if (!c.IsUserScript || c.Component == null || c.Component is Transform)
                        continue;
                    SerializedObject so;
                    try
                    {
                        so = new SerializedObject(c.Component);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    watchers.Add(new Watcher
                    {
                        NodeId = node.Id,
                        Component = c.Component,
                        So = so,
                        Snapshot = ValueSnapshot.Capture(so, MaxWatchedProperties),
                    });
                }
            }
        }

        /// <summary>One tick: live state for visible nodes + budgeted value diffs.</summary>
        public void Tick(CodeGraph graph, GraphCanvas canvas)
        {
            var sw = Stopwatch.StartNew();
            if (graph != null && canvas != null)
            {
                foreach (var id in canvas.VisibleNodeIds)
                {
                    if (graph.Nodes.TryGetValue(id, out var node) &&
                        node.Kind == NodeKind.GameObject && node.Target is GameObject go && go != null)
                    {
                        var inactive = !go.activeInHierarchy;
                        if (inactive != node.Inactive)
                        {
                            node.Inactive = inactive;
                            canvas.RefreshNodeLive(id);
                        }
                        if (node.Components != null)
                        {
                            var changed = false;
                            foreach (var c in node.Components)
                            {
                                if (c.Component == null)
                                    continue;
                                var en = IsEnabled(c.Component);
                                if (en != c.Enabled)
                                {
                                    c.Enabled = en;
                                    changed = true;
                                }
                            }
                            if (changed)
                                canvas.RefreshNodeLive(id);
                        }
                    }
                }
            }

            for (var i = watchers.Count - 1; i >= 0; i--)
            {
                if (watchers[i].Component == null)
                {
                    changesLog.Remove(watchers[i].Component);
                    watchers.RemoveAt(i);
                }
            }

            var processed = 0;
            var n = watchers.Count;
            watchCursor = n == 0 ? 0 : watchCursor % n;
            for (var k = 0; k < n && processed < WatchBudgetPerTick; k++)
            {
                var w = watchers[(watchCursor + k) % n];
                if (w.Component == null || w.So?.targetObject == null)
                    continue;
                processed++;
                try
                {
                    w.So.Update();
                }
                catch (Exception)
                {
                    continue;
                }
                var next = ValueSnapshot.Capture(w.So, MaxWatchedProperties);
                var diff = ValueSnapshot.Diff(w.Snapshot, next);
                w.Snapshot = next;
                if (diff.Count == 0)
                    continue;
                var lines = Recent(changesLog, w.Component);
                var t = Time.realtimeSinceStartup;
                foreach (var ch in diff)
                    lines.Enqueue($"{ch.Path}  {ch.OldValue} → {ch.NewValue}  · {t:0.0}s");
                if (lastPulseAt.TryGetValue(w.NodeId, out var last) && t - last < PulseCooldown)
                    continue;
                lastPulseAt[w.NodeId] = t;
                NodePulse?.Invoke(w.NodeId);
            }
            watchCursor = n == 0 ? 0 : (watchCursor + WatchBudgetPerTick) % n;

            var now = Time.realtimeSinceStartup;
            for (var i = transientEdges.Count - 1; i >= 0; i--)
            {
                if (now - transientEdges[i].LastContact > TransientEdgeTtl)
                {
                    canvas?.RemoveTransientEdge(transientEdges[i].Edge);
                    transientEdges.RemoveAt(i);
                }
            }

            sw.Stop();
            if (!slowTickWarned && sw.Elapsed.TotalMilliseconds > SlowTickMs)
            {
                slowTickWarned = true;
                Diagnostic?.Invoke(
                    $"PlayModeMonitor tick took {sw.Elapsed.TotalMilliseconds:0.0} ms " +
                    $"({n} watched components)");
            }
        }

        static bool IsEnabled(Component c)
        {
            switch (c)
            {
                case Collider col: return col.enabled;
                case Collider2D col2: return col2.enabled;
                case Renderer r: return r.enabled;
                case Behaviour b: return b.enabled;
                default: return true;
            }
        }

        static Queue<T> Recent<TK, T>(Dictionary<TK, Queue<T>> map, TK key,
            int cap = RingSize)
        {
            if (!map.TryGetValue(key, out var q))
                map[key] = q = new Queue<T>();
            while (q.Count >= cap)
                q.Dequeue();
            return q;
        }

        // ---- UnityEvent relays -------------------------------------------------

        void SyncRelays(CodeGraph graph)
        {
            var seen = new HashSet<(int, string)>();
            relayEdges.Clear();
            foreach (var e in graph.Edges)
            {
                if (e.Kind != EdgeKind.UnityEventBinding || e.SourceObject == null ||
                    string.IsNullOrEmpty(e.EventPath))
                    continue;
                var key = (e.SourceObject.GetInstanceID(), e.EventPath);
                seen.Add(key);
                if (!relayEdges.TryGetValue(key, out var edges))
                    relayEdges[key] = edges = new List<GraphEdge>();
                edges.Add(e);
                if (relays.ContainsKey(key))
                    continue;
                var reg = UnityEventRelay.Register(e.SourceObject, e.EventPath,
                    (_, __) => OnUnityEventFired(key));
                if (reg != null)
                    relays[key] = reg;
            }
            var stale = new List<(int, string)>();
            foreach (var kv in relays)
            {
                if (!seen.Contains(kv.Key))
                    stale.Add(kv.Key);
            }
            foreach (var key in stale)
            {
                UnityEventRelay.Unregister(relays[key]);
                relays.Remove(key);
            }
        }

        void OnUnityEventFired((int, string) key)
        {
            eventFireCounts.TryGetValue(key, out var n);
            eventFireCounts[key] = n + 1;
            if (!relayEdges.TryGetValue(key, out var edges))
                return;
            foreach (var edge in edges)
            {
                Recent(eventsLog, edge.FromId)
                    .Enqueue(edge.Label + "  · " + Time.realtimeSinceStartup.ToString("0.0") + "s");
                UnityEventFired?.Invoke(edge, n + 1);
            }
        }

        /// <summary>Recent UnityEvent fire lines for a source node.</summary>
        public List<string> EventsFor(string nodeId)
        {
            return eventsLog.TryGetValue(nodeId, out var q) ? new List<string>(q) : new List<string>();
        }

        // ---- Probes / contacts --------------------------------------------------

        void AttachProbes(CodeGraph graph)
        {
            foreach (var node in graph.Nodes.Values)
            {
                if (node.Kind != NodeKind.GameObject || !(node.Target is GameObject go) || go == null)
                    continue;
                var relevant = node.Badges.HasFlag(NodeBadges.TriggerCollider) ||
                               node.Badges.HasFlag(NodeBadges.Rigidbody) ||
                               node.Badges.HasFlag(NodeBadges.Trigger) ||
                               node.Badges.HasFlag(NodeBadges.Collision);
                if (!relevant)
                    continue;
                var id = go.GetInstanceID();
                if (probedGos.Contains(id))
                    continue;
                if (go.GetComponent<CodeInspectorProbe>() == null)
                {
                    var probe = go.AddComponent<CodeInspectorProbe>();
                    probe.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
                }
                probedGos.Add(id);
            }
        }

        /// <summary>Editor-side probe attach, exposed for tests.</summary>
        public static CodeInspectorProbe AttachProbe(GameObject go)
        {
            var existing = go.GetComponent<CodeInspectorProbe>();
            if (existing != null)
                return existing;
            var probe = go.AddComponent<CodeInspectorProbe>();
            probe.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            return probe;
        }

        void HookContacts()
        {
            if (contactHooked)
                return;
            contactHooked = true;
            CodeInspectorProbes.Contact += OnProbeContact;
        }

        void OnProbeContact(GameObject self, GameObject other, string message)
        {
            var selfId = self != null ? "go:" + self.GetInstanceID() : null;
            var otherId = other != null ? "go:" + other.GetInstanceID() : null;
            if (selfId == null)
                return;
            Recent(contactsLog, selfId, ContactRingSize)
                .Enqueue(new ContactEntry
                {
                    OtherId = otherId,
                    OtherName = other != null ? other.name : "?",
                    Message = message,
                    Time = Time.realtimeSinceStartup,
                });
            Contacted?.Invoke(selfId, otherId, message);
        }

        /// <summary>Handle a contact: transient edge when both nodes exist, else a counter.</summary>
        public void HandleContact(string selfId, string otherId, string message,
            CodeGraph graph, GraphCanvas canvas)
        {
            if (canvas == null || graph == null || selfId == null)
                return;
            if (otherId != null && graph.Nodes.ContainsKey(otherId) &&
                graph.Nodes.ContainsKey(selfId) && otherId != selfId)
            {
                TransientEdge t = null;
                foreach (var e in transientEdges)
                {
                    if (e.Edge.FromId == otherId && e.Edge.ToId == selfId &&
                        e.Edge.Label == message)
                        t = e;
                }
                if (t == null)
                {
                    t = new TransientEdge
                    {
                        Edge = canvas.AddTransientEdge(otherId, selfId,
                            EdgeKind.UnityMessage, message),
                    };
                    if (t.Edge == null)
                        return;
                    transientEdges.Add(t);
                }
                t.LastContact = Time.realtimeSinceStartup;
                canvas.FlashEdge(t.Edge);
            }
            else
            {
                contactCounts.TryGetValue(selfId, out var n);
                contactCounts[selfId] = n + 1;
            }
        }

        /// <summary>Recorded contacts for a node, oldest to newest.</summary>
        public List<ContactEntry> ContactsFor(string nodeId)
        {
            return contactsLog.TryGetValue(nodeId, out var q)
                ? new List<ContactEntry>(q)
                : new List<ContactEntry>();
        }

        /// <summary>Contacts that could not be mapped to a graph node.</summary>
        public int UnmappedContactsFor(string nodeId)
        {
            return contactCounts.TryGetValue(nodeId, out var n) ? n : 0;
        }

        /// <summary>Drops recorded contacts and the unmapped counter for a node.</summary>
        public void ClearContacts(string nodeId)
        {
            contactsLog.Remove(nodeId);
            contactCounts.Remove(nodeId);
        }

        /// <summary>Recent value-change lines for a node (all its watched components).</summary>
        public List<string> ChangesFor(GraphNode node)
        {
            var list = new List<string>();
            if (node?.Components == null)
                return list;
            foreach (var c in node.Components)
            {
                if (c.Component == null)
                    continue;
                if (changesLog.TryGetValue(c.Component, out var q))
                {
                    foreach (var line in q)
                        list.Add(c.Type.Name + "." + line);
                }
            }
            return list;
        }
    }
}
