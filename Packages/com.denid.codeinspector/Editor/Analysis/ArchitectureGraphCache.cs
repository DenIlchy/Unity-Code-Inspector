using UnityEditor;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Lazily built, domain-reload-aware cache for the Architecture graph.
    /// </summary>
    public static class ArchitectureGraphCache
    {
        static CodeGraph graph;
        static ScanOptions builtWith;

        /// <summary>Options used for the next build. Set before accessing <see cref="Graph"/>.</summary>
        public static ScanOptions Options;

        static ArchitectureGraphCache()
        {
            AssemblyReloadEvents.afterAssemblyReload += Invalidate;
        }

        public static CodeGraph Graph
        {
            get
            {
                if (graph == null || !SameOptions(builtWith, Options))
                {
                    graph = TypeScanner.Build(Options);
                    builtWith = Options;
                }
                return graph;
            }
        }

        static bool SameOptions(ScanOptions a, ScanOptions b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            return a.IncludeUnityTypes == b.IncludeUnityTypes &&
                   a.IncludeEditorAssemblies == b.IncludeEditorAssemblies &&
                   a.IncludePackageAssemblies == b.IncludePackageAssemblies &&
                   ReferenceEquals(a.AssemblyFilter, b.AssemblyFilter);
        }

        public static void Invalidate()
        {
            graph = null;
            builtWith = null;
        }
    }
}
