using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Core;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace AS.Modules.Stating.Editor.Graph
{
    internal sealed class StateGraphSearch : ScriptableObject, ISearchWindowProvider
    {
        internal StateGraphWindow Window;
        internal Vector2 Position;
        private readonly Dictionary<int, Action> m_Actions = new Dictionary<int, Action>();
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            m_Actions.Clear();
            var result = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Create Node"), 0) };
            void Group(string name) => result.Add(new SearchTreeGroupEntry(new GUIContent(name), 1));
            void Entry(string name, Action action)
            {
                int id = m_Actions.Count; m_Actions[id] = action;
                result.Add(new SearchTreeEntry(new GUIContent(name)) { level = 2, userData = id });
            }
            Group("States");
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(IGraphState).IsAssignableFrom(t)).OrderBy(t => t.FullName))
            { Type captured = type; Entry(type.FullName, () => Window.AddState(captured, Position)); }
            Group("State Machine");
            foreach (var kind in new[] { StateNodeKind.Entry, StateNodeKind.Exit, StateNodeKind.AnyState })
            { var captured = kind; Entry(ObjectNames.NicifyVariableName(kind.ToString()), () => Window.AddNode(captured, Position)); }
            Entry("Parent State / Sub-State Machine", () => { var node = Window.AddState(typeof(GraphState), Position); Window.Change("Create nested graph", () => node.Children = StateGraphData.Create(node.Name)); });
            Group("Blackboard");
            foreach (var variable in Window.AvailableVariables())
            {
                var captured = variable;
                Entry("Get " + variable.Name, () => Window.AddVariableNode(captured, StateNodeKind.GetVariable, Position));
                Entry("Set " + variable.Name, () => Window.AddVariableNode(captured, StateNodeKind.SetVariable, Position));
                Entry("Compare " + variable.Name, () => Window.AddCompareVariable(captured, Position));
            }
            Entry("Constant Value", () => Window.AddNode(StateNodeKind.Constant, Position));
            Group("Logic & Values");
            foreach (var kind in new[] { StateNodeKind.And, StateNodeKind.Or, StateNodeKind.Not, StateNodeKind.Compare, StateNodeKind.Add, StateNodeKind.Subtract, StateNodeKind.Multiply, StateNodeKind.Divide, StateNodeKind.Convert })
            { var captured = kind; Entry(ObjectNames.NicifyVariableName(kind.ToString()), () => Window.AddNode(captured, Position)); }
            Group("Events & Utilities");
            foreach (var kind in new[] { StateNodeKind.ReceiveEvent, StateNodeKind.SendEvent, StateNodeKind.TriggerTransition, StateNodeKind.Log })
            { var captured = kind; Entry(ObjectNames.NicifyVariableName(kind.ToString()), () => Window.AddNode(captured, Position)); }
            Group("State Functions");
            foreach (var node in Window.CurrentGraph.Nodes.Where(n => n.Kind == StateNodeKind.State))
            {
                var metadata = StateMetadata.For(StateType.Resolve(node.EffectiveStateType)); if (metadata == null) continue;
                foreach (var method in metadata.Methods.Values.OrderBy(m => m.Method.Name))
                {
                    var capturedNode = node; var capturedMethod = method;
                    string parameters = string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name + " " + p.Name));
                    Entry($"{node.Name} / {method.Method.Name}({parameters}) → {method.Method.ReturnType.Name}", () => Window.AddFunction(capturedNode, capturedMethod, Position));
                }
            }
            Group("Reusable States & Trees");
            foreach (string guid in AssetDatabase.FindAssets("t:ReusableStateAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ReusableStateAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) { var captured = asset; Entry(asset.name, () => Window.AddReusable(captured, Position)); }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:StateMachineGraph"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<StateMachineGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) { var captured = asset; Entry("Graph / " + asset.name, () => Window.AddGraphReference(captured, Position)); }
            }
            Group("Organization");
            Entry("Group Selection", () => Window.View.CreateGroup(Position));
            Entry("Sticky Note", () => Window.View.CreateNote(Position));
            Group("Custom Logic");
            foreach (var type in TypeCache.GetTypesDerivedFrom<StateLogicNode>().Where(t => !t.IsAbstract && t.IsSerializable && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.FullName))
            {
                Type captured = type;
                Entry(type.Name, () => Window.Change("Create custom logic node", () => Window.CurrentGraph.Nodes.Add(new StateNodeData { Kind = StateNodeKind.Custom, Name = captured.Name, Custom = (StateLogicNode)Activator.CreateInstance(captured), Position = new Rect(Position, new Vector2(260, 150)) })));
            }
            return result;
        }
        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (!(entry.userData is int id) || !m_Actions.TryGetValue(id, out var action)) return false;
            action(); return true;
        }
    }

    internal sealed class StateTypeSearch : ScriptableObject, ISearchWindowProvider
    {
        internal Action<Type> Selected;
        internal bool StateOnly;
        private static readonly Type[] s_Builtins = { typeof(bool), typeof(int), typeof(float), typeof(double), typeof(string), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Color), typeof(GameObject), typeof(Transform), typeof(UnityEngine.Object) };
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            if (StateOnly)
            {
                var states = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("C# State Type"), 0) };
                foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(IGraphState).IsAssignableFrom(t)).OrderBy(t => t.FullName))
                    states.Add(new SearchTreeEntry(new GUIContent(type.FullName)) { level = 1, userData = type });
                return states;
            }
            var entries = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("Variable Type"), 0), new SearchTreeGroupEntry(new GUIContent("Built-in"), 1) };
            foreach (Type type in s_Builtins) entries.Add(new SearchTreeEntry(new GUIContent(type.Name)) { level = 2, userData = type });
            entries.Add(new SearchTreeGroupEntry(new GUIContent("Project & Unity Types"), 1));
            var types = new HashSet<Type>();
            foreach (var type in TypeCache.GetTypesWithAttribute<SerializableAttribute>())
                if ((type.IsPublic || type.IsNestedPublic) && !type.IsGenericType && !type.IsAbstract && StateType.IsSupported(type) && !s_Builtins.Contains(type) && (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null)) types.Add(type);
            foreach (var type in TypeCache.GetTypesDerivedFrom<UnityEngine.Object>()) if (type.IsPublic && !type.IsAbstract && !type.ContainsGenericParameters && !typeof(UnityEditor.Editor).IsAssignableFrom(type) && !typeof(EditorWindow).IsAssignableFrom(type) && !type.Assembly.GetName().Name.StartsWith("UnityEditor")) types.Add(type);
            foreach (var type in types.OrderBy(t => t.FullName)) entries.Add(new SearchTreeEntry(new GUIContent(type.FullName)) { level = 2, userData = type });
            entries.Add(new SearchTreeGroupEntry(new GUIContent("Collections"), 1));
            foreach (Type type in s_Builtins.Concat(types.Where(t => t.Assembly.GetName().Name == "Assembly-CSharp" || t.Assembly.GetName().Name.StartsWith("AS."))))
            {
                entries.Add(new SearchTreeEntry(new GUIContent(type.Name + "[]")) { level = 2, userData = type.MakeArrayType() });
                entries.Add(new SearchTreeEntry(new GUIContent("List<" + type.Name + ">")) { level = 2, userData = typeof(List<>).MakeGenericType(type) });
            }
            return entries;
        }
        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (!(entry.userData is Type type)) return false;
            Selected?.Invoke(type); return true;
        }
        internal static void Open(Action<Type> selected)
        {
            var search = CreateInstance<StateTypeSearch>(); search.Selected = selected;
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero)), search);
        }
        internal static void OpenState(Action<Type> selected)
        {
            var search = CreateInstance<StateTypeSearch>(); search.Selected = selected; search.StateOnly = true;
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero)), search);
        }
    }
}
