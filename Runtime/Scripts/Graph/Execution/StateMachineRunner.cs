using System;
using System.Collections.Generic;
using AS.Modules.Stating.Core;
using UnityEngine;
using UnityEngine.Events;

namespace AS.Modules.Stating.Graph
{
    [AddComponentMenu("ArushaSoft/Stating/State Machine Runner")]
    public sealed class StateMachineRunner : MonoBehaviour
    {
        [SerializeField] private StateMachineGraph m_Graph;
        [SerializeField] private Target m_Target;
        [SerializeField] private bool m_AutoStart = true;
        [SerializeField] private string m_InitialStateId;
        [SerializeField] private List<StateVariableOverride> m_Overrides = new List<StateVariableOverride>();
        [SerializeField] private bool m_RecordHistory = true;
        [SerializeField] private bool m_RecordSnapshots;
        [SerializeField, Min(1)] private int m_HistoryCapacity = 256;
        [SerializeField] private UnityEvent<string> m_OnStateChanged = new UnityEvent<string>();
        private StateGraphInstance m_Instance;
        private readonly Queue<StateRuntimeEvent> m_Events = new Queue<StateRuntimeEvent>();
        private bool m_IsTicking;
        private bool m_StopRequested;
        private bool m_DisposeRequested;
        private double m_Time;
        private StateHistory m_History;
        public static readonly HashSet<StateMachineRunner> Instances = new HashSet<StateMachineRunner>();
        public static event Action<StateMachineRunner, string> BreakpointHit;
        public event Action<StateHistoryRecord> StateChanged;
        public StateMachineGraph Graph { get => m_Graph; set { if (IsRunning) throw new InvalidOperationException("Stop before replacing the graph."); DisposeInstance(); m_Graph = value; } }
        public Target Target { get => m_Target; set => m_Target = value; }
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public string ActiveStateId => m_Instance?.ActiveNodeId;
        public string ActivePath => m_Instance?.ActivePath ?? "";
        public StateBlackboard Blackboard => m_Instance?.Blackboard;
        public StateHistory History => m_History ?? (m_History = new StateHistory(m_HistoryCapacity) { Recording = m_RecordHistory, Snapshots = m_RecordSnapshots });
        public double ElapsedTime => m_Time;
        internal IReadOnlyList<StateVariableOverride> Overrides => m_Overrides;
        public IEnumerable<string> ActiveNodeIds => m_Instance?.ActiveIds() ?? Array.Empty<string>();
        public IEnumerable<string> ActiveStatePaths => m_Instance?.ActivePaths() ?? Array.Empty<string>();

        private void OnEnable() { Instances.Add(this); }
        private void Start() { if (m_AutoStart) StartMachine(); }
        private void Update() { Tick(Time.deltaTime); }
        private void FixedUpdate() { FixedTick(Time.fixedDeltaTime); }
        private void OnDisable() { StopMachine(); Instances.Remove(this); }
        private void OnDestroy() { DisposeInstance(); }
        public void StartMachine()
        {
            if (IsRunning) return;
            if (m_Graph == null) throw new InvalidOperationException("Assign a State Machine Graph before starting.");
            var issues = StateGraphValidator.Validate(m_Graph.Graph);
            foreach (var issue in issues) if (issue.Severity == StateValidationSeverity.Error) throw new InvalidOperationException($"{m_Graph.name}: {issue.Message} (node {issue.NodeId}).");
            if (m_Target == null) m_Target = GetComponent<Target>();
            if (m_Target == null) m_Target = gameObject.AddComponent<Target>();
            DisposeInstance();
            m_Time = 0; m_Events.Clear(); IsPaused = false; IsRunning = true;
            m_IsTicking = true;
            try { m_Instance = new StateGraphInstance(this, m_Graph.Graph, null, m_Graph.Graph.Id, null, 0); m_Instance.Start(m_InitialStateId); }
            catch { IsRunning = false; m_DisposeRequested = true; throw; }
            finally { EndTick(); }
        }
        public void StopMachine()
        {
            if (m_IsTicking) { m_StopRequested = true; return; }
            if (!IsRunning) return;
            IsRunning = false; IsPaused = false;
            m_Instance?.Stop(); m_Events.Clear();
        }
        public void Pause() { if (IsRunning) IsPaused = true; }
        public void Resume() { if (IsRunning) IsPaused = false; }
        public void ResetMachine() { StopMachine(); if (m_IsTicking) throw new InvalidOperationException("Reset is only supported outside a graph callback."); DisposeInstance(); StartMachine(); }
        public void SendEvent(string name, object payload = null)
        {
            if (!IsRunning) return;
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An event name is required.");
            if (m_Events.Count >= 256) throw new InvalidOperationException("State event queue limit (256) exceeded.");
            m_Events.Enqueue(new StateRuntimeEvent(name, payload));
        }
        public void Tick(float deltaTime)
        {
            if (!IsRunning || IsPaused || m_IsTicking) return;
            if (deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            m_IsTicking = true;
            try
            {
                m_Time += deltaTime;
                // Process only the events present at the start of this pass. New events wait until the next tick.
                int count = Math.Min(m_Events.Count, 64);
                for (int i = 0; i < count && !m_StopRequested; i++) m_Instance.Dispatch(m_Events.Dequeue());
                if (!m_StopRequested) m_Instance.Tick(deltaTime);
                if (m_Instance.Completed) m_StopRequested = true;
            }
            catch (Exception exception) { Fail(exception); }
            finally { EndTick(); }
        }
        public void FixedTick(float deltaTime)
        {
            if (!IsRunning || IsPaused || m_IsTicking) return;
            m_IsTicking = true;
            try { m_Instance.FixedTick(deltaTime); }
            catch (Exception exception) { Fail(exception); }
            finally { EndTick(); }
        }
        private void EndTick()
        {
            m_IsTicking = false;
            if (m_StopRequested) { m_StopRequested = false; StopMachine(); }
            if (m_DisposeRequested) { m_DisposeRequested = false; DisposeInstance(); }
        }
        private void Fail(Exception exception)
        {
            m_StopRequested = true;
            Debug.LogException(new InvalidOperationException($"Graph '{m_Graph?.name}', state '{ActivePath}': {exception.Message}", exception), this);
        }
        public bool RequestTransition(string transitionId) => IsRunning && !IsPaused && m_Instance != null && m_Instance.RequestTransition(transitionId);
        public StateBlackboard FindBlackboard(string path) => m_Instance?.FindBlackboard(path);
        internal void Record(StateHistoryKind kind, StateGraphData graph, string node, string previous, string edge, string path, string reason, double duration, StateBlackboard blackboard)
        {
            if (kind == StateHistoryKind.Enter) m_OnStateChanged.Invoke(path);
            if (!History.Recording && StateChanged == null) return;
            var record = new StateHistoryRecord { Kind = kind, GraphId = graph.Id, NodeId = node, PreviousNodeId = previous, TransitionId = edge, Path = path, Reason = reason, Duration = duration, Timestamp = m_Time, Blackboard = History.Snapshots ? blackboard.Snapshot() : null };
            History.Add(record);
            StateChanged?.Invoke(record);
        }
        internal void HitBreakpoint(string path)
        {
#if UNITY_EDITOR
            BreakpointHit?.Invoke(this, path);
#endif
        }
        private void DisposeInstance()
        {
            if (m_IsTicking) { m_StopRequested = true; m_DisposeRequested = true; return; }
            m_Instance?.Dispose(); m_Instance = null;
        }
    }
    internal readonly struct StateRuntimeEvent
    {
        public readonly string Name;
        public readonly object Payload;
        public StateRuntimeEvent(string name, object payload) { Name = name; Payload = payload; }
    }
}
