using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Decodes method bodies as raw IL bytes (no external libraries) to find method
    /// calls, event subscriptions, GetComponent-style lookups, cross-type field
    /// access and string-based SendMessage calls between scanned types. Methods of
    /// compiler-generated nested types (coroutines, lambdas, closures, async) are
    /// attributed to the outermost user declaring type.
    /// </summary>
    internal static class ILCallScanner
    {
        static readonly BindingFlags Declared =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        static readonly BindingFlags DeclaredInstance =
            BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly HashSet<string> GetComponentMethodNames = new HashSet<string>
        {
            "GetComponent", "GetComponentInChildren", "GetComponentInParent",
            "GetComponents", "GetComponentsInChildren", "GetComponentsInParent",
            "TryGetComponent", "AddComponent",
            "FindObjectOfType", "FindFirstObjectByType", "FindAnyObjectByType",
            "FindObjectsOfType", "FindObjectsByType",
        };

        static readonly HashSet<string> SendMessageMethodNames = new HashSet<string>
        {
            "SendMessage", "BroadcastMessage", "SendMessageUpwards",
        };

        static readonly Dictionary<short, OpCode> opcodesByValue = BuildOpcodeTable();

        static Dictionary<short, OpCode> BuildOpcodeTable()
        {
            var table = new Dictionary<short, OpCode>();
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.GetValue(null) is OpCode op)
                    table[op.Value] = op;
            }
            return table;
        }

        internal static void Scan(IEnumerable<Type> types, CodeGraph graph)
        {
            var scanned = types as HashSet<Type> ?? new HashSet<Type>(types);

            var typesByMethodName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (var t in scanned)
            {
                foreach (var m in t.GetMethods(DeclaredInstance))
                {
                    if (!typesByMethodName.TryGetValue(m.Name, out var list))
                        typesByMethodName[m.Name] = list = new List<Type>();
                    list.Add(t);
                }
            }

            var ctx = new ScanContext
            {
                Scanned = scanned,
                TypesByMethodName = typesByMethodName,
                Graph = graph,
            };

            foreach (var type in scanned)
            {
                var failed = false;
                foreach (var declaring in EnumerateScannableDeclaringTypes(type))
                {
                    foreach (var method in EnumerateMethods(declaring))
                    {
                        try
                        {
                            ScanMethod(type, method, ctx);
                        }
                        catch (Exception e)
                        {
                            if (!failed)
                            {
                                failed = true;
                                graph.Diagnostics.Add(
                                    "IL decode failed for " + type.FullName + ": " + e.Message);
                            }
                        }
                    }
                }
            }
        }

        sealed class ScanContext
        {
            public HashSet<Type> Scanned;
            public Dictionary<string, List<Type>> TypesByMethodName;
            public CodeGraph Graph;
        }

        /// <summary>The type itself plus all recursively nested compiler-generated types.</summary>
        static IEnumerable<Type> EnumerateScannableDeclaringTypes(Type type)
        {
            yield return type;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var t in GeneratedNested(nested))
                    yield return t;
            }
        }

        static IEnumerable<Type> GeneratedNested(Type t)
        {
            if (!TypeScanner.IsCompilerGeneratedType(t))
                yield break;
            yield return t;
            foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var nn in GeneratedNested(nested))
                    yield return nn;
            }
        }

        static IEnumerable<MethodBase> EnumerateMethods(Type type)
        {
            foreach (var m in type.GetMethods(Declared))
                yield return m;
            foreach (var c in type.GetConstructors(Declared))
                yield return c;
        }

        /// <summary>
        /// Maps a compiler-generated nested type to its outermost user declaring type.
        /// </summary>
        static Type OutermostUserType(Type t)
        {
            while (t != null && t.DeclaringType != null && TypeScanner.IsCompilerGeneratedType(t))
                t = t.DeclaringType;
            return t;
        }

        /// <summary>Recovers the original method name from generated type names like "&lt;Start&gt;d__1".</summary>
        static string CallerMethodName(MethodBase method, Type rootType)
        {
            var dt = method.DeclaringType;
            if (dt == rootType || dt == null)
                return method.Name;
            var n = dt.Name;
            if (n.StartsWith("<", StringComparison.Ordinal))
            {
                var gt = n.IndexOf('>');
                if (gt > 1)
                    return n.Substring(1, gt - 1);
            }
            return method.Name;
        }

        static void ScanMethod(Type callerType, MethodBase method, ScanContext ctx)
        {
            var body = method.GetMethodBody();
            if (body == null)
                return;
            var il = body.GetILAsByteArray();
            if (il == null || il.Length == 0)
                return;

            var dt = method.DeclaringType;
            var typeArgs = dt != null && dt.IsGenericType ? dt.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var module = method.Module;

            string lastString = null;
            Type lastType = null;
            var callerId = CodeGraph.NodeIdFor(callerType);
            var callerName = CallerMethodName(method, callerType);

            var pos = 0;
            while (pos < il.Length)
            {
                OpCode op;
                var b = il[pos++];
                if (b == 0xFE && pos < il.Length)
                {
                    opcodesByValue.TryGetValue((short)(0xFE00 | il[pos]), out op);
                    pos++;
                }
                else
                {
                    opcodesByValue.TryGetValue(b, out op);
                }

                var operandPos = pos;
                var operandSize = OperandSize(op, il, pos);
                pos += operandSize;
                if (pos > il.Length)
                    break;

                var flow = op.FlowControl;
                if (flow == FlowControl.Call &&
                    (op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineTok))
                {
                    var token = BitConverter.ToInt32(il, operandPos);
                    MethodBase callee = null;
                    try
                    {
                        callee = module.ResolveMethod(token, typeArgs, methodArgs);
                    }
                    catch (Exception)
                    {
                        // Unresolvable token (e.g. vararg signature) - skip.
                    }
                    if (callee != null)
                    {
                        if (HandleCall(callerType, callerName, callee, lastString, lastType,
                                callerId, ctx))
                            lastType = null;
                        if (SendMessageMethodNames.Contains(callee.Name))
                            lastString = null;
                        else
                        {
                            foreach (var p in callee.GetParameters())
                            {
                                if (p.ParameterType.IsAssignableFrom(typeof(string)))
                                {
                                    lastString = null;
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (op.OperandType == OperandType.InlineField)
                {
                    var token = BitConverter.ToInt32(il, operandPos);
                    FieldInfo field = null;
                    try
                    {
                        field = module.ResolveField(token, typeArgs, methodArgs);
                    }
                    catch (Exception)
                    {
                        // Unresolvable token - skip.
                    }
                    if (field != null)
                        HandleFieldAccess(callerType, callerName, field, op, callerId, ctx);
                }
                else if (op.OperandType == OperandType.InlineString)
                {
                    var token = BitConverter.ToInt32(il, operandPos);
                    try
                    {
                        lastString = module.ResolveString(token);
                    }
                    catch (Exception)
                    {
                        lastString = null;
                    }
                }
                else if (op == OpCodes.Ldtoken && op.OperandType == OperandType.InlineTok)
                {
                    var token = BitConverter.ToInt32(il, operandPos);
                    try
                    {
                        lastType = module.ResolveMember(token, typeArgs, methodArgs) as Type;
                    }
                    catch (Exception)
                    {
                        lastType = null;
                    }
                }
                else if (flow == FlowControl.Branch || flow == FlowControl.Cond_Branch)
                {
                    lastString = null;
                    lastType = null;
                }
                if (IsStoreOrPop(op))
                {
                    lastString = null;
                    lastType = null;
                }
            }
        }

        static bool IsStoreOrPop(OpCode op)
        {
            if (op == OpCodes.Pop)
                return true;
            var n = op.Name;
            return n != null &&
                   (n.StartsWith("stloc", StringComparison.Ordinal) ||
                    n == "stfld" || n == "stsfld" ||
                    n.StartsWith("starg", StringComparison.Ordinal) ||
                    n.StartsWith("stelem", StringComparison.Ordinal));
        }

        static bool HandleCall(Type callerType, string callerName, MethodBase callee,
            string lastString, Type lastType, string callerId, ScanContext ctx)
        {
            var declType = OutermostUserType(callee.DeclaringType);

            // Generic GetComponent<T> / FindObjectOfType<T> family.
            if (callee.IsGenericMethod && GetComponentMethodNames.Contains(callee.Name))
            {
                foreach (var rawArg in callee.GetGenericArguments())
                {
                    var arg = OutermostUserType(rawArg);
                    if (arg != callerType && ctx.Scanned.Contains(arg))
                    {
                        ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(arg),
                            EdgeKind.GetComponent, callee.Name + "<" + arg.Name + ">");
                    }
                }
                return false;
            }

            // Non-generic GetComponent(typeof(T)) family: use the last ldtoken type.
            if (!callee.IsGenericMethod && GetComponentMethodNames.Contains(callee.Name))
            {
                var hasTypeParam = false;
                foreach (var p in callee.GetParameters())
                {
                    if (p.ParameterType == typeof(Type))
                    {
                        hasTypeParam = true;
                        break;
                    }
                }
                if (hasTypeParam && lastType != null)
                {
                    var arg = OutermostUserType(lastType);
                    if (arg != callerType && ctx.Scanned.Contains(arg))
                    {
                        ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(arg),
                            EdgeKind.GetComponent,
                            callee.Name + "(typeof(" + arg.Name + "))");
                    }
                    return true;
                }
            }

            // Event subscription: call to special-name add_X on another scanned type.
            if (callee.IsSpecialName && callee.Name.StartsWith("add_", StringComparison.Ordinal) &&
                declType != null && declType != callerType && ctx.Scanned.Contains(declType))
            {
                ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(declType),
                    EdgeKind.CSharpEventSubscription, callee.Name.Substring(4));
                return false;
            }

            // String-based SendMessage family: use the most recent ldstr.
            if (SendMessageMethodNames.Contains(callee.Name) && lastString != null)
            {
                if (ctx.TypesByMethodName.TryGetValue(lastString, out var targets))
                {
                    foreach (var t in targets)
                    {
                        if (t == callerType)
                            continue;
                        ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(t),
                            EdgeKind.SendMessage, lastString, approximate: true);
                    }
                }
                return false;
            }

            // Plain method call to another scanned type.
            if (declType != null && declType != callerType && ctx.Scanned.Contains(declType))
            {
                ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(declType),
                    EdgeKind.MethodCall,
                    callerType.Name + "." + callerName + " -> " + declType.Name + "." + callee.Name);
            }
            return false;
        }

        static void HandleFieldAccess(Type callerType, string callerName, FieldInfo field,
            OpCode op, string callerId, ScanContext ctx)
        {
            var owner = OutermostUserType(field.DeclaringType);
            if (owner == null || owner == callerType || !ctx.Scanned.Contains(owner))
                return;
            var isStore = op == OpCodes.Stfld || op == OpCodes.Stsfld;
            ctx.Graph.AddEdge(callerId, CodeGraph.NodeIdFor(owner),
                EdgeKind.FieldAccess,
                (isStore ? "set " : "") + callerType.Name + "." + callerName +
                " -> " + owner.Name + "." + field.Name);
        }

        static int OperandSize(OpCode op, byte[] il, int operandPos)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    if (operandPos + 4 > il.Length)
                        return il.Length - operandPos;
                    var count = BitConverter.ToInt32(il, operandPos);
                    return 4 + count * 4;
                default:
                    return 0;
            }
        }
    }
}
