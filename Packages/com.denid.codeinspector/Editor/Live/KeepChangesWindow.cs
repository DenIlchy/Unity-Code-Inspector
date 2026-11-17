using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Post-Play-Mode dialog listing every recorded tweak so the user can pick
    /// which ones to keep.
    /// </summary>
    public sealed class KeepChangesWindow : EditorWindow
    {
        class Row
        {
            public TweakRecord Record;
            public TweakStatus Status;
            public Toggle Toggle;
            public Toggle RevertToggle;
        }

        readonly List<Row> rows = new List<Row>();
        Label summary;
        VisualElement listBox;

        public static void Open()
        {
            var w = GetWindow<KeepChangesWindow>(utility: true, title: "Keep Play Mode changes?");
            w.minSize = new Vector2(460, 320);
            w.Build();
            w.Show();
        }

        void Build()
        {
            rows.Clear();
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            summary = new Label();
            summary.AddToClassList("ci-sidepanel-sub");
            root.Add(summary);

            listBox = new ScrollView();
            listBox.style.flexGrow = 1f;
            root.Add(listBox);

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            root.Add(footer);
            footer.Add(new Button(KeepSelected) { text = "Keep selected" });
            footer.Add(new Button(() => { TweakLog.Records.Clear(); Close(); }) { text = "Discard all" });
            footer.Add(new Button(SelectAll) { text = "Select all" });
            footer.Add(new Button(SelectNone) { text = "Select none" });

            var pref = new DropdownField("When Play Mode ends",
                new List<string> { "Ask", "Always discard", "Always keep" },
                (int)TweakLog.Mode);
            pref.RegisterValueChangedCallback(e =>
                TweakLog.Mode = (TweakLog.Preference)pref.index);
            footer.Add(pref);

            Populate();
        }

        void Populate()
        {
            listBox.Clear();
            rows.Clear();
            var keepable = 0;
            string lastGroup = null;
            foreach (var r in TweakLog.Records)
            {
                var status = KeepChanges.Classify(r);
                if (status == TweakStatus.Keepable)
                    keepable++;
                var row = new Row { Record = r, Status = status };

                if (r.TargetDisplay != lastGroup)
                {
                    lastGroup = r.TargetDisplay;
                    var gh = new Label(lastGroup);
                    gh.AddToClassList("ci-member-group");
                    listBox.Add(gh);
                }

                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                listBox.Add(line);

                if (status == TweakStatus.AssetSaved)
                {
                    var revert = new Toggle { value = false };
                    row.RevertToggle = revert;
                    line.Add(revert);
                }
                else
                {
                    var toggle = new Toggle { value = status == TweakStatus.Keepable };
                    toggle.SetEnabled(status == TweakStatus.Keepable);
                    row.Toggle = toggle;
                    line.Add(toggle);
                }

                var name = r.PropertyDisplay ?? r.PropertyPath;
                var text = status == TweakStatus.AssetSaved
                    ? $"{name} = {r.LastValue}   (already saved — check to revert to {r.OriginalValue})"
                    : $"{name}  {r.OriginalValue} → {r.LastValue}";
                if (status == TweakStatus.RuntimeObject || status == TweakStatus.Unresolved)
                    text += "   [cannot keep]";
                else if (status == TweakStatus.UnsupportedType)
                    text += "   [unsupported type]";
                var label = new Label(text);
                label.AddToClassList("ci-edge-entry");
                line.Add(label);
                rows.Add(row);
            }
            summary.text = $"{TweakLog.Records.Count} change(s), {keepable} keepable.";
        }

        void KeepSelected()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Code Inspector: Keep Play Mode changes");
            var group = Undo.GetCurrentGroup();
            var applied = 0;
            foreach (var row in rows)
            {
                if (row.Status == TweakStatus.Keepable && row.Toggle != null && row.Toggle.value &&
                    KeepChanges.Apply(row.Record))
                    applied++;
                else if (row.Status == TweakStatus.AssetSaved && row.RevertToggle != null &&
                         row.RevertToggle.value)
                    KeepChanges.Revert(row.Record);
            }
            Undo.CollapseUndoOperations(group);
            Debug.Log($"CodeInspector: kept {applied} Play Mode change(s).");
            TweakLog.Records.Clear();
            Close();
        }

        void SelectAll()
        {
            foreach (var r in rows)
            {
                if (r.Toggle != null && r.Status == TweakStatus.Keepable)
                    r.Toggle.value = true;
            }
        }

        void SelectNone()
        {
            foreach (var r in rows)
            {
                if (r.Toggle != null)
                    r.Toggle.value = false;
                if (r.RevertToggle != null)
                    r.RevertToggle.value = false;
            }
        }
    }
}
