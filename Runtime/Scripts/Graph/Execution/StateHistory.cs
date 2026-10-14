using System;
using System.Collections.Generic;

namespace AS.Modules.Stating.Graph
{
    public sealed class StateHistoryRecord
    {
        public double Timestamp;
        public double Duration;
        public StateHistoryKind Kind;
        public string GraphId;
        public string NodeId;
        public string PreviousNodeId;
        public string TransitionId;
        public string Path;
        public string Reason;
        public Dictionary<string, object> Blackboard;
    }
    /// <summary>A fixed-capacity ring. Recording and snapshots are independently optional.</summary>
    public sealed class StateHistory
    {
        private readonly StateHistoryRecord[] m_Records;
        private int m_Start;
        public bool Recording { get; set; }
        public bool Snapshots { get; set; }
        public int Count { get; private set; }
        public StateHistory(int capacity = 256) { m_Records = new StateHistoryRecord[Math.Max(1, capacity)]; }
        public StateHistoryRecord this[int index] => index >= 0 && index < Count ? m_Records[(m_Start + index) % m_Records.Length] : throw new ArgumentOutOfRangeException(nameof(index));
        public void Add(StateHistoryRecord record)
        {
            if (!Recording) return;
            int slot = (m_Start + Count) % m_Records.Length;
            m_Records[slot] = record;
            if (Count < m_Records.Length) Count++; else m_Start = (m_Start + 1) % m_Records.Length;
        }
        public void Clear() { Array.Clear(m_Records, 0, m_Records.Length); m_Start = 0; Count = 0; }
    }
}
