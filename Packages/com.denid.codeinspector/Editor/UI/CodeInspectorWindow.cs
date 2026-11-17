using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    public sealed class CodeInspectorWindow : EditorWindow
    {
        const string StylesheetPath = "Packages/com.denid.codeinspector/Editor/UI/Styles/CodeInspector.uss";

        enum View { Architecture = 0, Scene = 1 }

        [SerializeField] int view;
        [SerializeField] int edgeKindsMask;
        [SerializeField] int categoriesMask = -1;
        [SerializeField] string search = "";
        [SerializeField] bool focusOn;
        [SerializeField] int focusDepth = 1;
        [SerializeField] float panelWidth = 300f;
        [SerializeField] bool showPanel = true;
        [SerializeField] bool includePackages;
        [SerializeField] bool includeEditorScripts;
        [SerializeField] bool sceneAutoRefresh = true;
        [SerializeField] bool sceneSyncSelection = true;
        [SerializeField] bool sceneShowAll;
        [SerializeField] bool defaultsInitialized;

        GraphCanvas archCanvas;
        GraphCanvas sceneCanvas;
        SidePanel sidePanel;
        VisualElement emptyState;
        VisualElement legend;
        Label statusLabel;
        ToolbarToggle archToggle;
        ToolbarToggle sceneToggle;
        ToolbarToggle focusToggle;
        ToolbarToggle autoRefreshToggle;
        ToolbarToggle syncSelectionToggle;
        ToolbarToggle showAllToggle;
        SliderInt focusSlider;
        TwoPaneSplitView splitView;
        LayoutStore layoutStore;
        GraphFilterResult archFiltered;
        GraphFilterResult sceneFiltered;
        CodeGraph typeGraph;
        CodeGraph sceneGraph;
        IVisualElementScheduledItem pendingSceneRefresh;
        readonly RefreshThrottle sceneThrottle = new RefreshThrottle(300, 1000);
        bool suppressSelectionSync;
        bool rebuilding;
        readonly PlayModeMonitor liveMonitor = new PlayModeMonitor();
        Label livePill;
        ToolbarButton tweaksBadge;

        GraphCanvas ActiveCanvas => view == (int)View.Scene ? sceneCanvas : archCanvas;
        GraphFilterResult ActiveFiltered => view == (int)View.Scene ? sceneFiltered : archFiltered;
        CodeGraph ActiveGraph => view == (int)View.Scene ? sceneGraph : typeGraph;

        [MenuItem("Window/Code Inspector")]
        public static void Open()
        {
            var window = GetWindow<CodeInspectorWindow>();
            window.titleContent = new GUIContent("Code Inspector");
            window.Show();
        }

        void OnEnable()
        {
            EnsureDefaults();
            layoutStore = new LayoutStore();
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.hierarchyChanged += OnSceneContentChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChangedRuntime;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            Selection.selectionChanged += OnUnitySelectionChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            EditorApplication.hierarchyChanged -= OnSceneContentChanged;
            ObjectChangeEvents.changesPublished -= OnObjectChanges;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged -= OnActiveSceneChangedRuntime;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            Selection.selectionChanged -= OnUnitySelectionChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;
        }

        void EnsureDefaults()
        {
            if (defaultsInitialized)
                return;
            defaultsInitialized = true;
            edgeKindsMask = 0;
            foreach (EdgeKind k in Enum.GetValues(typeof(EdgeKind)))
                if (k != EdgeKind.TypeUsage)
                    edgeKindsMask |= 1 << (int)k;
            categoriesMask = 0;
            foreach (ScriptCategory c in Enum.GetValues(typeof(ScriptCategory)))
                categoriesMask |= 1 << (int)c;
        }

        void OnAfterAssemblyReload()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;
                RebuildArchitecture();
            };
        }

        // ---- Scene auto refresh (debounced) ----------------------------------

        void ScheduleSceneRefresh()
        {
            if (rebuilding || view != (int)View.Scene || !sceneAutoRefresh ||
                rootVisualElement == null)
                return;
            sceneThrottle.MaxWaitMs = Application.isPlaying ? 1500 : 1000;
            var delay = sceneThrottle.Ping((long)(EditorApplication.timeSinceStartup * 1000.0));
            pendingSceneRefresh?.Pause();
            pendingSceneRefresh = rootVisualElement.schedule.Execute(() =>
            {
                sceneThrottle.Reset();
                RebuildScene();
            }).StartingIn(delay);
        }

        void OnSceneContentChanged() => ScheduleSceneRefresh();

        void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (rebuilding)
                return;
            var kinds = new List<ObjectChangeKind>(stream.length);
            for (var i = 0; i < stream.length; i++)
                kinds.Add(stream.GetEventType(i));
            if (SceneRefreshPolicy.ShouldRefresh(kinds))
                ScheduleSceneRefresh();
        }

        UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] mods)
        {
            // Pulse nodes whose objects were just edited (from our panel or the
            // regular Inspector); the graph itself updates via auto refresh.
            if (sceneCanvas != null && sceneGraph != null)
            {
                var targets = new List<Object>(mods.Length);
                foreach (var m in mods)
                {
                    if (m.currentValue != null && m.currentValue.target != null)
                        targets.Add(m.currentValue.target);
                }
                foreach (var id in GraphPulse.NodeIdsForObjects(targets, sceneGraph))
                    sceneCanvas.PulseNode(id);
            }
            return mods;
        }

        void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode) => ScheduleSceneRefresh();
        void OnSceneClosed(UnityEngine.SceneManagement.Scene scene) => ScheduleSceneRefresh();
        void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b) => ScheduleSceneRefresh();
        void OnActiveSceneChangedRuntime(UnityEngine.SceneManagement.Scene a, UnityEngine.SceneManagement.Scene b) => ScheduleSceneRefresh();
        void OnPrefabStageChanged(PrefabStage stage) => ScheduleSceneRefresh();
        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode ||
                change == PlayModeStateChange.EnteredEditMode)
                liveMonitor.Clear();
            if (view == (int)View.Scene)
                RebuildScene();
        }

        // ---- Play Mode live view ----------------------------------------------

        void OnLiveTick()
        {
            var playing = EditorApplication.isPlaying;
            if (livePill != null)
                livePill.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            var tweaks = TweakLog.Records.Count;
            if (tweaksBadge != null)
            {
                tweaksBadge.style.display = playing && tweaks > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                tweaksBadge.text = tweaks + " tweak" + (tweaks == 1 ? "" : "s");
            }
            if (!playing || sceneGraph == null || sceneCanvas == null)
                return;
            liveMonitor.Tick(sceneGraph, sceneCanvas);
            sidePanel?.RefreshLive();
        }

        void OnLiveUnityEventFired(GraphEdge edge, int count)
        {
            if (sceneCanvas != null)
            {
                sceneCanvas.FlashEdge(edge);
                sceneCanvas.SetEdgeFlashCount(edge, count);
            }
            sidePanel?.RefreshLive();
        }

        void OnLiveContact(string selfId, string otherId, string message)
        {
            liveMonitor.HandleContact(selfId, otherId, message, sceneGraph, sceneCanvas);
        }

        List<TweakRecord> TweaksForNode(GraphNode node)
        {
            var list = new List<TweakRecord>();
            if (node == null || TweakLog.Records.Count == 0)
                return list;
            foreach (var r in TweakLog.Records)
            {
                var target = EditorIds.IdToObject(r.InstanceId);
                if (target == null)
                    continue;
                if (node.Target == target || IsNodeComponent(node, target))
                    list.Add(r);
            }
            return list;
        }

        static bool IsNodeComponent(GraphNode node, UnityEngine.Object target)
        {
            if (node.Components == null)
                return false;
            foreach (var c in node.Components)
                if (c.Component == target)
                    return true;
            return false;
        }

        void OnTweakRevert(TweakRecord record, GraphNode node)
        {
            UnityEngine.Object target = null;
            if (node.Components != null)
            {
                foreach (var c in node.Components)
                {
                    if (c.Component != null && c.Component.GetInstanceID() == record.InstanceId)
                    {
                        target = c.Component;
                        break;
                    }
                }
            }
            if (target == null && node.Target != null && node.Target.GetInstanceID() == record.InstanceId)
                target = node.Target;
            if (KeepChanges.Revert(record, target))
                sidePanel?.RefreshLive();
        }

        void OnUnitySelectionChanged()
        {
            if (suppressSelectionSync || !sceneSyncSelection || view != (int)View.Scene ||
                sceneCanvas == null)
                return;
            var go = Selection.activeGameObject;
            if (go == null)
                return;
            var id = sceneCanvas.FindNodeIdForTarget(go);
            if (id != null)
            {
                sceneCanvas.Select(id);
                sceneCanvas.FrameNode(id);
            }
        }

        // ---- GUI ---------------------------------------------------------------

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("ci-window");
            root.EnableInClassList("ci-light", !EditorGUIUtility.isProSkin);

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesheetPath);
            if (sheet != null)
                root.styleSheets.Add(sheet);

            root.Add(BuildToolbar());

            splitView = new TwoPaneSplitView(1, Mathf.Max(200f, panelWidth), TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            root.Add(splitView);

            var graphArea = new VisualElement();
            graphArea.style.flexGrow = 1f;
            graphArea.style.overflow = Overflow.Hidden;
            splitView.Add(graphArea);

            archCanvas = new GraphCanvas(layoutStore, "Architecture");
            archCanvas.NodeSelected += OnNodeSelected;
            archCanvas.NodeActivated += OnNodeActivated;
            graphArea.Add(archCanvas);

            sceneCanvas = new GraphCanvas(layoutStore, "Scene");
            sceneCanvas.NodeSelected += OnNodeSelected;
            sceneCanvas.NodeActivated += OnNodeActivated;
            sceneCanvas.style.display = view == (int)View.Scene ? DisplayStyle.Flex : DisplayStyle.None;
            graphArea.Add(sceneCanvas);
            archCanvas.style.display = view == (int)View.Scene ? DisplayStyle.None : DisplayStyle.Flex;

            emptyState = new VisualElement();
            emptyState.AddToClassList("ci-empty");
            var emptyLabel = new Label(
                "No user scripts found in Assets/.\nToggle 'Include package scripts' in the Types menu, or add scripts to your project.");
            emptyLabel.AddToClassList("ci-empty-label");
            emptyState.Add(emptyLabel);
            graphArea.Add(emptyState);

            legend = BuildLegend();
            graphArea.Add(legend);

            sidePanel = new SidePanel();
            sidePanel.NavigateToNode += OnNavigateToNode;
            sidePanel.NavigateToSceneNode += OnNavigateToSceneNode;
            sidePanel.InspectedObjectChanged += OnInspectedObjectChanged;
            sidePanel.LiveChangesProvider = liveMonitor.ChangesFor;
            sidePanel.LiveEventsProvider = n => liveMonitor.EventsFor(n.Id);
            sidePanel.LiveContactsProvider = n => liveMonitor.ContactsFor(n.Id);
            sidePanel.TweaksProvider = TweaksForNode;
            sidePanel.TweakRevertRequested = OnTweakRevert;
            splitView.Add(sidePanel);
            sidePanel.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;

            var statusBar = new VisualElement();
            statusBar.AddToClassList("ci-statusbar");
            statusLabel = new Label();
            statusLabel.AddToClassList("ci-status-label");
            statusBar.Add(statusLabel);
            var diagButton = new Button(LogDiagnostics) { text = "Diagnostics" };
            statusBar.Add(diagButton);
            root.Add(statusBar);

            RebuildArchitecture();
            if (view == (int)View.Scene)
                RebuildScene();
            UpdateSceneToolbarVisibility();

            liveMonitor.UnityEventFired += OnLiveUnityEventFired;
            liveMonitor.NodePulse += id => ActiveCanvas?.PulseNode(id);
            liveMonitor.Contacted += OnLiveContact;
            liveMonitor.Diagnostic += d => Debug.LogWarning("CodeInspector: " + d);
            rootVisualElement.schedule.Execute(OnLiveTick).Every(200);
        }

        VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();

            archToggle = new ToolbarToggle { text = "Architecture", value = view == (int)View.Architecture };
            archToggle.RegisterValueChangedCallback(e => { if (e.newValue) SetView(View.Architecture); else archToggle.SetValueWithoutNotify(true); });
            toolbar.Add(archToggle);

            sceneToggle = new ToolbarToggle { text = "Scene", value = view == (int)View.Scene };
            sceneToggle.RegisterValueChangedCallback(e => { if (e.newValue) SetView(View.Scene); else sceneToggle.SetValueWithoutNotify(true); });
            toolbar.Add(sceneToggle);

            toolbar.Add(new ToolbarButton(Refresh) { text = "Refresh" });

            var edgesMenu = new ToolbarMenu { text = "Edges" };
            foreach (EdgeKind k in Enum.GetValues(typeof(EdgeKind)))
            {
                var kind = k;
                edgesMenu.menu.AppendAction(kind.ToString(),
                    _ => ToggleEdgeKind(kind),
                    _ => IsEdgeKindEnabled(kind) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(edgesMenu);

            var typesMenu = new ToolbarMenu { text = "Types" };
            foreach (ScriptCategory c in Enum.GetValues(typeof(ScriptCategory)))
            {
                var cat = c;
                typesMenu.menu.AppendAction(cat.ToString(),
                    _ => ToggleCategory(cat),
                    _ => IsCategoryEnabled(cat) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            typesMenu.menu.AppendSeparator();
            typesMenu.menu.AppendAction("Include package scripts",
                _ => { includePackages = !includePackages; RebuildArchitecture(); },
                _ => includePackages ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            typesMenu.menu.AppendAction("Include editor scripts",
                _ => { includeEditorScripts = !includeEditorScripts; RebuildArchitecture(); },
                _ => includeEditorScripts ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            toolbar.Add(typesMenu);

            autoRefreshToggle = new ToolbarToggle { text = "Auto refresh", value = sceneAutoRefresh };
            autoRefreshToggle.RegisterValueChangedCallback(e => sceneAutoRefresh = e.newValue);
            toolbar.Add(autoRefreshToggle);

            syncSelectionToggle = new ToolbarToggle { text = "Sync selection", value = sceneSyncSelection };
            syncSelectionToggle.RegisterValueChangedCallback(e => sceneSyncSelection = e.newValue);
            toolbar.Add(syncSelectionToggle);

            showAllToggle = new ToolbarToggle { text = "Show all", value = sceneShowAll };
            showAllToggle.RegisterValueChangedCallback(e =>
            {
                sceneShowAll = e.newValue;
                RebuildScene();
            });
            toolbar.Add(showAllToggle);

            var searchField = new ToolbarSearchField { value = search };
            searchField.style.width = 140f;
            searchField.RegisterValueChangedCallback(e =>
            {
                search = e.newValue;
                ApplyFiltersAndBind();
            });
            toolbar.Add(searchField);

            focusToggle = new ToolbarToggle { text = "Focus", value = focusOn };
            focusToggle.RegisterValueChangedCallback(e =>
            {
                focusOn = e.newValue;
                focusSlider.style.display = focusOn ? DisplayStyle.Flex : DisplayStyle.None;
                ApplyFiltersAndBind();
            });
            toolbar.Add(focusToggle);

            focusSlider = new SliderInt(1, 3) { value = focusDepth };
            focusSlider.style.width = 90f;
            focusSlider.style.display = focusOn ? DisplayStyle.Flex : DisplayStyle.None;
            focusSlider.RegisterValueChangedCallback(e =>
            {
                focusDepth = e.newValue;
                ApplyFiltersAndBind();
            });
            toolbar.Add(focusSlider);

            toolbar.Add(new ToolbarButton(() => ActiveCanvas?.FrameAll()) { text = "Frame" });
            toolbar.Add(new ToolbarButton(() => ActiveCanvas?.ResetLayout()) { text = "Reset layout" });

            var spacer = new VisualElement();
            spacer.AddToClassList("ci-toolbar-spacer");
            toolbar.Add(spacer);

            var panelToggle = new ToolbarToggle { text = "Panel", value = showPanel };
            panelToggle.RegisterValueChangedCallback(e =>
            {
                showPanel = e.newValue;
                if (sidePanel != null)
                    sidePanel.style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;
            });
            toolbar.Add(panelToggle);

            livePill = new Label("● LIVE");
            livePill.AddToClassList("ci-live-pill");
            livePill.style.display = DisplayStyle.None;
            toolbar.Add(livePill);

            tweaksBadge = new ToolbarButton(() =>
            {
                if (sceneCanvas != null)
                    view = (int)View.Scene;
            }) { text = "0 tweaks" };
            tweaksBadge.style.display = DisplayStyle.None;
            toolbar.Add(tweaksBadge);

            var optionsMenu = new ToolbarMenu { text = "⋮" };
            foreach (var (label, mode) in new[]
                     {
                         ("Play Mode tweaks: Ask", TweakLog.Preference.Ask),
                         ("Play Mode tweaks: Always discard", TweakLog.Preference.AlwaysDiscard),
                         ("Play Mode tweaks: Always keep", TweakLog.Preference.AlwaysKeep),
                     })
            {
                var m = mode;
                optionsMenu.menu.AppendAction(label,
                    _ => TweakLog.Mode = m,
                    _ => TweakLog.Mode == m
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(optionsMenu);

            return toolbar;
        }

        VisualElement BuildLegend()
        {
            var foldout = new Foldout { text = "Edge legend", value = false };
            foldout.AddToClassList("ci-legend");
            foreach (EdgeKind k in Enum.GetValues(typeof(EdgeKind)))
            {
                var row = new VisualElement();
                row.AddToClassList("ci-legend-row");
                var swatch = new VisualElement();
                swatch.AddToClassList("ci-legend-swatch");
                swatch.style.backgroundColor = EdgeColors.Get(k);
                row.Add(swatch);
                var label = new Label(k.ToString() + (k == EdgeKind.SendMessage ? " (dashed)" : ""));
                label.AddToClassList("ci-legend-label");
                row.Add(label);
                foldout.Add(row);
            }
            return foldout;
        }

        // ---- Filtering / binding ----------------------------------------------

        bool IsEdgeKindEnabled(EdgeKind k) => (edgeKindsMask & (1 << (int)k)) != 0;
        void ToggleEdgeKind(EdgeKind k)
        {
            edgeKindsMask ^= 1 << (int)k;
            ApplyFiltersAndBind();
        }

        bool IsCategoryEnabled(ScriptCategory c) => (categoriesMask & (1 << (int)c)) != 0;
        void ToggleCategory(ScriptCategory c)
        {
            categoriesMask ^= 1 << (int)c;
            ApplyFiltersAndBind();
        }

        GraphFilterSettings BuildFilterSettings(GraphCanvas canvas)
        {
            var s = GraphFilterSettings.CreateDefault();
            s.EnabledEdgeKinds.Clear();
            foreach (EdgeKind k in Enum.GetValues(typeof(EdgeKind)))
                if (IsEdgeKindEnabled(k))
                    s.EnabledEdgeKinds.Add(k);
            s.EnabledCategories.Clear();
            foreach (ScriptCategory c in Enum.GetValues(typeof(ScriptCategory)))
                if (IsCategoryEnabled(c))
                    s.EnabledCategories.Add(c);
            s.Search = search ?? "";
            s.FocusDepth = focusDepth;
            s.FocusNodeId = focusOn ? canvas?.SelectedNodeId : null;
            return s;
        }

        void SetView(View v)
        {
            view = (int)v;
            archToggle?.SetValueWithoutNotify(v == View.Architecture);
            sceneToggle?.SetValueWithoutNotify(v == View.Scene);
            if (archCanvas != null)
                archCanvas.style.display = v == View.Architecture ? DisplayStyle.Flex : DisplayStyle.None;
            if (sceneCanvas != null)
                sceneCanvas.style.display = v == View.Scene ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateSceneToolbarVisibility();
            if (v == View.Scene)
            {
                if (sceneGraph == null)
                    RebuildScene();
            }
            else
            {
                UpdateStatus();
            }
        }

        void UpdateSceneToolbarVisibility()
        {
            var sceneView = view == (int)View.Scene;
            var d = sceneView ? DisplayStyle.Flex : DisplayStyle.None;
            if (autoRefreshToggle != null) autoRefreshToggle.style.display = d;
            if (syncSelectionToggle != null) syncSelectionToggle.style.display = d;
            if (showAllToggle != null) showAllToggle.style.display = d;
        }

        void Refresh()
        {
            if (view == (int)View.Scene)
                RebuildScene();
            else
                RebuildArchitecture();
        }

        void RebuildArchitecture()
        {
            if (archCanvas == null)
                return;
            ArchitectureGraphCache.Options = new ScanOptions
            {
                IncludePackageAssemblies = includePackages,
                IncludeEditorAssemblies = includeEditorScripts,
            };
            ArchitectureGraphCache.Invalidate();
            typeGraph = ArchitectureGraphCache.Graph;
            archFiltered = GraphFilter.Apply(typeGraph, BuildFilterSettings(archCanvas));
            archCanvas.Bind(typeGraph, archFiltered);
            sceneGraph = null;
            if (view == (int)View.Scene)
                RebuildScene();
            else
                SyncSidePanelAndStatus();
        }

        void RebuildScene()
        {
            if (sceneCanvas == null || rebuilding)
                return;
            rebuilding = true;
            try
            {
                if (typeGraph == null)
                {
                    ArchitectureGraphCache.Options = new ScanOptions
                    {
                        IncludePackageAssemblies = includePackages,
                        IncludeEditorAssemblies = includeEditorScripts,
                    };
                    typeGraph = ArchitectureGraphCache.Graph;
                    archFiltered = GraphFilter.Apply(typeGraph, BuildFilterSettings(archCanvas));
                    archCanvas?.Bind(typeGraph, archFiltered);
                }
                var sceneKey = "Scene:" + CurrentSceneKey();
                sceneCanvas.ViewName = sceneKey;
                sceneGraph = SceneScanner.BuildForOpenScenes(new SceneScanOptions
                {
                    IsUserScript = t => typeGraph.Nodes.ContainsKey(CodeGraph.NodeIdFor(t)),
                    ShowAllGameObjects = sceneShowAll,
                });
                sceneFiltered = GraphFilter.Apply(sceneGraph, BuildFilterSettings(sceneCanvas));
                sceneCanvas.Bind(sceneGraph, sceneFiltered);
                if (EditorApplication.isPlaying)
                    liveMonitor.Attach(sceneGraph, sceneCanvas);
            }
            catch (Exception e)
            {
                Debug.LogWarning("CodeInspector: scene scan failed: " + e);
            }
            finally
            {
                rebuilding = false;
            }
            SyncSidePanelAndStatus();
        }

        static string CurrentSceneKey()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                return stage.assetPath ?? "prefab";
            var scene = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.path) ? "(unsaved)" : scene.path;
        }

        void ApplyFiltersAndBind()
        {
            if (typeGraph != null && archCanvas != null)
            {
                archFiltered = GraphFilter.Apply(typeGraph, BuildFilterSettings(archCanvas));
                archCanvas.Bind(typeGraph, archFiltered);
            }
            if (sceneGraph != null && sceneCanvas != null)
            {
                sceneFiltered = GraphFilter.Apply(sceneGraph, BuildFilterSettings(sceneCanvas));
                sceneCanvas.Bind(sceneGraph, sceneFiltered);
            }
            SyncSidePanelAndStatus();
        }

        void SyncSidePanelAndStatus()
        {
            sidePanel?.SetContext(ActiveGraph, ActiveFiltered, typeGraph, sceneGraph);
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusLabel == null)
                return;
            var f = ActiveFiltered;
            if (f != null)
            {
                statusLabel.text = $"{f.NodeIds.Count} nodes, {f.Edges.Count} edges shown " +
                                   $"(of {f.TotalNodes}/{f.TotalEdges})";
            }
            var g = ActiveGraph;
            if (g != null && g.Diagnostics.Count > 0)
                statusLabel.text += $" · {g.Diagnostics.Count} diagnostics";
            if (emptyState != null)
            {
                var showEmpty = f != null && f.NodeIds.Count == 0;
                emptyState.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void LogDiagnostics()
        {
            var g = ActiveGraph;
            if (g == null)
                return;
            foreach (var d in g.Diagnostics)
                Debug.LogWarning("CodeInspector: " + d);
        }

        void OnInspectedObjectChanged(Object target)
        {
            // Pulse the graph node that owns the edited object.
            sceneCanvas?.PulseObject(target);
            archCanvas?.PulseObject(target);
        }

        void OnNodeSelected(string nodeId)
        {
            sidePanel?.ShowNode(nodeId);
            if (view == (int)View.Scene && sceneSyncSelection && nodeId != null &&
                sceneGraph != null && sceneGraph.Nodes.TryGetValue(nodeId, out var node) &&
                node.Target != null)
            {
                suppressSelectionSync = true;
                try
                {
                    Selection.activeObject = node.Target;
                    EditorGUIUtility.PingObject(node.Target);
                }
                finally
                {
                    suppressSelectionSync = false;
                }
            }
        }

        void OnNodeActivated(GraphNode node)
        {
            if (view == (int)View.Scene)
            {
                if (node.Kind == NodeKind.GameObject && node.Target is GameObject)
                {
                    suppressSelectionSync = true;
                    try
                    {
                        Selection.activeObject = node.Target;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                    finally
                    {
                        suppressSelectionSync = false;
                    }
                }
                else if (node.Kind == NodeKind.Asset && node.Target != null)
                {
                    AssetDatabase.OpenAsset(node.Target);
                }
            }
            else if (node.Type != null)
            {
                ScriptLocator.Open(node.Type);
            }
        }

        void OnNavigateToNode(string nodeId)
        {
            var canvas = ActiveCanvas;
            if (canvas == null)
                return;
            canvas.Select(nodeId);
            canvas.FrameNode(nodeId);
        }

        void OnNavigateToSceneNode(string nodeId)
        {
            SetView(View.Scene);
            if (sceneGraph == null)
                RebuildScene();
            if (sceneCanvas != null)
            {
                sceneCanvas.Select(nodeId);
                sceneCanvas.FrameNode(nodeId);
            }
        }
    }
}
