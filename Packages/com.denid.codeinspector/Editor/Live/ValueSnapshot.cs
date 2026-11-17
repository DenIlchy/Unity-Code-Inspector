using System.Collections.Generic;
using UnityEditor;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Fingerprint + diff over the leaf properties of a SerializedObject.
    /// Used by PlayModeMonitor to detect live value changes cheaply.
    /// </summary>
    public static class ValueSnapshot
    {
        public const int MaxProperties = 64;

        public struct FieldValue
        {
            public string Path;
            public string Text;
        }

        public struct FieldChange
        {
            public string Path;
            public string OldValue;
            public string NewValue;
        }

        /// <summary>Captures leaf property values (path-sorted). Caller does so.Update() first.</summary>
        public static List<FieldValue> Capture(SerializedObject so, int maxProperties = MaxProperties)
        {
            var list = new List<FieldValue>();
            if (so == null)
                return list;
            var it = so.GetIterator();
            var guard = 0;
            while (it.NextVisible(true) && guard++ < maxProperties)
            {
                var path = it.propertyPath;
                if (path == "m_Script")
                    continue;
                var text = PropertyValueCodec.ReadLeaf(it);
                if (text != null)
                    list.Add(new FieldValue { Path = path, Text = text });
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return list;
        }

        /// <summary>Ordered merge join over two sorted captures.</summary>
        public static List<FieldChange> Diff(List<FieldValue> before, List<FieldValue> after)
        {
            var changes = new List<FieldChange>();
            if (before == null || after == null)
                return changes;
            var i = 0;
            var j = 0;
            while (i < before.Count && j < after.Count)
            {
                var cmp = string.CompareOrdinal(before[i].Path, after[j].Path);
                if (cmp < 0)
                {
                    i++;
                }
                else if (cmp > 0)
                {
                    j++;
                }
                else
                {
                    if (before[i].Text != after[j].Text)
                    {
                        changes.Add(new FieldChange
                        {
                            Path = before[i].Path,
                            OldValue = before[i].Text,
                            NewValue = after[j].Text,
                        });
                    }
                    i++;
                    j++;
                }
            }
            return changes;
        }
    }
}
