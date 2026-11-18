using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor.Tests
{
    public class ValueSnapshotTests
    {
        GameObject go;
        FxFields fields;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("snap");
            fields = go.AddComponent<FxFields>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void DiffReportsChangedField()
        {
            var so = new SerializedObject(fields);
            so.Update();
            var before = ValueSnapshot.Capture(so);
            so.FindProperty("i").intValue = 42;
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();
            var diff = ValueSnapshot.Diff(before, ValueSnapshot.Capture(so));
            var change = diff.Single(c => c.Path == "i");
            Assert.AreEqual("1", change.OldValue);
            Assert.AreEqual("42", change.NewValue);
        }

        [Test]
        public void UnchangedProducesNoDiff()
        {
            var so = new SerializedObject(fields);
            so.Update();
            var snap = ValueSnapshot.Capture(so);
            Assert.AreEqual(0, ValueSnapshot.Diff(snap, ValueSnapshot.Capture(so)).Count);
        }

        [Test]
        public void PropertyCapRespected()
        {
            var so = new SerializedObject(fields);
            so.Update();
            Assert.LessOrEqual(ValueSnapshot.Capture(so, 3).Count, 3);
        }

        [Test]
        public void LeafTypesDontExpandChildren()
        {
            var so = new SerializedObject(fields);
            so.Update();
            var snap = ValueSnapshot.Capture(so);
            Assert.AreEqual(1, snap.Count(f => f.Path == "v"));
            Assert.IsFalse(snap.Any(f => f.Path.StartsWith("v.", StringComparison.Ordinal)));
        }
    }

    public class PropertyValueCodecTests
    {
        GameObject go;
        FxFields fields;
        SerializedObject so;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("codec");
            fields = go.AddComponent<FxFields>();
            so = new SerializedObject(fields);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        string Read(string path)
        {
            so.Update();
            return PropertyValueCodec.ReadLeaf(so.FindProperty(path));
        }

        bool Write(string path, string value, Object refObj = null)
        {
            so.Update();
            var ok = PropertyValueCodec.WriteLeaf(so.FindProperty(path), value, refObj);
            if (ok)
                so.ApplyModifiedPropertiesWithoutUndo();
            return ok;
        }

        [Test]
        public void RoundTrips()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.IsTrue(Write("i", "77"));
                Assert.AreEqual(77, fields.i);
                Assert.AreEqual("77", Read("i"));

                Assert.IsTrue(Write("f", "2.5"));
                Assert.AreEqual(2.5f, fields.f);
                Assert.AreEqual(Read("f"), "2.5");

                Assert.IsTrue(Write("b", "0"));
                Assert.IsFalse(fields.b);
                Assert.AreEqual("0", Read("b"));

                Assert.IsTrue(Write("s", "welt"));
                Assert.AreEqual("welt", fields.s);

                Assert.IsTrue(Write("e", "2"));
                Assert.AreEqual(FxEnum.C, fields.e);
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
        }

        [Test]
        public void ObjectReferenceRoundTrip()
        {
            var cfg = ScriptableObject.CreateInstance<FxConfig>();
            try
            {
                Assert.IsTrue(Write("reference", null, cfg));
                Assert.AreSame(cfg, fields.reference);
                Assert.IsTrue(Read("reference").StartsWith("ref:"));
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void VectorIsReadableButNotWritable()
        {
            Assert.IsNotNull(Read("v"));
            var p = so.FindProperty("v");
            Assert.IsFalse(PropertyValueCodec.IsWritable(p));
        }

        [Test]
        public void Vector4ReadsItsOwnComponents()
        {
            Assert.AreEqual("1,2,3,4", Read("v4"));
        }
    }

    public class UnityEventRelayTests
    {
        GameObject go;
        FxEventSource source;
        int fired;
        string firedPath;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("relay");
            source = go.AddComponent<FxEventSource>();
            fired = 0;
            firedPath = null;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        void OnFire(Object owner, string path)
        {
            fired++;
            firedPath = path;
        }

        [Test]
        public void PlainEventRelays()
        {
            var reg = UnityEventRelay.Register(source, "onFire", OnFire);
            Assert.IsNotNull(reg);
            source.onFire.Invoke();
            Assert.AreEqual(1, fired);
            Assert.AreEqual("onFire", firedPath);
        }

        [Test]
        public void GenericEventRelays()
        {
            var reg = UnityEventRelay.Register(source, "onValue", OnFire);
            Assert.IsNotNull(reg);
            source.onValue.Invoke(7);
            Assert.AreEqual(1, fired);
            Assert.AreEqual("onValue", firedPath);
        }

        [Test]
        public void NestedEventRelays()
        {
            var reg = UnityEventRelay.Register(source, "nested.nestedEvent", OnFire);
            Assert.IsNotNull(reg);
            source.nested.nestedEvent.Invoke();
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void UnregisterStopsCallbacks()
        {
            var reg = UnityEventRelay.Register(source, "onFire", OnFire);
            UnityEventRelay.Unregister(reg);
            source.onFire.Invoke();
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void SingleRegistrationFiresOnce()
        {
            UnityEventRelay.Register(source, "onFire", OnFire);
            source.onFire.Invoke();
            Assert.AreEqual(1, fired, "one registration → one callback per invoke");
        }
    }

    public class PlayModeMonitorRelayTests
    {
        GameObject go;
        PlayModeMonitor monitor;

        [TearDown]
        public void TearDown()
        {
            monitor?.Clear();
            if (go != null)
                Object.DestroyImmediate(go);
        }

        [Test]
        public void SharedRelayFiresOncePerEdge()
        {
            go = new GameObject("multi");
            var source = go.AddComponent<FxEventSource>();
            var graph = new CodeGraph();
            var e1 = new GraphEdge
            {
                FromId = "x", ToId = "a", Kind = EdgeKind.UnityEventBinding,
                Label = "onFire → a", SourceObject = source, EventPath = "onFire",
            };
            var e2 = new GraphEdge
            {
                FromId = "x", ToId = "b", Kind = EdgeKind.UnityEventBinding,
                Label = "onFire → b", SourceObject = source, EventPath = "onFire",
            };
            graph.Edges.Add(e1);
            graph.Edges.Add(e2);

            monitor = new PlayModeMonitor();
            var counts = new Dictionary<GraphEdge, int>();
            monitor.UnityEventFired += (e, n) => counts[e] = n;
            monitor.Attach(graph, null);

            source.onFire.Invoke();
            Assert.AreEqual(1, counts[e1]);
            Assert.AreEqual(1, counts[e2]);

            source.onFire.Invoke();
            Assert.AreEqual(2, counts[e1]);
            Assert.AreEqual(2, counts[e2]);
        }
    }

    public class ProbeTests
    {
        GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }

        [Test]
        public void AttachProbeIdempotentAndHidden()
        {
            go = new GameObject("p");
            var a = PlayModeMonitor.AttachProbe(go);
            var b = PlayModeMonitor.AttachProbe(go);
            Assert.AreSame(a, b);
            Assert.IsTrue(a.hideFlags.HasFlag(HideFlags.HideInInspector));
            Assert.IsTrue(a.hideFlags.HasFlag(HideFlags.DontSave));
        }

        [Test]
        public void ContactEventFires()
        {
            go = new GameObject("p");
            var other = new GameObject("o");
            try
            {
                (GameObject self, GameObject o, string msg)? got = null;
                void Handler(GameObject s, GameObject o2, string m) => got = (s, o2, m);
                CodeInspectorProbes.Contact += Handler;
                try
                {
                    CodeInspectorProbes.Raise(go, other, "OnTriggerEnter");
                }
                finally
                {
                    CodeInspectorProbes.Contact -= Handler;
                }
                Assert.IsNotNull(got);
                Assert.AreSame(go, got.Value.self);
                Assert.AreSame(other, got.Value.o);
                Assert.AreEqual("OnTriggerEnter", got.Value.msg);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }
    }

    public class TweakLogTests
    {
        GameObject go;
        FxFields fields;

        [SetUp]
        public void SetUp()
        {
            TweakLog.Records.Clear();
            go = new GameObject("t");
            fields = go.AddComponent<FxFields>();
        }

        [TearDown]
        public void TearDown()
        {
            TweakLog.Records.Clear();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MergesSamePathKeepsFirstOriginal()
        {
            TweakLog.RecordModification(fields, "i", "1", null, "5", null);
            var r = TweakLog.RecordModification(fields, "i", "5", null, "9", null);
            Assert.AreEqual(1, TweakLog.Records.Count);
            Assert.AreEqual("1", r.OriginalValue);
            Assert.AreEqual("9", r.LastValue);
        }

        [Test]
        public void DifferentPathsAreSeparate()
        {
            TweakLog.RecordModification(fields, "i", "1", null, "5", null);
            TweakLog.RecordModification(fields, "f", "1.5", null, "3", null);
            Assert.AreEqual(2, TweakLog.Records.Count);
        }

        [Test]
        public void IgnoredPathsSkipped()
        {
            Assert.IsNull(TweakLog.RecordModification(fields, "m_RootOrder", "0", null, "1", null));
            Assert.AreEqual(0, TweakLog.Records.Count);
        }

        [Test]
        public void ClearAllowsNewRecord()
        {
            var r1 = TweakLog.RecordModification(fields, "i", "1", null, "5", null);
            TweakLog.Clear();
            var r2 = TweakLog.RecordModification(fields, "i", "1", null, "9", null);
            Assert.AreEqual(1, TweakLog.Records.Count);
            Assert.AreNotSame(r1, r2);
            Assert.AreEqual("9", r2.LastValue);
        }
    }

    public class TweakResolverTests
    {
        const string TempDir = "Assets/__CodeInspectorTests__";
        const string TempScene = TempDir + "/resolve.unity";

        [SetUp]
        public void SetUp()
        {
            TweakLog.Records.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
            TweakLog.Records.Clear();
        }

        FxFields BuildSavedScene()
        {
            AssetDatabase.CreateFolder("Assets", "__CodeInspectorTests__");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            var fields = child.AddComponent<FxFields>();
            EditorSceneManager.SaveScene(scene, TempScene);
            return fields;
        }

        [Test]
        public void ResolvesViaGlobalObjectId()
        {
            var fields = BuildSavedScene();
            var r = TweakLog.RecordModification(fields, "i", "1", null, "9", null);
            Assert.IsNotNull(r.GlobalObjectId);
            var resolved = TweakResolver.ResolveTarget(r);
            Assert.AreSame(fields, resolved);
        }

        [Test]
        public void ResolvesViaLocatorFallback()
        {
            var fields = BuildSavedScene();
            var r = TweakLog.RecordModification(fields, "i", "1", null, "9", null);
            r.GlobalObjectId = "garbage";
            var resolved = TweakResolver.ResolveTarget(r);
            Assert.AreSame(fields, resolved);
        }

        [Test]
        public void UnknownLocatorIsUnresolved()
        {
            BuildSavedScene();
            var r = new TweakRecord
            {
                ScenePath = TempScene,
                HierarchyPath = "Nowhere/Missing",
                TargetTypeName = typeof(FxFields).AssemblyQualifiedName,
            };
            Assert.IsNull(TweakResolver.ResolveTarget(r));
        }

        [Test]
        public void ApplyAndUndoRevertsAll()
        {
            var fields = BuildSavedScene();
            var r = TweakLog.RecordModification(fields, "i", "1", null, "9", null);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Assert.IsTrue(KeepChanges.Apply(r));
            Assert.AreEqual(9, fields.i);
            Undo.CollapseUndoOperations(group);
            Undo.PerformUndo();
            Assert.AreEqual(1, fields.i);
        }

        [Test]
        public void LocatorHandlesSlashInObjectName()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("A/B");
            var child = new GameObject("C");
            child.transform.SetParent(root.transform);
            var resolved = TweakResolver.ResolveByLocator(null, "A/B/C",
                typeof(GameObject).AssemblyQualifiedName, 0);
            Assert.AreSame(child, resolved);
        }

        [Test]
        public void LocatorSkipsDuplicateSiblingWithoutComponent()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Dup");
            var second = new GameObject("Dup");
            var comp = second.AddComponent<FxFields>();
            var resolved = TweakResolver.ResolveByLocator(null, "Dup",
                typeof(FxFields).AssemblyQualifiedName, 0);
            Assert.AreSame(comp, resolved);
        }

        [Test]
        public void LocatorUsesSiblingPath()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Dup");
            var second = new GameObject("Dup");
            var comp = second.AddComponent<FxFields>();
            var rootIndex = Array.IndexOf(second.scene.GetRootGameObjects(), second);
            var resolved = TweakResolver.ResolveByLocator(null, "Dup",
                typeof(FxFields).AssemblyQualifiedName, 0, rootIndex.ToString());
            Assert.AreSame(comp, resolved);
        }

        [Test]
        public void ClassifyStatuses()
        {
            var fields = BuildSavedScene();
            var r = TweakLog.RecordModification(fields, "i", "1", null, "9", null);
            Assert.AreEqual(TweakStatus.Keepable, KeepChanges.Classify(r));
            var bad = new TweakRecord
            {
                HierarchyPath = "Nowhere/Missing",
                TargetTypeName = typeof(FxFields).AssemblyQualifiedName,
            };
            Assert.AreEqual(TweakStatus.RuntimeObject, KeepChanges.Classify(bad));
        }
    }

    public class MonitorPersistenceTests
    {
        static readonly Type[] FixtureTypes =
        {
            typeof(FxHealth), typeof(FxEventSource),
        };

        CodeGraph typeGraph;
        GameObject sourceGo;
        GameObject healthGo;
        FxEventSource source;
        readonly string tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ci-monitor-" + System.IO.Path.GetRandomFileName());

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            typeGraph = TypeScanner.Build(FixtureTypes, new ScanOptions { AssemblyFilter = _ => true });
            sourceGo = new GameObject("source");
            source = sourceGo.AddComponent<FxEventSource>();
            healthGo = new GameObject("health");
            var health = healthGo.AddComponent<FxHealth>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(source.onFire, health.Heal);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, true);
        }

        CodeGraph BuildGraph() => SceneScanner.Build(
            new[] { sourceGo, healthGo }, typeGraph,
            new SceneScanOptions
            {
                IsUserScript = t => typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)),
            });

        [Test]
        public void FireCountsSurviveRebuild()
        {
            var monitor = new PlayModeMonitor();
            var canvas = new GraphCanvas(new LayoutStore(tempDir + "/Layout.json"), "Scene:test");
            var graph = BuildGraph();
            canvas.Bind(graph, GraphFilter.Apply(graph, GraphFilterSettings.CreateDefault()));
            monitor.UnityEventFired += (e, n) =>
            {
                canvas.FlashEdge(e);
                canvas.SetEdgeFlashCount(e, n);
            };
            monitor.Attach(graph, canvas);

            source.onFire.Invoke();
            source.onFire.Invoke();
            source.onFire.Invoke();

            var edge = graph.Edges.Find(e => e.Kind == EdgeKind.UnityEventBinding);
            Assert.IsNotNull(edge);
            Assert.AreEqual(3, canvas.FlashCountFor(edge));

            // Simulate a Play Mode rebuild: new graph objects, same scene.
            var graph2 = BuildGraph();
            canvas.Bind(graph2, GraphFilter.Apply(graph2, GraphFilterSettings.CreateDefault()));
            Assert.AreEqual(0, canvas.FlashCountFor(
                graph2.Edges.Find(e => e.Kind == EdgeKind.UnityEventBinding)),
                "canvas-level counts are per edge object");

            monitor.Attach(graph2, canvas);
            var edge2 = graph2.Edges.Find(e => e.Kind == EdgeKind.UnityEventBinding);
            Assert.AreEqual(3, canvas.FlashCountFor(edge2), "counter restored after rebuild");
            monitor.Clear();
        }
    }

    public class PlayModeIntegrationTests
    {
        [UnityTest, Timeout(60000)]
        public IEnumerator PlayModeTweakIsRecordedAndResolved()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("requires graphics device for Play Mode in this environment");
            // Objects in an unsaved scene do not survive the play-mode domain
            // reload in batchmode, so use a saved scene asset.
            const string ScenePath = "Assets/__CodeInspectorPlayTest.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var go = new GameObject("pm");
            var fields = go.AddComponent<FxFields>();
            fields.i = 3;
            EditorSceneManager.SaveScene(scene, ScenePath);
            TweakLog.Records.Clear();
            TweakLog.Mode = TweakLog.Preference.Ask;

            yield return new EnterPlayMode();

            // Entering Play Mode destroys and recreates scene objects, so the
            // old managed reference is dead — re-fetch inside play mode.
            fields = Object.FindFirstObjectByType<FxFields>();
            Assert.IsNotNull(fields);

            // postprocessModifications may not fire for scripted edits, so
            // record through the same path the handler uses.
            TweakLog.RecordModification(fields, "i", "3", null, "8", null);
            fields.i = 8;

            yield return new ExitPlayMode();

            // Back in edit mode: the record exists and resolves to the
            // edit-mode counterpart of the play object.
            var records = TweakLog.Records;
            Assert.AreEqual(1, records.Count);
            var resolved = TweakResolver.ResolveTarget(records[0]);
            Assert.IsNotNull(resolved);
            Assert.IsTrue(resolved is FxFields);
            TweakLog.Records.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            AssetDatabase.DeleteAsset(ScenePath);
        }
    }
}
