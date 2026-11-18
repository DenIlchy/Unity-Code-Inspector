using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Denid.CodeInspector.Editor.Tests
{
    public class GraphCanvasSmokeTests
    {
        [Test]
        public void BindCreatesOneViewPerVisibleNode()
        {
            var options = new ScanOptions { AssemblyFilter = _ => true };
            var graph = TypeScanner.Build(new[]
            {
                typeof(FxHealth), typeof(FxEnemy), typeof(FxListener), typeof(FxUsage),
            }, options);
            var filtered = GraphFilter.Apply(graph, GraphFilterSettings.CreateDefault());

            var tempDir = Path.Combine(Path.GetTempPath(), "ci-canvas-" + Path.GetRandomFileName());
            var store = new LayoutStore(Path.Combine(tempDir, "Layout.json"));
            GraphCanvas canvas = null;
            Assert.DoesNotThrow(() => canvas = new GraphCanvas(store));
            Assert.DoesNotThrow(() => canvas.Bind(graph, filtered));
            Assert.AreEqual(filtered.NodeIds.Count, canvas.VisibleNodeCount);

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        [Test]
        public void RebindSameGraphKeepsNodeViews()
        {
            var options = new ScanOptions { AssemblyFilter = _ => true };
            var graph = TypeScanner.Build(new[]
            {
                typeof(FxHealth), typeof(FxEnemy), typeof(FxListener), typeof(FxUsage),
            }, options);

            var tempDir = Path.Combine(Path.GetTempPath(), "ci-canvas-" + Path.GetRandomFileName());
            try
            {
                var store = new LayoutStore(Path.Combine(tempDir, "Layout.json"));
                var canvas = new GraphCanvas(store);
                canvas.Bind(graph, GraphFilter.Apply(graph, GraphFilterSettings.CreateDefault()));

                var id = CodeGraph.NodeIdFor(typeof(FxHealth));
                Assert.IsTrue(canvas.TryGetNodeView(id, out var v1));

                var s = GraphFilterSettings.CreateDefault();
                s.Search = "FxHealth";
                canvas.Bind(graph, GraphFilter.Apply(graph, s));

                Assert.IsTrue(canvas.TryGetNodeView(id, out var v2));
                Assert.AreSame(v1, v2, "NodeView must be reused for an unchanged node");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        static Vector2 InlinePos(NodeView view) =>
            new Vector2(view.style.left.value.value, view.style.top.value.value);

        [Test]
        public void ResetLayoutRestoresAutoPosition()
        {
            var options = new ScanOptions { AssemblyFilter = _ => true };
            var graph = TypeScanner.Build(new[]
            {
                typeof(FxHealth), typeof(FxEnemy), typeof(FxListener), typeof(FxUsage),
            }, options);
            var filtered = GraphFilter.Apply(graph, GraphFilterSettings.CreateDefault());

            var tempDir = Path.Combine(Path.GetTempPath(), "ci-canvas-" + Path.GetRandomFileName());
            try
            {
                var store = new LayoutStore(Path.Combine(tempDir, "Layout.json"));
                var canvasA = new GraphCanvas(store);
                canvasA.Bind(graph, filtered);

                var id = CodeGraph.NodeIdFor(typeof(FxHealth));
                Assert.IsTrue(canvasA.TryGetNodeView(id, out var viewA));
                var autoPos = InlinePos(viewA);

                // A persisted position wins over auto-layout for a fresh canvas
                // (same session positions always win over the store).
                var moved = autoPos + new Vector2(999f, 999f);
                store.Set("Architecture", id, moved);
                var canvasB = new GraphCanvas(store);
                canvasB.Bind(graph, filtered);
                Assert.IsTrue(canvasB.TryGetNodeView(id, out var viewB));
                Assert.AreEqual(moved, InlinePos(viewB));

                canvasB.ResetLayout();
                Assert.AreEqual(autoPos, InlinePos(viewB));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
    }
}
