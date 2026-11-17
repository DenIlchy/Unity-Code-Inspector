using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Denid.CodeInspector.Editor.Tests
{
    public class TypeScannerTests
    {
        static readonly Type[] FixtureTypes =
        {
            typeof(FxBase),
            typeof(FxEnemy),
            typeof(FxHealth),
            typeof(FxListener),
            typeof(FxMessenger),
            typeof(FxUsage),
            typeof(IFxDamageDealer),
            typeof(FxCoroutineUser),
            typeof(FxLambdaUser),
            typeof(FxService),
            typeof(FxScorer),
            typeof(FxInterfaceCaller),
        };

        static CodeGraph BuildGraph()
        {
            var options = new ScanOptions
            {
                AssemblyFilter = _ => true,
            };
            return TypeScanner.Build(FixtureTypes, options);
        }

        static string Id(Type t) => CodeGraph.NodeIdFor(t);

        static bool HasEdge(CodeGraph g, Type from, Type to, EdgeKind kind)
        {
            var fromId = Id(from);
            var toId = Id(to);
            foreach (var e in g.Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind == kind)
                    return true;
            }
            return false;
        }

        [Test]
        public void NodesExistForAllFixtureTypes()
        {
            var g = BuildGraph();
            foreach (var t in FixtureTypes)
                Assert.IsTrue(g.Nodes.ContainsKey(Id(t)), "Missing node for " + t.Name);
        }

        [Test]
        public void InheritanceEdges()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxEnemy), typeof(FxBase), EdgeKind.Inheritance),
                "FxEnemy -> FxBase inheritance edge missing");
            Assert.IsTrue(HasEdge(g, typeof(FxEnemy), typeof(IFxDamageDealer), EdgeKind.Inheritance),
                "FxEnemy -> IFxDamageDealer interface edge missing");
        }

        [Test]
        public void FieldReferenceEdge()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxEnemy), typeof(FxHealth), EdgeKind.FieldReference),
                "FxEnemy -> FxHealth field reference missing");
        }

        [Test]
        public void MethodCallEdge()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxEnemy), typeof(FxHealth), EdgeKind.MethodCall),
                "FxEnemy.Attack -> FxHealth.TakeDamage call edge missing");
        }

        [Test]
        public void GetComponentEdge()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxEnemy), typeof(FxHealth), EdgeKind.GetComponent),
                "FxEnemy -> FxHealth GetComponent edge missing");
            Assert.IsTrue(HasEdge(g, typeof(FxListener), typeof(FxHealth), EdgeKind.GetComponent),
                "FxListener -> FxHealth GetComponent edge missing");
        }

        [Test]
        public void CSharpEventSubscriptionEdge()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxListener), typeof(FxHealth), EdgeKind.CSharpEventSubscription),
                "FxListener -> FxHealth event subscription edge missing");
        }

        [Test]
        public void SendMessageEdgeIsApproximate()
        {
            var g = BuildGraph();
            var fromId = Id(typeof(FxMessenger));
            var toId = Id(typeof(FxHealth));
            GraphEdge found = null;
            foreach (var e in g.Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind == EdgeKind.SendMessage)
                    found = e;
            }
            Assert.IsNotNull(found, "FxMessenger -> FxHealth SendMessage edge missing");
            Assert.IsTrue(found.Approximate, "SendMessage edge should be approximate");
            Assert.AreEqual("TakeDamage", found.Label);
        }

        [Test]
        public void TypeUsageEdge()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxUsage), typeof(FxHealth), EdgeKind.TypeUsage),
                "FxUsage -> FxHealth type-usage edge missing");
        }

        [Test]
        public void BadgesAreSet()
        {
            var g = BuildGraph();
            var listener = g.Nodes[Id(typeof(FxListener))];
            Assert.IsTrue(listener.Badges.HasFlag(NodeBadges.Update), "FxListener should have Update badge");
            Assert.IsTrue(listener.Badges.HasFlag(NodeBadges.Trigger), "FxListener should have Trigger badge");

            var health = g.Nodes[Id(typeof(FxHealth))];
            Assert.IsTrue(health.Badges.HasFlag(NodeBadges.HasEvents), "FxHealth should have HasEvents badge");
        }

        [Test]
        public void NoSelfEdges()
        {
            var g = BuildGraph();
            foreach (var e in g.Edges)
                Assert.AreNotEqual(e.FromId, e.ToId, "Self edge of kind " + e.Kind + " on " + e.FromId);
        }

        [Test]
        public void MembersAreFilled()
        {
            var g = BuildGraph();
            var enemy = g.Nodes[Id(typeof(FxEnemy))];
            var names = new List<string>();
            foreach (var m in enemy.Members)
                names.Add(m.Name);
            Assert.Contains("target", names);
            Assert.Contains("Deal", names);
            Assert.Contains("Attack", names);
        }

        [Test]
        public void CoroutineCallIsDetected()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxCoroutineUser), typeof(FxHealth), EdgeKind.MethodCall),
                "FxCoroutineUser -> FxHealth coroutine call edge missing");
            foreach (var n in g.Nodes.Values)
                Assert.IsFalse(n.Type != null && n.Type.Name.StartsWith("<"),
                    "Compiler-generated node present: " + n.Label);
        }

        [Test]
        public void LambdaCallIsDetected()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxLambdaUser), typeof(FxHealth), EdgeKind.MethodCall),
                "FxLambdaUser -> FxHealth lambda call edge missing");
        }

        [Test]
        public void PlainClassCallAndFieldAccessAreDetected()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxScorer), typeof(FxService), EdgeKind.MethodCall),
                "FxScorer -> FxService call edge missing");
            Assert.IsTrue(HasEdge(g, typeof(FxScorer), typeof(FxService), EdgeKind.FieldAccess),
                "FxScorer -> FxService field-access edge missing");
            Assert.AreEqual(ScriptCategory.PlainClass, g.Nodes[Id(typeof(FxService))].Category);
        }

        [Test]
        public void InterfaceCallIsDetected()
        {
            var g = BuildGraph();
            Assert.IsTrue(HasEdge(g, typeof(FxInterfaceCaller), typeof(IFxDamageDealer), EdgeKind.MethodCall),
                "FxInterfaceCaller -> IFxDamageDealer call edge missing");
            Assert.AreEqual(ScriptCategory.Interface, g.Nodes[Id(typeof(IFxDamageDealer))].Category);
        }

        [Test]
        public void CategoriesAreAssigned()
        {
            var g = BuildGraph();
            Assert.AreEqual(ScriptCategory.MonoBehaviour, g.Nodes[Id(typeof(FxHealth))].Category);
            Assert.AreEqual(ScriptCategory.MonoBehaviour, g.Nodes[Id(typeof(FxEnemy))].Category);
        }

        [Test]
        public void DefaultScanExcludesOwnAndGeneratedTypes()
        {
            var g = TypeScanner.Build();
            foreach (var n in g.Nodes.Values)
            {
                if (n.Type != null)
                {
                    var asmName = n.Type.Assembly.GetName().Name;
                    Assert.IsFalse(
                        asmName == "Denid.CodeInspector.Runtime" ||
                        asmName == "Denid.CodeInspector.Editor" ||
                        asmName == "Denid.CodeInspector.Editor.Tests",
                        "Own-assembly node present: " + n.Label);
                    Assert.IsFalse(n.Type.Name.StartsWith("<"),
                        "Compiler-generated node present: " + n.Label);
                }
            }
        }

        [Test]
        public void DefaultScanDoesNotThrow()
        {
            CodeGraph g = null;
            Assert.DoesNotThrow(() => g = TypeScanner.Build());
            Assert.IsNotNull(g);
        }
    }
}
