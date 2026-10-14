using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEngine;

namespace AS.Modules.Stating.Editor.Graph
{
    [CustomEditor(typeof(StateMachineGraph))]
    internal sealed class StateMachineGraphInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (StateMachineGraph)target;
            GUILayout.Label(asset.Graph.Name, EditorStyles.boldLabel); EditorGUILayout.LabelField("States", asset.Graph.Nodes.Count.ToString());
            if (GUILayout.Button("Open State Machine Graph", GUILayout.Height(30))) StateGraphWindow.Open(asset);
            foreach (var issue in StateGraphValidator.Validate(asset.Graph)) EditorGUILayout.HelpBox(issue.Message, issue.Severity == StateValidationSeverity.Error ? MessageType.Error : MessageType.Warning);
        }
    }
    [CustomEditor(typeof(ReusableStateAsset), true)]
    internal sealed class ReusableStateInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (ReusableStateAsset)target;
            EditorGUILayout.LabelField("State", asset.Definition.Name); EditorGUILayout.LabelField("C# type", asset.Definition.StateTypeName);
            EditorGUILayout.HelpBox("Shared definition. Linked graphs use these defaults unless they supply local overrides.", MessageType.Info);
            if (GUILayout.Button("Edit Shared Definition", GUILayout.Height(30))) StateGraphWindow.Open(asset);
        }
    }
    [CustomEditor(typeof(StateMachineRunner))]
    internal sealed class StateMachineRunnerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var iterator = serializedObject.GetIterator(); bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.name == "m_Overrides") continue;
                using (new EditorGUI.DisabledScope(iterator.name == "m_Script")) EditorGUILayout.PropertyField(iterator, true);
            }
            var overrides = serializedObject.FindProperty("m_Overrides");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Runner Blackboard Overrides", EditorStyles.boldLabel);
            for (int i = 0; i < overrides.arraySize; i++)
            {
                var element = overrides.GetArrayElementAtIndex(i); EditorGUILayout.PropertyField(element.FindPropertyRelative("VariableId"));
                var valueProperty = element.FindPropertyRelative("Value"); var value = (StateValue)valueProperty.boxedValue;
                EditorGUI.BeginChangeCheck(); StateValueGUI.Draw("Value", value); if (EditorGUI.EndChangeCheck()) valueProperty.boxedValue = value;
                if (GUILayout.Button("Remove Override", EditorStyles.miniButton)) { overrides.DeleteArrayElementAtIndex(i); break; }
            }
            var runner = (StateMachineRunner)target;
            if (runner.Graph != null)
                foreach (var variable in runner.Graph.Graph.Variables)
                    if (GUILayout.Button("Override " + variable.Name, EditorStyles.miniButton))
                    {
                        int index = overrides.arraySize; overrides.InsertArrayElementAtIndex(index);
                        overrides.GetArrayElementAtIndex(index).boxedValue = new StateVariableOverride { VariableId = variable.Id, Value = JsonUtility.FromJson<StateValue>(JsonUtility.ToJson(variable.DefaultValue)) };
                    }
            serializedObject.ApplyModifiedProperties();
            if (runner.Graph != null && GUILayout.Button("Open Graph")) StateGraphWindow.Open(runner.Graph);
            if (!Application.isPlaying) return;
            EditorGUILayout.LabelField("Active", runner.ActivePath);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(runner.IsRunning ? "Stop" : "Start")) { if (runner.IsRunning) runner.StopMachine(); else runner.StartMachine(); }
                if (GUILayout.Button(runner.IsPaused ? "Resume" : "Pause")) { if (runner.IsPaused) runner.Resume(); else runner.Pause(); }
                if (GUILayout.Button("Reset")) runner.ResetMachine();
            }
        }
    }
    [InitializeOnLoad]
    internal static class StateGraphBreakpointBridge
    {
        static StateGraphBreakpointBridge() { StateMachineRunner.BreakpointHit += OnHit; }
        private static void OnHit(StateMachineRunner runner, string path)
        {
            if (!EditorApplication.isPlaying) return;
            Selection.activeGameObject = runner.gameObject; EditorApplication.isPaused = true;
        }
    }
}
