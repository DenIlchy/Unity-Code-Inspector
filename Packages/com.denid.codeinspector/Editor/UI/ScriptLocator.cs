using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;

namespace Denid.CodeInspector.Editor
{
    /// <summary>Resolves a scanned <see cref="Type"/> back to its MonoScript asset.</summary>
    internal static class ScriptLocator
    {
        static readonly Dictionary<Type, MonoScript> cache = new Dictionary<Type, MonoScript>();
        static readonly Dictionary<Type, int> declarationLines = new Dictionary<Type, int>();

        public static MonoScript Find(Type type)
        {
            if (type == null)
                return null;
            if (cache.TryGetValue(type, out var cached))
                return cached;

            MonoScript script = null;
            var map = TypeScanner.GetMonoScriptMap();
            if (map != null)
                map.TryGetValue(type, out script);

            if (script == null)
            {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                var kind = type.IsInterface ? "interface" : (type.IsValueType ? "struct" : "class");
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (candidate == null)
                        continue;
                    string text;
                    try
                    {
                        text = File.ReadAllText(path);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    var line = FindDeclarationLine(text, kind, name);
                    if (line > 0)
                    {
                        script = candidate;
                        declarationLines[type] = line;
                        break;
                    }
                }
            }

            cache[type] = script;
            return script;
        }

        static int FindDeclarationLine(string text, string kind, string name)
        {
            var needle = kind + " " + name;
            var idx = 0;
            var line = 1;
            while (idx < text.Length)
            {
                var found = text.IndexOf(needle, idx, StringComparison.Ordinal);
                if (found < 0)
                    return -1;
                var nl = text.IndexOf('\n', found);
                line += CountNewlines(text, idx, found);
                idx = found + needle.Length;
                var next = idx < text.Length ? text[idx] : ' ';
                if (char.IsLetterOrDigit(next) || next == '_')
                    continue;
                return line;
            }
            return -1;
        }

        static int CountNewlines(string text, int from, int to)
        {
            var count = 0;
            for (var i = from; i < to; i++)
                if (text[i] == '\n')
                    count++;
            return count;
        }

        public static int DeclarationLine(Type type)
        {
            Find(type);
            return declarationLines.TryGetValue(type, out var line) ? line : 1;
        }

        public static void Ping(Type type)
        {
            var script = Find(type);
            if (script != null)
                EditorGUIUtility.PingObject(script);
        }

        public static void Open(Type type)
        {
            var script = Find(type);
            if (script != null)
                AssetDatabase.OpenAsset(script, DeclarationLine(type));
        }

        public static void Clear()
        {
            cache.Clear();
            declarationLines.Clear();
        }
    }
}
