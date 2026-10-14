using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Editor.Graph;
using AS.Modules.Stating.Graph;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AS.Modules.Stating.Tests.Editor
{
    public sealed class StateGraphEditorTests
    {
        private const string TestDirectory = "Assets/StatingGraphTestTemp";
        [SetUp] public void Setup() { if (!AssetDatabase.IsValidFolder(TestDirectory)) AssetDatabase.CreateFolder("Assets", "StatingGraphTestTemp"); }
        [TearDown] public void Cleanup()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<StateGraphWindow>()) window.Close();
            AssetDatabase.DeleteAsset(TestDirectory);
        }
        private StateMachineGraph Graph()
        {
            var asset = ScriptableObject.CreateInstance<StateMachineGraph>();
            var state = StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), Vector2.zero);
            StateGraphAuthoring.Connect(asset.Graph, asset.Graph.Nodes[0], "out", state, "in", StateConnectionKind.Transition);
            AssetDatabase.CreateAsset(asset, TestDirectory + "/Graph.asset"); return asset;
        }
        [Test] public void ReflectionDiscoversFieldsAndOverloadsButExcludesUnsafeSignatures()
        {
            var metadata = StateMetadata.For(typeof(GraphProbeState));
            Assert.That(metadata.Fields.Values.Any(f => f.Name == "MovementSpeed"), Is.True);
            Assert.That(metadata.Fields.Values.Any(f => f.Name == "m_JumpHeight"), Is.True);
            Assert.That(metadata.Methods.Values.Count(m => m.Method.Name == "Overload"), Is.EqualTo(2));
            Assert.That(metadata.Methods.Values.Any(m => m.Method.Name == "Hidden" || m.Method.Name == "Unsupported" || m.Method.Name == "Enter"), Is.False);
            Assert.That(metadata.Warnings.Count, Is.GreaterThan(0));
        }
        [Test] public void TypedPortsRejectImplicitNumericConversion()
        {
            Assert.That(StateGraphPorts.Compatible(new StatePort("out", "", typeof(float), false), new StatePort("in", "", typeof(int), true)), Is.False);
            Assert.That(StateGraphPorts.Compatible(new StatePort("out", "", typeof(float), false), new StatePort("in", "", typeof(float), true)), Is.True);
        }
        [Test] public void NestedGraphsManagedDefaultsAndLayoutSurviveSerialization()
        {
            var asset = Graph(); var parent = asset.Graph.Nodes[1]; parent.Children = StateGraphData.Create("Nested");
            var child = StateGraphAuthoring.AddState(parent.Children, typeof(GraphState), new Vector2(450, 200)); StateGraphAuthoring.Connect(parent.Children, parent.Children.Nodes[0], "out", child, "in", StateConnectionKind.Transition);
            asset.Graph.Variables.Add(new StateVariable { Name = "Custom", DefaultValue = StateValue.From(new GraphProbeValue()) });
            asset.Graph.Groups.Add(new StateGroupData { Title = "Movement", NodeIds = new List<string> { parent.Id } }); asset.Graph.Notes.Add(new StateNoteData { Contents = "Persistent note" });
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets(); AssetDatabase.ForceReserializeAssets(new[] { TestDirectory + "/Graph.asset" });
            var copy = StateGraphCopy.Graph(asset.Graph);
            Assert.That(copy.Nodes[1].Children.Nodes[1].Position.x, Is.EqualTo(450)); Assert.That(copy.Groups[0].Title, Is.EqualTo("Movement")); Assert.That(copy.Notes[0].Contents, Is.EqualTo("Persistent note")); Assert.That(((GraphProbeValue)copy.Variables[0].DefaultValue.Get()).Values[1], Is.EqualTo(2));
        }
        [Test] public void UndoRedoRestoresGraphNodeCreation()
        {
            var asset = Graph(); Undo.RegisterCompleteObjectUndo(asset, "Create state"); StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), Vector2.one); Undo.FlushUndoRecordObjects();
            Assert.That(asset.Graph.Nodes.Count, Is.EqualTo(3)); Undo.PerformUndo(); Assert.That(asset.Graph.Nodes.Count, Is.EqualTo(2)); Undo.PerformRedo(); Assert.That(asset.Graph.Nodes.Count, Is.EqualTo(3));
        }
        [Test] public void IndependentCopyPreservesConnectionsAndDoesNotMutateSource()
        {
            var asset = Graph(); var copy = StateGraphCopy.Selection(asset.Graph, new HashSet<string>(asset.Graph.Nodes.Select(n => n.Id)), new Vector2(50, 50));
            Assert.That(copy.Nodes[1].Id, Is.Not.EqualTo(asset.Graph.Nodes[1].Id)); Assert.That(copy.Connections[0].TargetNodeId, Is.EqualTo(copy.Nodes[1].Id)); copy.Nodes[1].Name = "Changed"; Assert.That(asset.Graph.Nodes[1].Name, Is.Not.EqualTo("Changed"));
        }
        [Test] public void ReusableAssetRetainsTreeAndLinkedReferencesSurviveDuplication()
        {
            var asset = Graph(); var parent = asset.Graph.Nodes[1]; parent.Children = StateGraphData.Create("Child Tree"); var child = StateGraphAuthoring.AddState(parent.Children, typeof(GraphState), Vector2.zero); StateGraphAuthoring.Connect(parent.Children, parent.Children.Nodes[0], "out", child, "in", StateConnectionKind.Transition);
            var reusable = StateGraphAuthoring.SaveReusable(asset.Graph, parent, TestDirectory + "/Tree.asset", true); var linked = StateGraphAuthoring.AddLinked(asset.Graph, reusable, Vector2.zero);
            var copy = StateGraphCopy.Node(linked); Assert.That(copy.Template, Is.SameAs(reusable)); var independent = StateGraphCopy.Independent(linked); Assert.That(independent.IsLinked, Is.False); Assert.That(independent.Children.Nodes.Count, Is.EqualTo(2));
            independent.Children.Nodes[1].Name = "Private"; Assert.That(reusable.Definition.Children.Nodes[1].Name, Is.Not.EqualTo("Private"));
        }
        [Test] public void MissingTypesAndMembersAreReportedWithoutDiscardingData()
        {
            var asset = Graph(); asset.Graph.Nodes[1].StateTypeName = "Missing.State, Missing.Assembly"; var before = JsonUtility.ToJson(asset.Graph); var issues = StateGraphValidator.Validate(asset.Graph);
            Assert.That(issues.Any(i => i.Severity == StateValidationSeverity.Error), Is.True); Assert.That(JsonUtility.ToJson(asset.Graph), Is.EqualTo(before));
        }
        [Test] public void CircularReusableReferencesAreRejected()
        {
            var template = ScriptableObject.CreateInstance<StateTreeAsset>(); template.Definition.StateTypeName = StateType.Id(typeof(GraphState)); template.Definition.Children = StateGraphData.Create();
            var linked = StateGraphAuthoring.AddLinked(template.Definition.Children, template, Vector2.zero); StateGraphAuthoring.Connect(template.Definition.Children, template.Definition.Children.Nodes[0], "out", linked, "in", StateConnectionKind.Transition);
            Assert.That(StateGraphValidator.Validate(template.Definition.Children).Any(i => i.Message.Contains("Circular")), Is.True); Object.DestroyImmediate(template);
        }
        [Test] public void ValueCyclesAreRejected()
        {
            var asset = Graph(); var a = new StateNodeData { Kind = StateNodeKind.Not }; var b = new StateNodeData { Kind = StateNodeKind.Not }; asset.Graph.Nodes.Add(a); asset.Graph.Nodes.Add(b);
            StateGraphAuthoring.Connect(asset.Graph, a, "value", b, "a", StateConnectionKind.Value); StateGraphAuthoring.Connect(asset.Graph, b, "value", a, "a", StateConnectionKind.Value);
            Assert.That(StateGraphValidator.Validate(asset.Graph).Any(i => i.Message.Contains("cycle")), Is.True);
        }
        [Test] public void NestSelectionPreservesInternalStateTransitions()
        {
            var asset = Graph(); var first = asset.Graph.Nodes[1]; var second = StateGraphAuthoring.AddState(asset.Graph, typeof(GraphState), Vector2.one); StateGraphAuthoring.Connect(asset.Graph, first, "out", second, "in", StateConnectionKind.Transition);
            var parent = StateGraphAuthoring.NestSelection(asset.Graph, new HashSet<string> { first.Id, second.Id });
            Assert.That(parent.Children.Nodes.Count, Is.EqualTo(3)); Assert.That(StateGraphValidator.Validate(asset.Graph).Any(i => i.Severity == StateValidationSeverity.Error), Is.False);
        }
        [UnityTest] public IEnumerator GraphWindowCreatesNativeCanvasBlackboardInspectorAndStatePorts()
        {
            var asset = Graph(); var window = StateGraphWindow.Open(asset); yield return null; yield return null;
            Assert.That(window.rootVisualElement.Q<GraphView>(), Is.Not.Null); Assert.That(window.rootVisualElement.Q("blackboard-panel"), Is.Not.Null); Assert.That(window.rootVisualElement.Q("inspector-panel"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Query<Port>().ToList().Count, Is.GreaterThan(4)); window.Close();
        }
    }
}
