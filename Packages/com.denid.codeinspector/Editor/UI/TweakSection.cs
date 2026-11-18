using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// "Parameters" section of the side panel: editable inspectors for the
    /// components/assets behind the selected node. Kept stable across graph
    /// rebuilds so in-progress edits are not interrupted.
    /// </summary>
    public sealed class TweakSection : VisualElement
    {
        static readonly Dictionary<string, bool> expandedByType = new Dictionary<string, bool>();

        /// <summary>Raised when a tracked serialized object changes.</summary>
        public event Action<Object> InspectedObjectChanged;

        readonly Toggle onlyMineToggle;
        readonly Label statusLabel;
        readonly VisualElement body;
        readonly Dictionary<Component, VisualElement> headerByComponent =
            new Dictionary<Component, VisualElement>();

        GraphNode boundNode;
        Object boundTarget;
        List<Component> boundComponents;
        CodeGraph typeGraph;
        CodeGraph sceneGraph;

        public bool OnlyUserScripts
        {
            get => onlyMineToggle.value;
            set
            {
                onlyMineToggle.value = value;
                // ChangeEvent may not reach the callback without a panel.
                Rebuild();
            }
        }

        internal VisualElement Body => body;

        public TweakSection()
        {
            AddToClassList("ci-tweak");

            var header = new Foldout
            {
                text = "Parameters",
                value = !expandedByType.TryGetValue("Parameters", out var e) || e,
            };
            header.AddToClassList("ci-sidepanel-foldout");
            header.RegisterValueChangedCallback(evt => expandedByType["Parameters"] = evt.newValue);
            Add(header);

            onlyMineToggle = new Toggle("Only my scripts") { value = true };
            onlyMineToggle.AddToClassList("ci-tweak-onlymine");
            onlyMineToggle.RegisterValueChangedCallback(_ => Rebuild());
            header.Add(onlyMineToggle);

            statusLabel = new Label();
            statusLabel.AddToClassList("ci-sidepanel-sub");
            header.Add(statusLabel);

            body = new VisualElement();
            header.Add(body);

            style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Binds a node. If the new node represents the same target object and
        /// the same components, the existing inspectors are kept untouched.
        /// </summary>
        public void Bind(GraphNode node, CodeGraph typeGraph, CodeGraph sceneGraph)
        {
            this.typeGraph = typeGraph;
            this.sceneGraph = sceneGraph;
            if (IsStable(node))
            {
                boundNode = node;
                return;
            }
            boundNode = node;
            Rebuild();
        }

        public void Unbind()
        {
            boundNode = null;
            boundTarget = null;
            boundComponents = null;
            Rebuild();
        }

        bool IsStable(GraphNode node)
        {
            if (node == null || boundNode == null)
                return false;
            if (node.Id != boundNode.Id)
                return false;
            if (node.Kind != boundNode.Kind || node.Target != boundTarget)
                return false;
            var a = ComponentsOf(node);
            var b = boundComponents ?? new List<Component>();
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        static List<Component> ComponentsOf(GraphNode node)
        {
            var list = new List<Component>();
            if (node.Components != null)
            {
                foreach (var c in node.Components)
                {
                    if (c.Component != null)
                        list.Add(c.Component);
                }
            }
            return list;
        }

        internal int RebuildCount { get; private set; }

        void Rebuild()
        {
            RebuildCount++;
            body.Clear();
            headerByComponent.Clear();
            boundTarget = null;
            boundComponents = null;
            statusLabel.text = "";

            var node = boundNode;
            if (node == null)
            {
                style.display = DisplayStyle.None;
                return;
            }
            if (node.Target == null && node.Kind != NodeKind.ScriptType)
            {
                style.display = DisplayStyle.Flex;
                statusLabel.text = "Object no longer exists.";
                return;
            }

            style.display = DisplayStyle.Flex;
            boundTarget = node.Target;
            boundComponents = ComponentsOf(node);

            switch (node.Kind)
            {
                case NodeKind.GameObject:
                    BuildGameObject(node);
                    break;
                case NodeKind.Asset:
                    BuildAsset(node);
                    break;
                case NodeKind.ScriptType:
                    BuildType(node);
                    break;
                default:
                    style.display = DisplayStyle.None;
                    break;
            }
        }

        // ---- GameObject -----------------------------------------------------

        void BuildGameObject(GraphNode node)
        {
            var visible = 0;
            if (node.Components != null)
            {
                foreach (var c in node.Components)
                {
                    if (c.Component == null || c.Type == null)
                        continue;
                    if (onlyMineToggle.value && !c.IsUserScript)
                        continue;
                    body.Add(ComponentFoldout(c));
                    visible++;
                }
            }
            onlyMineToggle.style.display = DisplayStyle.Flex;
            if (visible == 0)
                statusLabel.text = "No components to inspect.";
        }

        Foldout ComponentFoldout(ComponentInfo info)
        {
            var comp = info.Component;
            var type = info.Type;
            var expanded = expandedByType.TryGetValue(type.FullName, out var e)
                ? e
                : info.IsUserScript;

            var foldout = new Foldout { text = type.Name, value = expanded };
            foldout.AddToClassList("ci-tweak-foldout");

            var header = foldout.Q<Toggle>();
            var icon = new Image
            {
                image = EditorGUIUtility.ObjectContent(null, type).image,
                scaleMode = ScaleMode.ScaleToFit,
            };
            icon.AddToClassList("ci-comp-icon");
            header.Add(icon);

            if (comp is Behaviour behaviour)
            {
                var so = new SerializedObject(behaviour);
                var enabledProp = so.FindProperty("m_Enabled");
                if (enabledProp != null)
                {
                    var enabledToggle = new Toggle { tooltip = "Enabled" };
                    enabledToggle.AddToClassList("ci-tweak-enabled");
                    enabledToggle.BindProperty(enabledProp);
                    header.Add(enabledToggle);
                }
            }

            headerByComponent[comp] = header;

            var populated = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                expandedByType[type.FullName] = evt.newValue;
                if (!evt.newValue || populated)
                    return;
                populated = true;
                foldout.Add(InspectorFor(comp));
            });
            if (expanded)
            {
                populated = true;
                foldout.Add(InspectorFor(comp));
            }
            return foldout;
        }

        VisualElement InspectorFor(Object target)
        {
            var host = new VisualElement();
            host.AddToClassList("ci-tweak-inspector");
            SerializedObject so = null;
            try
            {
                so = new SerializedObject(target);
            }
            catch (Exception)
            {
                // Some objects cannot be serialized; inspector still shows basics.
            }
            if (so != null)
            {
                var tracker = new VisualElement();
                tracker.style.display = DisplayStyle.None;
                var t = target;
                tracker.TrackSerializedObjectValue(so, _ => OnInspectedValueChanged(t));
                host.Add(tracker);
            }
            host.Add(new InspectorElement(target));
            return host;
        }

        void OnInspectedValueChanged(Object target)
        {
            if (target is Component c && headerByComponent.TryGetValue(c, out var header))
                Flash(header);
            InspectedObjectChanged?.Invoke(target);
        }

        void Flash(VisualElement element)
        {
            element.AddToClassList("ci-comp-flash");
            element.schedule.Execute(() => element.RemoveFromClassList("ci-comp-flash"))
                .StartingIn(600);
        }

        // ---- Asset ----------------------------------------------------------

        void BuildAsset(GraphNode node)
        {
            onlyMineToggle.style.display = DisplayStyle.None;
            body.Add(InspectorFor(node.Target));
        }

        // ---- ScriptType ------------------------------------------------------

        void BuildType(GraphNode node)
        {
            onlyMineToggle.style.display = DisplayStyle.None;
            var instances = FindInstances(node.Type);
            if (instances.Count == 0)
            {
                statusLabel.text = "No instances in open scenes or assets.";
                return;
            }

            var labels = new List<string>();
            foreach (var inst in instances)
                labels.Add(inst.Label);

            var inspectorHost = new VisualElement();
            var dropdown = new DropdownField("Edit instance", labels, 0);
            dropdown.AddToClassList("ci-tweak-dropdown");
            var current = instances[0].Target;
            void Show(Object o)
            {
                inspectorHost.Clear();
                if (o != null)
                    inspectorHost.Add(InspectorFor(o));
            }
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var idx = labels.IndexOf(evt.newValue);
                if (idx >= 0)
                    Show(instances[idx].Target);
            });
            body.Add(dropdown);
            body.Add(inspectorHost);
            Show(current);
        }

        List<(Object Target, string Label)> FindInstances(Type type)
        {
            var list = new List<(Object, string)>();
            if (type == null)
                return list;
            if (typeof(Component).IsAssignableFrom(type) && sceneGraph != null)
            {
                foreach (var n in sceneGraph.Nodes.Values)
                {
                    if (n.Kind != NodeKind.GameObject || n.Components == null)
                        continue;
                    foreach (var c in n.Components)
                    {
                        if (c.Type == type && c.Component != null)
                            list.Add((c.Component,
                                n.Label + (string.IsNullOrEmpty(n.Caption) ? "" : " (" + n.Caption + ")")));
                    }
                }
            }
            if (typeof(ScriptableObject).IsAssignableFrom(type))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (obj != null && obj.GetType() == type)
                        list.Add((obj, path));
                }
            }
            return list;
        }
    }
}
