using System;
using System.Collections.Generic;
using System.Reflection;
using AS.Modules.Stating.Core;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    /// <summary>Compiled, private execution of one exclusive-child graph scope.</summary>
    internal sealed class StateGraphInstance : IDisposable
    {
        internal sealed class StateInstance : IRunnable, IDisposable
        {
            private readonly StateGraphInstance m_Owner;
            internal readonly StateNodeData Node;
            internal readonly GameObject Object;
            internal readonly MonoBehaviour Component;
            internal readonly IGraphState State;
            internal readonly StateMetadata Metadata;
            internal readonly StateContext Context;
            internal readonly StateGraphInstance Child;
            internal double EnteredAt;
            internal bool Active;
            private string m_HistoryState;

            internal StateInstance(StateGraphInstance owner, StateNodeData node, int depth)
            {
                m_Owner = owner; Node = node;
                var definition = node.Definition;
                string path = owner.Path + "/" + node.Id;
                var local = new StateBlackboard(definition.Variables, owner.Blackboard, path, false);
                local.ApplyOverrides(node.Overrides); local.ApplyOverrides(owner.Runner.Overrides);
                Context = new StateContext(owner.Runner, local, node.Id, path);
                Type type = StateType.Resolve(node.EffectiveStateType);
                Object = new GameObject($"[Stating] {node.Name}") { hideFlags = HideFlags.DontSave };
                Object.transform.SetParent(owner.Runner.transform, false);
                Object.SetActive(false);
                try
                {
                    Component = StateAotRegistry.AddState(Object, type);
                    State = (IGraphState)Component; Metadata = StateMetadata.For(type);
                    ApplyFields(definition.Fields);
                    if (node.IsLinked) ApplyFields(node.Fields);
                    State.BindContext(Context);
                    State.InitializeTarget(owner.Runner.Target);
                    if (node.EffectiveChildren != null) Child = new StateGraphInstance(owner.Runner, node.EffectiveChildren, local, path + "/" + node.EffectiveChildren.Id, node.Overrides, depth + 1);
                }
                catch { DestroyObject(Object); throw; }
            }
            private void ApplyFields(List<StateFieldValue> fields)
            {
                foreach (var configuration in fields)
                    if (Metadata.Fields.TryGetValue(configuration.MemberId, out FieldInfo field)) field.SetValue(Component, configuration.Value.CreateRuntimeValue());
            }
            public void Enter()
            {
                Active = true; EnteredAt = m_Owner.Runner.ElapsedTime;
                try
                {
                    Object.SetActive(true);
                    m_Owner.Values.ApplyInputs(this);
                    State.Enter();
                    m_Owner.Values.Emit(Node.Id, "enter");
                    m_Owner.Record(StateHistoryKind.Enter, this, null, null, "State entry");
                    if (Node.BreakOnEnter) m_Owner.Runner.HitBreakpoint(Context.Path);
                    Child?.Start(Node.ResumeChildren ? m_HistoryState : null);
                }
                catch { Object.SetActive(false); Active = false; throw; }
            }
            public void Exit()
            {
                if (!Active) return;
                try
                {
                    if (Child != null) { m_HistoryState = Child.ActiveNodeId; Child.Stop(); }
                    State.Exit();
                    m_Owner.Values.Emit(Node.Id, "exit");
                    m_Owner.Record(StateHistoryKind.Exit, this, null, null, "State exit");
                    if (Node.BreakOnExit) m_Owner.Runner.HitBreakpoint(Context.Path);
                }
                finally { Active = false; Object.SetActive(false); }
            }
            public void Dispose() { Exit(); Child?.Dispose(); DestroyObject(Object); }
            private static void DestroyObject(GameObject obj)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        internal readonly StateMachineRunner Runner;
        internal readonly StateGraphData Graph;
        internal readonly string Path;
        internal readonly Dictionary<string, StateInstance> States = new Dictionary<string, StateInstance>();
        internal readonly StateValueEvaluator Values;
        internal StateInstance Current;
        public StateBlackboard Blackboard { get; }
        public bool Completed { get; private set; }
        public string ActiveNodeId => Current?.Node.Id;
        public string ActivePath => Current == null ? "" : Current.Node.Name + (Current.Child?.Current == null ? "" : " > " + Current.Child.ActivePath);
        private StateMachine m_Machine;
        private readonly List<StateConnection> m_Transitions = new List<StateConnection>();
        private readonly Dictionary<string, double> m_EligibleSince = new Dictionary<string, double>();
        private readonly HashSet<string> m_Events = new HashSet<string>();
        private string m_RequestedTransition;
        private bool m_Changing;

        internal StateGraphInstance(StateMachineRunner runner, StateGraphData graph, StateBlackboard parent, string path, IEnumerable<StateVariableOverride> overrides, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("State hierarchy exceeds 32 levels.");
            Runner = runner; Graph = graph; Path = path;
            Blackboard = new StateBlackboard(graph.Variables, parent, path);
            Blackboard.ApplyOverrides(overrides); Blackboard.ApplyOverrides(runner.Overrides);
            Values = new StateValueEvaluator(this);
            try
            {
                foreach (StateNodeData node in graph.Nodes)
                    if (node.Kind == StateNodeKind.State) States.Add(node.Id, new StateInstance(this, node, depth));
                var indexed = new List<KeyValuePair<int, StateConnection>>();
                for (int i = 0; i < graph.Connections.Count; i++) if (graph.Connections[i].Kind == StateConnectionKind.Transition) indexed.Add(new KeyValuePair<int, StateConnection>(i, graph.Connections[i]));
                indexed.Sort((a, b) => { int priority = b.Value.Transition.Priority.CompareTo(a.Value.Transition.Priority); return priority == 0 ? a.Key.CompareTo(b.Key) : priority; });
                foreach (var pair in indexed) m_Transitions.Add(pair.Value);
                Values.Compile();
            }
            catch { Dispose(); throw; }
        }
        internal void Start(string initial = null)
        {
            Completed = false;
            if (!string.IsNullOrEmpty(initial) && States.ContainsKey(initial)) { Change(initial, null, "Initial override / history"); return; }
            var entry = Graph.Nodes.Find(n => n.Kind == StateNodeKind.Entry);
            foreach (var edge in m_Transitions) if (edge.SourceNodeId == entry.Id && edge.Transition.Enabled) { Change(edge.TargetNodeId, edge, "Entry"); return; }
            throw new InvalidOperationException($"Graph '{Graph.Name}' has no enabled Entry transition.");
        }
        internal void Stop()
        {
            try { m_Machine.SwitchState(null); }
            finally { Current = null; m_Events.Clear(); m_EligibleSince.Clear(); m_RequestedTransition = null; }
        }
        internal void Tick(float deltaTime)
        {
            UpdateStates(deltaTime);
            EvaluateTransitions();
        }
        private void UpdateStates(float deltaTime)
        {
            if (Current == null) return;
            Values.ApplyInputs(Current);
            Current.State.Tick(deltaTime); Values.Emit(Current.Node.Id, "update");
            Current.Child?.UpdateStates(deltaTime);
        }
        internal void FixedTick(float deltaTime)
        {
            if (Current == null) return;
            Current.State.FixedTick(deltaTime); Values.Emit(Current.Node.Id, "fixed");
            Current.Child?.FixedTick(deltaTime);
        }
        private bool EvaluateTransitions()
        {
            if (Current == null) return false;
            if (Graph.TransitionOrder == StateTransitionOrder.ChildFirst && Current.Child != null && Current.Child.EvaluateTransitions()) return true;
            if (!string.IsNullOrEmpty(m_RequestedTransition))
            {
                string id = m_RequestedTransition; m_RequestedTransition = null;
                var requested = m_Transitions.Find(t => t.Id == id);
                if (requested != null && Eligible(requested)) { Take(requested, "Requested transition"); return true; }
            }
            foreach (var edge in m_Transitions) if (Eligible(edge)) { Take(edge, string.IsNullOrEmpty(edge.Transition.EventName) ? "Conditions satisfied" : "Event: " + edge.Transition.EventName); return true; }
            return Graph.TransitionOrder == StateTransitionOrder.ParentFirst && Current.Child != null && Current.Child.EvaluateTransitions();
        }
        private bool Eligible(StateConnection edge)
        {
            StateTransition transition = edge.Transition;
            var source = Graph.FindNode(edge.SourceNodeId);
            bool valid = Current != null && transition.Enabled && source != null && (source.Id == Current.Node.Id || source.Kind == StateNodeKind.AnyState) &&
                (transition.AllowSelf || edge.TargetNodeId != Current.Node.Id) && Runner.ElapsedTime - Current.EnteredAt >= transition.ExitTime &&
                (!transition.RequireChildCompletion || Current.Child == null || Current.Child.Completed) &&
                (string.IsNullOrEmpty(transition.EventName) || m_Events.Contains(transition.EventName));
            if (valid && !string.IsNullOrEmpty(transition.ConditionNodeId)) valid = Values.Read(transition.ConditionNodeId, "value") is bool condition && condition;
            if (valid && transition.Conditions.Count > 0)
            {
                bool result = transition.ConditionMode == StateConditionMode.All;
                foreach (var condition in transition.Conditions)
                {
                    bool value = condition.Evaluate(Current.Context);
                    if (transition.ConditionMode == StateConditionMode.All) result &= value; else result |= value;
                }
                valid = result;
            }
            if (!valid) { m_EligibleSince.Remove(edge.Id); return false; }
            if (!m_EligibleSince.TryGetValue(edge.Id, out double since)) { since = Runner.ElapsedTime; m_EligibleSince[edge.Id] = since; }
            return Runner.ElapsedTime - since >= transition.Delay;
        }
        private void Take(StateConnection edge, string reason)
        {
            if (!string.IsNullOrEmpty(edge.Transition.OnTakenNodeId)) Values.Execute(edge.Transition.OnTakenNodeId);
            Change(edge.TargetNodeId, edge, reason);
            if (edge.Transition.Breakpoint) Runner.HitBreakpoint(Path + "/transition/" + edge.Id);
        }
        private void Change(string target, StateConnection edge, string reason)
        {
            if (m_Changing) throw new InvalidOperationException("Reentrant state changes are not supported. Queue an event instead.");
            m_Changing = true;
            var previous = Current;
            try
            {
                m_Events.Clear(); m_EligibleSince.Clear();
                if (!States.TryGetValue(target, out var next))
                {
                    var exit = Graph.FindNode(target);
                    if (exit?.Kind != StateNodeKind.Exit) throw new InvalidOperationException($"Missing target '{target}' in '{Graph.Name}'.");
                    m_Machine.SwitchState(null); Current = null; Completed = true;
                }
                else
                {
                    // Exit flow reads the old scope; entry flow sees the new scope.
                    m_Machine.SwitchState(null); Current = next; m_Machine.SwitchState(next);
                }
                if (edge != null && previous != null) Record(StateHistoryKind.Transition, next ?? previous, previous, edge, reason);
            }
            finally { m_Changing = false; }
        }
        internal void Dispatch(StateRuntimeEvent evt)
        {
            DispatchToActive(evt);
            EvaluateTransitions();
        }
        private void DispatchToActive(StateRuntimeEvent evt)
        {
            if (Current == null) return;
            m_Events.Add(evt.Name);
            Record(StateHistoryKind.Event, Current, null, null, evt.Name);
            Values.Receive(evt);
            Current.Child?.DispatchToActive(evt);
        }
        internal bool RequestTransition(string id)
        {
            var edge = m_Transitions.Find(e => e.Id == id);
            if (edge != null && Current != null && (edge.SourceNodeId == Current.Node.Id || Graph.FindNode(edge.SourceNodeId)?.Kind == StateNodeKind.AnyState)) { m_RequestedTransition = id; return true; }
            return Current?.Child?.RequestTransition(id) ?? false;
        }
        internal IEnumerable<string> ActiveIds()
        {
            if (Current == null) yield break;
            yield return Current.Node.Id;
            if (Current.Child != null) foreach (var id in Current.Child.ActiveIds()) yield return id;
        }
        internal IEnumerable<string> ActivePaths()
        {
            if (Current == null) yield break;
            yield return Current.Context.Path;
            if (Current.Child != null) foreach (var path in Current.Child.ActivePaths()) yield return path;
        }
        internal StateBlackboard FindBlackboard(string path)
        {
            if (Path == path) return Blackboard;
            foreach (var state in States.Values)
            {
                if (state.Context.Path == path) return state.Context.Blackboard;
                var found = state.Child?.FindBlackboard(path); if (found != null) return found;
            }
            return null;
        }
        private void Record(StateHistoryKind kind, StateInstance state, StateInstance previous, StateConnection edge, string reason)
        {
            Runner.Record(kind, Graph, state.Node.Id, previous?.Node.Id, edge?.Id, state.Context.Path, reason, Runner.ElapsedTime - (previous ?? state).EnteredAt, state.Context.Blackboard);
        }
        public void Dispose()
        {
            Stop();
            foreach (var state in States.Values) state.Dispose();
            States.Clear();
        }
    }
}
