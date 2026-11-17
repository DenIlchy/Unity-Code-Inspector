using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Denid.CodeInspector.Editor.Tests
{
    public class LayoutStoreTests
    {
        string tempPath;

        [SetUp]
        public void SetUp()
        {
            tempPath = Path.Combine(Path.GetTempPath(), "ci-layout-" + Path.GetRandomFileName(), "Layout.json");
        }

        [TearDown]
        public void TearDown()
        {
            var dir = Path.GetDirectoryName(tempPath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        [Test]
        public void RoundTrip()
        {
            var store = new LayoutStore(tempPath);
            Assert.IsFalse(store.TryGet("Architecture", "n1", out _));
            store.Set("Architecture", "n1", new Vector2(10f, 20f));
            store.Set("Scene", "n2", new Vector2(5f, 6f));

            var store2 = new LayoutStore(tempPath);
            Assert.IsTrue(store2.TryGet("Architecture", "n1", out var p));
            Assert.AreEqual(new Vector2(10f, 20f), p);
            Assert.IsTrue(store2.TryGet("Scene", "n2", out var p2));
            Assert.AreEqual(new Vector2(5f, 6f), p2);
        }

        [Test]
        public void ClearViewRemovesOnlyThatView()
        {
            var store = new LayoutStore(tempPath);
            store.Set("Architecture", "n1", Vector2.one);
            store.Set("Scene", "n2", Vector2.one * 2f);
            store.ClearView("Architecture");

            var store2 = new LayoutStore(tempPath);
            Assert.IsFalse(store2.TryGet("Architecture", "n1", out _));
            Assert.IsTrue(store2.TryGet("Scene", "n2", out _));
        }

        [Test]
        public void MissingFileIsEmpty()
        {
            var store = new LayoutStore(tempPath);
            Assert.IsFalse(store.TryGet("Architecture", "anything", out _));
        }
    }
}
