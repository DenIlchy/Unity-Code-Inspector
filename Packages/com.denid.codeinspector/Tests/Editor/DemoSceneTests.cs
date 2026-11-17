using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Denid.CodeInspector.Editor.Tests
{
    public class DemoSceneTests
    {
        const string ScenePath = "Assets/Samples/Code Inspector/0.1.0/Demo/Demo.unity";

        CodeGraph graph;

        [SetUp]
        public void SetUp()
        {
            if (!System.IO.File.Exists(ScenePath))
                Assert.Ignore("Demo sample not imported into this project");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            graph = SceneScanner.BuildForOpenScenes();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        List<GraphNode> NodesNamed(string label)
        {
            var list = new List<GraphNode>();
            foreach (var n in graph.Nodes.Values)
                if (n.Label == label)
                    list.Add(n);
            return list;
        }

        GraphNode FindNodeByLabel(string label)
        {
            var list = NodesNamed(label);
            return list.Count > 0 ? list[0] : null;
        }

        bool HasEdge(string fromId, string toId, EdgeKind kind, bool? approximate = null)
        {
            foreach (var e in graph.Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind == kind &&
                    (approximate == null || e.Approximate == approximate.Value))
                    return true;
            }
            return false;
        }

        [Test]
        public void DemoSceneHasExpectedNodes()
        {
            foreach (var name in new[] { "Player", "GameManager", "TriggerZone", "HUD", "Spawner" })
                Assert.IsNotNull(FindNodeByLabel(name), "missing node " + name);
            Assert.GreaterOrEqual(NodesNamed("Enemy 1").Count + NodesNamed("Enemy 2").Count, 2,
                "expected two enemy nodes");

            var config = false;
            foreach (var n in graph.Nodes.Values)
            {
                if (n.Kind == NodeKind.Asset && n.Caption != null && n.Caption.EndsWith("GameConfig.asset"))
                    config = true;
            }
            Assert.IsTrue(config, "GameConfig asset node missing");
        }

        [Test]
        public void DemoSceneEdges()
        {
            var player = FindNodeByLabel("Player");
            var gm = FindNodeByLabel("GameManager");
            var spawner = FindNodeByLabel("Spawner");
            var hud = FindNodeByLabel("HUD");
            var enemy = FindNodeByLabel("Enemy 1");
            Assert.IsNotNull(player);
            Assert.IsNotNull(gm);
            Assert.IsNotNull(spawner);
            Assert.IsNotNull(hud);
            Assert.IsNotNull(enemy);

            Assert.IsTrue(HasEdge(enemy.Id, player.Id, EdgeKind.FieldReference),
                "Enemy -> Player FieldReference missing");
            Assert.IsTrue(HasEdge(gm.Id, spawner.Id, EdgeKind.UnityEventBinding),
                "GameManager -> Spawner UnityEventBinding missing");
            Assert.IsTrue(HasEdge(hud.Id, player.Id, EdgeKind.FieldReference),
                "HUD -> Player FieldReference missing");

            var approx = false;
            foreach (var e in graph.Edges)
            {
                if (e.FromId == spawner.Id && e.ToId == gm.Id && e.Approximate)
                    approx = true;
            }
            Assert.IsTrue(approx, "Spawner -> GameManager approximate edge missing");
        }
    }
}
