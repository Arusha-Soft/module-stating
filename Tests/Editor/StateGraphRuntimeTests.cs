using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Editor.Graph;
using AS.Modules.Stating.Graph;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AS.Modules.Stating.Tests.Editor
{
    public sealed class StateGraphRuntimeTests
    {
        private readonly List<Object> m_Objects = new List<Object>();
        [SetUp] public void Setup() { GraphProbeState.Calls.Clear(); }
        [TearDown] public void Cleanup() { foreach (var obj in m_Objects) if (obj != null) Object.DestroyImmediate(obj); m_Objects.Clear(); }
        private StateMachineGraph Asset()
        {
            var asset = ScriptableObject.CreateInstance<StateMachineGraph>(); m_Objects.Add(asset); return asset;
        }
        private StateNodeData State(StateGraphData graph, string name)
        {
            var node = StateGraphAuthoring.AddState(graph, typeof(GraphProbeState), Vector2.zero); node.Name = name;
            node.Fields.Add(new StateFieldValue { MemberId = StateType.FieldId(typeof(GraphProbeState).GetField("Label")), Value = StateValue.From(name) }); return node;
        }
        private StateMachineRunner Runner(StateMachineGraph asset)
        {
            var go = new GameObject("Test Runner"); m_Objects.Insert(0, go); var runner = go.AddComponent<StateMachineRunner>(); runner.Graph = asset; runner.StartMachine(); return runner;
        }
        private StateConnection Link(StateGraphData graph, StateNodeData from, StateNodeData to) => StateGraphAuthoring.Connect(graph, from, "out", to, "in", StateConnectionKind.Transition);
        [Test] public void ExistingStateLifecycleIsDrivenByGraph()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state); var runner = Runner(asset);
            runner.Tick(0.1f); runner.FixedTick(0.02f); runner.StopMachine();
            CollectionAssert.AreEqual(new[] { "A.Enter", "A.Update", "A.Fixed", "A.Exit" }, GraphProbeState.Calls);
        }
        [Test] public void HighestPriorityWinsWithStableTieBreak()
        {
            var asset = Asset(); var a = State(asset.Graph, "A"); var b = State(asset.Graph, "B"); var c = State(asset.Graph, "C"); Link(asset.Graph, asset.Graph.Nodes[0], a);
            Link(asset.Graph, a, b).Transition.Priority = 4; Link(asset.Graph, a, c).Transition.Priority = 5;
            var runner = Runner(asset); runner.Tick(0); Assert.That(runner.ActiveStateId, Is.EqualTo(c.Id));
        }
        [Test] public void EqualPriorityUsesSerializedOrder()
        {
            var asset = Asset(); var a = State(asset.Graph, "A"); var b = State(asset.Graph, "B"); var c = State(asset.Graph, "C"); Link(asset.Graph, asset.Graph.Nodes[0], a); Link(asset.Graph, a, b); Link(asset.Graph, a, c);
            var runner = Runner(asset); runner.Tick(0); Assert.That(runner.ActiveStateId, Is.EqualTo(b.Id));
        }
        [Test] public void EventsRemainQueuedWhilePaused()
        {
            var asset = Asset(); var a = State(asset.Graph, "A"); var b = State(asset.Graph, "B"); Link(asset.Graph, asset.Graph.Nodes[0], a); Link(asset.Graph, a, b).Transition.EventName = "Go";
            var runner = Runner(asset); runner.Pause(); runner.SendEvent("Go"); runner.Tick(1); Assert.That(runner.ActiveStateId, Is.EqualTo(a.Id)); runner.Resume(); runner.Tick(0); Assert.That(runner.ActiveStateId, Is.EqualTo(b.Id));
        }
        [Test] public void DelayedConditionsMustRemainTrue()
        {
            var asset = Asset(); var flag = new StateVariable { Name = "Flag", DefaultValue = StateValue.From(false) }; asset.Graph.Variables.Add(flag);
            var a = State(asset.Graph, "A"); var b = State(asset.Graph, "B"); Link(asset.Graph, asset.Graph.Nodes[0], a); var edge = Link(asset.Graph, a, b); edge.Transition.Delay = 1;
            edge.Transition.Conditions.Add(new BlackboardStateCondition { VariableId = flag.Id, Value = StateValue.From(true) });
            var runner = Runner(asset); runner.Blackboard.Set("Flag", true); runner.Tick(0); runner.Tick(0.5f); runner.Blackboard.Set("Flag", false); runner.Tick(0.1f); runner.Blackboard.Set("Flag", true); runner.Tick(0); runner.Tick(0.9f); Assert.That(runner.ActiveStateId, Is.EqualTo(a.Id)); runner.Tick(0.2f); Assert.That(runner.ActiveStateId, Is.EqualTo(b.Id));
        }
        [Test] public void ParentEntersBeforeChildAndChildExitsFirst()
        {
            var asset = Asset(); var parent = State(asset.Graph, "Parent"); parent.Children = StateGraphData.Create(); var child = State(parent.Children, "Child"); Link(parent.Children, parent.Children.Nodes[0], child); var other = State(asset.Graph, "Other"); Link(asset.Graph, asset.Graph.Nodes[0], parent); Link(asset.Graph, parent, other).Transition.EventName = "Leave";
            var runner = Runner(asset); CollectionAssert.AreEqual(new[] { "Parent.Enter", "Child.Enter" }, GraphProbeState.Calls);
            GraphProbeState.Calls.Clear(); runner.SendEvent("Leave"); runner.Tick(0);
            CollectionAssert.AreEqual(new[] { "Child.Exit", "Parent.Exit", "Other.Enter", "Other.Update" }, GraphProbeState.Calls); Assert.That(runner.ActivePath, Is.EqualTo("Other"));
        }
        [Test] public void ParentTransitionInterruptsChildTransition()
        {
            var asset = Asset(); var p = State(asset.Graph, "P"); p.Children = StateGraphData.Create(); var a = State(p.Children, "A"); var b = State(p.Children, "B"); Link(p.Children, p.Children.Nodes[0], a); Link(p.Children, a, b);
            var q = State(asset.Graph, "Q"); Link(asset.Graph, asset.Graph.Nodes[0], p); Link(asset.Graph, p, q); var runner = Runner(asset); runner.Tick(0);
            Assert.That(runner.ActiveStateId, Is.EqualTo(q.Id)); Assert.That(GraphProbeState.Calls, Does.Not.Contain("B.Enter"));
        }
        [Test] public void BlackboardMutableDefaultsAreIndependentAcrossRunners()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state); var defaultValue = new GraphProbeValue(); asset.Graph.Variables.Add(new StateVariable { Name = "Data", DefaultValue = StateValue.From(defaultValue) });
            var first = Runner(asset); var second = Runner(asset); first.Blackboard.Get<GraphProbeValue>("Data").Values[0] = 99;
            Assert.That(second.Blackboard.Get<GraphProbeValue>("Data").Values[0], Is.EqualTo(1)); Assert.That(defaultValue.Values[0], Is.EqualTo(1));
        }
        [Test] public void NestedScopeOnlySeesExposedParentValues()
        {
            var root = new StateBlackboard(new[] { new StateVariable { Name = "Private", DefaultValue = StateValue.From(1) }, new StateVariable { Name = "Public", Exposed = true, DefaultValue = StateValue.From(2) } });
            var child = new StateBlackboard(Array.Empty<StateVariable>(), root); Assert.That(child.Contains("Private"), Is.False); Assert.That(child.Get<int>("Public"), Is.EqualTo(2));
        }
        [Test] public void LinkedTreesKeepRuntimeValuesPrivateAndApplyReferenceOverrides()
        {
            var template = ScriptableObject.CreateInstance<StateTreeAsset>(); m_Objects.Add(template); template.Definition.StateTypeName = StateType.Id(typeof(GraphProbeState)); template.Definition.Children = StateGraphData.Create();
            var child = State(template.Definition.Children, "Child"); Link(template.Definition.Children, template.Definition.Children.Nodes[0], child);
            var variable = new StateVariable { Name = "Speed", Exposed = true, DefaultValue = StateValue.From(4f) }; template.Definition.Variables.Add(variable);
            var asset = Asset(); var linked = StateGraphAuthoring.AddLinked(asset.Graph, template, Vector2.zero); linked.Overrides.Add(new StateVariableOverride { VariableId = variable.Id, Value = StateValue.From(7f) }); Link(asset.Graph, asset.Graph.Nodes[0], linked);
            var runner = Runner(asset); Assert.That(runner.FindBlackboard(asset.Graph.Id + "/" + linked.Id).Get<float>("Speed"), Is.EqualTo(7)); Assert.That(variable.DefaultValue.Get(), Is.EqualTo(4f));
        }
        [Test] public void SideEffectFunctionOnlyRunsThroughExecutionFlow()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state); var method = StateMetadata.For(typeof(GraphProbeState)).Methods.Values.First(m => m.Method.Name == "Attack");
            var function = new StateNodeData { Kind = StateNodeKind.Function, Name = "Attack", TargetNodeId = state.Id, MethodId = method.Id, Arguments = new List<StateValue> { StateValue.From(5f), StateValue.From(2) } }; asset.Graph.Nodes.Add(function);
            StateGraphAuthoring.Connect(asset.Graph, state, "enter", function, "in", StateConnectionKind.Execution);
            var runner = Runner(asset); runner.Tick(0); runner.Tick(0);
            Assert.That(runner.GetComponentInChildren<GraphProbeState>(true).InvocationCount, Is.EqualTo(1));
        }
        [Test] public void TypedFunctionResultCanDriveSetVariable()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state); var variable = new StateVariable { Name = "Damage", DefaultValue = StateValue.From(0f) }; asset.Graph.Variables.Add(variable);
            var method = StateMetadata.For(typeof(GraphProbeState)).Methods.Values.First(m => m.Method.Name == "Attack");
            var function = new StateNodeData { Kind = StateNodeKind.Function, TargetNodeId = state.Id, MethodId = method.Id, Arguments = new List<StateValue> { StateValue.From(5f), StateValue.From(3) } };
            var set = new StateNodeData { Kind = StateNodeKind.SetVariable, VariableId = variable.Id, Value = StateValue.From(0f) }; asset.Graph.Nodes.Add(function); asset.Graph.Nodes.Add(set);
            StateGraphAuthoring.Connect(asset.Graph, state, "enter", function, "in", StateConnectionKind.Execution); StateGraphAuthoring.Connect(asset.Graph, function, "out", set, "in", StateConnectionKind.Execution); StateGraphAuthoring.Connect(asset.Graph, function, "value", set, "value", StateConnectionKind.Value);
            var runner = Runner(asset); Assert.That(runner.Blackboard.Get<float>("Damage"), Is.EqualTo(15));
        }
        [Test] public void ExitNodeCompletesMachine()
        {
            var asset = Asset(); var a = State(asset.Graph, "A"); var exit = new StateNodeData { Kind = StateNodeKind.Exit }; asset.Graph.Nodes.Add(exit); Link(asset.Graph, asset.Graph.Nodes[0], a); Link(asset.Graph, a, exit);
            var runner = Runner(asset); runner.Tick(0); Assert.That(runner.IsRunning, Is.False); Assert.That(GraphProbeState.Calls.Last(), Is.EqualTo("A.Exit"));
        }
        [Test] public void HistoryIsBoundedAndCanRecordSnapshots()
        {
            var history = new StateHistory(2) { Recording = true }; history.Add(new StateHistoryRecord { NodeId = "A" }); history.Add(new StateHistoryRecord { NodeId = "B" }); history.Add(new StateHistoryRecord { NodeId = "C" });
            Assert.That(history.Count, Is.EqualTo(2)); Assert.That(history[0].NodeId, Is.EqualTo("B")); Assert.That(history[1].NodeId, Is.EqualTo("C")); history.Clear(); Assert.That(history.Count, Is.Zero);
        }
        [Test] public void StopAndRestartCreatesFreshStateInstances()
        {
            var asset = Asset(); var a = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], a); var runner = Runner(asset); var before = runner.GetComponentInChildren<GraphProbeState>(true); runner.StopMachine(); runner.StartMachine();
            Assert.That(runner.GetComponentInChildren<GraphProbeState>(true), Is.Not.SameAs(before)); Assert.That(runner.IsRunning, Is.True);
        }
        [Test] public void StopRequestedByEntryFunctionFinishesTheLifecycleSafely()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state);
            var method = StateMetadata.For(typeof(GraphProbeState)).Methods.Values.First(m => m.Method.Name == "StopRunner");
            var function = new StateNodeData { Kind = StateNodeKind.Function, TargetNodeId = state.Id, MethodId = method.Id }; asset.Graph.Nodes.Add(function);
            StateGraphAuthoring.Connect(asset.Graph, state, "enter", function, "in", StateConnectionKind.Execution);
            var runner = Runner(asset); Assert.That(runner.IsRunning, Is.False); CollectionAssert.AreEqual(new[] { "A.Enter", "A.Exit" }, GraphProbeState.Calls);
        }
        [Test] public void MutableConstantDefaultsAlsoRemainPrivateAcrossRunners()
        {
            var asset = Asset(); var state = State(asset.Graph, "A"); Link(asset.Graph, asset.Graph.Nodes[0], state);
            var variable = new StateVariable { Name = "Data", DefaultValue = StateValue.Default(typeof(GraphProbeValue)) }; asset.Graph.Variables.Add(variable);
            var original = new GraphProbeValue(); var constant = new StateNodeData { Kind = StateNodeKind.Constant, Value = StateValue.From(original) };
            var set = new StateNodeData { Kind = StateNodeKind.SetVariable, VariableId = variable.Id, Value = StateValue.Default(typeof(GraphProbeValue)) }; asset.Graph.Nodes.Add(constant); asset.Graph.Nodes.Add(set);
            StateGraphAuthoring.Connect(asset.Graph, constant, "value", set, "value", StateConnectionKind.Value); StateGraphAuthoring.Connect(asset.Graph, state, "enter", set, "in", StateConnectionKind.Execution);
            var first = Runner(asset); var second = Runner(asset); first.Blackboard.Get<GraphProbeValue>("Data").Values[0] = 88;
            Assert.That(second.Blackboard.Get<GraphProbeValue>("Data").Values[0], Is.EqualTo(1)); Assert.That(original.Values[0], Is.EqualTo(1));
        }
        [Test] public void NestedExitEnablesWaitingParentTransition()
        {
            var asset = Asset(); var parent = State(asset.Graph, "Parent"); var next = State(asset.Graph, "Next"); parent.Children = StateGraphData.Create();
            var child = State(parent.Children, "Child"); var exit = new StateNodeData { Kind = StateNodeKind.Exit }; parent.Children.Nodes.Add(exit); Link(parent.Children, parent.Children.Nodes[0], child); Link(parent.Children, child, exit);
            Link(asset.Graph, asset.Graph.Nodes[0], parent); Link(asset.Graph, parent, next).Transition.RequireChildCompletion = true;
            var runner = Runner(asset); runner.Tick(0); Assert.That(runner.ActiveStateId, Is.EqualTo(parent.Id)); runner.Tick(0); Assert.That(runner.ActiveStateId, Is.EqualTo(next.Id));
        }
        [Test] public void RepeatedLinkedTreesHaveDistinctActiveRuntimePaths()
        {
            var template = ScriptableObject.CreateInstance<StateTreeAsset>(); m_Objects.Add(template); template.Definition.StateTypeName = StateType.Id(typeof(GraphProbeState)); template.Definition.Children = StateGraphData.Create();
            var child = State(template.Definition.Children, "Child"); Link(template.Definition.Children, template.Definition.Children.Nodes[0], child);
            var asset = Asset(); var first = StateGraphAuthoring.AddLinked(asset.Graph, template, Vector2.zero); var second = StateGraphAuthoring.AddLinked(asset.Graph, template, Vector2.one);
            Link(asset.Graph, asset.Graph.Nodes[0], first); Link(asset.Graph, first, second).Transition.EventName = "Next";
            var runner = Runner(asset); string firstPath = asset.Graph.Id + "/" + first.Id; string secondPath = asset.Graph.Id + "/" + second.Id;
            Assert.That(runner.ActiveStatePaths.Any(p => p.StartsWith(firstPath + "/")), Is.True);
            runner.SendEvent("Next"); runner.Tick(0);
            Assert.That(runner.ActiveStatePaths.Any(p => p.StartsWith(firstPath + "/")), Is.False); Assert.That(runner.ActiveStatePaths.Any(p => p.StartsWith(secondPath + "/")), Is.True);
        }
    }
}
