using AS.Modules.Stating.Core;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    /// <summary>Optional ready-to-use state. Existing State&lt;T&gt; subclasses also work in graphs.</summary>
    public class GraphState : State<Target>
    {
        [SerializeField] private string m_Message;
        [SerializeField] private bool m_LogLifecycle;
        protected override void OnEnter() { if (m_LogLifecycle) Debug.Log($"Enter {Context?.Path}: {m_Message}", this); }
        protected override void OnExit() { if (m_LogLifecycle) Debug.Log($"Exit {Context?.Path}", this); }
        public void Log(string message) => Debug.Log(message, this);
        [StateCallable(Pure = true)] public bool IsTrue(bool value) => value;
        [StateCallable(Pure = true)] public float Multiply(float value, float factor = 1) => value * factor;
    }
}
