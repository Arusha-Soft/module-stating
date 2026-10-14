using System;
using System.Collections.Generic;
using System.Linq;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEngine;

namespace AS.Modules.Stating.Editor.Graph
{
    /// <summary>Asset operations independent of GraphView, also usable by tests and custom tools.</summary>
    public static class StateGraphAuthoring
    {
        public static StateNodeData AddState(StateGraphData graph, Type type, Vector2 position)
        {
            var node = new StateNodeData { Name = ObjectNames.NicifyVariableName(type.Name), StateTypeName = StateType.Id(type), Position = new Rect(position, new Vector2(260, 180)) };
            graph.Nodes.Add(node); return node;
        }
        public static StateConnection Connect(StateGraphData graph, StateNodeData source, string sourcePort, StateNodeData target, string targetPort, StateConnectionKind kind)
        {
            var edge = new StateConnection { SourceNodeId = source.Id, SourcePort = sourcePort, TargetNodeId = target.Id, TargetPort = targetPort, Kind = kind };
            graph.Connections.Add(edge); return edge;
        }
        public static void RemoveNode(StateGraphData graph, string id)
        {
            graph.Nodes.RemoveAll(n => n.Id == id);
            graph.Connections.RemoveAll(e => e.SourceNodeId == id || e.TargetNodeId == id);
            foreach (var group in graph.Groups) group.NodeIds.Remove(id);
            // References from function/condition nodes are retained and visibly validated.
        }
        public static ReusableStateAsset SaveReusable(StateGraphData host, StateNodeData node, string path, bool includeTree)
        {
            var definition = StateGraphCopy.Independent(node);
            if (!includeTree) definition.Children = null;
            definition.IsLinked = false; definition.Template = null;
            // Capture exposed external defaults as explicit parameters, preserving their IDs and all internal bindings.
            if (definition.Children != null)
            {
                foreach (var variable in host.Variables.Where(v => v.Exposed))
                    if (!definition.Variables.Exists(v => v.Id == variable.Id)) definition.Variables.Add(JsonUtility.FromJson<StateVariable>(JsonUtility.ToJson(variable)));
            }
            var wrapper = StateGraphData.Create(definition.Name);
            wrapper.Nodes.Add(definition);
            Connect(wrapper, wrapper.Nodes[0], "out", definition, "in", StateConnectionKind.Transition);
            var errors = StateGraphValidator.Validate(wrapper).Where(i => i.Severity == StateValidationSeverity.Error).ToList();
            if (errors.Count > 0) throw new InvalidOperationException("Cannot extract a broken definition. " + string.Join("\n", errors.Select(i => i.Message)));
            var asset = includeTree ? ScriptableObject.CreateInstance<StateTreeAsset>() : ScriptableObject.CreateInstance<ReusableStateAsset>();
            asset.Definition = definition;
            AssetDatabase.CreateAsset(asset, path); Undo.RegisterCreatedObjectUndo(asset, "Save reusable state"); AssetDatabase.SaveAssets();
            return asset;
        }
        public static StateNodeData AddLinked(StateGraphData graph, ReusableStateAsset asset, Vector2 position)
        {
            var node = new StateNodeData { Name = asset.name, StateTypeName = asset.Definition.StateTypeName, IsLinked = true, Template = asset, ResumeChildren = asset.Definition.ResumeChildren, Position = new Rect(position, new Vector2(260, 180)) };
            graph.Nodes.Add(node); return node;
        }
        public static StateNodeData NestSelection(StateGraphData graph, HashSet<string> selected)
        {
            var nodes = graph.Nodes.Where(n => selected.Contains(n.Id)).ToList();
            if (nodes.Count == 0 || nodes.Any(n => n.Kind == StateNodeKind.Entry || n.Kind == StateNodeKind.AnyState || n.Kind == StateNodeKind.Exit)) throw new InvalidOperationException("Select ordinary states and their logic nodes.");
            var incoming = graph.Connections.Where(e => !selected.Contains(e.SourceNodeId) && selected.Contains(e.TargetNodeId)).ToList();
            var outgoing = graph.Connections.Where(e => selected.Contains(e.SourceNodeId) && !selected.Contains(e.TargetNodeId)).ToList();
            if (incoming.Any(e => e.Kind != StateConnectionKind.Transition) || outgoing.Any(e => e.Kind != StateConnectionKind.Transition) || incoming.Select(e => e.TargetNodeId).Distinct().Count() > 1 || outgoing.Count > 1)
                throw new InvalidOperationException("Extraction needs one child entry and at most one outgoing transition. Include dependent value/function nodes in the selection first.");
            var first = incoming.Count > 0 ? nodes.Find(n => n.Id == incoming[0].TargetNodeId) : nodes.Find(n => n.Kind == StateNodeKind.State);
            if (first == null) throw new InvalidOperationException("Include at least one state.");
            var dependencies = graph.Connections.Where(e => selected.Contains(e.SourceNodeId) && selected.Contains(e.TargetNodeId)).ToList();
            if (dependencies.Any(e => (!string.IsNullOrEmpty(e.Transition.ConditionNodeId) && !selected.Contains(e.Transition.ConditionNodeId)) || (!string.IsNullOrEmpty(e.Transition.OnTakenNodeId) && !selected.Contains(e.Transition.OnTakenNodeId))) || nodes.Any(n => n.Kind == StateNodeKind.Function && !selected.Contains(n.TargetNodeId)))
                throw new InvalidOperationException("Include the selection's function targets, condition nodes and transition callbacks before extracting the tree.");
            var child = StateGraphData.Create("Sub-State Machine");
            var parent = new StateNodeData { Name = "Sub-State Machine", StateTypeName = StateType.Id(typeof(GraphState)), Position = first.Position, Children = child };
            foreach (var node in nodes) { graph.Nodes.Remove(node); child.Nodes.Add(node); }
            var internalEdges = graph.Connections.Where(e => selected.Contains(e.SourceNodeId) && selected.Contains(e.TargetNodeId)).ToList();
            foreach (var edge in internalEdges) { graph.Connections.Remove(edge); child.Connections.Add(edge); }
            Connect(child, child.Nodes[0], "out", first, "in", StateConnectionKind.Transition);
            foreach (var edge in incoming) edge.TargetNodeId = parent.Id;
            if (outgoing.Count == 1)
            {
                var edge = outgoing[0];
                var exit = new StateNodeData { Kind = StateNodeKind.Exit, Name = "Exit", Position = new Rect(850, 180, 140, 80) }; child.Nodes.Add(exit);
                var internalExit = JsonUtility.FromJson<StateConnection>(JsonUtility.ToJson(edge));
                internalExit.Id = Guid.NewGuid().ToString("N"); internalExit.TargetNodeId = exit.Id; internalExit.TargetPort = "in"; child.Connections.Add(internalExit);
                edge.SourceNodeId = parent.Id; edge.SourcePort = "out"; edge.Transition = new StateTransition { RequireChildCompletion = true };
            }
            foreach (var variable in graph.Variables) variable.Exposed = true;
            foreach (var group in graph.Groups) group.NodeIds.RemoveAll(selected.Contains);
            graph.Nodes.Add(parent); return parent;
        }
    }
}
