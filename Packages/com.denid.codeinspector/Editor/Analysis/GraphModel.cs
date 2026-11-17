using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    public enum NodeKind
    {
        ScriptType,
        ComponentInstance,
        GameObject,
        Event,
        Trigger,
        Asset,
    }

    public enum ScriptCategory
    {
        MonoBehaviour,
        ScriptableObject,
        PlainClass,
        Interface,
        Struct,
        StaticClass,
    }

    [Flags]
    public enum NodeBadges
    {
        None = 0,
        Update = 1 << 0,
        FixedUpdate = 1 << 1,
        LateUpdate = 1 << 2,
        Trigger = 1 << 3,
        Collision = 1 << 4,
        Mouse = 1 << 5,
        HasEvents = 1 << 6,
        BrokenEvent = 1 << 7,
        TriggerCollider = 1 << 8,
        Rigidbody = 1 << 9,
    }

    public enum MemberKind
    {
        Field,
        Property,
        Method,
        Event,
    }

    public enum EdgeKind
    {
        FieldReference,
        MethodCall,
        GetComponent,
        Inheritance,
        TypeUsage,
        UnityEventBinding,
        CSharpEventSubscription,
        UnityMessage,
        SendMessage,
        FieldAccess,
    }

    public sealed class GraphMember
    {
        public string Name;
        public MemberKind Kind;
        public string TypeName;
    }

    /// <summary>A component hosted on a GameObject node.</summary>
    public sealed class ComponentInfo
    {
        public Component Component;
        public Type Type;
        public ScriptCategory Category;
        public NodeBadges Badges;
        public bool IsUserScript;
        public bool Enabled;
    }

    public sealed class GraphNode
    {
        public string Id;
        public NodeKind Kind;
        public ScriptCategory Category;
        public string Label;
        /// <summary>Secondary line: hierarchy path / scene name / asset path.</summary>
        public string Caption;
        public Type Type;
        public Object Target;
        /// <summary>Stable key for layout persistence (GlobalObjectId etc.), may be null.</summary>
        public string PersistKey;
        public bool Inactive;
        public List<ComponentInfo> Components;
        public readonly List<GraphMember> Members = new List<GraphMember>();
        public NodeBadges Badges;
        public int ExecutionOrder;
    }

    public sealed class GraphEdge
    {
        public string FromId;
        public string ToId;
        public EdgeKind Kind;
        public string Label;
        public bool Approximate;
        public Object SourceObject;
        public Object TargetObject;
        /// <summary>Serialized property path of the UnityEvent field (UnityEventBinding edges).</summary>
        public string EventPath;
        public List<string> Details;
        /// <summary>Runtime-only edge drawn by the canvas (probe contacts).</summary>
        public bool Transient;
    }

    public sealed class CodeGraph
    {
        public readonly Dictionary<string, GraphNode> Nodes = new Dictionary<string, GraphNode>();
        public readonly List<GraphEdge> Edges = new List<GraphEdge>();
        public readonly List<string> Diagnostics = new List<string>();

        readonly HashSet<string> edgeKeys = new HashSet<string>();
        readonly HashSet<(string From, string To)> nonTypeUsagePairs = new HashSet<(string, string)>();

        public static string NodeIdFor(Type type)
        {
            return "type:" + type.AssemblyQualifiedName;
        }

        public static ScriptCategory CategoryOf(Type type)
        {
            if (type.IsInterface)
                return ScriptCategory.Interface;
            if (type.IsValueType)
                return ScriptCategory.Struct;
            if (type.IsAbstract && type.IsSealed)
                return ScriptCategory.StaticClass;
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
                return ScriptCategory.MonoBehaviour;
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return ScriptCategory.ScriptableObject;
            return ScriptCategory.PlainClass;
        }

        public GraphNode GetOrAddNode(Type type, NodeKind kind = NodeKind.ScriptType)
        {
            var id = NodeIdFor(type);
            if (Nodes.TryGetValue(id, out var node))
                return node;
            node = new GraphNode
            {
                Id = id,
                Kind = kind,
                Category = kind == NodeKind.ScriptType ? CategoryOf(type) : default,
                Label = FriendlyName(type),
                Type = type,
                ExecutionOrder = 0,
            };
            Nodes.Add(id, node);
            return node;
        }

        public bool AddEdge(string fromId, string toId, EdgeKind kind, string label, bool approximate = false)
        {
            var key = fromId + "|" + toId + "|" + kind + "|" + (label ?? string.Empty);
            if (!edgeKeys.Add(key))
                return false;
            if (kind != EdgeKind.TypeUsage)
                nonTypeUsagePairs.Add((fromId, toId));
            Edges.Add(new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Kind = kind,
                Label = label,
                Approximate = approximate,
            });
            return true;
        }

        /// <summary>Returns the existing edge with this identity, or adds a new one.</summary>
        public GraphEdge GetOrAddEdge(string fromId, string toId, EdgeKind kind, string label, bool approximate = false)
        {
            foreach (var e in Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind == kind &&
                    (e.Label ?? string.Empty) == (label ?? string.Empty))
                    return e;
            }
            var edge = new GraphEdge
            {
                FromId = fromId,
                ToId = toId,
                Kind = kind,
                Label = label,
                Approximate = approximate,
            };
            Edges.Add(edge);
            edgeKeys.Add(fromId + "|" + toId + "|" + kind + "|" + (label ?? string.Empty));
            if (kind != EdgeKind.TypeUsage)
                nonTypeUsagePairs.Add((fromId, toId));
            return edge;
        }

        /// <summary>True when any edge other than <paramref name="weakerThan"/> links the pair.</summary>
        public bool HasEdgeBetween(string fromId, string toId, EdgeKind weakerThan)
        {
            if (weakerThan == EdgeKind.TypeUsage)
                return nonTypeUsagePairs.Contains((fromId, toId));
            foreach (var e in Edges)
            {
                if (e.FromId == fromId && e.ToId == toId && e.Kind != weakerThan)
                    return true;
            }
            return false;
        }

        public static string FriendlyName(Type type)
        {
            if (type == null)
                return "?";
            if (!type.IsGenericType)
                return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);
            var args = type.GetGenericArguments();
            var parts = new string[args.Length];
            for (var i = 0; i < args.Length; i++)
                parts[i] = FriendlyName(args[i]);
            return name + "<" + string.Join(", ", parts) + ">";
        }
    }
}
