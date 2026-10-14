using System;
using System.Collections.Generic;

namespace AS.Modules.Stating.Graph
{
    public sealed class StateExecutionFlow { }
    public sealed class StateTransitionFlow { }
    public sealed class StatePort
    {
        public string Id;
        public string Name;
        public Type Type;
        public bool Input;
        public StateConnectionKind Kind;
        public StatePort(string id, string name, Type type, bool input, StateConnectionKind kind = StateConnectionKind.Value)
        { Id = id; Name = name; Type = type; Input = input; Kind = kind; }
    }
    public static class StateGraphPorts
    {
        public static StateVariable FindVariable(StateGraphData graph, string id)
        {
            var variable = graph.Variables.Find(v => v.Id == id || v.Name == id);
            if (variable != null) return variable;
            foreach (var node in graph.Nodes)
            {
                variable = node.Definition.Variables.Find(v => v.Id == id || v.Name == id);
                if (variable != null) return variable;
            }
            return null;
        }
        public static List<StatePort> For(StateGraphData graph, StateNodeData node, Func<string, StateVariable> findVariable = null)
        {
            var ports = new List<StatePort>();
            Type type = node.Value.Type ?? typeof(float);
            void Value(string id, string name, Type valueType, bool input) => ports.Add(new StatePort(id, name, valueType, input));
            void Flow(string id, string name, bool input, StateConnectionKind kind = StateConnectionKind.Execution) => ports.Add(new StatePort(id, name, kind == StateConnectionKind.Transition ? typeof(StateTransitionFlow) : typeof(StateExecutionFlow), input, kind));
            void Executable() { Flow("in", "Execute", true); Flow("out", "Then", false); }
            switch (node.Kind)
            {
                case StateNodeKind.Custom: if (node.Custom != null) ports.AddRange(node.Custom.GetPorts()); break;
                case StateNodeKind.Entry: Flow("out", "Start", false, StateConnectionKind.Transition); break;
                case StateNodeKind.Exit: Flow("in", "Complete", true, StateConnectionKind.Transition); break;
                case StateNodeKind.AnyState: Flow("out", "Transition", false, StateConnectionKind.Transition); break;
                case StateNodeKind.State:
                    Flow("in", "Transition", true, StateConnectionKind.Transition); Flow("out", "Transition", false, StateConnectionKind.Transition);
                    Flow("enter", "On Enter", false); Flow("update", "On Update", false); Flow("fixed", "On Fixed Update", false); Flow("exit", "On Exit", false);
                    var metadata = StateMetadata.For(StateType.Resolve(node.EffectiveStateType));
                    if (metadata != null) foreach (var pair in metadata.Fields)
                    { Value("field:" + pair.Key, pair.Value.Name, pair.Value.FieldType, true); Value("field:" + pair.Key, pair.Value.Name, pair.Value.FieldType, false); }
                    break;
                case StateNodeKind.Function:
                    Executable();
                    var target = graph.FindNode(node.TargetNodeId);
                    var targetMetadata = StateMetadata.For(StateType.Resolve(target?.EffectiveStateType));
                    if (targetMetadata != null && targetMetadata.Methods.TryGetValue(node.MethodId, out var method))
                    {
                        for (int i = 0; i < method.Parameters.Length; i++) Value("arg:" + i, method.Parameters[i].Name, method.Parameters[i].ParameterType, true);
                        if (method.Method.ReturnType != typeof(void)) Value("value", "Result", method.Method.ReturnType, false);
                    }
                    break;
                case StateNodeKind.GetVariable: case StateNodeKind.SetVariable:
                    var variable = findVariable?.Invoke(node.VariableId) ?? FindVariable(graph, node.VariableId);
                    Type variableType = variable?.DefaultValue.Type ?? type;
                    if (node.Kind == StateNodeKind.SetVariable) { Executable(); Value("value", "Value", variableType, true); }
                    Value("value", variable?.Name ?? "Value", variableType, false); break;
                case StateNodeKind.Constant: Value("value", "Value", type, false); break;
                case StateNodeKind.Compare: Value("a", "A", type, true); Value("b", "B", type, true); Value("value", "Result", typeof(bool), false); break;
                case StateNodeKind.And: case StateNodeKind.Or:
                    Value("a", "A", typeof(bool), true); Value("b", "B", typeof(bool), true); Value("value", "Result", typeof(bool), false); break;
                case StateNodeKind.Not: Value("a", "In", typeof(bool), true); Value("value", "Result", typeof(bool), false); break;
                case StateNodeKind.Convert: Value("a", "In", type, true); Value("value", "Out", StateType.Resolve(node.StateTypeName) ?? typeof(float), false); break;
                case StateNodeKind.ReceiveEvent: Flow("out", "On Event", false); Value("value", "Payload", type, false); break;
                case StateNodeKind.SendEvent: case StateNodeKind.Log: Executable(); Value("value", "Value", type, true); break;
                case StateNodeKind.TriggerTransition: Executable(); break;
                case StateNodeKind.Add: case StateNodeKind.Subtract: case StateNodeKind.Multiply: case StateNodeKind.Divide:
                    Value("a", "A", type, true); Value("b", "B", type, true); Value("value", "Result", type, false); break;
            }
            return ports;
        }
        public static bool Compatible(StatePort output, StatePort input)
        {
            return output != null && input != null && !output.Input && input.Input && output.Kind == input.Kind &&
                (output.Kind != StateConnectionKind.Value || (input.Type != null && output.Type != null && input.Type.IsAssignableFrom(output.Type)));
        }
    }
}
