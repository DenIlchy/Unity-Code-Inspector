using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor.Tests
{
    public class SceneScannerTests
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
        CodeGraph graph;

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

        SceneScanOptions Options(bool showAll = false)
        {
            return new SceneScanOptions
            {
                IsUserScript = t => typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)),
                ShowAllGameObjects = showAll,
            };
        }

        static GameObject Go(string name, params Type[] comps)
        {
            var go = new GameObject(name);
            foreach (var t in comps)
                go.AddComponent(t);
            return go;
        }

        GraphNode NodeFor(GameObject go) =>
            graph.Nodes.TryGetValue("go:" + go.GetInstanceID(), out var n) ? n : null;

        List<GraphEdge> EdgesBetween(GameObject a, GameObject b) => EdgesBetween(
            "go:" + a.GetInstanceID(), "go:" + b.GetInstanceID());

        List<GraphEdge> EdgesBetween(string a, string b)
        {
            var list = new List<GraphEdge>();
            foreach (var e in graph.Edges)
                if (e.FromId == a && e.ToId == b)
                    list.Add(e);
            return list;
        }

        void Build(params GameObject[] roots)
        {
            graph = SceneScanner.Build(roots, typeGraph, Options());
        }

        [Test]
        public void FieldReferenceEdgeWithObjects()
        {
            var health = Go("h", typeof(FxHealth));
            var enemyGo = Go("e", typeof(FxEnemy));
            var enemy = enemyGo.GetComponent<FxEnemy>();
            enemy.target = health.GetComponent<FxHealth>();
            Build(health, enemyGo);

            var edges = EdgesBetween(enemyGo, health);
            Assert.IsTrue(edges.Exists(e => e.Kind == EdgeKind.FieldReference && e.Label == "FxEnemy.target"),
                "FieldReference FxEnemy.target missing");
            var edge = edges.Find(e => e.Kind == EdgeKind.FieldReference && e.Label == "FxEnemy.target");
            Assert.AreEqual(enemy, edge.SourceObject);
            Assert.AreEqual(health.GetComponent<FxHealth>(), edge.TargetObject);
        }

        [Test]
        public void ArrayElementReferenceHasIndex()
        {
            var h1 = Go("h1", typeof(FxHealth));
            var h2 = Go("h2", typeof(FxHealth));
            var enemyGo = Go("e", typeof(FxEnemy));
            enemyGo.GetComponent<FxEnemy>().target = h1.GetComponent<FxHealth>();
            var f = typeof(FxEnemy).GetField("others",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(enemyGo.GetComponent<FxEnemy>(), new[] { h1.GetComponent<FxHealth>(), h2.GetComponent<FxHealth>() });
            Build(h1, h2, enemyGo);

            var toH1 = EdgesBetween(enemyGo, h1);
            var toH2 = EdgesBetween(enemyGo, h2);
            Assert.IsTrue(toH1.Exists(e => e.Label == "FxEnemy.others[0]"),
                "array element edge others[0] missing");
            Assert.IsTrue(toH2.Exists(e => e.Label == "FxEnemy.others[1]"),
                "array element edge others[1] missing");
        }

        [Test]
        public void ScriptableObjectReferenceCreatesAssetNode()
        {
            var user = Go("u", typeof(FxConfigUser));
            var cfg = ScriptableObject.CreateInstance<FxConfig>();
            cfg.name = "testcfg";
            user.GetComponent<FxConfigUser>().config = cfg;
            Build(user);

            var assetId = "asset:" + cfg.GetInstanceID();
            Assert.IsTrue(graph.Nodes.TryGetValue(assetId, out var node), "asset node missing");
            Assert.AreEqual(NodeKind.Asset, node.Kind);
            Assert.AreEqual("(not saved)", node.Caption);
            var edges = EdgesBetween("go:" + user.GetInstanceID(), assetId);
            Assert.IsTrue(edges.Exists(e => e.Kind == EdgeKind.FieldReference));
        }

        [Test]
        public void UnityEventBindingEdge()
        {
            var src = Go("src", typeof(FxEventSource));
            var tgt = Go("tgt", typeof(FxHealth));
            var health = tgt.GetComponent<FxHealth>();
            var onFire = src.GetComponent<FxEventSource>().onFire;
            UnityEventTools.AddPersistentListener(onFire, health.Heal);
            Build(src, tgt);

            var edges = EdgesBetween(src, tgt);
            Assert.IsTrue(edges.Exists(e =>
                    e.Kind == EdgeKind.UnityEventBinding &&
                    e.Label.Contains("FxEventSource.onFire") &&
                    e.Label.Contains("FxHealth.Heal()")),
                "UnityEventBinding edge missing; edges: " + string.Join(" | ", edges.ConvertAll(x => x.Label)));
        }

        [Test]
        public void BrokenListenerGivesBadgeAndDiagnostic()
        {
            var src = Go("src", typeof(FxEventSource));
            var tgt = Go("tgt", typeof(FxHealth));
            var onFire = src.GetComponent<FxEventSource>().onFire;
            UnityEventTools.AddPersistentListener(onFire, tgt.GetComponent<FxHealth>().Heal);

            var so = new SerializedObject(src.GetComponent<FxEventSource>());
            var calls = so.FindProperty("onFire.m_PersistentCalls.m_Calls");
            Assert.IsNotNull(calls);
            var el = calls.GetArrayElementAtIndex(0);
            el.FindPropertyRelative("m_MethodName").stringValue = "";
            so.ApplyModifiedPropertiesWithoutUndo();
            Build(src, tgt);

            var node = NodeFor(src);
            Assert.IsNotNull(node);
            Assert.IsTrue(node.Badges.HasFlag(NodeBadges.BrokenEvent), "BrokenEvent badge missing");
            Assert.IsTrue(graph.Diagnostics.Exists(d => d.Contains("broken UnityEvent listener")),
                "diagnostic missing");
        }

        [Test]
        public void PossibleCallSingleHost()
        {
            var finder = Go("f", typeof(FxFinder));
            var health = Go("h", typeof(FxHealth));
            Build(finder, health);

            var edges = EdgesBetween(finder, health);
            Assert.IsTrue(edges.Exists(e => e.Approximate &&
                    (e.Kind == EdgeKind.MethodCall || e.Kind == EdgeKind.GetComponent)),
                "approximate possible-call edge missing");
        }

        [Test]
        public void PossibleCallTwoHostsNoEdge()
        {
            var finder = Go("f", typeof(FxFinder));
            var h1 = Go("h1", typeof(FxHealth));
            var h2 = Go("h2", typeof(FxHealth));
            Build(finder, h1, h2);

            Assert.AreEqual(0, EdgesBetween(finder, h1).Count);
            Assert.AreEqual(0, EdgesBetween(finder, h2).Count);
        }

        [Test]
        public void CallMergedIntoFieldReferenceDetails()
        {
            var health = Go("h", typeof(FxHealth));
            var enemyGo = Go("e", typeof(FxEnemy));
            enemyGo.GetComponent<FxEnemy>().target = health.GetComponent<FxHealth>();
            Build(health, enemyGo);

            var edges = EdgesBetween(enemyGo, health);
            var fr = edges.Find(e => e.Kind == EdgeKind.FieldReference);
            Assert.IsNotNull(fr);
            Assert.IsNotNull(fr.Details, "Details missing");
            Assert.IsTrue(fr.Details.Exists(d => d.Contains("TakeDamage")),
                "Details lacks TakeDamage: " + string.Join(" | ", fr.Details));
            Assert.IsFalse(edges.Exists(e => e.Kind == EdgeKind.MethodCall),
                "separate MethodCall edge should not exist");
        }

        [Test]
        public void TriggerColliderBadges()
        {
            var zone = Go("zone", typeof(FxListener), typeof(BoxCollider));
            zone.GetComponent<BoxCollider>().isTrigger = true;
            Build(zone);

            var node = NodeFor(zone);
            Assert.IsTrue(node.Badges.HasFlag(NodeBadges.TriggerCollider));
            Assert.IsTrue(node.Badges.HasFlag(NodeBadges.Trigger));
        }

        [Test]
        public void InclusionRules()
        {
            var plain = new GameObject("plain");
            plain.AddComponent<MeshRenderer>();
            var inactive = Go("inactive", typeof(FxHealth));
            inactive.SetActive(false);
            Build(plain, inactive);

            Assert.IsNull(NodeFor(plain), "plain MeshRenderer GO should not be a node");
            var inactiveNode = NodeFor(inactive);
            Assert.IsNotNull(inactiveNode);
            Assert.IsTrue(inactiveNode.Inactive);

            graph = SceneScanner.Build(new[] { plain }, typeGraph, Options(showAll: true));
            Assert.IsNotNull(NodeFor(plain), "ShowAllGameObjects should include plain GO");
        }

        [Test]
        public void DestroyedTargetReferenceDoesNotThrow()
        {
            var health = Go("h", typeof(FxHealth));
            var enemyGo = Go("e", typeof(FxEnemy));
            enemyGo.GetComponent<FxEnemy>().target = health.GetComponent<FxHealth>();
            Object.DestroyImmediate(health);
            Assert.DoesNotThrow(() => Build(enemyGo));
        }

        [Test]
        public void SceneGraphCanvasSmoke()
        {
            var a = Go("a", typeof(FxEnemy));
            var b = Go("b", typeof(FxHealth));
            a.GetComponent<FxEnemy>().target = b.GetComponent<FxHealth>();
            Build(a, b);

            var filtered = GraphFilter.Apply(graph, GraphFilterSettings.CreateDefault());
            var tempDir = Path.Combine(Path.GetTempPath(), "ci-scenecanvas-" + Path.GetRandomFileName());
            var canvas = new GraphCanvas(new LayoutStore(Path.Combine(tempDir, "Layout.json")), "Scene:test");
            Assert.DoesNotThrow(() => canvas.Bind(graph, filtered));
            Assert.AreEqual(filtered.NodeIds.Count, canvas.VisibleNodeCount);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        [Test]
        public void Perf500GameObjects()
        {
            var roots = new List<GameObject>();
            var rnd = new System.Random(3);
            var healths = new List<GameObject>();
            for (var i = 0; i < 50; i++)
                healths.Add(Go("h" + i, typeof(FxHealth)));
            roots.AddRange(healths);
            for (var i = 0; i < 500; i++)
            {
                var go = Go("e" + i, typeof(FxEnemy), typeof(FxEventSource));
                go.GetComponent<FxEnemy>().target =
                    healths[rnd.Next(healths.Count)].GetComponent<FxHealth>();
                var src = go.GetComponent<FxEventSource>().onFire;
                var listener = healths[rnd.Next(healths.Count)].GetComponent<FxHealth>();
                UnityEventTools.AddPersistentListener(src, listener.Heal);
                roots.Add(go);
            }

            var sw = Stopwatch.StartNew();
            graph = SceneScanner.Build(roots, typeGraph, Options());
            sw.Stop();
            UnityEngine.Debug.Log($"SceneScanner perf: {sw.ElapsedMilliseconds} ms for 550 GameObjects");
            Assert.Less(sw.ElapsedMilliseconds, 1000, "scene scan too slow");
            Assert.Greater(graph.Edges.Count, 0);
        }
    }
}
