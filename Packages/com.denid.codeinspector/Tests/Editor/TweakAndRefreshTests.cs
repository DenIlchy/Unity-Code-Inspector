using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor.Tests
{
    public class RefreshThrottleTests
    {
        [Test]
        public void SinglePingReturnsDebounce()
        {
            var t = new RefreshThrottle(300, 1000);
            Assert.AreEqual(300, t.Ping(0));
        }

        [Test]
        public void EventBurstCapsAtMaxWait()
        {
            var t = new RefreshThrottle(300, 1000);
            // Events every 100 ms: the debounce keeps pushing out, but the
            // refresh must fire within 1000 ms of the first event.
            for (long now = 0; now < 800; now += 100)
                Assert.AreEqual(300, t.Ping(now));
            Assert.AreEqual(200, t.Ping(800));
            Assert.AreEqual(100, t.Ping(900), "capped at max wait (due at 1000)");
        }

        [Test]
        public void PlayModeMaxWait1500()
        {
            var t = new RefreshThrottle(300, 1500);
            for (long now = 0; now < 1300; now += 100)
                Assert.AreEqual(300, t.Ping(now));
            Assert.AreEqual(200, t.Ping(1300));
            Assert.AreEqual(100, t.Ping(1400), "capped at max wait (due at 1500)");
        }

        [Test]
        public void ResetClearsPending()
        {
            var t = new RefreshThrottle(300, 1000);
            t.Ping(0);
            t.Reset();
            Assert.AreEqual(300, t.Ping(10000));
        }
    }

    public class SceneRefreshPolicyTests
    {
        [Test]
        public void StructuralKindsRefresh()
        {
            foreach (var k in new[]
                     {
                         ObjectChangeKind.CreateGameObjectHierarchy,
                         ObjectChangeKind.DestroyGameObjectHierarchy,
                         ObjectChangeKind.ChangeGameObjectStructure,
                         ObjectChangeKind.ChangeGameObjectParent,
                         ObjectChangeKind.ChangeScene,
                         ObjectChangeKind.ChangeGameObjectOrComponentProperties,
                         ObjectChangeKind.ChangeAssetObjectProperties,
                     })
            {
                Assert.IsTrue(SceneRefreshPolicy.ShouldRefresh(new[] { k }), k.ToString());
            }
        }

        [Test]
        public void IrrelevantKindsDoNotRefresh()
        {
            Assert.IsFalse(SceneRefreshPolicy.ShouldRefresh(new[] { ObjectChangeKind.None }));
            Assert.IsFalse(SceneRefreshPolicy.ShouldRefresh(new ObjectChangeKind[0]));
            Assert.IsFalse(SceneRefreshPolicy.ShouldRefresh(null));
        }
    }

    public class PropertyChangeFilterTests
    {
        GameObject go;
        FxFields fields;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("f");
            fields = go.AddComponent<FxFields>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void ReferenceChangesTriggerRefresh()
        {
            var filter = new SceneRefreshPolicy.PropertyChangeFilter();
            Assert.IsTrue(filter.ShouldRefresh(fields), "first call");
            fields.f = 9f;
            Assert.IsFalse(filter.ShouldRefresh(fields), "plain float is not in the signature");
            var cfg = ScriptableObject.CreateInstance<FxConfig>();
            try
            {
                fields.reference = cfg;
                Assert.IsTrue(filter.ShouldRefresh(fields), "object reference changed");
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }
    }

    public class TweakSectionTests
    {
        static readonly Type[] FixtureTypes =
        {
            typeof(FxBase), typeof(FxEnemy), typeof(FxHealth), typeof(FxListener),
            typeof(FxMessenger), typeof(FxUsage), typeof(IFxDamageDealer),
            typeof(FxCoroutineUser), typeof(FxLambdaUser), typeof(FxService),
            typeof(FxScorer), typeof(FxInterfaceCaller), typeof(FxEventSource),
            typeof(FxFinder), typeof(FxConfig), typeof(FxConfigUser),
        };

        CodeGraph typeGraph;
        CodeGraph sceneGraph;
        readonly List<GameObject> gos = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var options = new ScanOptions { AssemblyFilter = _ => true };
            typeGraph = TypeScanner.Build(FixtureTypes, options);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        SceneScanOptions ScanOptions() => new SceneScanOptions
        {
            IsUserScript = t => typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)),
        };

        GraphNode BuildNode(GameObject go, params GameObject[] extraRoots)
        {
            var roots = new List<GameObject> { go };
            roots.AddRange(extraRoots);
            sceneGraph = SceneScanner.Build(roots, typeGraph, ScanOptions());
            return sceneGraph.Nodes.TryGetValue("go:" + go.GetInstanceID(), out var n) ? n : null;
        }

        // Only the per-component foldouts that are direct children of the body;
        // InspectorElements contain their own internal foldouts.
        List<Foldout> Foldouts(TweakSection section) =>
            section.Body.Children().OfType<Foldout>().ToList();

        [Test]
        public void FoldoutsPerComponent()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            go.AddComponent<BoxCollider>();
            go.AddComponent<FxEnemy>();
            var node = BuildNode(go);
            Assert.IsNotNull(node);

            var section = new TweakSection { OnlyUserScripts = true };
            section.Bind(node, typeGraph, sceneGraph);
            Assert.AreEqual(2, Foldouts(section).Count, "only user scripts");

            section.OnlyUserScripts = false;
            Assert.AreEqual(3, Foldouts(section).Count, "all node components");
        }

        [Test]
        public void InspectorElementIsLazy()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            go.AddComponent<FxEnemy>();
            var node = BuildNode(go);
            var section = new TweakSection { OnlyUserScripts = true };
            section.Bind(node, typeGraph, sceneGraph);
            var foldouts = Foldouts(section);
            Assert.AreEqual(2, foldouts.Count);

            var expanded = foldouts.FirstOrDefault(f => f.value);
            var collapsed = foldouts.FirstOrDefault(f => !f.value);
            Assert.IsNotNull(expanded, "user script foldout expanded by default");
            Assert.AreEqual(1, expanded.Query<InspectorElement>().ToList().Count);
            if (collapsed != null)
                Assert.AreEqual(0, collapsed.Query<InspectorElement>().ToList().Count);
        }

        [Test]
        public void RebindSameTargetKeepsInspectors()
        {
            var go = new GameObject("a");
            var health = go.AddComponent<FxHealth>();
            var node1 = BuildNode(go);

            var section = new TweakSection();
            section.Bind(node1, typeGraph, sceneGraph);
            var inspector1 = section.Query<InspectorElement>().ToList().FirstOrDefault();
            Assert.IsNotNull(inspector1);

            // Simulate an auto refresh: rebuild the graph (new node objects,
            // same target + components).
            var node2 = BuildNode(go);
            section.Bind(node2, typeGraph, sceneGraph);
            var inspector2 = section.Query<InspectorElement>().ToList().FirstOrDefault();
            Assert.AreSame(inspector1, inspector2, "inspector must survive stable rebind");
        }

        [Test]
        public void BindDifferentScriptTypeNodesRebuilds()
        {
            var a = new GraphNode
            {
                Id = "type:a", Kind = NodeKind.ScriptType, Label = "A",
                Type = typeof(FxHealth),
            };
            var b = new GraphNode
            {
                Id = "type:b", Kind = NodeKind.ScriptType, Label = "B",
                Type = typeof(FxEnemy),
            };
            var section = new TweakSection();
            section.Bind(a, typeGraph, sceneGraph);
            var count = section.RebuildCount;
            section.Bind(b, typeGraph, sceneGraph);
            Assert.AreEqual(count + 1, section.RebuildCount,
                "two different ScriptType nodes must not be treated as stable");
        }

        [Test]
        public void RebindDifferentTargetReplacesInspectors()
        {
            var goA = new GameObject("a");
            goA.AddComponent<FxHealth>();
            var goB = new GameObject("b");
            goB.AddComponent<FxEnemy>();
            var nodeA = BuildNode(goA, goB);
            var nodeB = sceneGraph.Nodes["go:" + goB.GetInstanceID()];

            var section = new TweakSection();
            section.Bind(nodeA, typeGraph, sceneGraph);
            var inspectorA = section.Query<InspectorElement>().ToList().FirstOrDefault();
            section.Bind(nodeB, typeGraph, sceneGraph);
            var inspectorB = section.Query<InspectorElement>().ToList().FirstOrDefault();
            Assert.AreNotSame(inspectorA, inspectorB);
        }

        [Test]
        public void DestroyedTargetShowsGone()
        {
            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            var node = BuildNode(go);
            var section = new TweakSection();
            section.Bind(node, typeGraph, sceneGraph);
            Object.DestroyImmediate(go);
            // Rebuild: node for destroyed GO is gone; bind a synthetic node
            // carrying the dead target to simulate the stale selection.
            var stale = new GraphNode { Id = node.Id, Kind = NodeKind.GameObject, Label = "a", Target = node.Target };
            section.Bind(stale, typeGraph, sceneGraph);
            Assert.AreEqual(DisplayStyle.Flex, section.style.display.value);
        }

        [Test]
        public void UndoRestoresSerializedEdit()
        {
            var go = new GameObject("a");
            var health = go.AddComponent<FxHealth>();
            var original = health.hp;
            // Same path InspectorElement uses for property edits.
            var so = new SerializedObject(health);
            so.FindProperty("hp").intValue = original + 5;
            so.ApplyModifiedProperties();
            Assert.AreEqual(original + 5, health.hp);
            Undo.PerformUndo();
            Assert.AreEqual(original, health.hp);
        }

        [Test]
        public void PulseMapsComponentToNode()
        {
            var go = new GameObject("a");
            var health = go.AddComponent<FxHealth>();
            var node = BuildNode(go);
            var ids = GraphPulse.NodeIdsForObjects(new Object[] { health }, sceneGraph);
            Assert.Contains(node.Id, ids);
            // The GameObject itself maps via node.Target.
            Assert.Contains(node.Id, GraphPulse.NodeIdsForObjects(new Object[] { go }, sceneGraph));
            // A component unknown to the graph maps to nothing.
            var unknown = go.AddComponent<FxEnemy>();
            Assert.AreEqual(0, GraphPulse.NodeIdsForObjects(new Object[] { unknown }, sceneGraph).Count);
        }

        [Test]
        public void BuildForOpenScenesCreatesNoGameObjectsInEditMode()
        {
            int CountSceneObjects() =>
                Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.scene.IsValid());

            var go = new GameObject("a");
            go.AddComponent<FxHealth>();
            var before = CountSceneObjects();
            var g = SceneScanner.BuildForOpenScenes(ScanOptions());
            Assert.IsNotNull(g);
            Assert.AreEqual(before, CountSceneObjects(), "scan must not leave probe objects behind");
        }
    }
}
