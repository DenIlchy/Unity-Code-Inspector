using System.Collections.Generic;
using UnityEditor;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Decides whether a batch of ObjectChangeEvents can affect the scene graph
    /// (structure, references, or UnityEvent bindings).
    /// </summary>
    public static class SceneRefreshPolicy
    {
        static readonly HashSet<ObjectChangeKind> RefreshKinds = new HashSet<ObjectChangeKind>
        {
            ObjectChangeKind.CreateGameObjectHierarchy,
            ObjectChangeKind.DestroyGameObjectHierarchy,
            ObjectChangeKind.ChangeGameObjectStructure,
            ObjectChangeKind.ChangeGameObjectStructureHierarchy,
            ObjectChangeKind.ChangeGameObjectParent,
            ObjectChangeKind.ChangeScene,
            ObjectChangeKind.CreateAssetObject,
            ObjectChangeKind.DestroyAssetObject,
            ObjectChangeKind.UpdatePrefabInstances,
            ObjectChangeKind.ChangeChildrenOrder,
            // Property changes can modify serialized references / UnityEvents.
            ObjectChangeKind.ChangeGameObjectOrComponentProperties,
            ObjectChangeKind.ChangeAssetObjectProperties,
        };

        public static bool ShouldRefresh(IEnumerable<ObjectChangeKind> kinds)
        {
            if (kinds == null)
                return false;
            foreach (var k in kinds)
            {
                if (RefreshKinds.Contains(k))
                    return true;
            }
            return false;
        }
    }
}
