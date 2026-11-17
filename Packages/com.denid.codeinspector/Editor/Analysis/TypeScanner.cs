using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;
using Assembly = System.Reflection.Assembly;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Scans user code (all classes, interfaces and structs in user assemblies)
    /// and builds a <see cref="CodeGraph"/> describing their relationships.
    /// </summary>
    public static class TypeScanner
    {
        static readonly BindingFlags Declared =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        static readonly HashSet<string> UpdateMessages = new HashSet<string>
        {
            "Update", "FixedUpdate", "LateUpdate",
        };

        static readonly HashSet<string> TriggerMessages = new HashSet<string>
        {
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit",
            "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
        };

        static readonly HashSet<string> CollisionMessages = new HashSet<string>
        {
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
            "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",
        };

        static readonly HashSet<string> MouseMessages = new HashSet<string>
        {
            "OnMouseDown", "OnMouseUp", "OnMouseEnter", "OnMouseExit",
            "OnMouseOver", "OnMouseDrag", "OnMouseUpAsButton",
        };

        static Dictionary<Type, MonoScript> monoScriptByType;

        /// <summary>Scans all user types in the project.</summary>
        public static CodeGraph Build(ScanOptions options = null)
        {
            options ??= new ScanOptions();
            var graph = new CodeGraph();
            var types = new List<Type>();
            foreach (var asm in CollectUserAssemblies(options))
            {
                Type[] asmTypes;
                try
                {
                    asmTypes = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    asmTypes = Array.FindAll(e.Types, t => t != null);
                    graph.Diagnostics.Add("Partial type load for " + asm.GetName().Name + ": " + e.Message);
                }
                catch (Exception e)
                {
                    graph.Diagnostics.Add("Type load failed for " + asm.GetName().Name + ": " + e.Message);
                    continue;
                }
                foreach (var t in asmTypes)
                {
                    if (IsScannableType(t))
                        types.Add(t);
                }
            }
            return Build(types, options, graph);
        }

        static List<Assembly> CollectUserAssemblies(ScanOptions options)
        {
            var loaded = new Dictionary<string, Assembly>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                loaded[a.GetName().Name] = a;

            var result = new List<Assembly>();
            var groups = new List<AssembliesType> { AssembliesType.PlayerWithoutTestAssemblies };
            if (options.IncludeEditorAssemblies)
                groups.Add(AssembliesType.Editor);

            foreach (var group in groups)
            {
                UnityEditor.Compilation.Assembly[] compiled;
                try
                {
                    compiled = CompilationPipeline.GetAssemblies(group);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var ca in compiled)
                {
                    if (!loaded.TryGetValue(ca.name, out var asm))
                        continue;
                    if (ScanOptions.IsOwnAssembly(asm))
                        continue;
                    if (options.AssemblyFilter != null)
                    {
                        if (options.AssemblyFilter(asm))
                            result.Add(asm);
                        continue;
                    }
                    if (HasUserSourceFile(ca, options))
                        result.Add(asm);
                }
            }
            return result;
        }

        static bool HasUserSourceFile(UnityEditor.Compilation.Assembly ca, ScanOptions options)
        {
            var files = ca.sourceFiles;
            if (files == null)
                return false;
            foreach (var f in files)
            {
                var n = f.Replace('\\', '/');
                if (n.StartsWith("Assets/", StringComparison.Ordinal) ||
                    n.Contains("/Assets/"))
                    return true;
                if (options.IncludePackageAssemblies &&
                    (n.StartsWith("Packages/", StringComparison.Ordinal) ||
                     n.Contains("/Packages/")))
                    return true;
            }
            return false;
        }

        internal static bool IsCompilerGeneratedType(Type t)
        {
            for (var x = t; x != null; x = x.DeclaringType)
            {
                if (x.Name.StartsWith("<", StringComparison.Ordinal) ||
                    x.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    return true;
            }
            return false;
        }

        internal static bool IsScannableType(Type t)
        {
            if (t == null || (!t.IsClass && !t.IsInterface && !t.IsValueType))
                return false;
            if (t.IsEnum || typeof(Delegate).IsAssignableFrom(t))
                return false;
            if (t.IsGenericTypeDefinition || t.ContainsGenericParameters)
                return false;
            if (IsCompilerGeneratedType(t))
                return false;
            return true;
        }

        /// <summary>Builds a graph over an explicit set of types (used by tests).</summary>
        public static CodeGraph Build(IEnumerable<Type> types, ScanOptions options = null)
        {
            return Build(types, options, new CodeGraph());
        }

        static CodeGraph Build(IEnumerable<Type> types, ScanOptions options, CodeGraph graph)
        {
            options ??= new ScanOptions();
            var set = new HashSet<Type>();
            foreach (var t in types)
            {
                if (t == null || t.IsGenericTypeDefinition || IsCompilerGeneratedType(t))
                    continue;
                if (!set.Add(t))
                    continue;
                try
                {
                    var node = graph.GetOrAddNode(t);
                    FillMembers(t, node);
                    FillBadges(t, node);
                }
                catch (Exception e)
                {
                    graph.Diagnostics.Add("Member scan failed for " + t.FullName + ": " + e.Message);
                }
            }

            foreach (var t in set)
            {
                try
                {
                    AddInheritanceEdges(t, set, options, graph);
                    AddFieldReferenceEdges(t, set, graph);
                }
                catch (Exception e)
                {
                    graph.Diagnostics.Add("Type scan failed for " + t.FullName + ": " + e.Message);
                }
            }

            ILCallScanner.Scan(set, graph);

            // TypeUsage is emitted last so it can defer to stronger edges.
            foreach (var t in set)
            {
                try
                {
                    AddTypeUsageEdges(t, set, graph);
                }
                catch (Exception e)
                {
                    graph.Diagnostics.Add("Type-usage scan failed for " + t.FullName + ": " + e.Message);
                }
            }

            var scripts = GetMonoScriptMap();
            foreach (var t in set)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(t))
                    continue;
                if (scripts.TryGetValue(t, out var script) && script != null)
                {
                    try
                    {
                        graph.Nodes[CodeGraph.NodeIdFor(t)].ExecutionOrder =
                            MonoImporter.GetExecutionOrder(script);
                    }
                    catch (Exception)
                    {
                        // Missing meta file or invalid script - leave at default 0.
                    }
                }
            }

            return graph;
        }

        internal static Dictionary<Type, MonoScript> GetMonoScriptMap()
        {
            if (monoScriptByType != null)
                return monoScriptByType;
            monoScriptByType = new Dictionary<Type, MonoScript>();
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                var cls = script.GetClass();
                if (cls != null && !monoScriptByType.ContainsKey(cls))
                    monoScriptByType.Add(cls, script);
            }
            return monoScriptByType;
        }

        internal static void ClearMonoScriptCache()
        {
            monoScriptByType = null;
        }

        static void FillMembers(Type type, GraphNode node)
        {
            foreach (var f in type.GetFields(Declared))
            {
                node.Members.Add(new GraphMember
                {
                    Name = f.Name,
                    Kind = MemberKind.Field,
                    TypeName = CodeGraph.FriendlyName(f.FieldType),
                });
            }
            foreach (var p in type.GetProperties(Declared))
            {
                node.Members.Add(new GraphMember
                {
                    Name = p.Name,
                    Kind = MemberKind.Property,
                    TypeName = CodeGraph.FriendlyName(p.PropertyType),
                });
            }
            foreach (var e in type.GetEvents(Declared))
            {
                node.Members.Add(new GraphMember
                {
                    Name = e.Name,
                    Kind = MemberKind.Event,
                    TypeName = CodeGraph.FriendlyName(e.EventHandlerType),
                });
            }
            foreach (var m in type.GetMethods(Declared))
            {
                if (m.IsSpecialName)
                    continue;
                node.Members.Add(new GraphMember
                {
                    Name = m.Name,
                    Kind = MemberKind.Method,
                    TypeName = CodeGraph.FriendlyName(m.ReturnType),
                });
            }
        }

        static void FillBadges(Type type, GraphNode node)
        {
            foreach (var m in type.GetMethods(Declared))
            {
                if (UpdateMessages.Contains(m.Name))
                {
                    if (m.Name == "Update") node.Badges |= NodeBadges.Update;
                    else if (m.Name == "FixedUpdate") node.Badges |= NodeBadges.FixedUpdate;
                    else node.Badges |= NodeBadges.LateUpdate;
                }
                else if (TriggerMessages.Contains(m.Name))
                {
                    node.Badges |= NodeBadges.Trigger;
                }
                else if (CollisionMessages.Contains(m.Name))
                {
                    node.Badges |= NodeBadges.Collision;
                }
                else if (MouseMessages.Contains(m.Name))
                {
                    node.Badges |= NodeBadges.Mouse;
                }
            }

            if (type.GetEvents(Declared).Length > 0)
                node.Badges |= NodeBadges.HasEvents;
            else
            {
                foreach (var f in type.GetFields(Declared))
                {
                    if (typeof(UnityEventBase).IsAssignableFrom(f.FieldType))
                    {
                        node.Badges |= NodeBadges.HasEvents;
                        break;
                    }
                }
            }
        }

        static void AddInheritanceEdges(Type type, HashSet<Type> set, ScanOptions options, CodeGraph graph)
        {
            var fromId = CodeGraph.NodeIdFor(type);
            var baseType = type.BaseType;
            if (baseType != null && baseType != typeof(object) &&
                (set.Contains(baseType) || options.IncludeUnityTypes))
            {
                var node = graph.GetOrAddNode(baseType);
                graph.AddEdge(fromId, node.Id, EdgeKind.Inheritance, baseType.Name);
            }

            foreach (var iface in type.GetInterfaces())
            {
                if (set.Contains(iface) || options.IsUserType(iface))
                {
                    var node = graph.GetOrAddNode(iface);
                    graph.AddEdge(fromId, node.Id, EdgeKind.Inheritance, iface.Name);
                }
            }
        }

        static bool IsSerializedField(FieldInfo f)
        {
            if (f.IsLiteral || f.IsStatic || f.IsInitOnly)
                return false;
            if (f.IsDefined(typeof(NonSerializedAttribute), false))
                return false;
            return f.IsPublic || f.IsDefined(typeof(SerializeField), false);
        }

        /// <summary>Unwraps arrays and generic containers down to candidate element types.</summary>
        static void UnwrapType(Type t, HashSet<Type> into)
        {
            if (t == null)
                return;
            if (t.IsByRef || t.IsPointer)
            {
                if (t.HasElementType)
                    UnwrapType(t.GetElementType(), into);
                return;
            }
            if (t.IsArray)
            {
                UnwrapType(t.GetElementType(), into);
                return;
            }
            into.Add(t);
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    UnwrapType(arg, into);
            }
        }

        static void AddFieldReferenceEdges(Type type, HashSet<Type> set, CodeGraph graph)
        {
            var fromId = CodeGraph.NodeIdFor(type);
            var candidates = new HashSet<Type>();
            foreach (var f in type.GetFields(Declared))
            {
                if (!IsSerializedField(f))
                    continue;
                candidates.Clear();
                UnwrapType(f.FieldType, candidates);
                foreach (var c in candidates)
                {
                    if (c != type && set.Contains(c))
                        graph.AddEdge(fromId, CodeGraph.NodeIdFor(c), EdgeKind.FieldReference, f.Name);
                }
            }
        }

        static void AddTypeUsageEdges(Type type, HashSet<Type> set, CodeGraph graph)
        {
            var fromId = CodeGraph.NodeIdFor(type);
            var candidates = new HashSet<Type>();

            void Consider(Type used, string label)
            {
                candidates.Clear();
                UnwrapType(used, candidates);
                foreach (var c in candidates)
                {
                    if (c == type || !set.Contains(c))
                        continue;
                    var toId = CodeGraph.NodeIdFor(c);
                    if (graph.HasEdgeBetween(fromId, toId, EdgeKind.TypeUsage))
                        continue;
                    graph.AddEdge(fromId, toId, EdgeKind.TypeUsage, label);
                }
            }

            foreach (var f in type.GetFields(Declared))
            {
                if (!IsSerializedField(f) && !f.IsLiteral)
                    Consider(f.FieldType, f.Name);
            }
            foreach (var m in type.GetMethods(Declared))
            {
                if (m.IsSpecialName)
                    continue;
                foreach (var p in m.GetParameters())
                    Consider(p.ParameterType, m.Name + "(" + p.Name + ")");
                Consider(m.ReturnType, m.Name);
            }
        }
    }
}
