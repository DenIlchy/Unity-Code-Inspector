using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Bottom-docked console (like the Unity Console) listing trigger/collision
    /// contacts of the selected GameObject during Play Mode.
    /// </summary>
    public sealed class ContactsConsole : VisualElement
    {
        /// <summary>Contact entries for a node id, oldest to newest.</summary>
        public Func<string, List<PlayModeMonitor.ContactEntry>> ContactsProvider;
        /// <summary>Unmapped contact count for a node id.</summary>
        public Func<string, int> UnmappedProvider;
        /// <summary>Clear the contact log for a node id.</summary>
        public Action<string> ClearRequested;
        /// <summary>A row with a resolvable other object was clicked (other id).</summary>
        public event Action<string> EntryClicked;

        readonly Label titleLabel;
        readonly Label countLabel;
        readonly ToolbarToggle pauseToggle;
        readonly ScrollView body;
        readonly Label emptyLabel;
        readonly List<PlayModeMonitor.ContactEntry> shown =
            new List<PlayModeMonitor.ContactEntry>();

        GraphNode node;

        internal bool Paused
        {
            get => pauseToggle.value;
            set => pauseToggle.value = value;
        }

        internal int RowCount =>
            body.contentContainer.Query<VisualElement>(className: "ci-console-row").ToList().Count;

        /// <summary>Simulates the Clear button (tests).</summary>
        internal void SimulateClear() => OnClear();

        /// <summary>Simulates a row click (tests).</summary>
        internal void SimulateRowClick(int index)
        {
            if (index < 0 || index >= shown.Count)
                return;
            var id = shown[index].OtherId;
            if (id != null)
                EntryClicked?.Invoke(id);
        }

        public ContactsConsole()
        {
            AddToClassList("ci-console");

            var header = new Toolbar();
            header.AddToClassList("ci-console-header");
            titleLabel = new Label("Contacts");
            titleLabel.AddToClassList("ci-console-title");
            header.Add(titleLabel);
            countLabel = new Label();
            countLabel.AddToClassList("ci-console-count");
            header.Add(countLabel);
            var spacer = new VisualElement();
            spacer.AddToClassList("ci-toolbar-spacer");
            header.Add(spacer);
            pauseToggle = new ToolbarToggle { text = "Pause" };
            pauseToggle.RegisterValueChangedCallback(e =>
            {
                if (!e.newValue)
                    Refresh();
            });
            header.Add(pauseToggle);
            header.Add(new ToolbarButton(OnClear) { text = "Clear" });
            Add(header);

            body = new ScrollView();
            body.AddToClassList("ci-console-body");
            Add(body);

            emptyLabel = new Label();
            emptyLabel.AddToClassList("ci-console-empty");
            body.Add(emptyLabel);
        }

        /// <summary>Binds the console to a node (null = nothing selected).</summary>
        public void SetNode(GraphNode n)
        {
            node = n;
            Refresh();
        }

        /// <summary>Pulls the latest entries (called every live tick).</summary>
        public void Refresh()
        {
            if (Paused)
                return;
            RenderCurrent();
        }

        void OnClear()
        {
            if (node != null)
                ClearRequested?.Invoke(node.Id);
            RenderCurrent();
        }

        void RenderCurrent()
        {
            var isGo = node != null && node.Kind == NodeKind.GameObject;
            titleLabel.text = isGo ? "Contacts — " + node.Label : "Contacts";
            var unmapped = isGo && UnmappedProvider != null
                ? UnmappedProvider(node.Id)
                : 0;
            var entries = isGo && ContactsProvider != null
                ? ContactsProvider(node.Id)
                : null;
            var count = entries?.Count ?? 0;
            countLabel.text = count + (count == 1 ? " entry" : " entries") +
                              (unmapped > 0 ? " · " + unmapped + " unmapped" : "");

            var nearBottom = NearBottom();
            var prevCount = shown.Count;
            ReconcileRows(entries);
            emptyLabel.text = isGo
                ? "No contacts yet for this object."
                : "Select a GameObject in the Scene view to see its contacts.";
            emptyLabel.style.display = count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (nearBottom && count > prevCount)
                ScrollToBottom();
        }

        bool NearBottom()
        {
            var contentH = body.contentContainer.layout.height;
            var viewH = body.layout.height;
            if (float.IsNaN(contentH) || float.IsNaN(viewH))
                return true;
            var max = contentH - viewH;
            return max <= 1f || body.scrollOffset.y >= max - 4f;
        }

        void ScrollToBottom()
        {
            body.schedule.Execute(() =>
                body.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        void ReconcileRows(List<PlayModeMonitor.ContactEntry> entries)
        {
            var count = entries?.Count ?? 0;
            var i = 0;
            for (; i < count; i++)
            {
                var e = entries[i];
                VisualElement row;
                if (i < body.contentContainer.childCount)
                {
                    var child = body.contentContainer[i];
                    if (child == emptyLabel)
                    {
                        // empty label is always kept last; insert before it.
                        row = MakeRow();
                        body.contentContainer.Insert(i, row);
                    }
                    else
                    {
                        row = child;
                    }
                }
                else
                {
                    row = MakeRow();
                    body.contentContainer.Insert(body.contentContainer.childCount - 1, row);
                }
                FillRow(row, e, i);
            }
            shown.Clear();
            if (entries != null)
                shown.AddRange(entries);
            // Trim surplus rows (emptyLabel stays last at index childCount-1).
            while (body.contentContainer.childCount - 1 > count)
                body.contentContainer.RemoveAt(body.contentContainer.childCount - 2);
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ci-console-row");
            var time = new Label();
            time.AddToClassList("ci-console-time");
            row.Add(time);
            var msg = new Label();
            msg.AddToClassList("ci-console-msg");
            row.Add(msg);
            var name = new Label();
            name.AddToClassList("ci-console-name");
            row.Add(name);
            return row;
        }

        void FillRow(VisualElement row, PlayModeMonitor.ContactEntry e, int index)
        {
            row.EnableInClassList("ci-console-row-alt", index % 2 == 1);
            var labels = row.Query<Label>().ToList();
            labels[0].text = e.Time.ToString("0.0") + "s";
            labels[1].text = e.Message;
            labels[1].EnableInClassList("ci-console-exit",
                e.Message != null && e.Message.IndexOf("Exit", StringComparison.Ordinal) >= 0);
            labels[1].EnableInClassList("ci-console-enter",
                e.Message == null || e.Message.IndexOf("Exit", StringComparison.Ordinal) < 0);
            labels[2].text = e.OtherName;

            var clickable = e.OtherId != null;
            row.EnableInClassList("ci-console-row-linked", clickable);
            row.tooltip = clickable ? "Select " + e.OtherName : null;
            var id = e.OtherId;
            row.userData = clickable ? (Action)(() => EntryClicked?.Invoke(id)) : null;
            row.UnregisterCallback<ClickEvent>(OnRowClick);
            if (clickable)
                row.RegisterCallback<ClickEvent>(OnRowClick);
        }

        static void OnRowClick(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is Action act)
                act();
        }
    }
}
