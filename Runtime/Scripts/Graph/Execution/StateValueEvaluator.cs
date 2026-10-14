using System;
using System.Collections.Generic;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    internal sealed class StateValueEvaluator
    {
        private sealed class FunctionBinding
        {
            internal StateGraphInstance.StateInstance State;
            internal StateMethod Method;
            internal object[] Arguments;
            internal object[] Defaults;
            internal object Result;
        }
        private readonly StateGraphInstance m_Instance;
        private readonly Dictionary<string, StateNodeData> m_Nodes = new Dictionary<string, StateNodeData>();
        private readonly Dictionary<string, StateConnection> m_Inputs = new Dictionary<string, StateConnection>();
        private readonly Dictionary<string, List<string>> m_Flow = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, FunctionBinding> m_Functions = new Dictionary<string, FunctionBinding>();
        private readonly HashSet<string> m_Evaluating = new HashSet<string>();
        private readonly Queue<string> m_Execution = new Queue<string>();
        private readonly Dictionary<string, object> m_Defaults = new Dictionary<string, object>();
        private readonly Dictionary<string, object> m_Secondary = new Dictionary<string, object>();
        private readonly Dictionary<string, object> m_EventPayloads = new Dictionary<string, object>();
        private int m_ExecutionDepth;
        private readonly Dictionary<string, StateLogicNode> m_Custom = new Dictionary<string, StateLogicNode>();
        internal StateValueEvaluator(StateGraphInstance instance) { m_Instance = instance; }
        private StateBlackboard Blackboard => m_Instance.Current?.Context.Blackboard ?? m_Instance.Blackboard;
        private static string Key(string node, string port) => node + ":" + port;
        internal void Compile()
        {
            foreach (var node in m_Instance.Graph.Nodes)
            {
                m_Nodes[node.Id] = node;
                m_Defaults[node.Id] = node.Value.CreateRuntimeValue();
                if (node.Arguments.Count > 0) m_Secondary[node.Id] = node.Arguments[0].CreateRuntimeValue();
                if (node.Custom != null) m_Custom[node.Id] = (StateLogicNode)StateType.Clone(node.Custom);
                if (node.Kind == StateNodeKind.Function && m_Instance.States.TryGetValue(node.TargetNodeId, out var target) && target.Metadata.Methods.TryGetValue(node.MethodId, out var method))
                {
                    var defaults = new object[method.Parameters.Length];
                    for (int i = 0; i < defaults.Length; i++) defaults[i] = i < node.Arguments.Count ? node.Arguments[i].CreateRuntimeValue() : method.Parameters[i].HasDefaultValue ? method.Parameters[i].DefaultValue : StateValue.Default(method.Parameters[i].ParameterType).Get();
                    m_Functions[node.Id] = new FunctionBinding { State = target, Method = method, Arguments = new object[method.Parameters.Length], Defaults = defaults, Result = method.Method.ReturnType == typeof(void) ? null : StateValue.Default(method.Method.ReturnType).Get() };
                }
            }
            foreach (var edge in m_Instance.Graph.Connections)
            {
                if (edge.Kind == StateConnectionKind.Value) m_Inputs[Key(edge.TargetNodeId, edge.TargetPort)] = edge;
                if (edge.Kind == StateConnectionKind.Execution)
                {
                    string key = Key(edge.SourceNodeId, edge.SourcePort);
                    if (!m_Flow.TryGetValue(key, out var targets)) m_Flow[key] = targets = new List<string>();
                    targets.Add(edge.TargetNodeId);
                }
            }
        }
        internal object Input(StateNodeData node, string port, object fallback)
        {
            return m_Inputs.TryGetValue(Key(node.Id, port), out var edge) ? Read(edge.SourceNodeId, edge.SourcePort) : fallback;
        }
        internal object Read(string id, string port)
        {
            string key = Key(id, port);
            if (!m_Evaluating.Add(key)) throw new InvalidOperationException($"Data cycle at {id}/{port}.");
            try
            {
                var node = m_Nodes[id];
                object fallback = m_Defaults[id];
                switch (node.Kind)
                {
                    case StateNodeKind.Custom:
                        if (!(m_Custom[id] is IStateValueNode custom)) throw new InvalidOperationException("Custom node has no value evaluator.");
                        return custom.Evaluate(port, m_Instance.Current?.Context ?? new StateContext(m_Instance.Runner, Blackboard, node.Id, m_Instance.Path), input => Input(node, input, null));
                    case StateNodeKind.Constant: return fallback;
                    case StateNodeKind.GetVariable: return Blackboard.Get(node.VariableId);
                    case StateNodeKind.SetVariable: return Blackboard.Get(node.VariableId);
                    case StateNodeKind.State:
                        var state = m_Instance.States[id];
                        if (port.StartsWith("field:")) return state.Metadata.Fields[port.Substring(6)].GetValue(state.Component);
                        throw new InvalidOperationException($"Unknown state value port '{port}'.");
                    case StateNodeKind.Function:
                        var binding = m_Functions[id];
                        if (binding.Method.Pure) Invoke(node, binding);
                        return binding.Result;
                    case StateNodeKind.Compare: return StateOperations.Compare(Input(node, "a", fallback), Input(node, "b", m_Secondary.TryGetValue(id, out var comparisonDefault) ? comparisonDefault : fallback), node.Comparison);
                    case StateNodeKind.And: return (bool)Input(node, "a", false) && (bool)Input(node, "b", false);
                    case StateNodeKind.Or: return (bool)Input(node, "a", false) || (bool)Input(node, "b", false);
                    case StateNodeKind.Not: return !(bool)Input(node, "a", false);
                    case StateNodeKind.Convert: return StateOperations.ConvertValue(Input(node, "a", fallback), StateType.Resolve(node.StateTypeName));
                    case StateNodeKind.Add: case StateNodeKind.Subtract: case StateNodeKind.Multiply: case StateNodeKind.Divide:
                        return StateOperations.Arithmetic(Input(node, "a", fallback), Input(node, "b", m_Secondary.TryGetValue(id, out var arithmeticDefault) ? arithmeticDefault : fallback), node.Kind);
                    case StateNodeKind.ReceiveEvent: return m_EventPayloads.TryGetValue(id, out var payload) ? payload : fallback;
                    default: throw new InvalidOperationException($"Node '{node.Name}' has no readable result.");
                }
            }
            finally { m_Evaluating.Remove(key); }
        }
        internal void ApplyInputs(StateGraphInstance.StateInstance state)
        {
            foreach (var pair in state.Metadata.Fields)
                if (m_Inputs.TryGetValue(Key(state.Node.Id, "field:" + pair.Key), out var edge)) pair.Value.SetValue(state.Component, Read(edge.SourceNodeId, edge.SourcePort));
        }
        private void Invoke(StateNodeData node, FunctionBinding binding)
        {
            if (!binding.State.Active) throw new InvalidOperationException($"Function '{binding.Method.Method.Name}' targets inactive state '{binding.State.Node.Name}'.");
            for (int i = 0; i < binding.Arguments.Length; i++)
            {
                var parameter = binding.Method.Parameters[i];
                object fallback = binding.Defaults[i];
                binding.Arguments[i] = Input(node, "arg:" + i, fallback);
            }
            try { binding.Result = binding.Method.Method.Invoke(binding.State.Component, binding.Arguments); }
            catch (Exception exception) { throw new InvalidOperationException($"Function node '{node.Name}' ({node.Id}), method '{node.MethodId}': {exception.InnerException?.Message ?? exception.Message}", exception); }
        }
        internal void Emit(string node, string port)
        {
            if (!m_Flow.TryGetValue(Key(node, port), out var targets)) return;
            foreach (string target in targets) m_Execution.Enqueue(target);
            Drain();
        }
        internal void Execute(string id) { m_Execution.Enqueue(id); Drain(); }
        private void Drain()
        {
            if (m_ExecutionDepth > 0) return;
            m_ExecutionDepth++;
            try
            {
                int work = 0;
                while (m_Execution.Count > 0)
                {
                    if (++work > 256) throw new InvalidOperationException("Execution flow exceeded 256 operations. Check for a cycle.");
                    var node = m_Nodes[m_Execution.Dequeue()];
                    switch (node.Kind)
                    {
                        case StateNodeKind.Custom:
                            if (!(m_Custom[node.Id] is IStateExecutionNode custom)) throw new InvalidOperationException("Custom node has no execution handler.");
                            custom.Execute(m_Instance.Current.Context, input => Input(node, input, null)); break;
                        case StateNodeKind.SetVariable: Blackboard.Set(node.VariableId, Input(node, "value", m_Defaults[node.Id])); break;
                        case StateNodeKind.Function: Invoke(node, m_Functions[node.Id]); break;
                        case StateNodeKind.SendEvent: m_Instance.Runner.SendEvent(node.EventName, Input(node, "value", m_Defaults[node.Id])); break;
                        case StateNodeKind.TriggerTransition: m_Instance.Runner.RequestTransition(node.TransitionId); break;
                        case StateNodeKind.Log: Debug.Log(Input(node, "value", m_Defaults[node.Id]), m_Instance.Runner); break;
                        default: throw new InvalidOperationException($"Node '{node.Name}' is not an executable operation.");
                    }
                    if (m_Flow.TryGetValue(Key(node.Id, "out"), out var targets)) foreach (string target in targets) m_Execution.Enqueue(target);
                }
            }
            finally { m_ExecutionDepth--; m_Execution.Clear(); }
        }
        internal void Receive(StateRuntimeEvent evt)
        {
            foreach (var node in m_Nodes.Values)
                if (node.Kind == StateNodeKind.ReceiveEvent && node.EventName == evt.Name)
                {
                    if (evt.Payload != null && !node.Value.Type.IsInstanceOfType(evt.Payload)) throw new ArgumentException($"Event '{evt.Name}' payload at node '{node.Name}' expects {node.Value.Type.Name}.");
                    m_EventPayloads[node.Id] = evt.Payload ?? m_Defaults[node.Id]; Emit(node.Id, "out");
                }
        }
    }
}
