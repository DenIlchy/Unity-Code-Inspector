using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>Status classification and apply/revert logic for tweak records.</summary>
    public static class KeepChanges
    {
        /// <summary>Classify a record against the current (edit mode) world.</summary>
        public static TweakStatus Classify(TweakRecord r)
        {
            if (r.Status == (int)TweakStatus.Reverted)
                return TweakStatus.Reverted;
            if (r.IsAsset)
                return TweakStatus.AssetSaved;
            var target = TweakResolver.ResolveTarget(r);
            if (target == null)
                return TweakStatus.RuntimeObject;
            var p = FindProperty(target, r.PropertyPath);
            if (p == null)
                return TweakStatus.Unresolved;
            return PropertyValueCodec.IsWritable(p) ? TweakStatus.Keepable : TweakStatus.UnsupportedType;
        }

        static SerializedProperty FindProperty(Object target, string path)
        {
            try
            {
                return new SerializedObject(target).FindProperty(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Apply one record's LastValue to its resolved target (edit mode).</summary>
        public static bool Apply(TweakRecord r)
        {
            var target = TweakResolver.ResolveTarget(r);
            if (target == null)
                return false;
            var objectRef = TweakResolver.ResolveByGlobalId(r.LastObjectRefId);
            try
            {
                var so = new SerializedObject(target);
                var p = so.FindProperty(r.PropertyPath);
                if (p == null || !PropertyValueCodec.WriteLeaf(p, r.LastValue, objectRef))
                    return false;
                return so.ApplyModifiedProperties();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Revert one record back to OriginalValue (works in play or edit mode).</summary>
        public static bool Revert(TweakRecord r, Object targetOverride = null)
        {
            var target = targetOverride != null ? targetOverride : TweakResolver.ResolveTarget(r);
            if (target == null)
                return false;
            var objectRef = TweakResolver.ResolveByGlobalId(r.OriginalObjectRefId);
            try
            {
                var so = new SerializedObject(target);
                var p = so.FindProperty(r.PropertyPath);
                if (p == null || !PropertyValueCodec.WriteLeaf(p, r.OriginalValue, objectRef))
                    return false;
                var ok = so.ApplyModifiedProperties();
                if (ok && EditorUtility.IsPersistent(target))
                    EditorUtility.SetDirty(target);
                if (ok)
                    r.Status = (int)TweakStatus.Reverted;
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Applies every keepable record inside one Undo group.</summary>
        public static int ApplyAllKeepable()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Code Inspector: Keep Play Mode changes");
            var group = Undo.GetCurrentGroup();
            var applied = 0;
            foreach (var r in TweakLog.Records)
            {
                if (Classify(r) == TweakStatus.Keepable && Apply(r))
                    applied++;
            }
            Undo.CollapseUndoOperations(group);
            return applied;
        }
    }
}
