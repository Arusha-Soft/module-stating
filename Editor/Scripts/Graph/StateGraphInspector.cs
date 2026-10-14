using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Core;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEngine;

namespace AS.Modules.Stating.Editor.Graph
{
    internal sealed class StateGraphInspector : IDisposable
    {
        private readonly StateGraphWindow m_Window;
        private object m_Selection;
        private GameObject m_PreviewObject;
        private MonoBehaviour m_PreviewState;
        private SerializedObject m_SerializedState;
        private Type m_PreviewType;
        private bool m_GraphSettings;
        internal StateGraphInspector(StateGraphWindow window) { m_Window = window; }
        internal void SetSelection(object selection)
        {
            if (ReferenceEquals(selection, m_Selection)) return;
            Dispose(); m_Selection = selection;
        }
        internal void Draw(object selected, List<StateValidationIssue> issues)
        {
            if (m_Window.CurrentGraph == null) return;
            m_GraphSettings = GUILayout.Toolbar(m_GraphSettings ? 1 : 0, new[] { "Node Settings", "Graph Settings" }) == 1;
            using (new EditorGUI.DisabledScope(!m_Window.CanAuthor))
            {
                if (m_Window.CanAuthor) Undo.RecordObject(m_Window.EditingAsset, "Edit State Machine graph");
                EditorGUI.BeginChangeCheck();
                if (m_GraphSettings || selected == null) DrawGraph();
                else if (selected is StateNodeData node) DrawNode(node);
                else if (selected is StateConnection edge) DrawConnection(edge);
                else if (selected is StateVariable variable) DrawVariable(variable);
                else if (selected is StateGroupData group)
                { group.Title = EditorGUILayout.TextField("Title", group.Title); group.Color = EditorGUILayout.ColorField("Color", group.Color); }
                else if (selected is StateNoteData note)
                { note.Title = EditorGUILayout.TextField("Title", note.Title); note.Contents = EditorGUILayout.TextArea(note.Contents, GUILayout.MinHeight(80)); note.Color = EditorGUILayout.ColorField("Color", note.Color); }
                if (EditorGUI.EndChangeCheck()) m_Window.Dirty();
            }
            var visible = selected is StateNodeData state ? issues.Where(i => i.NodeId == state.Id).ToList() : issues;
            if (visible.Count > 0)
            {
                GUILayout.Space(10); GUILayout.Label("Validation · " + visible.Count, EditorStyles.boldLabel);
                foreach (var issue in visible)
                {
                    EditorGUILayout.HelpBox(issue.Message, issue.Severity == StateValidationSeverity.Error ? MessageType.Error : MessageType.Warning);
                    if (!string.IsNullOrEmpty(issue.NodeId) && GUILayout.Button("Focus affected node", EditorStyles.miniButton)) m_Window.FocusGraphNode(issue.GraphId, issue.NodeId);
                }
            }
        }
        private void DrawGraph()
        {
            if (m_Window.IsDefinitionRoot && m_Window.EditingAsset is ReusableStateAsset reusable) { DrawNode(reusable.Definition); return; }
            var graph = m_Window.CurrentGraph;
            GUILayout.Label("Graph Settings", EditorStyles.boldLabel);
            graph.Name = EditorGUILayout.TextField("Name", graph.Name);
            EditorGUILayout.LabelField("Description"); graph.Description = EditorGUILayout.TextArea(graph.Description, GUILayout.MinHeight(40));
            graph.TransitionOrder = (StateTransitionOrder)EditorGUILayout.EnumPopup("Transition order", graph.TransitionOrder);
            EditorGUILayout.LabelField("Schema", graph.Version.ToString());
            EditorGUILayout.LabelField("States", graph.Nodes.Count(n => n.Kind == StateNodeKind.State).ToString());
            EditorGUILayout.LabelField("Connections", graph.Connections.Count.ToString());
            EditorGUILayout.HelpBox("Transitions use descending priority, then edge order. One transition is taken per pass. Delays require continuously valid conditions. Exit time uses state seconds.", MessageType.None);
        }
        private void DrawNode(StateNodeData node)
        {
            if (StateGraphEditorExtensions.DrawInspector(node)) return;
            GUILayout.Label(ObjectNames.NicifyVariableName(node.Kind.ToString()), EditorStyles.boldLabel);
            node.Name = EditorGUILayout.TextField("Name", node.Name);
            node.Description = EditorGUILayout.TextArea(node.Description ?? "", GUILayout.MinHeight(35));
            node.Color = EditorGUILayout.ColorField("Title color", node.Color);
            node.Icon = (Texture2D)EditorGUILayout.ObjectField("Icon", node.Icon, typeof(Texture2D), false);
            if (node.Kind == StateNodeKind.Custom) { if (node.Custom != null) StateValueGUI.DrawObject("Custom settings", node.Custom, node.Custom.GetType(), 0); return; }
            if (node.Kind == StateNodeKind.State)
            {
                if (node.IsLinked)
                {
                    EditorGUILayout.ObjectField("Shared definition", node.Template, typeof(ReusableStateAsset), false);
                    EditorGUILayout.HelpBox("Field edits below are local reference overrides. The shared definition stays separate.", MessageType.Info);
                    if (GUILayout.Button("Edit Shared Definition")) StateGraphWindow.Open(node.Template);
                    if (GUILayout.Button("Clear Local Overrides")) m_Window.Change("Clear reference overrides", () => { node.Fields.Clear(); node.Overrides.Clear(); Dispose(); });
                }
                else
                {
                    EditorGUILayout.LabelField("C# State", StateType.Resolve(node.StateTypeName)?.FullName ?? "Missing type");
                    if (GUILayout.Button("Choose C# State…")) StateTypeSearch.OpenState(type => m_Window.Change("Assign state type", () => { node.StateTypeName = StateType.Id(type); Dispose(); }));
                }
                node.ResumeChildren = EditorGUILayout.Toggle("Resume child history", node.ResumeChildren);
                if (!node.IsLinked)
                {
                    node.IsSubGraphLinked = EditorGUILayout.Toggle("Use shared subgraph", node.IsSubGraphLinked);
                    if (node.IsSubGraphLinked) node.SubGraphAsset = (StateMachineGraph)EditorGUILayout.ObjectField("Shared subgraph", node.SubGraphAsset, typeof(StateMachineGraph), false);
                }
                node.BreakOnEnter = EditorGUILayout.Toggle("Entry breakpoint", node.BreakOnEnter); node.BreakOnExit = EditorGUILayout.Toggle("Exit breakpoint", node.BreakOnExit);
                if (GUILayout.Button(node.EffectiveChildren == null ? "Create Internal State Tree" : "Open Internal State Tree")) m_Window.Enter(node);
                GUILayout.Space(8); GUILayout.Label("Serialized Fields", EditorStyles.boldLabel); DrawNativeFields(node);
                GUILayout.Space(8); GUILayout.Label("Functions", EditorStyles.boldLabel);
                var metadata = StateMetadata.For(StateType.Resolve(node.EffectiveStateType));
                if (metadata != null)
                {
                    foreach (var method in metadata.Methods.Values)
                    {
                        if (GUILayout.Button(method.Method.Name + "(" + string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name)) + ")", EditorStyles.miniButton)) m_Window.AddFunction(node, method, node.Position.position + new Vector2(320, 60));
                        GUILayout.Label(method.Pure ? "Pure · can evaluate as data" : "Explicit execution · result cached", EditorStyles.miniLabel);
                    }
                    foreach (string warning in metadata.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
                GUILayout.Space(8); GUILayout.Label(node.IsLinked ? "Exposed Parameter Overrides" : "State-local Blackboard", EditorStyles.boldLabel);
                if (node.IsLinked || node.IsSubGraphLinked)
                {
                    foreach (var variable in node.Definition.Variables.Concat(node.EffectiveChildren?.Variables ?? new List<StateVariable>()).Where(v => v.Exposed)) DrawOverride(node, variable);
                }
                else
                {
                    foreach (var variable in node.Variables.ToArray())
                    {
                        DrawVariableProperties(variable);
                        if (GUILayout.Button("Remove " + variable.Name, EditorStyles.miniButton)) m_Window.Change("Remove local variable", () => node.Variables.Remove(variable));
                    }
                    if (GUILayout.Button("Add State-local Variable")) StateTypeSearch.Open(type => m_Window.Change("Add local variable", () => node.Variables.Add(new StateVariable { Name = type.Name + " " + node.Variables.Count, Scope = StateVariableScope.State, DefaultValue = StateValue.Default(type) })));
                }
                return;
            }
            if (node.Kind == StateNodeKind.GetVariable || node.Kind == StateNodeKind.SetVariable)
            {
                var variables = m_Window.AvailableVariables().ToList(); int current = variables.FindIndex(v => v.Id == node.VariableId);
                int next = EditorGUILayout.Popup("Variable", current, variables.Select(v => v.Name).ToArray());
                if (next >= 0 && next < variables.Count && next != current) { node.VariableId = variables[next].Id; node.Value = StateValue.Default(variables[next].DefaultValue.Type); m_Window.QueueRebuild(); }
            }
            if (node.Kind == StateNodeKind.Function) DrawFunction(node);
            if (node.Kind == StateNodeKind.SendEvent || node.Kind == StateNodeKind.ReceiveEvent) node.EventName = EditorGUILayout.TextField("Event name", node.EventName);
            if (node.Kind == StateNodeKind.TriggerTransition) node.TransitionId = ChooseNode("Transition", node.TransitionId, m_Window.CurrentGraph.Connections.Where(e => e.Kind == StateConnectionKind.Transition).Select(e => new KeyValuePair<string, string>(e.Id, (m_Window.CurrentGraph.FindNode(e.SourceNodeId)?.Name ?? "Missing") + " → " + m_Window.CurrentGraph.FindNode(e.TargetNodeId)?.Name)).ToList());
            if (node.Kind == StateNodeKind.Compare) node.Comparison = (StateComparison)EditorGUILayout.EnumPopup("Comparison", node.Comparison);
            if (node.Kind == StateNodeKind.Convert)
            {
                EditorGUILayout.LabelField("Output type", StateType.Resolve(node.StateTypeName)?.Name ?? "Missing");
                if (GUILayout.Button("Choose Output Type")) StateTypeSearch.Open(type => m_Window.Change("Set conversion output", () => node.StateTypeName = StateType.Id(type)));
            }
            if (node.Kind != StateNodeKind.Function && node.Kind != StateNodeKind.Entry && node.Kind != StateNodeKind.Exit && node.Kind != StateNodeKind.AnyState && node.Kind != StateNodeKind.TriggerTransition)
            {
                if (node.Kind != StateNodeKind.And && node.Kind != StateNodeKind.Or && node.Kind != StateNodeKind.Not)
                {
                    EditorGUILayout.LabelField("Value type", node.Value.Type?.Name ?? "Missing");
                    if (GUILayout.Button("Choose Value Type")) StateTypeSearch.Open(type => m_Window.Change("Set node value type", () => { node.Value = StateValue.Default(type); node.Arguments.Clear(); }));
                    StateValueGUI.Draw(node.Kind == StateNodeKind.Compare ? "A default" : "Default value", node.Value);
                    if (node.Kind == StateNodeKind.Compare || node.Kind == StateNodeKind.Add || node.Kind == StateNodeKind.Subtract || node.Kind == StateNodeKind.Multiply || node.Kind == StateNodeKind.Divide)
                    {
                        var value = node.Arguments.Count == 0 ? StateValue.Default(node.Value.Type) : node.Arguments[0];
                        EditorGUI.BeginChangeCheck(); StateValueGUI.Draw("B default", value);
                        if (EditorGUI.EndChangeCheck() && node.Arguments.Count == 0) node.Arguments.Add(value);
                    }
                }
            }
        }
        private void DrawOverride(StateNodeData node, StateVariable variable)
        {
            var item = node.Overrides.Find(o => o.VariableId == variable.Id);
            bool enabled = EditorGUILayout.ToggleLeft(variable.Name + " (" + variable.DefaultValue.Type?.Name + ")", item != null);
            if (enabled && item == null) { item = new StateVariableOverride { VariableId = variable.Id, Value = JsonUtility.FromJson<StateValue>(JsonUtility.ToJson(variable.DefaultValue)) }; node.Overrides.Add(item); GUI.changed = true; }
            if (!enabled && item != null) { node.Overrides.Remove(item); GUI.changed = true; }
            if (enabled) StateValueGUI.Draw("Override", item.Value);
        }
        private void DrawNativeFields(StateNodeData node)
        {
            Type type = StateType.Resolve(node.EffectiveStateType);
            if (type == null || type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type)) return;
            if (m_PreviewObject == null || m_PreviewType != type)
            {
                Dispose(); m_PreviewType = type;
                m_PreviewObject = new GameObject("Stating Inspector Configuration") { hideFlags = HideFlags.HideAndDontSave }; m_PreviewObject.SetActive(false);
                m_PreviewState = (MonoBehaviour)m_PreviewObject.AddComponent(type);
                var metadata = StateMetadata.For(type);
                foreach (var field in node.Definition.Fields.Concat(node.IsLinked ? node.Fields : new List<StateFieldValue>()))
                    if (metadata.Fields.TryGetValue(field.MemberId, out var info) && field.Value.Type != null && info.FieldType.IsAssignableFrom(field.Value.Type)) info.SetValue(m_PreviewState, field.Value.CreateRuntimeValue());
                m_SerializedState = new SerializedObject(m_PreviewState);
            }
            m_SerializedState.Update();
            var fieldMetadata = StateMetadata.For(type);
            var names = new HashSet<string>(fieldMetadata.Fields.Values.Select(f => f.Name));
            var iterator = m_SerializedState.GetIterator(); bool enter = true;
            EditorGUI.BeginChangeCheck();
            while (iterator.NextVisible(enter)) { enter = false; if (names.Contains(iterator.name)) EditorGUILayout.PropertyField(iterator, true); }
            if (EditorGUI.EndChangeCheck())
            {
                m_SerializedState.ApplyModifiedProperties();
                foreach (var pair in fieldMetadata.Fields)
                {
                    node.Fields.RemoveAll(f => f.MemberId == pair.Key);
                    node.Fields.Add(new StateFieldValue { MemberId = pair.Key, Value = StateValue.From(StateType.Clone(pair.Value.GetValue(m_PreviewState)), pair.Value.FieldType) });
                }
                m_Window.Dirty();
            }
        }
        private void DrawFunction(StateNodeData node)
        {
            var states = m_Window.CurrentGraph.Nodes.Where(n => n.Kind == StateNodeKind.State).ToList(); int current = states.FindIndex(s => s.Id == node.TargetNodeId);
            int next = EditorGUILayout.Popup("Target state", current, states.Select(s => s.Name).ToArray());
            if (next >= 0 && next < states.Count && next != current) { node.TargetNodeId = states[next].Id; m_Window.QueueRebuild(); }
            var target = m_Window.CurrentGraph.FindNode(node.TargetNodeId); var metadata = StateMetadata.For(StateType.Resolve(target?.EffectiveStateType));
            if (metadata == null) return;
            var methods = metadata.Methods.Values.ToList(); current = methods.FindIndex(m => m.Id == node.MethodId);
            next = EditorGUILayout.Popup("Function", current, methods.Select(m => m.Method.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name)) + ")").ToArray());
            if (next >= 0 && next < methods.Count && next != current)
            {
                node.MethodId = methods[next].Id; node.Arguments = methods[next].Parameters.Select(p => p.HasDefaultValue ? StateValue.From(p.DefaultValue, p.ParameterType) : StateValue.Default(p.ParameterType)).ToList(); m_Window.QueueRebuild();
            }
            if (next >= 0 && next < methods.Count)
            {
                var method = methods[next];
                for (int i = 0; i < method.Parameters.Length; i++)
                {
                    var value = node.Arguments.Count <= i ? StateValue.Default(method.Parameters[i].ParameterType) : node.Arguments[i];
                    EditorGUI.BeginChangeCheck(); StateValueGUI.Draw(method.Parameters[i].Name, value);
                    if (EditorGUI.EndChangeCheck() && node.Arguments.Count <= i) { while (node.Arguments.Count < i) node.Arguments.Add(StateValue.Default(method.Parameters[node.Arguments.Count].ParameterType)); node.Arguments.Add(value); }
                }
                EditorGUILayout.HelpBox(method.Pure ? "Pure method: result may be evaluated by value connections and transition conditions." : "Side effects execute only through Execute. The result port returns the latest explicit call's cached result.", MessageType.Info);
            }
        }
        private void DrawConnection(StateConnection edge)
        {
            GUILayout.Label(edge.Kind + " Connection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("From", m_Window.CurrentGraph.FindNode(edge.SourceNodeId)?.Name ?? edge.SourceNodeId);
            EditorGUILayout.LabelField("To", m_Window.CurrentGraph.FindNode(edge.TargetNodeId)?.Name ?? edge.TargetNodeId);
            if (edge.Kind == StateConnectionKind.Transition)
            {
                var transition = edge.Transition;
                transition.Enabled = EditorGUILayout.Toggle("Enabled", transition.Enabled); transition.Priority = EditorGUILayout.IntField("Priority", transition.Priority);
                transition.Delay = Mathf.Max(0, EditorGUILayout.FloatField("Condition delay (s)", transition.Delay)); transition.ExitTime = Mathf.Max(0, EditorGUILayout.FloatField("Exit time (s)", transition.ExitTime));
                transition.AllowSelf = EditorGUILayout.Toggle("Allow self transition", transition.AllowSelf); transition.RequireChildCompletion = EditorGUILayout.Toggle("Wait for child Exit", transition.RequireChildCompletion);
                transition.EventName = EditorGUILayout.TextField("Event gate", transition.EventName); transition.Breakpoint = EditorGUILayout.Toggle("Breakpoint", transition.Breakpoint);
                transition.Description = EditorGUILayout.TextArea(transition.Description ?? "", GUILayout.MinHeight(30));
                var booleanNodes = m_Window.CurrentGraph.Nodes.Where(n => StateGraphPorts.For(m_Window.CurrentGraph, n, id => m_Window.AvailableVariables().FirstOrDefault(v => v.Id == id)).Any(p => !p.Input && p.Id == "value" && p.Type == typeof(bool))).ToList();
                transition.ConditionNodeId = ChooseNode("Boolean result", transition.ConditionNodeId, booleanNodes.Select(n => new KeyValuePair<string, string>(n.Id, n.Name)).ToList());
                transition.ConditionMode = (StateConditionMode)EditorGUILayout.EnumPopup("Combine conditions", transition.ConditionMode);
                foreach (var condition in transition.Conditions.ToArray())
                {
                    if (condition == null) { EditorGUILayout.HelpBox("Missing condition type. Data is retained.", MessageType.Error); continue; }
                    GUILayout.Label(condition.GetType().Name, EditorStyles.miniBoldLabel);
                    if (condition is BlackboardStateCondition blackboard)
                    {
                        var variables = m_Window.AvailableVariables().ToList(); int index = variables.FindIndex(v => v.Id == blackboard.VariableId);
                        int next = EditorGUILayout.Popup("Variable", index, variables.Select(v => v.Name).ToArray());
                        if (next >= 0 && next < variables.Count && next != index) { blackboard.VariableId = variables[next].Id; blackboard.Value = StateValue.Default(variables[next].DefaultValue.Type); }
                        blackboard.Comparison = (StateComparison)EditorGUILayout.EnumPopup("Compare", blackboard.Comparison); StateValueGUI.Draw("Value", blackboard.Value);
                    }
                    else StateValueGUI.DrawObject("Settings", condition, condition.GetType(), 0);
                    if (GUILayout.Button("Remove condition", EditorStyles.miniButton)) { transition.Conditions.Remove(condition); GUI.changed = true; }
                }
                if (GUILayout.Button("Add Condition…"))
                {
                    var menu = new GenericMenu();
                    foreach (var type in TypeCache.GetTypesDerivedFrom<StateCondition>().Where(t => !t.IsAbstract && t.IsSerializable && t.GetConstructor(Type.EmptyTypes) != null))
                    { Type captured = type; menu.AddItem(new GUIContent(type.Name), false, () => m_Window.Change("Add condition", () => transition.Conditions.Add((StateCondition)Activator.CreateInstance(captured)))); }
                    menu.ShowAsContext();
                }
                transition.OnTakenNodeId = ChooseNode("On taken callback", transition.OnTakenNodeId, m_Window.CurrentGraph.Nodes.Where(n => StateGraphPorts.For(m_Window.CurrentGraph, n).Any(p => p.Input && p.Kind == StateConnectionKind.Execution)).Select(n => new KeyValuePair<string, string>(n.Id, n.Name)).ToList());
            }
            if (GUILayout.Button("Delete Connection")) m_Window.Change("Delete connection", () => m_Window.CurrentGraph.Connections.Remove(edge));
        }
        private static string ChooseNode(string label, string id, List<KeyValuePair<string, string>> choices)
        {
            var names = new List<string> { "None" }; names.AddRange(choices.Select(p => p.Value));
            int index = choices.FindIndex(p => p.Key == id) + 1;
            int next = EditorGUILayout.Popup(label, index, names.ToArray());
            return next == index ? id : next == 0 ? "" : choices[next - 1].Key;
        }
        private void DrawVariable(StateVariable variable)
        {
            bool own = m_Window.CurrentGraph.Variables.Contains(variable) || m_Window.CurrentGraph.Nodes.Any(n => !n.IsLinked && n.Variables.Contains(variable));
            using (new EditorGUI.DisabledScope(!own)) DrawVariableProperties(variable);
            if (!own) EditorGUILayout.HelpBox("This variable belongs to a parent or shared definition. Navigate to its owning scope to edit defaults.", MessageType.Info);
        }
        private void DrawVariableProperties(StateVariable variable)
        {
            variable.Name = EditorGUILayout.DelayedTextField("Name", variable.Name); variable.Category = EditorGUILayout.DelayedTextField("Category", variable.Category);
            variable.Description = EditorGUILayout.TextArea(variable.Description ?? "", GUILayout.MinHeight(30)); variable.Exposed = EditorGUILayout.Toggle("Expose to children", variable.Exposed);
            EditorGUILayout.LabelField("Scope", variable.Scope.ToString()); EditorGUILayout.LabelField("Type", variable.DefaultValue.Type?.Name ?? "Missing type");
            if (GUILayout.Button("Change Type…")) StateTypeSearch.Open(type => m_Window.Change("Change variable type", () => variable.DefaultValue = StateValue.Default(type)));
            StateValueGUI.Draw("Default", variable.DefaultValue);
        }
        public void Dispose()
        {
            m_SerializedState?.Dispose(); m_SerializedState = null;
            if (m_PreviewObject != null) UnityEngine.Object.DestroyImmediate(m_PreviewObject);
            m_PreviewObject = null; m_PreviewState = null; m_PreviewType = null;
        }
    }
}
