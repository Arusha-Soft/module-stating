using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AS.Modules.Stating.Editor.Graph
{
    public sealed class StateGraphWindow : EditorWindow
    {
        private sealed class Navigation
        {
            internal UnityEngine.Object Asset;
            internal StateGraphData Graph;
            internal StateNodeData Parent;
            internal string Label;
        }
        [SerializeField] private UnityEngine.Object m_RootAsset;
        [SerializeField] private List<string> m_NavigationIds = new List<string>();
        [SerializeField] private bool m_Debug;
        [SerializeField] private bool m_Follow = true;
        [SerializeField] private StateMachineRunner m_Runner;
        private readonly List<Navigation> m_Frames = new List<Navigation>();
        private StateGraphInspector m_Inspector;
        private IMGUIContainer m_Blackboard;
        private IMGUIContainer m_InspectorGUI;
        private IMGUIContainer m_History;
        private VisualElement m_Breadcrumbs;
        private StateGraphSearch m_Search;
        private List<StateValidationIssue> m_Issues = new List<StateValidationIssue>();
        private string m_BlackboardSearch = "";
        private string m_GraphSearch = "";
        private string m_HistorySearch = "";
        private Vector2 m_BlackboardScroll;
        private Vector2 m_HistoryScroll;
        private double m_NextDebugRefresh;
        private string m_LastFocused;
        private object m_Selected;
        private bool m_RuntimeEditing;
        private bool m_RebuildQueued;
        private StateHistoryRecord m_SelectedRecord;
        internal StateGraphView View { get; private set; }
        public StateGraphData CurrentGraph => m_Frames.Count == 0 ? null : m_Frames[m_Frames.Count - 1].Graph;
        internal UnityEngine.Object EditingAsset => m_Frames.Count == 0 ? null : m_Frames[m_Frames.Count - 1].Asset;
        internal bool CanAuthor => CurrentGraph != null && !(m_Debug && EditorApplication.isPlaying);
        internal bool IsDefinitionRoot => m_Frames.Count == 1 && m_RootAsset is ReusableStateAsset;

        [MenuItem("Window/ArushaSoft/State Machine Graph")]
        public static void ShowWindow() => GetWindow<StateGraphWindow>("State Machine Graph");
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId);
            if (!(asset is StateMachineGraph) && !(asset is ReusableStateAsset)) return false;
            Open(asset); return true;
        }
        public static StateGraphWindow Open(UnityEngine.Object asset)
        {
            var window = GetWindow<StateGraphWindow>();
            window.m_RootAsset = asset; window.m_NavigationIds.Clear(); window.RestoreNavigation(); window.BuildUI(); window.Show(); window.Focus(); return window;
        }
        private void OnEnable()
        {
            titleContent = new GUIContent("State Machine Graph", EditorGUIUtility.IconContent("AnimatorController Icon").image);
            minSize = new Vector2(900, 520);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            RestoreNavigation(); BuildUI();
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnSelectionChanged;
            m_Inspector?.Dispose();
            if (m_Search != null) DestroyImmediate(m_Search);
        }
        private void RestoreNavigation()
        {
            m_Frames.Clear();
            if (m_RootAsset == null) return;
            StateGraphData root;
            if (m_RootAsset is StateMachineGraph graph) root = graph.Graph;
            else if (m_RootAsset is ReusableStateAsset reusable)
            {
                root = StateGraphData.Create(reusable.name);
                root.Nodes.Add(reusable.Definition);
                StateGraphAuthoring.Connect(root, root.Nodes[0], "out", reusable.Definition, "in", StateConnectionKind.Transition);
            }
            else return;
            m_Frames.Add(new Navigation { Asset = m_RootAsset, Graph = root, Label = m_RootAsset.name });
            var valid = new List<string>();
            foreach (string id in m_NavigationIds)
            {
                var node = CurrentGraph.FindNode(id);
                if (node?.EffectiveChildren == null) break;
                m_Frames.Add(new Navigation { Asset = ChildOwner(node), Graph = node.EffectiveChildren, Parent = node, Label = node.Name }); valid.Add(id);
            }
            m_NavigationIds = valid;
        }
        private void BuildUI()
        {
            m_LastFocused = null;
            m_Inspector?.Dispose(); m_Inspector = new StateGraphInspector(this); m_Selected = null;
            rootVisualElement.Clear();
            foreach (string guid in AssetDatabase.FindAssets("StateGraph t:StyleSheet"))
            {
                var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                if (style != null) { rootVisualElement.styleSheets.Add(style); break; }
            }
            rootVisualElement.AddToClassList("stating-window");
            rootVisualElement.Add(new IMGUIContainer(DrawToolbar) { name = "graph-toolbar" });
            m_Breadcrumbs = new VisualElement { name = "breadcrumbs" }; rootVisualElement.Add(m_Breadcrumbs);
            if (CurrentGraph == null)
            {
                var empty = new VisualElement { name = "empty-state" };
                empty.Add(new Label("State Machine Graph")); empty.Add(new Label("Create a graph asset, or double-click an existing graph in the Project window."));
                empty.Add(new Button(CreateGraphAsset) { text = "Create State Machine Graph" });
                empty.Add(new Button(StateGraphExamples.CreateExample) { text = "Create Three-State Example" }); rootVisualElement.Add(empty); return;
            }
            var horizontal = new TwoPaneSplitView(0, 280, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "Stating.HorizontalPanels", style = { flexGrow = 1 } };
            var left = new TwoPaneSplitView(0, 290, TwoPaneSplitViewOrientation.Vertical) { name = "left-panels", viewDataKey = "Stating.LeftPanels", style = { minWidth = 220 } };
            var blackboardPanel = new VisualElement { name = "blackboard-panel", style = { minHeight = 120 } };
            blackboardPanel.Add(new Label("Blackboard") { name = "panel-title" });
            m_Blackboard = new IMGUIContainer(DrawBlackboard); blackboardPanel.Add(m_Blackboard); left.Add(blackboardPanel);
            var inspectorPanel = new VisualElement { name = "inspector-panel", style = { minHeight = 120 } };
            inspectorPanel.Add(new Label("Graph Inspector") { name = "panel-title" });
            var inspectorScroll = new ScrollView(); m_InspectorGUI = new IMGUIContainer(() => m_Inspector.Draw(m_Selected, m_Issues)); inspectorScroll.Add(m_InspectorGUI); inspectorPanel.Add(inspectorScroll); left.Add(inspectorPanel);
            horizontal.Add(left);
            var workspace = new TwoPaneSplitView(1, 190, TwoPaneSplitViewOrientation.Vertical) { viewDataKey = "Stating.HistoryPanel", style = { minWidth = 400 } };
            View = new StateGraphView(this); workspace.Add(View);
            var historyPanel = new VisualElement { name = "history-panel", style = { minHeight = 50 } };
            historyPanel.Add(new Label("State History · Runtime") { name = "panel-title" }); m_History = new IMGUIContainer(DrawHistory); historyPanel.Add(m_History); workspace.Add(historyPanel);
            horizontal.Add(workspace); rootVisualElement.Add(horizontal);
            BuildBreadcrumbs(); Validate(); View.Populate();
        }
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(CurrentGraph == null))
                {
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44))) { EditorUtility.SetDirty(EditingAsset); AssetDatabase.SaveAssets(); }
                    if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(58))) SaveAs();
                    if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(66))) View?.FrameAll();
                    if (GUILayout.Button("Frame Selection", EditorStyles.toolbarButton, GUILayout.Width(96))) View?.FrameSelection();
                    if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(60))) { Validate(); Select(null); }
                }
                GUILayout.Space(8);
                bool wasDebug = m_Debug;
                m_Debug = GUILayout.Toggle(m_Debug, "Debug", EditorStyles.toolbarButton, GUILayout.Width(52));
                if (wasDebug && !m_Debug) View?.Highlight(new HashSet<string>(), null, false);
                m_Follow = GUILayout.Toggle(m_Follow, "Follow Active", EditorStyles.toolbarButton, GUILayout.Width(86));
                if (m_Debug)
                {
                    var runners = StateMachineRunner.Instances.Where(r => r != null && (m_RootAsset == null || !(m_RootAsset is StateMachineGraph root) || r.Graph == root)).OrderBy(r => r.GetInstanceID()).ToList();
                    var names = new List<string> { "Runtime Instance…" }; names.AddRange(runners.Select(r => r.name + " #" + r.GetInstanceID()));
                    int selected = m_Runner == null ? 0 : runners.IndexOf(m_Runner) + 1;
                    int next = EditorGUILayout.Popup(Math.Max(0, selected), names.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(180));
                    m_Runner = next > 0 && next <= runners.Count ? runners[next - 1] : null;
                    using (new EditorGUI.DisabledScope(m_Runner == null || !m_Runner.IsRunning))
                    {
                        if (GUILayout.Button(m_Runner != null && m_Runner.IsPaused ? "Resume" : "Pause", EditorStyles.toolbarButton, GUILayout.Width(54))) { if (m_Runner.IsPaused) m_Runner.Resume(); else m_Runner.Pause(); }
                        using (new EditorGUI.DisabledScope(m_Runner == null || !m_Runner.IsPaused))
                            if (GUILayout.Button(new GUIContent("Step", "One graph update; Unity physics and the Unity frame do not advance."), EditorStyles.toolbarButton, GUILayout.Width(38))) { m_Runner.Resume(); m_Runner.Tick(Time.fixedDeltaTime); m_Runner.Pause(); }
                    }
                }
                GUILayout.FlexibleSpace();
                m_GraphSearch = GUILayout.TextField(m_GraphSearch, EditorStyles.toolbarSearchField, GUILayout.Width(140));
                if (GUILayout.Button("Find", EditorStyles.toolbarButton, GUILayout.Width(38))) SearchGraph();
            }
        }
        private void BuildBreadcrumbs()
        {
            m_Breadcrumbs.Clear();
            for (int i = 0; i < m_Frames.Count; i++)
            {
                int index = i;
                m_Breadcrumbs.Add(new Button(() => BackTo(index)) { text = m_Frames[i].Label, tooltip = AssetDatabase.GetAssetPath(m_Frames[i].Asset) });
                if (i < m_Frames.Count - 1) m_Breadcrumbs.Add(new Label("›"));
            }
            m_Breadcrumbs.Add(new Label(EditingAsset is ReusableStateAsset || EditingAsset != m_RootAsset ? "Editing shared asset definition" : "Graph asset") { name = "ownership-label" });
        }
        private UnityEngine.Object ChildOwner(StateNodeData node) => node.Definition.IsSubGraphLinked ? (UnityEngine.Object)node.Definition.SubGraphAsset : node.IsLinked ? node.Template : EditingAsset;
        internal void Enter(StateNodeData node)
        {
            if (node.Kind != StateNodeKind.State) return;
            if ((node.IsLinked && node.Template == null) || (node.Definition.IsSubGraphLinked && node.Definition.SubGraphAsset == null)) { ShowNotification(new GUIContent("Reassign the missing shared asset before opening this tree.")); return; }
            if (node.EffectiveChildren == null)
            {
                if (!CanAuthor) return;
                Change("Create nested State Tree", () => node.Definition.Children = StateGraphData.Create(node.Name), false, node.IsLinked ? node.Template : EditingAsset);
            }
            if (m_Frames.Any(f => ReferenceEquals(f.Graph, node.EffectiveChildren))) { ShowNotification(new GUIContent("Circular hierarchy: remove the recursive asset reference.")); return; }
            m_NavigationIds.Add(node.Id);
            m_Frames.Add(new Navigation { Asset = ChildOwner(node), Graph = node.EffectiveChildren, Parent = node, Label = node.Name });
            BuildUI();
        }
        private void BackTo(int index)
        {
            if (index < 0 || index >= m_Frames.Count) return;
            m_Frames.RemoveRange(index + 1, m_Frames.Count - index - 1);
            if (m_NavigationIds.Count > index) m_NavigationIds.RemoveRange(index, m_NavigationIds.Count - index);
            BuildUI();
        }
        internal void Change(string name, Action action, bool rebuild = true, UnityEngine.Object owner = null)
        {
            if (!CanAuthor) return;
            var asset = owner != null ? owner : EditingAsset;
            Undo.RegisterCompleteObjectUndo(asset, name); action(); EditorUtility.SetDirty(asset);
            if (rebuild) QueueRebuild();
        }
        internal void Dirty() { if (EditingAsset != null) EditorUtility.SetDirty(EditingAsset); View?.RefreshTitles(); }
        internal void QueueRebuild()
        {
            if (m_RebuildQueued) return; m_RebuildQueued = true;
            rootVisualElement.schedule.Execute(() => { m_RebuildQueued = false; View?.Populate(); Validate(); m_Blackboard?.MarkDirtyRepaint(); m_InspectorGUI?.MarkDirtyRepaint(); });
        }
        private void OnUndoRedo()
        {
            StateMetadata.ClearCache(); RestoreNavigation(); BuildUI();
        }
        public void Select(object data)
        {
            m_Selected = data; m_Inspector?.SetSelection(data); m_InspectorGUI?.MarkDirtyRepaint();
        }
        internal void Validate()
        {
            m_Issues = CurrentGraph == null ? new List<StateValidationIssue>() : StateGraphValidator.Validate(CurrentGraph);
            View?.ShowIssues(m_Issues);
        }
        internal StateNodeData AddState(Type type, Vector2 position)
        {
            StateNodeData node = null; Change("Create state", () => node = StateGraphAuthoring.AddState(CurrentGraph, type, position)); return node;
        }
        internal StateNodeData AddNode(StateNodeKind kind, Vector2 position)
        {
            var node = new StateNodeData { Kind = kind, Name = ObjectNames.NicifyVariableName(kind.ToString()), Position = new Rect(position, new Vector2(240, 120)) };
            if (kind == StateNodeKind.Convert) node.StateTypeName = StateType.Id(typeof(int));
            if (kind == StateNodeKind.Log) node.Value = StateValue.From("Message");
            Change("Create node", () => CurrentGraph.Nodes.Add(node)); return node;
        }
        internal void AddVariableNode(StateVariable variable, StateNodeKind kind, Vector2 position)
        {
            var node = new StateNodeData { Kind = kind, Name = (kind == StateNodeKind.GetVariable ? "Get " : "Set ") + variable.Name, VariableId = variable.Id, Value = StateValue.Default(variable.DefaultValue.Type), Position = new Rect(position, new Vector2(220, 120)) };
            Change("Create variable node", () => CurrentGraph.Nodes.Add(node));
        }
        internal void AddCompareVariable(StateVariable variable, Vector2 position)
        {
            Change("Create variable comparison", () =>
            {
                var get = new StateNodeData { Kind = StateNodeKind.GetVariable, Name = "Get " + variable.Name, VariableId = variable.Id, Value = StateValue.Default(variable.DefaultValue.Type), Position = new Rect(position, new Vector2(200, 100)) };
                var compare = new StateNodeData { Kind = StateNodeKind.Compare, Name = "Compare " + variable.Name, Value = StateValue.Default(variable.DefaultValue.Type), Position = new Rect(position + new Vector2(260, 0), new Vector2(220, 140)) };
                CurrentGraph.Nodes.Add(get); CurrentGraph.Nodes.Add(compare); StateGraphAuthoring.Connect(CurrentGraph, get, "value", compare, "a", StateConnectionKind.Value);
            });
        }
        internal void AddFunction(StateNodeData target, StateMethod method, Vector2 position)
        {
            var node = new StateNodeData { Kind = StateNodeKind.Function, Name = method.Method.Name, TargetNodeId = target.Id, MethodId = method.Id, Position = new Rect(position, new Vector2(260, 160)) };
            foreach (var parameter in method.Parameters) node.Arguments.Add(parameter.HasDefaultValue ? StateValue.From(parameter.DefaultValue, parameter.ParameterType) : StateValue.Default(parameter.ParameterType));
            Change("Create function call", () => CurrentGraph.Nodes.Add(node));
        }
        internal void AddReusable(ReusableStateAsset asset, Vector2 position) => Change("Add reusable definition", () => StateGraphAuthoring.AddLinked(CurrentGraph, asset, position));
        internal void AddGraphReference(StateMachineGraph asset, Vector2 position) => Change("Add linked subgraph", () => { var node = StateGraphAuthoring.AddState(CurrentGraph, typeof(GraphState), position); node.Name = asset.name; node.IsSubGraphLinked = true; node.SubGraphAsset = asset; });
        internal void ShowSearch(Vector2 screenPosition, Vector2 graphPosition)
        {
            if (!CanAuthor || IsDefinitionRoot) return;
            if (m_Search == null) m_Search = CreateInstance<StateGraphSearch>(); m_Search.Window = this; m_Search.Position = graphPosition;
            SearchWindow.Open(new SearchWindowContext(screenPosition), m_Search);
        }
        internal IEnumerable<StateVariable> AvailableVariables()
        {
            var ids = new HashSet<string>();
            foreach (var variable in CurrentGraph.Variables) if (ids.Add(variable.Id)) yield return variable;
            foreach (var node in CurrentGraph.Nodes) foreach (var variable in node.Definition.Variables) if (ids.Add(variable.Id)) yield return variable;
            for (int i = m_Frames.Count - 1; i >= 0; i--)
            {
                if (m_Frames[i].Parent != null) foreach (var variable in m_Frames[i].Parent.Definition.Variables) if (variable.Exposed && ids.Add(variable.Id)) yield return variable;
                foreach (var variable in m_Frames[i].Graph.Variables) if (variable.Exposed && ids.Add(variable.Id)) yield return variable;
            }
        }
        private void DrawBlackboard()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                m_BlackboardSearch = GUILayout.TextField(m_BlackboardSearch, EditorStyles.toolbarSearchField);
                using (new EditorGUI.DisabledScope(!CanAuthor || IsDefinitionRoot))
                    if (GUILayout.Button("+", GUILayout.Width(26))) StateTypeSearch.Open(type => Change("Add Blackboard variable", () => CurrentGraph.Variables.Add(new StateVariable { Name = UniqueVariableName(type.Name), Scope = m_Frames.Count > 1 ? StateVariableScope.Tree : StateVariableScope.Graph, DefaultValue = StateValue.Default(type) })));
            }
            GUILayout.Label(CurrentGraph.Name, EditorStyles.miniLabel);
            m_BlackboardScroll = EditorGUILayout.BeginScrollView(m_BlackboardScroll, GUILayout.MaxHeight(550));
            foreach (var category in AvailableVariables().Where(v => string.IsNullOrEmpty(m_BlackboardSearch) || v.Name.IndexOf(m_BlackboardSearch, StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(v => v.Category))
            {
                GUILayout.Label(category.Key, EditorStyles.boldLabel);
                foreach (var variable in category)
                {
                    Rect rect = EditorGUILayout.GetControlRect(false, 24);
                    bool local = CurrentGraph.Variables.Contains(variable);
                    if (GUI.Button(rect, new GUIContent("●  " + variable.Name + "    " + (variable.DefaultValue.Type?.Name ?? "Missing type"), variable.Description), EditorStyles.toolbarButton)) Select(variable);
                    if (!local) GUI.Label(new Rect(rect.xMax - 24, rect.y + 4, 22, 20), "↥", EditorStyles.miniLabel);
                    Event evt = Event.current;
                    if (rect.Contains(evt.mousePosition) && evt.type == EventType.MouseDrag)
                    {
                        DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData("Stating.Variable", variable.Id); DragAndDrop.StartDrag(variable.Name); evt.Use();
                    }
                    if (rect.Contains(evt.mousePosition) && evt.type == EventType.ContextClick)
                    {
                        var captured = variable; var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("Get Variable"), false, () => AddVariableNode(captured, StateNodeKind.GetVariable, new Vector2(350, 180)));
                        menu.AddItem(new GUIContent("Set Variable"), false, () => AddVariableNode(captured, StateNodeKind.SetVariable, new Vector2(350, 180)));
                        menu.AddItem(new GUIContent("Compare Variable"), false, () => AddCompareVariable(captured, new Vector2(350, 180)));
                        if (local && CanAuthor)
                        {
                            menu.AddItem(new GUIContent("Duplicate"), false, () => Change("Duplicate variable", () => { var copy = JsonUtility.FromJson<StateVariable>(JsonUtility.ToJson(captured)); copy.Id = Guid.NewGuid().ToString("N"); copy.Name = UniqueVariableName(captured.Name); CurrentGraph.Variables.Add(copy); }));
                            menu.AddItem(new GUIContent("Move Up"), false, () => Change("Reorder variable", () => { int index = CurrentGraph.Variables.IndexOf(captured); if (index > 0) { CurrentGraph.Variables.RemoveAt(index); CurrentGraph.Variables.Insert(index - 1, captured); } }));
                            menu.AddItem(new GUIContent("Move Down"), false, () => Change("Reorder variable", () => { int index = CurrentGraph.Variables.IndexOf(captured); if (index < CurrentGraph.Variables.Count - 1) { CurrentGraph.Variables.RemoveAt(index); CurrentGraph.Variables.Insert(index + 1, captured); } }));
                            menu.AddItem(new GUIContent("Delete"), false, () => Change("Delete variable", () => CurrentGraph.Variables.Remove(captured)));
                        }
                        menu.ShowAsContext(); evt.Use();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            if (m_Debug && m_Runner != null && m_Runner.IsRunning) DrawRuntimeBlackboard();
        }
        private string UniqueVariableName(string name)
        {
            string result = name; int index = 1; while (CurrentGraph.Variables.Exists(v => v.Name == result)) result = name + " " + index++; return result;
        }
        private string RuntimePath()
        {
            string path = m_Frames[0].Graph.Id;
            for (int i = 1; i < m_Frames.Count; i++) path += "/" + m_Frames[i].Parent.Id + "/" + m_Frames[i].Graph.Id;
            return path;
        }
        private void DrawRuntimeBlackboard()
        {
            GUILayout.Space(8); GUILayout.Label("Runtime Watch", EditorStyles.boldLabel);
            m_RuntimeEditing = EditorGUILayout.Toggle("Edit runtime values", m_RuntimeEditing);
            var board = m_Runner.FindBlackboard(RuntimePath()); if (board == null) return;
            using (new EditorGUI.DisabledScope(!m_RuntimeEditing))
                foreach (var variable in board.Definitions)
                {
                    var value = StateValue.From(StateType.Clone(board.Get(variable.Id)), variable.DefaultValue.Type);
                    EditorGUI.BeginChangeCheck(); StateValueGUI.Draw(variable.Name, value);
                    if (EditorGUI.EndChangeCheck()) board.Set(variable.Id, value.Get());
                }
        }
        private void DrawHistory()
        {
            if (!m_Debug || m_Runner == null) { GUILayout.Label("Enable Debug and select a running instance to inspect active states and recorded transitions.", EditorStyles.wordWrappedMiniLabel); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label((m_Runner.IsPaused ? "Paused  ·  " : m_Runner.IsRunning ? "Running  ·  " : "Stopped  ·  ") + m_Runner.ActivePath, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace(); m_HistorySearch = GUILayout.TextField(m_HistorySearch, EditorStyles.toolbarSearchField, GUILayout.Width(180));
                m_Runner.History.Recording = GUILayout.Toggle(m_Runner.History.Recording, "Record", EditorStyles.miniButton, GUILayout.Width(58));
                m_Runner.History.Snapshots = GUILayout.Toggle(m_Runner.History.Snapshots, "Snapshots", EditorStyles.miniButton, GUILayout.Width(75));
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(45))) { m_Runner.History.Clear(); m_SelectedRecord = null; }
            }
            m_HistoryScroll = EditorGUILayout.BeginScrollView(m_HistoryScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(320));
            for (int i = 0; i < m_Runner.History.Count; i++)
            {
                var record = m_Runner.History[i];
                var node = CurrentGraph.FindNode(record.NodeId); string name = node?.Name ?? record.Path;
                string text = $"{record.Timestamp,7:0.000}s  {record.Kind,-10} {name}   {record.Duration:0.000}s  ·  {record.Reason}";
                if (!string.IsNullOrEmpty(m_HistorySearch) && text.IndexOf(m_HistorySearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(text, EditorStyles.label)) { m_SelectedRecord = record; FocusGraphNode(record.GraphId, record.NodeId, record.Path); }
            }
            if (m_SelectedRecord?.Blackboard != null)
            {
                GUILayout.Label("Recorded Blackboard snapshot", EditorStyles.boldLabel);
                foreach (var pair in m_SelectedRecord.Blackboard) GUILayout.Label(pair.Key + " = " + (pair.Value ?? "null"), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
        private void OnSelectionChanged()
        {
            if (Selection.activeGameObject == null) return;
            var runner = Selection.activeGameObject.GetComponent<StateMachineRunner>();
            if (runner != null && (m_RootAsset == runner.Graph || m_RootAsset == null)) { m_Runner = runner; Repaint(); }
        }
        private void OnEditorUpdate()
        {
            if (!m_Debug || View == null || EditorApplication.timeSinceStartup < m_NextDebugRefresh) return;
            m_NextDebugRefresh = EditorApplication.timeSinceStartup + 0.1;
            if (m_Runner != null)
            {
                string prefix = RuntimePath() + "/";
                var active = new HashSet<string>(m_Runner.ActiveStatePaths.Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && path.IndexOf('/', prefix.Length) < 0).Select(path => path.Substring(prefix.Length)));
                string recentEdge = null;
                for (int i = m_Runner.History.Count - 1; i >= 0; i--) if (m_Runner.History[i].Kind == StateHistoryKind.Transition) { if (m_Runner.ElapsedTime - m_Runner.History[i].Timestamp < 0.7) recentEdge = m_Runner.History[i].TransitionId; break; }
                View.Highlight(active, recentEdge, m_Runner.IsPaused);
                if (m_Follow)
                {
                    string visible = CurrentGraph.Nodes.LastOrDefault(n => active.Contains(n.Id))?.Id;
                    if (visible != null && visible != m_LastFocused) { View.FocusNode(visible); m_LastFocused = visible; }
                    if (visible == null) m_LastFocused = null;
                }
            }
            else View.Highlight(new HashSet<string>(), null, false);
            Repaint(); m_History?.MarkDirtyRepaint(); m_Blackboard?.MarkDirtyRepaint();
        }
        private void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Graph As", m_RootAsset.name + " Copy", "asset", "Create an independent graph asset.");
            if (string.IsNullOrEmpty(path)) return;
            var copy = Instantiate(m_RootAsset); AssetDatabase.CreateAsset(copy, path); AssetDatabase.SaveAssets(); Open(copy);
        }
        private void CreateGraphAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create State Machine", "State Machine", "asset", "Choose the graph asset location.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<StateMachineGraph>(); AssetDatabase.CreateAsset(asset, path); AssetDatabase.SaveAssets(); Open(asset);
        }
        internal void SaveReusable(StateNodeData node, bool tree)
        {
            string path = EditorUtility.SaveFilePanelInProject(tree ? "Save State Tree as Asset" : "Save State as Reusable Asset", node.Name, "asset", "Exposed external defaults become state parameters. Host transitions stay in the host graph.");
            if (string.IsNullOrEmpty(path)) return;
            try { var asset = StateGraphAuthoring.SaveReusable(CurrentGraph, node, path, tree); EditorGUIUtility.PingObject(asset); }
            catch (Exception exception) { ShowNotification(new GUIContent("Extraction failed. See Unity Console.")); Debug.LogError(exception.Message); }
        }
        private void SearchGraph()
        {
            var menu = new GenericMenu(); int count = 0;
            foreach (var item in FindGraphs(m_Frames[0].Graph, new HashSet<StateGraphData>()))
                foreach (var node in item.Nodes)
                    if (string.IsNullOrEmpty(m_GraphSearch) || (node.Name + " " + node.EffectiveStateType + " " + node.MethodId).IndexOf(m_GraphSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string graphId = item.Id, nodeId = node.Id;
                        menu.AddItem(new GUIContent(item.Name + "/" + node.Name + " [" + node.Id.Substring(0, Math.Min(5, node.Id.Length)) + "]"), false, () => FocusGraphNode(graphId, nodeId)); count++;
                    }
            if (count == 0) menu.AddDisabledItem(new GUIContent("No matching nodes")); menu.ShowAsContext();
        }
        private static IEnumerable<StateGraphData> FindGraphs(StateGraphData graph, HashSet<StateGraphData> visited)
        {
            if (graph == null || !visited.Add(graph)) yield break;
            yield return graph;
            foreach (var node in graph.Nodes) foreach (var child in FindGraphs(node.EffectiveChildren, visited)) yield return child;
        }
        internal void FocusGraphNode(string graphId, string nodeId, string runtimePath = null)
        {
            bool Find(StateGraphData graph, List<StateNodeData> route, HashSet<StateGraphData> visited)
            {
                if (!visited.Add(graph)) return false;
                if (graph.Id == graphId) return true;
                foreach (var node in graph.Nodes)
                    if (node.EffectiveChildren != null) { route.Add(node); if (Find(node.EffectiveChildren, route, visited)) return true; route.RemoveAt(route.Count - 1); }
                return false;
            }
            var path = new List<StateNodeData>();
            bool exact = false;
            if (!string.IsNullOrEmpty(runtimePath))
            {
                string[] segments = runtimePath.Split('/'); var graph = m_Frames[0].Graph;
                if (segments[0] == graph.Id)
                {
                    for (int i = 1; i + 1 < segments.Length; i += 2)
                    {
                        var parent = graph.FindNode(segments[i]);
                        if (parent?.EffectiveChildren == null || parent.EffectiveChildren.Id != segments[i + 1]) break;
                        path.Add(parent); graph = parent.EffectiveChildren;
                    }
                    exact = graph.Id == graphId;
                }
            }
            if (!exact) { path.Clear(); if (!Find(m_Frames[0].Graph, path, new HashSet<StateGraphData>())) return; }
            m_NavigationIds = path.Select(n => n.Id).ToList(); RestoreNavigation(); BuildUI(); View?.schedule.Execute(() => View.FocusNode(nodeId));
        }
    }
}
