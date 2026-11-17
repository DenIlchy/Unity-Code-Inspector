using System;
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
            return ResolveByLocator(r.ScenePath, r.HierarchyPath, r.TargetTypeName, r.ComponentIndex);
        }

        /// <summary>Scene path + hierarchy path + component type + index → object.</summary>
        public static Object ResolveByLocator(string scenePath, string hierarchyPath,
            string targetTypeName, int componentIndex)
        {
            if (string.IsNullOrEmpty(hierarchyPath) || string.IsNullOrEmpty(targetTypeName))
                return null;
            var go = FindGameObject(scenePath, hierarchyPath);
            if (go == null)
                return null;
            var type = Type.GetType(targetTypeName);
            if (type == null || type == typeof(GameObject))
                return type == typeof(GameObject) ? go : null;
            var index = 0;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c.GetType() != type)
                    continue;
                if (index++ == componentIndex)
                    return c;
            }
            return null;
        }

        static GameObject FindGameObject(string scenePath, string hierarchyPath)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (!string.IsNullOrEmpty(scenePath) && scene.path != scenePath)
                    continue;
                var found = FindInScene(scene, hierarchyPath);
                if (found != null)
                    return found;
            }
            return null;
        }

        static GameObject FindInScene(Scene scene, string hierarchyPath)
        {
            var parts = hierarchyPath.Split('/');
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = Walk(root.transform, parts, 0);
                if (found != null)
                    return found;
            }
            return null;
        }

        static GameObject Walk(Transform t, string[] parts, int depth)
        {
            if (t.name != parts[depth])
                return null;
            if (depth == parts.Length - 1)
                return t.gameObject;
            foreach (Transform child in t)
            {
                var found = Walk(child, parts, depth + 1);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
