using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Reads/writes leaf SerializedProperty values as strings in the same
    /// format UndoPropertyModification uses (ints as int, floats invariant
    /// round-trip, bools "1"/"0", ObjectReference carried separately).
    /// </summary>
    public static class PropertyValueCodec
    {
        /// <summary>Leaf → string. Returns null for container/unsupported types.</summary>
        public static string ReadLeaf(SerializedProperty p)
        {
            try
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.Character:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.ArraySize:
                        return p.longValue.ToString(CultureInfo.InvariantCulture);
                    case SerializedPropertyType.Boolean:
                        return p.boolValue ? "1" : "0";
                    case SerializedPropertyType.Float:
                        return p.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                    case SerializedPropertyType.String:
                        return p.stringValue;
                    case SerializedPropertyType.ObjectReference:
                        return "ref:" + p.objectReferenceInstanceIDValue;
                    case SerializedPropertyType.Vector2:
                        var v2 = p.vector2Value;
                        return F(v2.x) + "," + F(v2.y);
                    case SerializedPropertyType.Vector3:
                        var v3 = p.vector3Value;
                        return F(v3.x) + "," + F(v3.y) + "," + F(v3.z);
                    case SerializedPropertyType.Vector4:
                        var v4 = p.vector4Value;
                        return F(v4.x) + "," + F(v4.y) + "," + F(v4.z) + "," + F(v4.w);
                    case SerializedPropertyType.Quaternion:
                        var q = p.quaternionValue;
                        return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
                    case SerializedPropertyType.Color:
                        var c = p.colorValue;
                        return F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);
                    case SerializedPropertyType.Rect:
                        var r = p.rectValue;
                        return F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height);
                    case SerializedPropertyType.Vector2Int:
                        var v2i = p.vector2IntValue;
                        return v2i.x + "," + v2i.y;
                    case SerializedPropertyType.Vector3Int:
                        var v3i = p.vector3IntValue;
                        return v3i.x + "," + v3i.y + "," + v3i.z;
                    case SerializedPropertyType.RectInt:
                        var ri = p.rectIntValue;
                        return ri.x + "," + ri.y + "," + ri.width + "," + ri.height;
                    case SerializedPropertyType.Bounds:
                        var b = p.boundsValue;
                        return F(b.center.x) + "," + F(b.center.y) + "," + F(b.center.z) + "," +
                               F(b.size.x) + "," + F(b.size.y) + "," + F(b.size.z);
                    default:
                        return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Types the codec can write back (keep across Play exit).</summary>
        public static bool IsWritable(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.ObjectReference:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>String → leaf. objectRef is used for ObjectReference leaves.</summary>
        public static bool WriteLeaf(SerializedProperty p, string value, Object objectRef)
        {
            if (p == null)
                return false;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.Character:
                    case SerializedPropertyType.LayerMask:
                        p.longValue = long.Parse(value, inv);
                        return true;
                    case SerializedPropertyType.ArraySize:
                        p.intValue = int.Parse(value, inv);
                        return true;
                    case SerializedPropertyType.Boolean:
                        p.boolValue = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                        return true;
                    case SerializedPropertyType.Float:
                        p.doubleValue = double.Parse(value, inv);
                        return true;
                    case SerializedPropertyType.String:
                        p.stringValue = value ?? "";
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        p.objectReferenceValue = objectRef;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
