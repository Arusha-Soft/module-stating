using AS.Modules.Stating.Graph;
using UnityEngine;

namespace AS.Modules.Stating.Samples
{
    [RequireComponent(typeof(StateMachineRunner))]
    public sealed class GraphExampleControls : MonoBehaviour
    {
        private StateMachineRunner m_Runner;
        private void Awake() { m_Runner = GetComponent<StateMachineRunner>(); }
        private void OnGUI()
        {
            if (m_Runner?.Blackboard == null) return;
            GUILayout.BeginArea(new Rect(24, 24, 360, 220), GUI.skin.box);
            GUILayout.Label("Stating · Three-State Example"); GUILayout.Label("Active: " + m_Runner.ActivePath);
            float speed = m_Runner.Blackboard.Get<float>("Speed");
            GUILayout.Label("Speed: " + speed.ToString("0.0")); speed = GUILayout.HorizontalSlider(speed, 0, 6); m_Runner.Blackboard.Set("Speed", speed);
            GUILayout.Label("Idle → Walking at 0.1 · Walking → Running at 4");
            if (GUILayout.Button("Reset to Idle")) { m_Runner.Blackboard.Set("Speed", 0f); m_Runner.SendEvent("Reset"); }
            if (GUILayout.Button(m_Runner.IsPaused ? "Resume" : "Pause")) { if (m_Runner.IsPaused) m_Runner.Resume(); else m_Runner.Pause(); }
            GUILayout.Label("Open the graph's Debug view for active states and history."); GUILayout.EndArea();
        }
    }
}
