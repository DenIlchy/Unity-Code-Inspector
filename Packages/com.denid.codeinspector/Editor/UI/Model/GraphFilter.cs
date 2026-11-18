using System;
using System.Collections.Generic;

namespace Denid.CodeInspector.Editor
{
    public sealed class GraphFilterSettings
    {
        public HashSet<EdgeKind> EnabledEdgeKinds;
        public HashSet<ScriptCategory> EnabledCategories;
        public string Search = "";
        public string FocusNodeId;
        public int FocusDepth = 1;
        public bool HideIsolated;

        public static GraphFilterSettings CreateDefault()
        {
            var kinds = new HashSet<EdgeKind>();
            foreach (EdgeKind k in Enum.GetValues(typeof(EdgeKind)))
            {
                if (k != EdgeKind.TypeUsage)
                    kinds.Add(k);
            }
            var cats = new HashSet<ScriptCategory>();
            foreach (ScriptCategory c in Enum.GetValues(typeof(ScriptCategory)))
                cats.Add(c);
            return new GraphFilterSettings
            {
                EnabledEdgeKinds = kinds,
                EnabledCategories = cats,
            };
        }
    }

    public sealed class GraphFilterResult
    {
        public HashSet<string> NodeIds = new HashSet<string>();
        public List<GraphEdge> Edges = new List<GraphEdge>();
        public int TotalNodes;
        public int TotalEdges;
    }

    /// <summary>
    /// Filters a <see cref="CodeGraph"/> down to the nodes and edges that should be
    /// drawn, applying edge-kind, category, search (1-hop), focus-depth and
    /// hide-isolated rules in that order.
    /// </summary>
    public static class GraphFilter
    {
        public static GraphFilterResult Apply(CodeGraph graph, GraphFilterSettings settings)
        {
            settings ??= GraphFilterSettings.CreateDefault();
            var result = new GraphFilterResult
            {
                TotalNodes = graph.Nodes.Count,
                TotalEdges = graph.Edges.Count,
            };

            var kept = new HashSet<string>();
            foreach (var kv in graph.Nodes)
            {
                var n = kv.Value;
                if (n.Kind != NodeKind.ScriptType || settings.EnabledCategories.Contains(n.Category))
                    kept.Add(kv.Key);
            }

            var edges = new List<GraphEdge>();
            var adjacency = new Dictionary<string, List<(string Other, GraphEdge Edge)>>();

            void FilterEdges()
            {
                edges.Clear();
                adjacency.Clear();
                foreach (var e in graph.Edges)
                {
                    if (!settings.EnabledEdgeKinds.Contains(e.Kind) ||
                        !kept.Contains(e.FromId) || !kept.Contains(e.ToId))
                        continue;
                    edges.Add(e);
                    AddAdj(e.FromId, e.ToId, e);
                    AddAdj(e.ToId, e.FromId, e);
                }
            }

            void AddAdj(string a, string b, GraphEdge e)
            {
                if (!adjacency.TryGetValue(a, out var list))
                    adjacency[a] = list = new List<(string, GraphEdge)>();
                list.Add((b, e));
            }

            FilterEdges();

            if (!string.IsNullOrEmpty(settings.Search))
            {
                var next = new HashSet<string>();
                foreach (var id in kept)
                {
                    if (graph.Nodes[id].Label.IndexOf(settings.Search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        next.Add(id);
                        if (adjacency.TryGetValue(id, out var list))
                            foreach (var (other, _) in list)
                                next.Add(other);
                    }
                }
                kept = next;
                FilterEdges();
            }

            if (!string.IsNullOrEmpty(settings.FocusNodeId) && kept.Contains(settings.FocusNodeId))
            {
                var seen = new HashSet<string> { settings.FocusNodeId };
                var frontier = new List<string> { settings.FocusNodeId };
                var depth = Math.Max(1, settings.FocusDepth);
                for (var d = 0; d < depth; d++)
                {
                    var next = new List<string>();
                    foreach (var id in frontier)
                    {
                        if (!adjacency.TryGetValue(id, out var list))
                            continue;
                        foreach (var (other, _) in list)
                        {
                            if (seen.Add(other))
                                next.Add(other);
                        }
                    }
                    frontier = next;
                }
                kept = seen;
                FilterEdges();
            }

            if (settings.HideIsolated)
            {
                var connected = new HashSet<string>();
                foreach (var e in edges)
                {
                    connected.Add(e.FromId);
                    connected.Add(e.ToId);
                }
                kept.IntersectWith(connected);
                FilterEdges();
            }

            result.NodeIds = kept;
            result.Edges = edges;
            return result;
        }
    }
}
