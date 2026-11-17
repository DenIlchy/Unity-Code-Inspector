using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Denid.CodeInspector.Editor.Tests
{
    public class GraphFilterTests
    {
        static string Id(Type t) => CodeGraph.NodeIdFor(t);

        static CodeGraph ChainGraph()
        {
            var g = new CodeGraph();
            var health = g.GetOrAddNode(typeof(FxHealth));
            var enemy = g.GetOrAddNode(typeof(FxEnemy));
            var listener = g.GetOrAddNode(typeof(FxListener));
            var messenger = g.GetOrAddNode(typeof(FxMessenger));
            var service = g.GetOrAddNode(typeof(FxService));

            g.AddEdge(health.Id, enemy.Id, EdgeKind.MethodCall, "a");
            g.AddEdge(enemy.Id, listener.Id, EdgeKind.MethodCall, "b");
            g.AddEdge(listener.Id, messenger.Id, EdgeKind.MethodCall, "c");
            g.AddEdge(service.Id, health.Id, EdgeKind.FieldReference, "d");
            g.AddEdge(enemy.Id, listener.Id, EdgeKind.TypeUsage, "e");
            return g;
        }

        [Test]
        public void DefaultFilterKeepsAllNodesButDropsTypeUsage()
        {
            var r = GraphFilter.Apply(ChainGraph(), GraphFilterSettings.CreateDefault());
            Assert.AreEqual(5, r.NodeIds.Count);
            Assert.AreEqual(4, r.Edges.Count);
            foreach (var e in r.Edges)
                Assert.AreNotEqual(EdgeKind.TypeUsage, e.Kind);
        }

        [Test]
        public void EdgeKindFiltering()
        {
            var s = GraphFilterSettings.CreateDefault();
            s.EnabledEdgeKinds.Remove(EdgeKind.MethodCall);
            var r = GraphFilter.Apply(ChainGraph(), s);
            Assert.AreEqual(1, r.Edges.Count);
            Assert.AreEqual(EdgeKind.FieldReference, r.Edges[0].Kind);
        }

        [Test]
        public void CategoryFilteringRemovesNodeAndEdges()
        {
            var s = GraphFilterSettings.CreateDefault();
            s.EnabledCategories.Remove(ScriptCategory.PlainClass);
            var r = GraphFilter.Apply(ChainGraph(), s);
            Assert.IsFalse(r.NodeIds.Contains(Id(typeof(FxService))));
            foreach (var e in r.Edges)
            {
                Assert.AreNotEqual(Id(typeof(FxService)), e.FromId);
                Assert.AreNotEqual(Id(typeof(FxService)), e.ToId);
            }
        }

        [Test]
        public void SearchKeepsMatchAndOneHopNeighbors()
        {
            var s = GraphFilterSettings.CreateDefault();
            s.Search = "FxListener";
            var r = GraphFilter.Apply(ChainGraph(), s);
            Assert.AreEqual(3, r.NodeIds.Count);
            Assert.IsTrue(r.NodeIds.Contains(Id(typeof(FxListener))));
            Assert.IsTrue(r.NodeIds.Contains(Id(typeof(FxEnemy))));
            Assert.IsTrue(r.NodeIds.Contains(Id(typeof(FxMessenger))));
        }

        [Test]
        public void FocusDepthLimitsNodes()
        {
            var s = GraphFilterSettings.CreateDefault();
            s.FocusNodeId = Id(typeof(FxEnemy));
            s.FocusDepth = 1;
            var r1 = GraphFilter.Apply(ChainGraph(), s);
            Assert.IsTrue(r1.NodeIds.Contains(Id(typeof(FxEnemy))));
            Assert.IsTrue(r1.NodeIds.Contains(Id(typeof(FxHealth))));
            Assert.IsTrue(r1.NodeIds.Contains(Id(typeof(FxListener))));
            Assert.IsFalse(r1.NodeIds.Contains(Id(typeof(FxMessenger))));

            s.FocusDepth = 2;
            var r2 = GraphFilter.Apply(ChainGraph(), s);
            Assert.IsTrue(r2.NodeIds.Contains(Id(typeof(FxMessenger))));
            Assert.IsTrue(r2.NodeIds.Contains(Id(typeof(FxService))));
        }

        [Test]
        public void HideIsolatedDropsDegreeZeroNodes()
        {
            var g = ChainGraph();
            g.GetOrAddNode(typeof(FxScorer)); // no edges
            var s = GraphFilterSettings.CreateDefault();
            var withIsolated = GraphFilter.Apply(g, s);
            Assert.IsTrue(withIsolated.NodeIds.Contains(Id(typeof(FxScorer))));

            s.HideIsolated = true;
            var r = GraphFilter.Apply(g, s);
            Assert.IsFalse(r.NodeIds.Contains(Id(typeof(FxScorer))));
        }
    }
}
