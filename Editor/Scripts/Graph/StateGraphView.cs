using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AS.Modules.Stating.Editor.Graph
{
    internal sealed class StateGraphView : GraphView
    {
        private const string ClipboardPrefix = "AS.Stating.Graph.v1\n";
        internal readonly StateGraphWindow Window;
        private readonly Dictionary<string, StateGraphNodeView> m_Nodes = new Dictionary<string, StateGraphNodeView>();
        private readonly Dictionary<string, StateGraphEdgeView> m_Edges = new Dictionary<string, StateGraphEdgeView>();
        private bool m_Loading;
        internal StateGraphView(StateGraphWindow window)
        {
            Window = window; name = "state-graph"; style.flexGrow = 1; style.minHeight = 200;
            SetupZoom(0.15f, 2f);
            Insert(0, new GridBackground());
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger()); this.AddManipulator(new RectangleSelector());
            graphViewChanged = OnGraphChanged;
            nodeCreationRequest = context => Window.ShowSearch(context.screenMousePosition, contentViewContainer.WorldToLocal(context.screenMousePosition - Window.position.position));
            viewTransformChanged = view =>
            {
                if (m_Loading || Window.CurrentGraph == null) return;
                Window.CurrentGraph.ViewPosition = viewTransform.position; Window.CurrentGraph.ViewScale = viewTransform.scale; Window.Dirty();
            };
            groupTitleChanged = (group, title) => { if (!m_Loading) Window.Change("Rename group", () => ((StateGroupData)group.userData).Title = title, false); };
            elementsAddedToGroup = (group, elements) =>
            {
                if (m_Loading) return;
                Window.Change("Group nodes", () => { var data = (StateGroupData)group.userData; foreach (var node in elements.OfType<StateGraphNodeView>()) if (!data.NodeIds.Contains(node.Data.Id)) data.NodeIds.Add(node.Data.Id); }, false);
            };
            elementsRemovedFromGroup = (group, elements) =>
            {
                if (m_Loading) return;
                Window.Change("Ungroup nodes", () => { var data = (StateGroupData)group.userData; foreach (var node in elements.OfType<StateGraphNodeView>()) data.NodeIds.Remove(node.Data.Id); }, false);
            };
            elementResized = element => { if (!m_Loading) SavePositions(); };
            serializeGraphElements = Copy;
            canPasteSerializedData = data => data != null && data.StartsWith(ClipboardPrefix, StringComparison.Ordinal);
            unserializeAndPaste = (operation, data) => Paste(data);
            RegisterCallback<MouseUpEvent>(evt => { if (selection.Count == 0) Window.Select(null); });
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.F) { FrameSelection(); evt.StopPropagation(); }
                if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.D) { Paste(Copy(selection.OfType<GraphElement>())); evt.StopPropagation(); }
            });
        }
        internal void Populate()
        {
            m_Loading = true;
            try
            {
                DeleteElements(graphElements.ToList()); m_Nodes.Clear(); m_Edges.Clear();
                foreach (var data in Window.CurrentGraph.Nodes)
                {
                    var view = new StateGraphNodeView(this, data); m_Nodes[data.Id] = view; AddElement(view);
                }
                foreach (var data in Window.CurrentGraph.Connections)
                {
                    if (!m_Nodes.TryGetValue(data.SourceNodeId ?? "", out var source) || !m_Nodes.TryGetValue(data.TargetNodeId ?? "", out var target)) continue;
                    var output = source.FindPort(data.SourcePort, false); var input = target.FindPort(data.TargetPort, true);
                    // Invalid connections stay in the asset and appear in validation until repaired.
                    if (output == null || input == null) continue;
                    var edge = new StateGraphEdgeView(Window, data) { output = output, input = input };
                    output.Connect(edge); input.Connect(edge); m_Edges[data.Id] = edge; AddElement(edge);
                }
                foreach (var data in Window.CurrentGraph.Groups)
                {
                    var group = new Group { title = data.Title, userData = data, viewDataKey = data.Id };
                    group.style.backgroundColor = data.Color; group.SetPosition(data.Position); AddElement(group);
                    foreach (string id in data.NodeIds) if (m_Nodes.TryGetValue(id, out var node)) group.AddElement(node);
                    group.style.minWidth = data.Position.width; group.style.minHeight = data.Position.height;
                    group.hierarchy.Add(new StateElementResizeHandle(group, rect => Window.Change("Resize group", () => data.Position = rect, false)));
                    group.RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0) Window.Select(data); });
                }
                foreach (var data in Window.CurrentGraph.Notes) AddElement(new StateNoteView(Window, data));
                UpdateViewTransform(Window.CurrentGraph.ViewPosition, Window.CurrentGraph.ViewScale == Vector3.zero ? Vector3.one : Window.CurrentGraph.ViewScale);
                if (Window.IsDefinitionRoot) foreach (var edge in edges) edge.capabilities &= ~Capabilities.Deletable;
            }
            finally { m_Loading = false; }
        }
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            var start = (StatePort)startPort.userData;
            return ports.Where(port =>
            {
                if (port == startPort || !(port.userData is StatePort candidate) || candidate.Input == start.Input) return false;
                var output = start.Input ? candidate : start; var input = start.Input ? start : candidate;
                return StateGraphPorts.Compatible(output, input) && (port.node != startPort.node || start.Kind == StateConnectionKind.Transition);
            }).ToList();
        }
        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            if (m_Loading) return change;
            if (!Window.CanAuthor || Window.IsDefinitionRoot) { change.edgesToCreate?.Clear(); change.elementsToRemove?.Clear(); return change; }
            Window.Change("Edit graph", () =>
            {
                if (change.edgesToCreate != null)
                {
                    foreach (var edge in change.edgesToCreate)
                    {
                        var source = (StateGraphNodeView)edge.output.node; var target = (StateGraphNodeView)edge.input.node;
                        var output = (StatePort)edge.output.userData; var input = (StatePort)edge.input.userData;
                        var data = StateGraphAuthoring.Connect(Window.CurrentGraph, source.Data, output.Id, target.Data, input.Id, output.Kind);
                        edge.userData = data;
                        if (source.Data.Id == target.Data.Id) data.Transition.AllowSelf = true;
                        edge.RegisterCallback<MouseDownEvent>(evt => Window.Select(data));
                    }
                }
                if (change.elementsToRemove != null)
                {
                    foreach (var element in change.elementsToRemove)
                    {
                        if (element is StateGraphNodeView node) StateGraphAuthoring.RemoveNode(Window.CurrentGraph, node.Data.Id);
                        else if (element is Edge edge && edge.userData is StateConnection connection) Window.CurrentGraph.Connections.RemoveAll(e => e.Id == connection.Id);
                        else if (element.userData is StateGroupData group) Window.CurrentGraph.Groups.Remove(group);
                        else if (element.userData is StateNoteData note) Window.CurrentGraph.Notes.Remove(note);
                    }
                }
                if (change.movedElements != null) StorePositions();
            }, false);
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0) Window.QueueRebuild();
            Window.Validate(); return change;
        }
        private void StorePositions()
        {
            foreach (var node in m_Nodes.Values) node.Data.Position = node.GetPosition();
            foreach (var element in graphElements)
            {
                if (element.userData is StateGroupData group) group.Position = element.GetPosition();
                if (element.userData is StateNoteData note) note.Position = element.GetPosition();
            }
        }
        private void SavePositions() => Window.Change("Resize graph element", StorePositions, false);
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 position = contentViewContainer.WorldToLocal(evt.mousePosition);
            if (Window.CanAuthor && !Window.IsDefinitionRoot)
            {
                evt.menu.AppendAction("Create Node…", action => Window.ShowSearch(evt.mousePosition + Window.position.position, position));
                evt.menu.AppendAction("Create Group", action => CreateGroup(position));
                evt.menu.AppendAction("Create Sticky Note", action => CreateNote(position));
                evt.menu.AppendAction("Create Sub-State Machine from Selection", action =>
                {
                    try { Window.Change("Nest selection", () => StateGraphAuthoring.NestSelection(Window.CurrentGraph, new HashSet<string>(selection.OfType<StateGraphNodeView>().Select(n => n.Data.Id)))); }
                    catch (Exception exception) { Window.ShowNotification(new GUIContent(exception.Message)); }
                });
                evt.menu.AppendAction("Align Selection Horizontally", action => Align(true));
                evt.menu.AppendAction("Align Selection Vertically", action => Align(false));
            }
            evt.menu.AppendAction("Frame All", action => FrameAll());
            evt.menu.AppendAction("Frame Selection", action => FrameSelection());
            base.BuildContextualMenu(evt);
        }
        private void Align(bool horizontal)
        {
            var nodes = selection.OfType<StateGraphNodeView>().ToList(); if (nodes.Count < 2) return;
            Window.Change("Align nodes", () =>
            {
                float coordinate = horizontal ? nodes.Average(n => n.Data.Position.y) : nodes.Average(n => n.Data.Position.x);
                foreach (var node in nodes) { Rect rect = node.Data.Position; if (horizontal) rect.y = coordinate; else rect.x = coordinate; node.Data.Position = rect; }
            });
        }
        internal void CreateGroup(Vector2 position)
        {
            Window.Change("Create group", () => Window.CurrentGraph.Groups.Add(new StateGroupData { Position = new Rect(position, new Vector2(400, 300)), NodeIds = selection.OfType<StateGraphNodeView>().Select(n => n.Data.Id).ToList() }));
        }
        internal void CreateNote(Vector2 position) => Window.Change("Create note", () => Window.CurrentGraph.Notes.Add(new StateNoteData { Position = new Rect(position, new Vector2(240, 160)) }));
        private string Copy(IEnumerable<GraphElement> elements)
        {
            var list = elements.ToList();
            var ids = new HashSet<string>(list.OfType<StateGraphNodeView>().Select(n => n.Data.Id));
            foreach (var element in list) if (element.userData is StateGroupData group) foreach (string id in group.NodeIds) ids.Add(id);
            var payload = StateGraphCopy.Selection(Window.CurrentGraph, ids, Vector2.zero);
            foreach (var element in list) if (element.userData is StateNoteData note) payload.Notes.Add(JsonUtility.FromJson<StateNoteData>(JsonUtility.ToJson(note)));
            return ClipboardPrefix + JsonUtility.ToJson(payload);
        }
        private void Paste(string data)
        {
            if (!Window.CanAuthor || Window.IsDefinitionRoot || !canPasteSerializedData(data)) return;
            try
            {
                var source = JsonUtility.FromJson<StateGraphData>(data.Substring(ClipboardPrefix.Length));
                var copy = StateGraphCopy.Selection(source, new HashSet<string>(source.Nodes.Select(n => n.Id)), new Vector2(35, 35));
                foreach (var note in source.Notes) { note.Id = Guid.NewGuid().ToString("N"); note.Position.position += new Vector2(35, 35); copy.Notes.Add(note); }
                Window.Change("Paste graph elements", () => { Window.CurrentGraph.Nodes.AddRange(copy.Nodes); Window.CurrentGraph.Connections.AddRange(copy.Connections); Window.CurrentGraph.Groups.AddRange(copy.Groups); Window.CurrentGraph.Notes.AddRange(copy.Notes); });
            }
            catch (Exception exception) { Debug.LogWarning("Cannot paste Stating graph: " + exception.Message); }
        }
        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (!Window.CanAuthor || Window.IsDefinitionRoot) return;
            if (DragAndDrop.GetGenericData("Stating.Variable") is string || DragAndDrop.objectReferences.Any(o => o is ReusableStateAsset || o is MonoScript || o is StateMachineGraph)) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!Window.CanAuthor || Window.IsDefinitionRoot) return;
            Vector2 position = contentViewContainer.WorldToLocal(evt.mousePosition);
            if (DragAndDrop.GetGenericData("Stating.Variable") is string id)
            {
                var variable = Window.AvailableVariables().FirstOrDefault(v => v.Id == id); if (variable != null) Window.AddVariableNode(variable, StateNodeKind.GetVariable, position);
                DragAndDrop.SetGenericData("Stating.Variable", null);
            }
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is ReusableStateAsset reusable) Window.AddReusable(reusable, position);
                else if (obj is MonoScript script && script.GetClass() is Type type && typeof(AS.Modules.Stating.Core.IGraphState).IsAssignableFrom(type) && !type.IsAbstract) Window.AddState(type, position);
                else if (obj is StateMachineGraph graph) Window.AddGraphReference(graph, position);
                position += new Vector2(30, 30);
            }
            DragAndDrop.AcceptDrag(); evt.StopPropagation();
        }
        internal void FocusNode(string id)
        {
            if (!m_Nodes.TryGetValue(id ?? "", out var node)) return;
            ClearSelection(); AddToSelection(node); Window.Select(node.Data); FrameSelection();
        }
        internal void RefreshTitles()
        {
            foreach (var node in m_Nodes.Values) { node.title = node.Data.Name + (node.Data.IsLinked ? " ↗" : ""); if (node.Data.Kind == StateNodeKind.State) node.titleContainer.style.backgroundColor = node.Data.Color; }
            foreach (var element in graphElements) if (element is Group group && group.userData is StateGroupData data) { group.title = data.Title; group.style.backgroundColor = data.Color; }
        }
        internal void Highlight(HashSet<string> active, string transition, bool paused)
        {
            foreach (var pair in m_Nodes) { pair.Value.EnableInClassList("runtime-active", active.Contains(pair.Key)); pair.Value.EnableInClassList("runtime-paused", paused && active.Contains(pair.Key)); }
            foreach (var pair in m_Edges) pair.Value.EnableInClassList("runtime-transition", pair.Key == transition);
        }
        internal void ShowIssues(List<StateValidationIssue> issues)
        {
            foreach (var pair in m_Nodes)
            {
                var nodeIssues = issues.Where(i => i.GraphId == Window.CurrentGraph.Id && i.NodeId == pair.Key).ToList();
                pair.Value.EnableInClassList("invalid-node", nodeIssues.Any(i => i.Severity == StateValidationSeverity.Error));
                pair.Value.tooltip = nodeIssues.Count == 0 ? pair.Value.Data.Description : string.Join("\n", nodeIssues.Select(i => i.Message));
            }
        }
    }

    internal sealed class StateGraphNodeView : Node
    {
        internal readonly StateNodeData Data;
        private readonly StateGraphView m_Graph;
        private readonly Dictionary<string, Port> m_Ports = new Dictionary<string, Port>();
        internal StateGraphNodeView(StateGraphView graph, StateNodeData data)
        {
            m_Graph = graph; Data = data; userData = data; viewDataKey = data.Id;
            title = data.Name + (data.IsLinked ? " ↗" : ""); AddToClassList("stating-node"); AddToClassList("kind-" + data.Kind.ToString().ToLowerInvariant());
            titleContainer.style.backgroundColor = data.Kind == StateNodeKind.State ? data.Color : new Color(0.22f, 0.22f, 0.22f);
            if (data.Icon != null) titleContainer.Insert(0, new Image { image = data.Icon, style = { width = 18, height = 18, marginLeft = 6, alignSelf = Align.Center } });
            var subtitle = new Label(data.Kind == StateNodeKind.State ? StateType.Resolve(data.EffectiveStateType)?.Name ?? "Missing State type" : data.Kind.ToString()) { name = "node-subtitle" }; extensionContainer.Add(subtitle);
            var fields = new Foldout { text = "Fields", value = data.FieldsExpanded };
            var lifecycle = new Foldout { text = "Lifecycle", value = data.LifecycleExpanded };
            fields.RegisterValueChangedCallback(evt => graph.Window.Change("Toggle field section", () => data.FieldsExpanded = evt.newValue, false));
            lifecycle.RegisterValueChangedCallback(evt => graph.Window.Change("Toggle lifecycle section", () => data.LifecycleExpanded = evt.newValue, false));
            foreach (var info in StateGraphPorts.For(graph.Window.CurrentGraph, data, id => graph.Window.AvailableVariables().FirstOrDefault(v => v.Id == id)))
            {
                var port = InstantiatePort(Orientation.Horizontal, info.Input ? Direction.Input : Direction.Output, info.Kind == StateConnectionKind.Value && info.Input ? Port.Capacity.Single : Port.Capacity.Multi, info.Type ?? typeof(object));
                port.portName = info.Name; port.userData = info; port.tooltip = info.Kind + " · " + (info.Type?.Name ?? "Missing type");
                port.portColor = PortColor(info); m_Ports[(info.Input ? "in:" : "out:") + info.Id] = port;
                if (data.Kind == StateNodeKind.State && info.Kind == StateConnectionKind.Value) fields.Add(port);
                else if (data.Kind == StateNodeKind.State && info.Kind == StateConnectionKind.Execution) lifecycle.Add(port);
                else if (info.Input) inputContainer.Add(port); else outputContainer.Add(port);
            }
            if (data.Kind == StateNodeKind.State)
            {
                extensionContainer.Add(lifecycle); extensionContainer.Add(fields);
                var functions = new Foldout { text = "Functions", value = data.FunctionsExpanded };
                functions.RegisterValueChangedCallback(evt => graph.Window.Change("Toggle function section", () => data.FunctionsExpanded = evt.newValue, false));
                var metadata = StateMetadata.For(StateType.Resolve(data.EffectiveStateType));
                if (metadata != null)
                    foreach (var method in metadata.Methods.Values)
                    {
                        var captured = method;
                        functions.Add(new Button(() => graph.Window.AddFunction(data, captured, data.Position.position + new Vector2(320, 80))) { text = method.Method.Name + "(" + string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name)) + ")", tooltip = method.Pure ? "Pure result can be used as a condition" : "Runs only through an execution connection" });
                    }
                extensionContainer.Add(functions);
                if (data.EffectiveChildren != null) extensionContainer.Add(new Button(() => graph.Window.Enter(data)) { text = "Open State Tree  ›" });
                RegisterCallback<MouseDownEvent>(evt => { if (evt.button == 0 && evt.clickCount == 2) { graph.Window.Enter(data); evt.StopPropagation(); } });
            }
            else if (data.Kind == StateNodeKind.Constant) extensionContainer.Add(new Label(data.Value.Type == null ? "Missing value type" : Convert.ToString(data.Value.Get())) { name = "constant-preview" });
            if (data.IsLinked) extensionContainer.Add(new Label("Linked asset · local overrides") { name = "reference-label" });
            expanded = data.Expanded; RefreshExpandedState(); RefreshPorts(); SetPosition(data.Position);
            StateGraphEditorExtensions.Configure(this, data);
            RegisterCallback<GeometryChangedEvent>(evt => { if (expanded != data.Expanded) graph.Window.Change("Collapse node", () => data.Expanded = expanded, false); });
            if (graph.Window.IsDefinitionRoot) capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable);
        }
        internal Port FindPort(string id, bool input) => m_Ports.TryGetValue((input ? "in:" : "out:") + id, out var port) ? port : null;
        private static Color PortColor(StatePort port)
        {
            if (port.Kind == StateConnectionKind.Transition) return new Color(0.45f, 0.9f, 0.92f);
            if (port.Kind == StateConnectionKind.Execution) return new Color(1, 0.73f, 0.35f);
            if (port.Type == typeof(bool)) return new Color(0.85f, 0.4f, 0.65f);
            if (port.Type == typeof(int)) return new Color(0.5f, 0.8f, 0.9f);
            if (port.Type == typeof(float) || port.Type == typeof(double)) return new Color(0.65f, 0.85f, 0.45f);
            return new Color(0.65f, 0.65f, 1);
        }
        public override void OnSelected() { base.OnSelected(); m_Graph.Window.Select(Data); }
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            if (Data.Kind != StateNodeKind.State || !m_Graph.Window.CanAuthor) return;
            evt.menu.AppendAction("Open State Tree", action => m_Graph.Window.Enter(Data));
            evt.menu.AppendAction("Save State as Reusable Asset", action => m_Graph.Window.SaveReusable(Data, false));
            evt.menu.AppendAction("Save State Tree as Asset", action => m_Graph.Window.SaveReusable(Data, true));
            if (Data.IsLinked || Data.IsSubGraphLinked)
            {
                evt.menu.AppendAction("Edit Shared Definition", action => StateGraphWindow.Open(Data.IsLinked ? (UnityEngine.Object)Data.Template : Data.SubGraphAsset));
                evt.menu.AppendAction("Duplicate as Independent Copy", action => m_Graph.Window.Change("Create independent state copy", () => { var copy = StateGraphCopy.Independent(Data); copy.Id = Guid.NewGuid().ToString("N"); copy.Position.position += new Vector2(50, 50); m_Graph.Window.CurrentGraph.Nodes.Add(copy); }));
                evt.menu.AppendAction("Make This Reference Independent", action => m_Graph.Window.Change("Detach reusable state", () => { int index = m_Graph.Window.CurrentGraph.Nodes.IndexOf(Data); m_Graph.Window.CurrentGraph.Nodes[index] = StateGraphCopy.Independent(Data); }));
            }
            evt.menu.AppendAction(Data.BreakOnEnter ? "Remove Entry Breakpoint" : "Set Entry Breakpoint", action => m_Graph.Window.Change("Toggle breakpoint", () => Data.BreakOnEnter = !Data.BreakOnEnter));
            evt.menu.AppendAction(Data.BreakOnExit ? "Remove Exit Breakpoint" : "Set Exit Breakpoint", action => m_Graph.Window.Change("Toggle breakpoint", () => Data.BreakOnExit = !Data.BreakOnExit));
        }
    }
    internal sealed class StateGraphEdgeView : Edge
    {
        private readonly StateGraphWindow m_Window;
        internal StateGraphEdgeView(StateGraphWindow window, StateConnection data) { m_Window = window; userData = data; viewDataKey = data.Id; AddToClassList("connection-" + data.Kind.ToString().ToLowerInvariant()); tooltip = data.Kind == StateConnectionKind.Transition ? "Priority " + data.Transition.Priority + " · " + data.Transition.Description : data.Kind.ToString(); }
        public override void OnSelected() { base.OnSelected(); m_Window.Select(userData); }
    }
    internal sealed class StateNoteView : Node
    {
        internal StateNoteView(StateGraphWindow window, StateNoteData data)
        {
            userData = data; viewDataKey = data.Id; title = data.Title; AddToClassList("stating-note"); capabilities |= Capabilities.Resizable;
            mainContainer.style.backgroundColor = data.Color; titleContainer.style.backgroundColor = data.Color;
            var text = new TextField { multiline = true, value = data.Contents }; text.style.minHeight = 80;
            text.RegisterValueChangedCallback(evt => window.Change("Edit sticky note", () => data.Contents = evt.newValue, false));
            extensionContainer.Add(text); RefreshExpandedState(); SetPosition(data.Position);
            style.minWidth = data.Position.width; style.minHeight = data.Position.height;
            hierarchy.Add(new StateElementResizeHandle(this, rect => window.Change("Resize sticky note", () => data.Position = rect, false)));
            RegisterCallback<MouseDownEvent>(evt => window.Select(data));
        }
    }
}
