using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Persists user-dragged node positions per view as JSON. Only nodes the user
    /// moved are stored; entries merge over the automatic layout.
    /// </summary>
    public sealed class LayoutStore
    {
        public const string DefaultRelativePath = "UserSettings/CodeInspector/Layout.json";

        [Serializable]
        class Entry
        {
            public string id;
            public float x;
            public float y;
        }

        [Serializable]
        class ViewData
        {
            public string view;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        class Root
        {
            public List<ViewData> views = new List<ViewData>();
        }

        readonly string path;
        readonly Dictionary<string, Dictionary<string, Vector2>> views =
            new Dictionary<string, Dictionary<string, Vector2>>();
        bool loaded;
        bool dirty;

        public LayoutStore(string path = null)
        {
            this.path = string.IsNullOrEmpty(path) ? DefaultRelativePath : path;
        }

        void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            try
            {
                if (!File.Exists(path))
                    return;
                var root = JsonUtility.FromJson<Root>(File.ReadAllText(path));
                if (root?.views == null)
                    return;
                foreach (var v in root.views)
                {
                    if (v == null || string.IsNullOrEmpty(v.view))
                        continue;
                    var map = new Dictionary<string, Vector2>();
                    foreach (var e in v.entries)
                    {
                        if (e != null && e.id != null)
                            map[e.id] = new Vector2(e.x, e.y);
                    }
                    views[v.view] = map;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("CodeInspector: failed to load layout: " + e.Message);
            }
        }

        public bool TryGet(string view, string nodeId, out Vector2 position)
        {
            EnsureLoaded();
            position = default;
            return views.TryGetValue(view, out var map) && map.TryGetValue(nodeId, out position);
        }

        public void Set(string view, string nodeId, Vector2 position)
        {
            EnsureLoaded();
            if (!views.TryGetValue(view, out var map))
                views[view] = map = new Dictionary<string, Vector2>();
            map[nodeId] = position;
            dirty = true;
            Save();
        }

        /// <summary>Removes entries the predicate marks stale; saves on change.</summary>
        public int Prune(Func<string, string, bool> isStale)
        {
            EnsureLoaded();
            var removed = 0;
            var emptyViews = new List<string>();
            foreach (var view in views)
            {
                var staleKeys = new List<string>();
                foreach (var key in view.Value.Keys)
                {
                    if (isStale(view.Key, key))
                        staleKeys.Add(key);
                }
                foreach (var k in staleKeys)
                {
                    view.Value.Remove(k);
                    removed++;
                }
                if (view.Value.Count == 0)
                    emptyViews.Add(view.Key);
            }
            foreach (var v in emptyViews)
                views.Remove(v);
            if (removed > 0)
            {
                dirty = true;
                Save();
            }
            return removed;
        }

        public void ClearView(string view)
        {
            EnsureLoaded();
            if (views.Remove(view))
            {
                dirty = true;
                Save();
            }
        }

        public void Save()
        {
            if (!dirty)
                return;
            dirty = false;
            EnsureLoaded();
            try
            {
                var root = new Root();
                foreach (var kv in views)
                {
                    var vd = new ViewData { view = kv.Key };
                    foreach (var e in kv.Value)
                        vd.entries.Add(new Entry { id = e.Key, x = e.Value.x, y = e.Value.y });
                    root.views.Add(vd);
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtility.ToJson(root, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("CodeInspector: failed to save layout: " + e.Message);
            }
        }
    }

    /// <summary>Decides whether a persisted layout entry can never resolve again.</summary>
    public static class LayoutPruning
    {
        public static bool IsStale(string view, string key)
        {
            if (view != null && view.StartsWith("Scene:", StringComparison.Ordinal))
            {
                var p = view.Substring(6);
                if ((p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                     p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) &&
                    string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                    return true;
            }
            if (key.StartsWith("go:", StringComparison.Ordinal) ||
                key.StartsWith("asset:", StringComparison.Ordinal))
                return true;
            if (key.StartsWith("type:", StringComparison.Ordinal))
                return Type.GetType(key.Substring(5), false) == null;
            if (GlobalObjectId.TryParse(key, out var id))
            {
                var path = AssetDatabase.GUIDToAssetPath(id.assetGUID);
                if (string.IsNullOrEmpty(path))
                    return true;
                if (id.identifierType == 2 && SceneManager.GetSceneByPath(path).isLoaded)
                {
                    try
                    {
                        if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) == null)
                            return true;
                    }
                    catch (Exception)
                    {
                        return true;
                    }
                }
                return false;
            }
            var colon = key.IndexOf(':');
            if (colon > 0 && LooksLikeGuid(key.Substring(0, colon)))
                return string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(key.Substring(0, colon)));
            return false;
        }

        static bool LooksLikeGuid(string s)
        {
            if (s.Length != 32)
                return false;
            foreach (var c in s)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }
            return true;
        }
    }
}
