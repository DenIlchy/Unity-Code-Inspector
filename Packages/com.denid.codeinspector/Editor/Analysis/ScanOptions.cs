using System;
using System.Reflection;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Options controlling which assemblies count as "user code" during a scan.
    /// </summary>
    public sealed class ScanOptions
    {
        /// <summary>
        /// When true, edges to engine base types (e.g. MonoBehaviour) are emitted
        /// and engine types may become nodes. Does not widen the scanned set.
        /// </summary>
        public bool IncludeUnityTypes;

        /// <summary>Also treat Editor-only compiled assemblies as user code.</summary>
        public bool IncludeEditorAssemblies;

        /// <summary>Also treat assemblies whose sources live under Packages/ as user code.</summary>
        public bool IncludePackageAssemblies;

        /// <summary>
        /// Optional predicate deciding whether an assembly is user code. When set
        /// (non-null) it wins over the CompilationPipeline source-file heuristic.
        /// </summary>
        public Func<Assembly, bool> AssemblyFilter;

        internal const string OwnAssemblyPrefix = "Denid.CodeInspector";

        internal static bool IsOwnAssembly(Assembly assembly)
        {
            if (assembly == null)
                return false;
            var name = assembly.GetName().Name;
            return name.StartsWith(OwnAssemblyPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a type outside the scanned set should still get a node/edge
        /// (e.g. a base interface). Only meaningful when <see cref="AssemblyFilter"/>
        /// narrows the user set; returns false when it is null because scanned-set
        /// membership is already handled by the caller.
        /// </summary>
        public bool IsUserType(Type type)
        {
            if (type == null || IsOwnAssembly(type.Assembly))
                return false;
            if (AssemblyFilter != null)
                return AssemblyFilter(type.Assembly);
            return false;
        }
    }
}
