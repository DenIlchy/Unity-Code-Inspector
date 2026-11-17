using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Denid.CodeInspector.Editor.Tests
{
    public class GraphLayoutTests
    {
        static Dictionary<string, Vector2> UniformSizes(IEnumerable<string> ids)
        {
            var d = new Dictionary<string, Vector2>();
            foreach (var id in ids)
                d[id] = new Vector2(220f, 80f);
            return d;
        }

        [Test]
        public void EmptyInputReturnsEmpty()
        {
            var r = GraphLayout.Layout(new List<string>(), new List<(string, string)>(), null);
            Assert.AreEqual(0, r.Count);
        }

        [Test]
        public void AcyclicEdgesFlowLeftToRight()
        {
            var ids = new List<string> { "a", "b", "c" };
            var edges = new List<(string, string)> { ("a", "b"), ("b", "c") };
            var r = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            Assert.Less(r["a"].x, r["b"].x);
            Assert.Less(r["b"].x, r["c"].x);
        }

        [Test]
        public void CycleDoesNotHangAndPlacesAll()
        {
            var ids = new List<string> { "a", "b", "c" };
            var edges = new List<(string, string)> { ("a", "b"), ("b", "c"), ("c", "a") };
            var r = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            Assert.AreEqual(3, r.Count);
        }

        [Test]
        public void SelfLoopIsIgnored()
        {
            var ids = new List<string> { "a", "b" };
            var edges = new List<(string, string)> { ("a", "a"), ("a", "b") };
            var r = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            Assert.Less(r["a"].x, r["b"].x);
        }

        [Test]
        public void Deterministic()
        {
            var ids = new List<string> { "a", "b", "c", "d", "e" };
            var edges = new List<(string, string)>
            {
                ("a", "c"), ("b", "c"), ("c", "d"), ("a", "e"),
            };
            var r1 = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            var r2 = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            foreach (var id in ids)
                Assert.AreEqual(r1[id], r2[id], "position differs for " + id);
        }

        [Test]
        public void NoOverlaps()
        {
            var ids = new List<string>();
            var edges = new List<(string, string)>();
            var rnd = new System.Random(42);
            for (var i = 0; i < 100; i++)
                ids.Add("n" + i);
            for (var i = 0; i < 250; i++)
                edges.Add((ids[rnd.Next(ids.Count)], ids[rnd.Next(ids.Count)]));

            var sizes = UniformSizes(ids);
            var pos = GraphLayout.Layout(ids, edges, sizes);
            var rects = new Dictionary<string, Rect>();
            foreach (var id in ids)
                rects[id] = new Rect(pos[id], sizes[id]);

            for (var i = 0; i < ids.Count; i++)
            {
                for (var j = i + 1; j < ids.Count; j++)
                {
                    Assert.IsFalse(rects[ids[i]].Overlaps(rects[ids[j]]),
                        ids[i] + " overlaps " + ids[j]);
                }
            }
        }

        [Test]
        public void Layout1000NodesUnderBudget()
        {
            var ids = new List<string>();
            var edges = new List<(string, string)>();
            var rnd = new System.Random(7);
            for (var i = 0; i < 1000; i++)
                ids.Add("n" + i);
            for (var i = 0; i < 3000; i++)
                edges.Add((ids[rnd.Next(ids.Count)], ids[rnd.Next(ids.Count)]));

            var sw = Stopwatch.StartNew();
            var r = GraphLayout.Layout(ids, edges, UniformSizes(ids));
            sw.Stop();
            Assert.AreEqual(1000, r.Count);
            UnityEngine.Debug.Log($"GraphLayout perf: {sw.ElapsedMilliseconds} ms for 1000 nodes / 3000 edges");
            Assert.Less(sw.ElapsedMilliseconds, 500, "layout too slow");
        }
    }
}
