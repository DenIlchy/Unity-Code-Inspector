using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>Maps recorded Play Mode tweak targets back to edit-mode objects.</summary>
    public static class TweakResolver
    {
        /// <summary>GlobalObjectId → object (works for scene objects and assets).</summary>
        public static Object ResolveByGlobalId(string globalObjectId)
        {
            if (string.IsNullOrEmpty(globalObjectId))
                return null;
            try
            {
                if (GlobalObjectId.TryParse(globalObjectId, out var id))
                    return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            }
            catch (Exception)
            {
                // Fall through.
            }
            return null;
        }

        /// <summary>Full resolution: GlobalObjectId first, then the hierarchy locator.</summary>
        public static Object ResolveTarget(TweakRecord r)
        {
            var viaId = ResolveByGlobalId(r.GlobalObjectId);
            if (viaId != null)
                return viaId;
            return ResolveByLocator(r.ScenePath, r.HierarchyPath, r.TargetTypeName,
                r.ComponentIndex, r.SiblingPath);
        }

        /// <summary>Scene path + hierarchy path + component type + index → object.</summary>
        public static Object ResolveByLocator(string scenePath, string hierarchyPath,
            string targetTypeName, int componentIndex, string siblingPath = null)
        {
            if (string.IsNullOrEmpty(hierarchyPath) || string.IsNullOrEmpty(targetTypeName))
                return null;
            var type = Type.GetType(targetTypeName);
            if (type == null)
                return null;
            foreach (var go in FindGameObjects(scenePath, hierarchyPath, siblingPath))
            {
                if (type == typeof(GameObject))
                    return go;
                var index = 0;
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null || c.GetType() != type)
                        continue;
                    if (index++ == componentIndex)
                        return c;
                }
            }
            return null;
        }

        static List<GameObject> FindGameObjects(string scenePath, string hierarchyPath,
            string siblingPath)
        {
            var result = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (!string.IsNullOrEmpty(scenePath) && scene.path != scenePath)
                    continue;
                FindInScene(scene, hierarchyPath, siblingPath, result);
                if (result.Count > 0)
                    break;
            }
            return result;
        }

        static void FindInScene(Scene scene, string hierarchyPath, string siblingPath,
            List<GameObject> result)
        {
            var indices = ParseSiblingPath(siblingPath);
            if (indices != null)
            {
                var found = WalkByIndices(scene, indices);
                if (found != null && TweakLog.HierarchyPath(found) == hierarchyPath)
                {
                    result.Add(found.gameObject);
                    return;
                }
            }
            foreach (var root in scene.GetRootGameObjects())
                CollectMatches(root.transform, root.name, hierarchyPath, result);
        }

        static int[] ParseSiblingPath(string siblingPath)
        {
            if (string.IsNullOrEmpty(siblingPath))
                return null;
            var parts = siblingPath.Split('/');
            var indices = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out indices[i]))
                    return null;
            }
            return indices;
        }

        static Transform WalkByIndices(Scene scene, int[] indices)
        {
            if (indices.Length == 0)
                return null;
            var roots = scene.GetRootGameObjects();
            if (indices[0] < 0 || indices[0] >= roots.Length)
                return null;
            var t = roots[indices[0]].transform;
            for (var d = 1; d < indices.Length; d++)
            {
                if (indices[d] < 0 || indices[d] >= t.childCount)
                    return null;
                t = t.GetChild(indices[d]);
            }
            return t;
        }

        // Name-joining prefix search: names may themselves contain '/'.
        static void CollectMatches(Transform t, string pathSoFar, string hierarchyPath,
            List<GameObject> result)
        {
            if (pathSoFar == hierarchyPath)
                result.Add(t.gameObject);
            var prefix = pathSoFar + "/";
            if (!hierarchyPath.StartsWith(prefix, StringComparison.Ordinal))
                return;
            foreach (Transform child in t)
                CollectMatches(child, prefix + child.name, hierarchyPath, result);
        }
    }
}
