using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    public sealed class SceneScanOptions
    {
        /// <summary>Decides whether a component type counts as a user script. Defaults to "in the type graph".</summary>
        public Func<Type, bool> IsUserScript;

        /// <summary>Include every GameObject, even those with no scripts or edges.</summary>
        public bool ShowAllGameObjects;

        /// <summary>Include inactive GameObjects.</summary>
        public bool IncludeInactive = true;
    }

    /// <summary>
    /// Builds a <see cref="CodeGraph"/> of GameObject nodes from loaded scenes or the
    /// prefab stage, layering serialized references, UnityEvent bindings and
    /// "possible call" edges on top of the architecture type graph.
    /// </summary>
    public static class SceneScanner
    {
        static readonly HashSet<EdgeKind> PossibleCallKinds = new HashSet<EdgeKind>
        {
            EdgeKind.MethodCall, EdgeKind.GetComponent, EdgeKind.CSharpEventSubscription,
            EdgeKind.SendMessage, EdgeKind.FieldAccess,
        };

        static readonly Dictionary<Type, List<string>> eventFieldPathsCache =
            new Dictionary<Type, List<string>>();

        /// <summary>Collects roots from all loaded scenes, or the open prefab stage.</summary>
        public static CodeGraph BuildForOpenScenes(SceneScanOptions options = null)
        {
            options ??= new SceneScanOptions();
            var roots = new List<GameObject>();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                roots.Add(stage.prefabContentsRoot);
            }
            else
            {
                var sceneManager = SceneManager.sceneCount;
                for (var i = 0; i < sceneManager; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                        roots.AddRange(scene.GetRootGameObjects());
                }
                if (Application.isPlaying)
                    AddDontDestroyOnLoadRoots(roots);
            }
            return Build(roots, ArchitectureGraphCache.Graph, options);
        }

        static Scene ddolScene;
        static bool ddolHookInstalled;

        static void AddDontDestroyOnLoadRoots(List<GameObject> roots)
        {
            if (!ddolHookInstalled)
            {
                ddolHookInstalled = true;
                EditorApplication.playModeStateChanged += change =>
                {
                    if (change == PlayModeStateChange.ExitingPlayMode ||
                        change == PlayModeStateChange.EnteredEditMode)
                        ddolScene = default;
                };
            }
            try
            {
                // Probe once per play session; creating a GameObject on every
                // scan would fire hierarchyChanged and retrigger the scan.
                if (!ddolScene.IsValid() || !ddolScene.isLoaded)
                {
                    var probe = new GameObject("__CodeInspector_DDOL_probe__");
                    Object.DontDestroyOnLoad(probe);
                    ddolScene = probe.scene;
                    Object.DestroyImmediate(probe);
                }
                if (ddolScene.IsValid() && ddolScene.isLoaded)
                    roots.AddRange(ddolScene.GetRootGameObjects());
            }
            catch (Exception)
            {
                // Best effort only.
            }
        }

        class EventBinding
        {
            public string EventPath;
            public Object Target;
            public string MethodName;
            public int Mode;
            public int CallState;
        }

        class FieldRef
        {
            public Component Component;
            public string DisplayPath;
            public Object Target;
        }

        class GoData
        {
            public GameObject Go;
            public string Path;
            public List<ComponentInfo> UserComps = new List<ComponentInfo>();
            public List<ComponentInfo> EventComps = new List<ComponentInfo>();
            public List<ComponentInfo> PhysicsComps = new List<ComponentInfo>();
            public List<FieldRef> FieldRefs = new List<FieldRef>();
            public List<(Component owner, EventBinding binding)> Bindings =
                new List<(Component, EventBinding)>();
            public int BrokenListeners;
            public int MissingScripts;
            public bool HasTriggerCollider;
            public bool HasRigidbody;
            public bool Included;
        }

        public static CodeGraph Build(IReadOnlyList<GameObject> roots, CodeGraph typeGraph,
            SceneScanOptions options = null)
        {
            options ??= new SceneScanOptions();
            var graph = new CodeGraph();
            var isUser = options.IsUserScript ?? (t => typeGraph != null &&
                                                     t != null &&
                                                     typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)));

            var sceneCount = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded)
                    sceneCount++;
            }

            var gos = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (var root in roots)
            {
                if (root == null)
                    continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(options.IncludeInactive))
                {
                    if (seen.Add(t.gameObject.GetInstanceID()))
                        gos.Add(t.gameObject);
                }
            }

            var data = new List<GoData>(gos.Count);
            var byGo = new Dictionary<int, GoData>(gos.Count);
            foreach (var go in gos)
            {
                var d = new GoData { Go = go, Path = HierarchyPath(go, sceneCount > 1) };
                try
                {
                    ScanGameObject(d, isUser, typeGraph, graph);
                }
                catch (Exception e)
                {
                    graph.Diagnostics.Add("Scan failed for " + d.Path + ": " + e.Message);
                }
                data.Add(d);
                byGo[go.GetInstanceID()] = d;
            }

            // Inclusion rule.
            foreach (var d in data)
            {
                d.Included = options.ShowAllGameObjects ||
                             d.UserComps.Count > 0 ||
                             d.FieldRefs.Count > 0 ||
                             d.Bindings.Count > 0 ||
                             d.BrokenListeners > 0;
            }

            // Targets of edges from included sources become nodes too.
            foreach (var d in data)
            {
                if (!d.Included)
                    continue;
                foreach (var fr in d.FieldRefs)
                    IncludeTarget(fr.Target, byGo);
                foreach (var b in d.Bindings)
                    IncludeTarget(b.binding.Target, byGo);
            }

            var nodeByGoId = new Dictionary<int, GraphNode>();
            var nodeByAssetId = new Dictionary<int, GraphNode>();
            foreach (var d in data)
            {
                if (!d.Included)
                    continue;
                var node = CreateGoNode(d, graph);
                nodeByGoId[d.Go.GetInstanceID()] = node;
            }

            GraphNode NodeForTarget(Object target)
            {
                if (target == null)
                    return null;
                var go = target as GameObject ?? (target as Component)?.gameObject;
                if (go != null && !EditorUtility.IsPersistent(go))
                {
                    if (nodeByGoId.TryGetValue(go.GetInstanceID(), out var existing))
                        return existing;
                    if (byGo.TryGetValue(go.GetInstanceID(), out var td))
                    {
                        // Target wasn't included by rule; include it now.
                        var created = CreateGoNode(td, graph);
                        nodeByGoId[go.GetInstanceID()] = created;
                        return created;
                    }
                    return null;
                }
                // Asset target.
                var isAllowed = target is ScriptableObject || target is GameObject ||
                                options.ShowAllGameObjects;
                if (!isAllowed)
                    return null;
                var id = target.GetInstanceID();
                if (!nodeByAssetId.TryGetValue(id, out var an))
                {
                    an = CreateAssetNode(target, graph);
                    nodeByAssetId[id] = an;
                }
                return an;
            }

            // FieldReference edges.
            foreach (var d in data)
            {
                if (!d.Included || !nodeByGoId.TryGetValue(d.Go.GetInstanceID(), out var fromNode))
                    continue;
                foreach (var fr in d.FieldRefs)
                {
                    var toNode = NodeForTarget(fr.Target);
                    if (toNode == null || toNode.Id == fromNode.Id)
                        continue;
                    var label = fr.Component.GetType().Name + "." + fr.DisplayPath;
                    var edge = graph.GetOrAddEdge( fromNode.Id, toNode.Id, EdgeKind.FieldReference, label);
                    edge.SourceObject = fr.Component;
                    edge.TargetObject = fr.Target;
                }
            }

            // UnityEventBinding edges.
            foreach (var d in data)
            {
                if (!d.Included || !nodeByGoId.TryGetValue(d.Go.GetInstanceID(), out var fromNode))
                    continue;
                if (d.BrokenListeners > 0)
                    fromNode.Badges |= NodeBadges.BrokenEvent;
                foreach (var (owner, b) in d.Bindings)
                {
                    var toNode = NodeForTarget(b.Target);
                    if (toNode == null)
                        continue;
                    var targetType = b.Target != null ? b.Target.GetType().Name : "?";
                    var label = owner.GetType().Name + "." + EventName(b.EventPath) +
                                " → " + targetType + "." + b.MethodName + "()";
                    var edge = graph.GetOrAddEdge( fromNode.Id, toNode.Id,
                        EdgeKind.UnityEventBinding, label);
                    edge.SourceObject = owner;
                    edge.TargetObject = b.Target;
                    edge.EventPath = b.EventPath;
                }
            }

            // Possible calls: merge into FieldReference edges or add approximate edges.
            if (typeGraph != null)
            {
                var hostsByType = new Dictionary<Type, List<GraphNode>>();
                foreach (var d in data)
                {
                    if (!d.Included || !nodeByGoId.TryGetValue(d.Go.GetInstanceID(), out var node))
                        continue;
                    foreach (var c in d.UserComps)
                    {
                        if (c.Type == null)
                            continue;
                        foreach (var kv in EnumerateBaseTypes(c.Type))
                        {
                            if (!hostsByType.TryGetValue(kv, out var l))
                                hostsByType[kv] = l = new List<GraphNode>();
                            if (!l.Contains(node))
                                l.Add(node);
                        }
                    }
                }

                var fieldEdgeBetween = new Dictionary<(string, string), GraphEdge>();
                foreach (var e in graph.Edges)
                {
                    if (e.Kind == EdgeKind.FieldReference)
                        fieldEdgeBetween[(e.FromId, e.ToId)] = e;
                }

                foreach (var te in typeGraph.Edges)
                {
                    if (!PossibleCallKinds.Contains(te.Kind) || te.FromId == te.ToId)
                        continue;
                    var typeA = typeGraph.Nodes.TryGetValue(te.FromId, out var na) ? na.Type : null;
                    var typeB = typeGraph.Nodes.TryGetValue(te.ToId, out var nb) ? nb.Type : null;
                    if (typeA == null || typeB == null)
                        continue;
                    if (!hostsByType.TryGetValue(typeA, out var hostsA) ||
                        !hostsByType.TryGetValue(typeB, out var hostsB))
                        continue;
                    foreach (var hostA in hostsA)
                    {
                        foreach (var hostB in hostsB)
                        {
                            if (hostA == hostB)
                                continue;
                            if (fieldEdgeBetween.TryGetValue((hostA.Id, hostB.Id), out var fe))
                            {
                                fe.Details ??= new List<string>();
                                var detail = "calls " + te.Label;
                                if (!fe.Details.Contains(detail))
                                    fe.Details.Add(detail);
                            }
                            else if (hostsB.Count == 1)
                            {
                                graph.GetOrAddEdge(hostA.Id, hostB.Id,
                                    te.Kind, te.Label, approximate: true);
                            }
                        }
                    }
                }
            }

            BatchPersistKeys(graph);
            return graph;
        }

        /// <summary>Fills PersistKey for GameObject nodes in one native call.</summary>
        static void BatchPersistKeys(CodeGraph graph)
        {
            var nodes = new List<GraphNode>();
            var objects = new List<Object>();
            foreach (var n in graph.Nodes.Values)
            {
                if (n.Kind == NodeKind.GameObject && n.Target != null)
                {
                    nodes.Add(n);
                    objects.Add(n.Target);
                }
            }
            if (objects.Count == 0)
                return;
            var ids = new GlobalObjectId[objects.Count];
            try
            {
                GlobalObjectId.GetGlobalObjectIdsSlow(objects.ToArray(), ids);
            }
            catch (Exception)
            {
                ids = null;
            }
            for (var i = 0; i < nodes.Count; i++)
            {
                if (ids != null && ids[i].identifierType != 0)
                {
                    nodes[i].PersistKey = ids[i].ToString();
                }
                else
                {
                    var go = nodes[i].Target as GameObject;
                    nodes[i].PersistKey = go != null
                        ? PersistKeyFor(go, nodes[i].Caption)
                        : null;
                }
            }
        }

        static void IncludeTarget(Object target, Dictionary<int, GoData> byGo)
        {
            var go = target as GameObject ?? (target as Component)?.gameObject;
            if (go != null && byGo.TryGetValue(go.GetInstanceID(), out var td))
                td.Included = true;
        }

        static IEnumerable<Type> EnumerateBaseTypes(Type t)
        {
            for (var x = t; x != null && x != typeof(object); x = x.BaseType)
                yield return x;
            foreach (var i in t.GetInterfaces())
                yield return i;
        }

        static string EventName(string eventPath)
        {
            var name = eventPath;
            var dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);
            if (name.StartsWith("m_", StringComparison.Ordinal))
                name = name.Substring(2);
            return name;
        }

        static string HierarchyPath(GameObject go, bool withScene)
        {
            var parts = new List<string>();
            var t = go.transform.parent;
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            var path = string.Join("/", parts);
            if (withScene && go.scene.IsValid())
                path = go.scene.name + (path.Length > 0 ? "/" + path : "");
            return path;
        }

        static void ScanGameObject(GoData d, Func<Type, bool> isUser, CodeGraph typeGraph, CodeGraph graph)
        {
            var go = d.Go;
            var comps = go.GetComponents<Component>();

            foreach (var c in comps)
            {
                if (c == null)
                {
                    d.MissingScripts++;
                    continue;
                }
                var type = c.GetType();
                var info = new ComponentInfo
                {
                    Component = c,
                    Type = type,
                    Category = CodeGraph.CategoryOf(type),
                    IsUserScript = c is MonoBehaviour && isUser(type),
                    Enabled = IsEnabled(c),
                };
                if (info.IsUserScript && typeGraph != null &&
                    typeGraph.Nodes.TryGetValue(CodeGraph.NodeIdFor(type), out var typeNode))
                    info.Badges = typeNode.Badges;

                if (info.IsUserScript)
                {
                    d.UserComps.Add(info);
                    ScanFieldReferences(d, c, graph);
                }
                else if (c is MonoBehaviour && HasEventFields(type))
                {
                    d.EventComps.Add(info);
                }

                if (c is Collider col)
                {
                    info.Category = ScriptCategory.PlainClass;
                    if (col.isTrigger)
                        d.HasTriggerCollider = true;
                    d.PhysicsComps.Add(info);
                }
                else if (c is Collider2D col2)
                {
                    if (col2.isTrigger)
                        d.HasTriggerCollider = true;
                    d.PhysicsComps.Add(info);
                }
                else if (c is Rigidbody || c is Rigidbody2D)
                {
                    d.HasRigidbody = true;
                    d.PhysicsComps.Add(info);
                }

                if (c is MonoBehaviour)
                    ScanUnityEvents(d, c, graph);
            }

            if (d.MissingScripts > 0)
                graph.Diagnostics.Add(d.Path + ": " + d.MissingScripts + " missing script(s)");
        }

        static bool IsEnabled(Component c)
        {
            switch (c)
            {
                case Collider col: return col.enabled;
                case Collider2D col2: return col2.enabled;
                case Renderer r: return r.enabled;
                case Behaviour b: return b.enabled;
                default: return true;
            }
        }

        static void ScanFieldReferences(GoData d, Component c, CodeGraph graph)
        {
            SerializedObject so;
            try
            {
                so = new SerializedObject(c);
            }
            catch (Exception)
            {
                return;
            }
            var it = so.GetIterator();
            var guard = 0;
            while (it.Next(true) && guard++ < 4000)
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                var path = it.propertyPath;
                if (path == "m_Script" || path == "m_GameObject" ||
                    path.Contains("m_PersistentCalls"))
                    continue;
                var target = it.objectReferenceValue;
                if (target == null)
                    continue;
                var displayPath = path
                    .Replace(".Array.data[", "[");
                d.FieldRefs.Add(new FieldRef
                {
                    Component = c,
                    DisplayPath = displayPath,
                    Target = target,
                });
            }
            if (guard >= 4000)
                graph.Diagnostics.Add(d.Path + ": " + c.GetType().Name +
                    " serialized property scan truncated");
        }

        static bool HasEventFields(Type type)
        {
            return EventFieldPaths(type).Count > 0;
        }

        /// <summary>Serialized field paths (depth <= 3) that hold a UnityEventBase.</summary>
        static List<string> EventFieldPaths(Type type)
        {
            if (eventFieldPathsCache.TryGetValue(type, out var cached))
                return cached;
            var paths = new List<string>();
            CollectEventFields(type, "", 0, paths, new HashSet<Type>());
            eventFieldPathsCache[type] = paths;
            return paths;
        }

        static void CollectEventFields(Type type, string prefix, int depth,
            List<string> paths, HashSet<Type> visited)
        {
            if (type == null || depth > 3 || !visited.Add(type))
                return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(flags);
            }
            catch (Exception)
            {
                return;
            }
            foreach (var f in fields)
            {
                var ft = f.FieldType;
                var path = prefix.Length == 0 ? f.Name : prefix + "." + f.Name;
                if (typeof(UnityEventBase).IsAssignableFrom(ft))
                {
                    paths.Add(path);
                }
                else if (depth < 3 && !ft.IsPrimitive && !ft.IsEnum && ft != typeof(string) &&
                         !typeof(Object).IsAssignableFrom(ft) &&
                         (f.IsPublic || f.IsDefined(typeof(SerializeField), true)) &&
                         (ft.IsClass || ft.IsValueType))
                {
                    CollectEventFields(ft, path, depth + 1, paths, visited);
                }
            }
        }

        static void ScanUnityEvents(GoData d, Component c, CodeGraph graph)
        {
            var paths = EventFieldPaths(c.GetType());
            if (paths.Count == 0)
                return;
            SerializedObject so;
            try
            {
                so = new SerializedObject(c);
            }
            catch (Exception)
            {
                return;
            }
            foreach (var path in paths)
            {
                SerializedProperty calls;
                try
                {
                    calls = so.FindProperty(path + ".m_PersistentCalls.m_Calls");
                }
                catch (Exception)
                {
                    continue;
                }
                if (calls == null || !calls.isArray)
                    continue;
                for (var i = 0; i < calls.arraySize; i++)
                {
                    var el = calls.GetArrayElementAtIndex(i);
                    var target = el.FindPropertyRelative("m_Target")?.objectReferenceValue;
                    var method = el.FindPropertyRelative("m_MethodName")?.stringValue;
                    var mode = el.FindPropertyRelative("m_Mode")?.intValue ?? 0;
                    var state = el.FindPropertyRelative("m_CallState")?.intValue ?? 0;
                    if (target == null || string.IsNullOrEmpty(method))
                    {
                        d.BrokenListeners++;
                        graph.Diagnostics.Add(d.Path + ": broken UnityEvent listener on " +
                                              c.GetType().Name + "." + EventName(path) +
                                              " (element " + i + ")");
                        continue;
                    }
                    d.Bindings.Add((c, new EventBinding
                    {
                        EventPath = path,
                        Target = target,
                        MethodName = method,
                        Mode = mode,
                        CallState = state,
                    }));
                }
            }
        }

        static GraphNode CreateGoNode(GoData d, CodeGraph graph)
        {
            var go = d.Go;
            var node = new GraphNode
            {
                Id = "go:" + go.GetInstanceID(),
                Kind = NodeKind.GameObject,
                Label = go.name,
                Caption = d.Path,
                Target = go,
                Inactive = !go.activeInHierarchy,
                Components = new List<ComponentInfo>(),
            };
            node.Components.AddRange(d.UserComps);
            node.Components.AddRange(d.EventComps);
            node.Components.AddRange(d.PhysicsComps);

            foreach (var c in d.UserComps)
            {
                node.Badges |= c.Badges;
            }
            if (d.HasTriggerCollider)
                node.Badges |= NodeBadges.TriggerCollider;
            if (d.HasRigidbody)
                node.Badges |= NodeBadges.Rigidbody;
            if (d.BrokenListeners > 0)
                node.Badges |= NodeBadges.BrokenEvent;
            graph.Nodes[node.Id] = node;
            return node;
        }

        static string PersistKeyFor(GameObject go, string path)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            }
            catch (Exception)
            {
                return "scene:" + (go.scene.IsValid() ? go.scene.path : "") + "/" + path + "/" + go.name;
            }
        }

        static GraphNode CreateAssetNode(Object target, CodeGraph graph)
        {
            var path = AssetDatabase.GetAssetPath(target);
            var node = new GraphNode
            {
                Id = "asset:" + target.GetInstanceID(),
                Kind = NodeKind.Asset,
                Category = target is ScriptableObject ? ScriptCategory.ScriptableObject : ScriptCategory.PlainClass,
                Label = target.name,
                Caption = string.IsNullOrEmpty(path) ? "(not saved)" : path,
                Target = target,
                PersistKey = PersistKeyForAsset(target),
                Components = new List<ComponentInfo>(),
            };
            if (target is MonoScript ms)
            {
                var cls = ms.GetClass();
                if (cls != null)
                    node.Type = cls;
            }
            else
            {
                node.Type = target.GetType();
            }
            graph.Nodes[node.Id] = node;
            return node;
        }

        static string PersistKeyForAsset(Object target)
        {
            try
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out var guid, out long localId))
                    return guid + ":" + localId;
            }
            catch (Exception)
            {
            }
            return null;
        }
    }
}
