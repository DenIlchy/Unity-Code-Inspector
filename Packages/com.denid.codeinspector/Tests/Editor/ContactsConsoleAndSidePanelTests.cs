using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor.Tests
{
    public class SidePanelFoldoutTests
    {
        static readonly Type[] FixtureTypes = { typeof(FxHealth), typeof(FxEnemy) };

        CodeGraph typeGraph;
        CodeGraph sceneGraph;
        SidePanel panel;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SidePanel.ResetExpandedSections();
            typeGraph = TypeScanner.Build(FixtureTypes, new ScanOptions { AssemblyFilter = _ => true });
            panel = new SidePanel();
        }

        [TearDown]
        public void TearDown()
        {
            SidePanel.ResetExpandedSections();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        GraphNode ShowNode(GameObject go)
        {
            sceneGraph = SceneScanner.Build(new[] { go }, typeGraph, new SceneScanOptions
            {
                IsUserScript = t => typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)),
            });
            var filtered = GraphFilter.Apply(sceneGraph, GraphFilterSettings.CreateDefault());
            panel.SetContext(sceneGraph, filtered, typeGraph, sceneGraph);
            var node = sceneGraph.Nodes["go:" + go.GetInstanceID()];
            panel.ShowNode(node.Id);
            return node;
        }

        Foldout FindFoldout(string text) =>
            panel.Query<Foldout>().ToList().FirstOrDefault(f => f.text == text);

        [Test]
        public void RootContainsScrollView()
        {
            Assert.IsNotNull(panel.Q<ScrollView>());
        }

        [Test]
        public void SectionTitlesAreFoldouts()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            ShowNode(go);

            Assert.IsNotNull(FindFoldout("Parameters"));
            Assert.IsNotNull(FindFoldout("Components"));
            Assert.IsNotNull(FindFoldout("Uses →"));
            Assert.IsNotNull(FindFoldout("Used by ←"));
        }

        [Test]
        public void CollapsedSectionStaysCollapsedAcrossShowNode()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            ShowNode(go);

            var components = FindFoldout("Components");
            Assert.IsNotNull(components);
            components.value = false;

            var goB = new GameObject("b");
            goB.AddComponent<FxEnemy>();
            ShowNode(goB);

            var again = FindFoldout("Components");
            Assert.IsNotNull(again);
            Assert.IsFalse(again.value, "collapsed state must persist across selections");
        }

        [Test]
        public void LiveFoldoutsPersistAcrossTicks()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            var node = ShowNode(go);
            panel.LiveChangesProvider = _ => new List<string> { "hp 1 → 2" };

            panel.RefreshLive();
            var live = FindFoldout("Live values");
            Assert.IsNotNull(live);
            Assert.AreEqual(DisplayStyle.Flex, live.style.display.value);
            panel.RefreshLive();
            Assert.AreSame(live, FindFoldout("Live values"),
                "live foldout must not be recreated every tick");
        }
    }

    public class PlayModeMonitorContactsTests
    {
        PlayModeMonitor monitor;
        GameObject self;
        GameObject other;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            self = new GameObject("self");
            other = new GameObject("other");
            monitor = new PlayModeMonitor();
            monitor.Attach(new CodeGraph(), null);
        }

        [TearDown]
        public void TearDown()
        {
            monitor.Clear();
            Object.DestroyImmediate(self);
            Object.DestroyImmediate(other);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        string SelfId => "go:" + self.GetInstanceID();

        [Test]
        public void ProbeContactProducesEntry()
        {
            CodeInspectorProbes.Raise(self, other, "OnTriggerEnter");
            var entries = monitor.ContactsFor(SelfId);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("go:" + other.GetInstanceID(), entries[0].OtherId);
            Assert.AreEqual("other", entries[0].OtherName);
            Assert.AreEqual("OnTriggerEnter", entries[0].Message);
        }

        [Test]
        public void ContactRingCapsAt200()
        {
            for (var i = 0; i < 210; i++)
                CodeInspectorProbes.Raise(self, other, "OnTriggerEnter");
            Assert.AreEqual(200, monitor.ContactsFor(SelfId).Count);
        }

        [Test]
        public void ClearContactsEmptiesLogAndUnmapped()
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ci-contacts-" + System.IO.Path.GetRandomFileName());
            var canvas = new GraphCanvas(new LayoutStore(dir + "/Layout.json"), "Scene:test");
            var graph = new CodeGraph();
            graph.Nodes[SelfId] = new GraphNode
            {
                Id = SelfId, Kind = NodeKind.GameObject, Label = "self",
            };

            CodeInspectorProbes.Raise(self, other, "OnTriggerEnter");
            monitor.HandleContact(SelfId, null, "unmapped", graph, canvas);
            Assert.AreEqual(1, monitor.ContactsFor(SelfId).Count);
            Assert.AreEqual(1, monitor.UnmappedContactsFor(SelfId));

            monitor.ClearContacts(SelfId);
            Assert.AreEqual(0, monitor.ContactsFor(SelfId).Count);
            Assert.AreEqual(0, monitor.UnmappedContactsFor(SelfId));
        }
    }

    public class ContactsConsoleTests
    {
        ContactsConsole console;
        List<PlayModeMonitor.ContactEntry> entries;
        List<string> cleared;
        GraphNode node;

        [SetUp]
        public void SetUp()
        {
            console = new ContactsConsole();
            entries = new List<PlayModeMonitor.ContactEntry>();
            cleared = new List<string>();
            console.ContactsProvider = _ => entries;
            console.ClearRequested = id => cleared.Add(id);
            node = new GraphNode
            {
                Id = "go:1", Kind = NodeKind.GameObject, Label = "Hero",
            };
            console.SetNode(node);
        }

        static PlayModeMonitor.ContactEntry Entry(string otherId, string msg = "OnTriggerEnter") =>
            new PlayModeMonitor.ContactEntry
            {
                OtherId = otherId, OtherName = "Enemy", Message = msg, Time = 1.2f,
            };

        [Test]
        public void RefreshCreatesRows()
        {
            entries.Add(Entry("go:2"));
            entries.Add(Entry("go:3", "OnTriggerExit"));
            entries.Add(Entry(null));
            console.Refresh();
            Assert.AreEqual(3, console.RowCount);
        }

        [Test]
        public void PauseFreezesRowsUntilUnpaused()
        {
            entries.Add(Entry("go:2"));
            console.Refresh();
            Assert.AreEqual(1, console.RowCount);

            console.Paused = true;
            entries.Add(Entry("go:3"));
            console.Refresh();
            Assert.AreEqual(1, console.RowCount, "paused console must not update");

            console.Paused = false;
            Assert.AreEqual(2, console.RowCount, "un-pausing refreshes immediately");
        }

        [Test]
        public void ClearInvokesClearRequestedWithNodeId()
        {
            entries.Add(Entry("go:2"));
            console.Refresh();
            console.SimulateClear();
            Assert.AreEqual(new[] { "go:1" }, cleared.ToArray());
            Assert.AreEqual(0, console.RowCount);
        }

        [Test]
        public void ClickableRowRaisesEntryClicked()
        {
            entries.Add(Entry("go:2"));
            entries.Add(Entry(null));
            console.Refresh();

            var clicked = new List<string>();
            console.EntryClicked += id => clicked.Add(id);

            console.SimulateRowClick(0);
            Assert.AreEqual(new[] { "go:2" }, clicked.ToArray());

            console.SimulateRowClick(1);
            Assert.AreEqual(1, clicked.Count, "rows without an OtherId are not clickable");
        }
    }
}
