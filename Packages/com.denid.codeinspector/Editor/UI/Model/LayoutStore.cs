using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
}
