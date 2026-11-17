using System;
using System.IO;
using NUnit.Framework;

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
    }
}
