using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    public enum TweakStatus
    {
        Keepable = 0,
        RuntimeObject = 1,
        UnsupportedType = 2,
        Unresolved = 3,
        AssetSaved = 4,
        Reverted = 5,
    }

    /// <summary>One recorded Play Mode property edit (merged per target+path).</summary>
    [Serializable]
    public sealed class TweakRecord
    {
        public int InstanceId;
        public string GlobalObjectId;
        public string ScenePath;
        public string HierarchyPath;
        /// <summary>AssemblyQualifiedName of the component (or UnityEngine.GameObject).</summary>
        public string TargetTypeName;
        public int ComponentIndex;
        public string PropertyPath;
        public string TargetDisplay;
        public string PropertyDisplay;
        public string OriginalValue;
        public string OriginalObjectRefId;
        public string LastValue;
        public string LastObjectRefId;
        public float Timestamp;
        public bool IsAsset;
        public int Status;
    }

    /// <summary>In-memory store that survives domain reloads.</summary>
    public sealed class TweakLogStore : ScriptableSingleton<TweakLogStore>
    {
        [SerializeField] public List<TweakRecord> Records = new List<TweakRecord>();
    }

    /// <summary>
    /// Records Play Mode edits via Undo.postprocessModifications and offers
    /// keep/discard on exit. Works even when the Code Inspector window is closed.
    /// </summary>
    [InitializeOnLoad]
    public static class TweakLog
    {
        public const string PreferenceKey = "Denid.CodeInspector.PlayModeTweaks";

        public enum Preference { Ask = 0, AlwaysDiscard = 1, AlwaysKeep = 2 }

        static readonly HashSet<string> IgnoredPaths = new HashSet<string>
        {
            "m_RootOrder", "m_LocalEulerAnglesHint", "m_EditorHideFlags",
            "m_EditorClassIdentifier",
        };

        static TweakLog()
        {
            Undo.postprocessModifications += OnPostprocess;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static List<TweakRecord> Records => TweakLogStore.instance.Records;

        public static Preference Mode
        {
            get => (Preference)EditorPrefs.GetInt(PreferenceKey, (int)Preference.Ask);
            set => EditorPrefs.SetInt(PreferenceKey, (int)value);
        }

        static UndoPropertyModification[] OnPostprocess(UndoPropertyModification[] mods)
        {
            if (!EditorApplication.isPlaying)
                return mods;
            foreach (var m in mods)
            {
                var cur = m.currentValue;
                var prev = m.previousValue;
                if (cur == null || cur.target == null || cur.propertyPath == null)
                    continue;
                RecordModification(cur.target, cur.propertyPath,
                    prev?.value, prev?.objectReference, cur.value, cur.objectReference);
            }
            return mods;
        }

        /// <summary>Directly record one property edit (also used by tests).</summary>
        public static TweakRecord RecordModification(Object target, string propertyPath,
            string prevValue, Object prevRef, string newValue, Object newRef)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath) ||
                IgnoredPaths.Contains(propertyPath))
                return null;
            var key = (target.GetInstanceID(), propertyPath);
            foreach (var r in Records)
            {
                if (r.InstanceId == key.Item1 && r.PropertyPath == key.Item2)
                {
                    r.LastValue = newValue;
                    r.LastObjectRefId = ObjectRefId(newRef);
                    r.Timestamp = Time.realtimeSinceStartup;
                    return r;
                }
            }
            var rec = new TweakRecord
            {
                InstanceId = target.GetInstanceID(),
                GlobalObjectId = GlobalIdOf(target),
                PropertyPath = propertyPath,
                PropertyDisplay = propertyPath,
                OriginalValue = prevValue,
                OriginalObjectRefId = ObjectRefId(prevRef),
                LastValue = newValue,
                LastObjectRefId = ObjectRefId(newRef),
                Timestamp = Time.realtimeSinceStartup,
                IsAsset = EditorUtility.IsPersistent(target),
            };
            FillLocator(rec, target);
            Records.Add(rec);
            return rec;
        }

        static void FillLocator(TweakRecord rec, Object target)
        {
            var comp = target as Component;
            var go = target as GameObject ?? comp?.gameObject;
            rec.TargetTypeName = target.GetType().AssemblyQualifiedName;
            if (go != null)
            {
                rec.ScenePath = go.scene.path;
                rec.HierarchyPath = HierarchyPath(go.transform);
                if (comp != null)
                {
                    var index = 0;
                    foreach (var c in go.GetComponents<Component>())
                    {
                        if (c == comp)
                            break;
                        if (c != null && c.GetType() == comp.GetType())
                            index++;
                    }
                    rec.ComponentIndex = index;
                }
                rec.TargetDisplay = rec.HierarchyPath +
                    (comp != null ? "  (" + comp.GetType().Name + ")" : "");
            }
            else
            {
                rec.TargetDisplay = target.name;
            }
        }

        static string HierarchyPath(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        static string GlobalIdOf(Object o)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string ObjectRefId(Object o) => o == null ? null : GlobalIdOf(o);

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                CaptureFinalValues();
            else if (change == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += PresentOrApply;
        }

        /// <summary>Read final live values before objects are torn down.</summary>
        static void CaptureFinalValues()
        {
            foreach (var r in Records)
            {
                var target = EditorIds.IdToObject(r.InstanceId);
                if (target == null)
                    continue;
                try
                {
                    var so = new SerializedObject(target);
                    var p = so.FindProperty(r.PropertyPath);
                    if (p == null)
                        continue;
                    var text = PropertyValueCodec.ReadLeaf(p);
                    if (text != null)
                        r.LastValue = text;
                    if (p.propertyType == SerializedPropertyType.ObjectReference)
                        r.LastObjectRefId = ObjectRefId(p.objectReferenceValue);
                }
                catch (Exception)
                {
                    // Keep the last recorded value.
                }
            }
        }

        static void PresentOrApply()
        {
            if (Records.Count == 0)
                return;
            switch (Mode)
            {
                case Preference.AlwaysDiscard:
                    Records.Clear();
                    break;
                case Preference.AlwaysKeep:
                    var applied = KeepChanges.ApplyAllKeepable();
                    Debug.Log($"CodeInspector: kept {applied} Play Mode change(s).");
                    Records.Clear();
                    break;
                default:
                    if (Application.isBatchMode ||
                        SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                        Debug.Log($"CodeInspector: {Records.Count} Play Mode change(s) recorded (no display for dialog).");
                    else
                        KeepChangesWindow.Open();
                    break;
            }
        }
    }
}
