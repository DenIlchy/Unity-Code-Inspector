using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

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

        /// <summary>
        /// Per-object signature check for property-change events: returns true
        /// only when serialized references/structure-relevant values changed
        /// since the last call (or were never seen).
        /// </summary>
        public sealed class PropertyChangeFilter
        {
            const int MaxProperties = 4000;

            readonly Dictionary<int, int> signatures = new Dictionary<int, int>();

            public void Clear() => signatures.Clear();

            public bool ShouldRefresh(Object o)
            {
                if (o == null)
                    return true;
                var sig = ReferenceSignature(o);
                if (sig == null)
                    return true;
                var id = o.GetInstanceID();
                if (signatures.TryGetValue(id, out var old) && old == sig.Value)
                    return false;
                signatures[id] = sig.Value;
                return true;
            }

            // Hash over object references, UnityEvent persistent calls, and the
            // few leaf values that change node rendering.
            static int? ReferenceSignature(Object o)
            {
                try
                {
                    var so = new SerializedObject(o);
                    var it = so.GetIterator();
                    var hash = 17;
                    var count = 0;
                    while (it.Next(true) && count++ < MaxProperties)
                    {
                        var path = it.propertyPath;
                        if (it.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            hash = hash * 31 + path.GetHashCode();
                            hash = hash * 31 + it.objectReferenceInstanceIDValue;
                        }
                        else if (path.IndexOf("m_PersistentCalls", StringComparison.Ordinal) >= 0 ||
                                 path == "m_Name" || path == "m_IsActive" || path == "m_Enabled")
                        {
                            var text = PropertyValueCodec.ReadLeaf(it);
                            if (text != null)
                            {
                                hash = hash * 31 + path.GetHashCode();
                                hash = hash * 31 + text.GetHashCode();
                            }
                        }
                    }
                    if (count > MaxProperties)
                        return null;
                    return hash;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
