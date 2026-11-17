using System.Collections.Generic;
using UnityEngine;

namespace Denid.CodeInspector.Editor
{
    /// <summary>Maps modified objects (components, GameObjects, assets) to graph node ids.</summary>
    public static class GraphPulse
    {
        public static List<string> NodeIdsForObjects(IEnumerable<Object> targets, CodeGraph graph)
        {
            var ids = new List<string>();
            if (graph == null || targets == null)
                return ids;
            foreach (var node in graph.Nodes.Values)
            {
                foreach (var t in targets)
                {
                    if (t == null)
                        continue;
                    if (node.Target == t || HasComponent(node, t))
                    {
                        ids.Add(node.Id);
                        break;
                    }
                }
            }
            return ids;
        }

        static bool HasComponent(GraphNode node, Object target)
        {
            if (node.Components == null)
                return false;
            foreach (var c in node.Components)
            {
                if (c.Component == target)
                    return true;
            }
            return false;
        }
    }
}
