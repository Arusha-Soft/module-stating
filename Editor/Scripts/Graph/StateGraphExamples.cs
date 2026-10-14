using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AS.Modules.Stating.Editor.Graph
{
    public static class StateGraphExamples
    {
        [MenuItem("Assets/Create/State Machine/Three-State Example")]
        public static void CreateExample()
        {
            var asset = Build("Assets/Modules/Stating/Samples/VisualGraph", true);
            Selection.activeObject = asset; StateGraphWindow.Open(asset);
        }
        public static void GenerateSampleAssets() => Build("Assets/Modules/Stating/Samples/VisualGraph", true);
        public static void GenerateVerificationAssets() { GenerateSampleAssets(); StateGraphBuildSupport.GenerateBindings(); }
        public static void BuildVerificationPlayer()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Modules/Stating/Samples/VisualGraph" });
            if (scenes.Length == 0) throw new System.InvalidOperationException("Generate the sample assets first.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { AssetDatabase.GUIDToAssetPath(scenes[0]) }, locationPathName = "Build/StatingSmoke.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new System.InvalidOperationException("Standalone verification build failed.");
        }
        public static StateMachineGraph Build(string directory, bool scene)
        {
            if (!AssetDatabase.IsValidFolder(directory))
            {
                string[] segments = directory.Split('/'); string parent = segments[0];
                for (int i = 1; i < segments.Length; i++) { if (!AssetDatabase.IsValidFolder(parent + "/" + segments[i])) AssetDatabase.CreateFolder(parent, segments[i]); parent += "/" + segments[i]; }
            }
            var asset = ScriptableObject.CreateInstance<StateMachineGraph>(); asset.Graph.Name = "Movement · Three States";
            var speed = new StateVariable { Name = "Speed", Category = "Movement", Exposed = true, DefaultValue = StateValue.From(0f), Description = "Use the runner Blackboard or the sample control panel in Play Mode." }; asset.Graph.Variables.Add(speed);
            var idle = StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), new Vector2(260, 140)); idle.Name = "Idle";
            var walk = StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), new Vector2(650, 140)); walk.Name = "Walking";
            var run = StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), new Vector2(1040, 140)); run.Name = "Running";
            var metadata = StateMetadata.For(typeof(GraphState));
            foreach (var node in new[] { idle, walk, run })
                foreach (var field in metadata.Fields)
                    if (field.Value.FieldType == typeof(bool)) node.Fields.Add(new StateFieldValue { MemberId = field.Key, Value = StateValue.From(true) });
                    else if (field.Value.FieldType == typeof(string)) node.Fields.Add(new StateFieldValue { MemberId = field.Key, Value = StateValue.From(node.Name) });
            StateGraphAuthoring.Connect(asset.Graph, asset.Graph.Nodes[0], "out", idle, "in", StateConnectionKind.Transition);
            var toWalk = StateGraphAuthoring.Connect(asset.Graph, idle, "out", walk, "in", StateConnectionKind.Transition);
            toWalk.Transition.Conditions.Add(new BlackboardStateCondition { VariableId = speed.Id, Comparison = StateComparison.GreaterOrEqual, Value = StateValue.From(0.1f) });
            var toRun = StateGraphAuthoring.Connect(asset.Graph, walk, "out", run, "in", StateConnectionKind.Transition);
            toRun.Transition.Conditions.Add(new BlackboardStateCondition { VariableId = speed.Id, Comparison = StateComparison.GreaterOrEqual, Value = StateValue.From(4f) });
            var any = new StateNodeData { Kind = StateNodeKind.AnyState, Name = "Any State", Position = new Rect(260, 480, 170, 100) }; asset.Graph.Nodes.Add(any);
            var reset = StateGraphAuthoring.Connect(asset.Graph, any, "out", idle, "in", StateConnectionKind.Transition); reset.Transition.EventName = "Reset"; reset.Transition.Priority = 100;
            asset.Graph.Groups.Add(new StateGroupData { Title = "Movement", NodeIds = new System.Collections.Generic.List<string> { idle.Id, walk.Id, run.Id }, Position = new Rect(230, 100, 1120, 300) });
            asset.Graph.Notes.Add(new StateNoteData { Title = "Try it", Contents = "Play the sample scene. Increase Speed to 1, then 5. Send Reset to return to Idle. Open Debug and select the sample runner to watch the history.", Position = new Rect(600, 480, 470, 180) });
            string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/Three State Machine.asset"); AssetDatabase.CreateAsset(asset, path);
            if (scene)
            {
                var original = SceneManager.GetActiveScene();
                bool createScene = Application.isBatchMode || !string.IsNullOrEmpty(original.path);
                var sampleScene = createScene ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive) : original;
                if (createScene) SceneManager.SetActiveScene(sampleScene);
                var obj = new GameObject("Three State Machine"); var runner = obj.AddComponent<StateMachineRunner>(); runner.Graph = asset;
                var controllerType = StateType.Resolve("AS.Modules.Stating.Samples.GraphExampleControls, AS.Modules.Stating.Samples");
                if (controllerType != null) obj.AddComponent(controllerType);
                PrefabUtility.SaveAsPrefabAsset(obj, AssetDatabase.GenerateUniqueAssetPath(directory + "/Three State Runner.prefab"));
                var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera"; cameraObject.AddComponent<Camera>().backgroundColor = new Color(0.12f, 0.13f, 0.15f);
                if (createScene)
                {
                    EditorSceneManager.SaveScene(sampleScene, AssetDatabase.GenerateUniqueAssetPath(directory + "/Three State Example.unity"));
                    if (!Application.isBatchMode) { if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(sampleScene, true); }
                }
                else { UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(cameraObject); }
            }
            AssetDatabase.SaveAssets(); return asset;
        }
    }
}
