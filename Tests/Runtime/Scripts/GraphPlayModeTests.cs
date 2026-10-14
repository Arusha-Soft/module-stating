using System.Collections;
using AS.Modules.Stating.Graph;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AS.Modules.Stating.Tests
{
    public sealed class GraphPlayModeTests
    {
        [UnityTest] public IEnumerator RunnerAutoStartsAndProcessesGameplayEvents()
        {
            var graph = ScriptableObject.CreateInstance<StateMachineGraph>();
            var a = new StateNodeData { Name = "Idle", StateTypeName = StateType.Id(typeof(GraphProbeState)) };
            var b = new StateNodeData { Name = "Active", StateTypeName = StateType.Id(typeof(GraphProbeState)) };
            graph.Graph.Nodes.Add(a); graph.Graph.Nodes.Add(b);
            graph.Graph.Connections.Add(new StateConnection { SourceNodeId = graph.Graph.Nodes[0].Id, TargetNodeId = a.Id });
            graph.Graph.Connections.Add(new StateConnection { SourceNodeId = a.Id, TargetNodeId = b.Id, Transition = new StateTransition { EventName = "Gameplay" } });
            var obj = new GameObject("Play Mode Runner"); var runner = obj.AddComponent<StateMachineRunner>(); runner.Graph = graph;
            yield return null; Assert.That(runner.IsRunning, Is.True); Assert.That(runner.ActiveStateId, Is.EqualTo(a.Id));
            runner.Pause(); runner.SendEvent("Gameplay"); yield return null; Assert.That(runner.ActiveStateId, Is.EqualTo(a.Id));
            runner.Resume(); yield return null; Assert.That(runner.ActiveStateId, Is.EqualTo(b.Id));
            obj.SetActive(false); Assert.That(runner.IsRunning, Is.False); Object.Destroy(obj); Object.Destroy(graph); yield return null;
        }
    }
}
