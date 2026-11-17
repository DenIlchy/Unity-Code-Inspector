using System;
using NUnit.Framework;

namespace Denid.CodeInspector.Editor.Tests
{
    /// <summary>
    /// Scans the real user assemblies (including the demo scripts under Assets/)
    /// and checks that all expected node categories and edges appear.
    /// </summary>
    public class DemoScanTests
    {
        static Type DemoType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Denid.CodeInspector.Demo." + name);
                if (t != null)
                    return t;
            }
            return null;
        }

        static bool HasEdge(CodeGraph g, Type from, Type to, EdgeKind kind)
        {
            if (from == null || to == null)
                return false;
            var fromId = CodeGraph.NodeIdFor(from);
            var toId = CodeGraph.NodeIdFor(to);
            foreach (var e in g.Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind == kind)
                    return true;
            }
            return false;
        }

        CodeGraph graph;

        [SetUp]
        public void Build()
        {
            if (DemoType("Health") == null)
                Assert.Ignore("Demo sample not imported into this project");
            graph = TypeScanner.Build();
        }

        [Test]
        public void DemoNodesExistWithCategories()
        {
            var cases = new (string Name, ScriptCategory Cat)[]
            {
                ("Health", ScriptCategory.MonoBehaviour),
                ("Enemy", ScriptCategory.MonoBehaviour),
                ("ScoreService", ScriptCategory.PlainClass),
                ("MathUtil", ScriptCategory.StaticClass),
                ("IDamageable", ScriptCategory.Interface),
                ("GameConfig", ScriptCategory.ScriptableObject),
            };
            foreach (var (name, cat) in cases)
            {
                var t = DemoType(name);
                Assert.IsNotNull(t, "demo type missing: " + name);
                var id = CodeGraph.NodeIdFor(t);
                Assert.IsTrue(graph.Nodes.TryGetValue(id, out var n), "node missing: " + name);
                Assert.AreEqual(cat, n.Category, "wrong category for " + name);
            }
        }

        [Test]
        public void CoroutineGetComponentEdgeExists()
        {
            Assert.IsTrue(
                HasEdge(graph, DemoType("Enemy"), DemoType("IDamageable"), EdgeKind.GetComponent),
                "Enemy -> IDamageable GetComponent edge (from coroutine) missing");
        }

        [Test]
        public void EventSubscriptionEdgeExists()
        {
            Assert.IsTrue(
                HasEdge(graph, DemoType("HUD"), DemoType("Health"), EdgeKind.CSharpEventSubscription),
                "HUD -> Health event subscription edge missing");
        }

        [Test]
        public void SendMessageEdgeIsApproximate()
        {
            var zone = DemoType("TriggerZone");
            var player = DemoType("PlayerController");
            Assert.IsNotNull(zone);
            Assert.IsNotNull(player);
            GraphEdge found = null;
            foreach (var e in graph.Edges)
            {
                if (e.Kind == EdgeKind.SendMessage &&
                    e.FromId == CodeGraph.NodeIdFor(zone) &&
                    e.ToId == CodeGraph.NodeIdFor(player))
                    found = e;
            }
            Assert.IsNotNull(found, "TriggerZone -> PlayerController SendMessage edge missing");
            Assert.IsTrue(found.Approximate);
        }
    }
}
