using System;
using System.Collections.Generic;
using UnityEngine;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Deterministic layered (longest-path + barycenter) graph layout.
    /// Dependencies flow left to right; isolated nodes go to a grid below.
    /// </summary>
    public static class GraphLayout
    {
        public const float DefaultLayerGap = 120f;
        public const float DefaultNodeGap = 30f;

        public static Dictionary<string, Vector2> Layout(
            IReadOnlyCollection<string> nodeIds,
            IReadOnlyList<(string From, string To)> edges,
            IReadOnlyDictionary<string, Vector2> sizes,
            float layerGap = DefaultLayerGap,
            float nodeGap = DefaultNodeGap)
        {
            var result = new Dictionary<string, Vector2>();
            if (nodeIds == null || nodeIds.Count == 0)
                return result;

            var inSet = new HashSet<string>(nodeIds);
            var outs = new Dictionary<string, List<string>>();
            var ins = new Dictionary<string, List<string>>();
            var degree = new HashSet<string>();

            foreach (var (from, to) in edges ?? Array.Empty<(string, string)>())
            {
                if (from == to || from == null || to == null ||
                    !inSet.Contains(from) || !inSet.Contains(to))
                    continue;
                if (!outs.TryGetValue(from, out var ol))
                    outs[from] = ol = new List<string>();
                ol.Add(to);
                if (!ins.TryGetValue(to, out var il))
                    ins[to] = il = new List<string>();
                il.Add(from);
                degree.Add(from);
                degree.Add(to);
            }

            // Break cycles: DFS, ignore back edges.
            var forwardOut = new Dictionary<string, List<string>>();
            var state = new Dictionary<string, int>(); // 0=white 1=gray 2=black
            foreach (var root in nodeIds)
            {
                if (state.TryGetValue(root, out var s) && s != 0)
                    continue;
                var stack = new Stack<(string Node, int ChildIdx)>();
                state[root] = 1;
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    var (node, idx) = stack.Pop();
                    var children = outs.TryGetValue(node, out var c) ? c : null;
                    if (children == null || idx >= children.Count)
                    {
                        state[node] = 2;
                        continue;
                    }
                    stack.Push((node, idx + 1));
                    var child = children[idx];
                    var childState = state.TryGetValue(child, out var cs) ? cs : 0;
                    if (childState == 0)
                    {
                        state[child] = 1;
                        stack.Push((child, 0));
                        AddForward(forwardOut, node, child);
                    }
                    else if (childState == 2)
                    {
                        AddForward(forwardOut, node, child);
                    }
                    // childState == 1: back edge, skipped (breaks the cycle).
                }
            }
            // Forward edges may duplicate when reached via multiple parents; dedupe.
            foreach (var kv in forwardOut)
                kv.Value.Sort(StringComparer.Ordinal);
            var forwardIns = new Dictionary<string, List<string>>();
            var seenEdge = new HashSet<(string, string)>();
            foreach (var kv in forwardOut)
            {
                var list = kv.Value;
                var write = 0;
                for (var i = 0; i < list.Count; i++)
                {
                    if (i + 1 < list.Count && list[i] == list[i + 1])
                        continue;
                    list[write++] = list[i];
                }
                list.RemoveRange(write, list.Count - write);
                foreach (var to in list)
                {
                    if (seenEdge.Add((kv.Key, to)))
                    {
                        if (!forwardIns.TryGetValue(to, out var il))
                            forwardIns[to] = il = new List<string>();
                        il.Add(kv.Key);
                    }
                }
            }

            // Longest-path layering via Kahn's algorithm.
            var layer = new Dictionary<string, int>();
            var inDegree = new Dictionary<string, int>();
            foreach (var id in nodeIds)
            {
                inDegree[id] = forwardIns.TryGetValue(id, out var il) ? il.Count : 0;
                if (!layer.ContainsKey(id))
                    layer[id] = 0;
            }
            var queue = new Queue<string>();
            foreach (var id in nodeIds)
                if (inDegree[id] == 0 && degree.Contains(id))
                    queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (!forwardOut.TryGetValue(n, out var children))
                    continue;
                foreach (var m in children)
                {
                    if (layer[m] < layer[n] + 1)
                        layer[m] = layer[n] + 1;
                    if (--inDegree[m] == 0)
                        queue.Enqueue(m);
                }
            }

            // Group nodes per layer (connected ones only; isolated go to the grid).
            var layers = new SortedDictionary<int, List<string>>();
            var isolated = new List<string>();
            foreach (var id in nodeIds)
            {
                if (!degree.Contains(id))
                {
                    isolated.Add(id);
                    continue;
                }
                var l = layer[id];
                if (!layers.TryGetValue(l, out var list))
                    layers[l] = list = new List<string>();
                list.Add(id);
            }

            // Barycenter ordering sweeps (down, then up).
            var order = new Dictionary<string, int>();
            var layerLists = new List<List<string>>(layers.Values);
            foreach (var list in layerLists)
            {
                list.Sort(StringComparer.Ordinal);
                for (var i = 0; i < list.Count; i++)
                    order[list[i]] = i;
            }

            double Barycenter(string id, Dictionary<string, List<string>> adj)
            {
                if (!adj.TryGetValue(id, out var list) || list.Count == 0)
                    return order[id];
                var sum = 0d;
                var count = 0;
                foreach (var other in list)
                {
                    if (order.TryGetValue(other, out var o))
                    {
                        sum += o;
                        count++;
                    }
                }
                return count == 0 ? order[id] : sum / count;
            }

            for (var sweep = 0; sweep < 4; sweep++)
            {
                var down = sweep % 2 == 0;
                for (var li = 0; li < layerLists.Count; li++)
                {
                    var list = layerLists[li];
                    var adj = down ? forwardIns : forwardOut;
                    list.Sort((a, b) =>
                    {
                        var c = Barycenter(a, adj).CompareTo(Barycenter(b, adj));
                        return c != 0 ? c : StringComparer.Ordinal.Compare(a, b);
                    });
                    for (var i = 0; i < list.Count; i++)
                        order[list[i]] = i;
                }
            }

            // Assign positions.
            Vector2 SizeOf(string id) => sizes != null && sizes.TryGetValue(id, out var s)
                ? new Vector2(Mathf.Max(40f, s.x), Mathf.Max(20f, s.y))
                : new Vector2(220f, 64f);

            var x = 0f;
            var maxY = 0f;
            foreach (var kv in layers)
            {
                var list = kv.Value;
                var maxW = 0f;
                foreach (var id in list)
                    maxW = Mathf.Max(maxW, SizeOf(id).x);
                var y = 0f;
                foreach (var id in list)
                {
                    var size = SizeOf(id);
                    result[id] = new Vector2(x, y);
                    y += size.y + nodeGap;
                }
                maxY = Mathf.Max(maxY, y);
                x += maxW + layerGap;
            }

            // Isolated nodes in a grid below.
            if (isolated.Count > 0)
            {
                isolated.Sort(StringComparer.Ordinal);
                var cols = Mathf.CeilToInt(Mathf.Sqrt(isolated.Count));
                var cellW = 0f;
                var cellH = 0f;
                foreach (var id in isolated)
                {
                    var s = SizeOf(id);
                    cellW = Mathf.Max(cellW, s.x);
                    cellH = Mathf.Max(cellH, s.y);
                }
                cellW += 60f;
                cellH += 30f;
                var baseY = maxY > 0f ? maxY + 200f : 0f;
                for (var i = 0; i < isolated.Count; i++)
                {
                    var col = i % cols;
                    var row = i / cols;
                    result[isolated[i]] = new Vector2(col * cellW, baseY + row * cellH);
                }
            }

            return result;
        }

        static void AddForward(Dictionary<string, List<string>> map, string from, string to)
        {
            if (!map.TryGetValue(from, out var list))
                map[from] = list = new List<string>();
            list.Add(to);
        }
    }
}
