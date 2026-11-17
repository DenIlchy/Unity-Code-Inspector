using UnityEditor;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Lazily built, domain-reload-aware cache for the Architecture graph.
    /// </summary>
    public static class ArchitectureGraphCache
    {
        static CodeGraph graph;

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
                graph ??= TypeScanner.Build(Options);
                return graph;
            }
        }

        public static void Invalidate()
        {
            graph = null;
        }
    }
}
