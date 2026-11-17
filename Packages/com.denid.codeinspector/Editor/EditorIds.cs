using UnityEditor;
using UnityEngine;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Instance-id lookups across Unity versions: EntityIdToObject is 6000.3+,
    /// InstanceIDToObject is used on older versions.
    /// </summary>
    static class EditorIds
    {
        public static Object IdToObject(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(instanceId);
#else
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }
    }
}
