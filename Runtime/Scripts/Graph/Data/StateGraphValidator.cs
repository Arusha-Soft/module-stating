using System;
using System.Collections.Generic;
using AS.Modules.Stating.Core;

namespace AS.Modules.Stating.Graph
{
    public enum StateValidationSeverity { Warning, Error }
    public sealed class StateValidationIssue
    {
        public StateValidationSeverity Severity;
        public string GraphId;
        public string NodeId;
        public string Message;
        public override string ToString() => $"{Severity}: {Message}";
    }
    public static class StateGraphValidator
    {
        public static event Action<StateGraphData, List<StateValidationIssue>> AdditionalValidation;
        public static List<StateValidationIssue> Validate(StateGraphData graph)
        {
            var issues = new List<StateValidationIssue>();
            ValidateGraph(graph, new Dictionary<string, StateVariable>(), new HashSet<StateGraphData>(), issues, 0);
            return issues;
        }
        private static void ValidateGraph(StateGraphData graph, Dictionary<string, StateVariable> parents, HashSet<StateGraphData> path, List<StateValidationIssue> issues, int depth)
        {
            if (graph == null) return;
            void Issue(string node, string message, StateValidationSeverity severity = StateValidationSeverity.Error) => issues.Add(new StateValidationIssue { Severity = severity, GraphId = graph.Id, NodeId = node, Message = message });
            if (depth > 32 || !path.Add(graph)) { Issue(null, "Circular State Tree reference or hierarchy deeper than 32 levels. Remove the recursive reference."); return; }
            if (graph.Version > StateGraphData.CurrentVersion) Issue(null, "Graph was authored with a newer schema. Update Stating before editing it.");
            var variables = new Dictionary<string, StateVariable>(parents);
            var localNames = new HashSet<string>(); var localIds = new HashSet<string>();
            foreach (var variable in graph.Variables)
            {
                if (string.IsNullOrWhiteSpace(variable.Name) || !localNames.Add(variable.Name) || !localIds.Add(variable.Id)) Issue(null, "Blackboard names and IDs must be unique within each scope.");
                if (!StateType.IsSupported(variable.DefaultValue.Type)) Issue(null, $"Variable '{variable.Name}' has unsupported or missing type '{variable.DefaultValue.TypeName}'.");
                variables[variable.Id] = variable;
            }
            StateVariable Find(string id) => variables.TryGetValue(id ?? "", out var variable) ? variable : StateGraphPorts.FindVariable(graph, id);
            var ids = new HashSet<string>();
            var ports = new Dictionary<string, List<StatePort>>();
            int entries = 0;
            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrEmpty(node.Id) || !ids.Add(node.Id)) Issue(node.Id, "Duplicate or empty node identifier.");
                if (node.Kind == StateNodeKind.Entry) entries++;
                if (node.Kind == StateNodeKind.Custom && node.Custom == null) Issue(node.Id, "Missing custom logic node type. Its managed data is retained.");
                if (node.IsLinked && node.Template == null) Issue(node.Id, $"Reusable definition for '{node.Name}' is missing. Reassign its asset.");
                if (node.Definition.IsSubGraphLinked && node.Definition.SubGraphAsset == null) Issue(node.Id, "Shared subgraph asset is missing. Reassign the reference; local data is retained.");
                if (node.IsLinked && node.Template != null && node.Template.Definition.IsLinked) Issue(node.Id, "A reusable root must own its definition. Convert its linked root to an independent copy.");
                if (node.Kind == StateNodeKind.State)
                {
                    Type type = StateType.Resolve(node.EffectiveStateType);
                    if (type == null || type.IsAbstract || type.ContainsGenericParameters || !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type) || !typeof(IGraphState).IsAssignableFrom(type)) Issue(node.Id, $"'{node.Name}' needs a concrete State<T> component type.");
                    else
                    {
                        var metadata = StateMetadata.For(type);
                        void CheckFields(List<StateFieldValue> fields)
                        {
                            foreach (var field in fields)
                                if (!metadata.Fields.TryGetValue(field.MemberId, out var info)) Issue(node.Id, $"Serialized field '{field.MemberId}' is missing. Its configuration has been retained.", StateValidationSeverity.Warning);
                                else if (field.Value.Type == null || !info.FieldType.IsAssignableFrom(field.Value.Type)) Issue(node.Id, $"Field '{info.Name}' changed type. Repair its retained value.");
                        }
                        CheckFields(node.Definition.Fields); if (node.IsLinked) CheckFields(node.Fields);
                    }
                    var nestedParents = new Dictionary<string, StateVariable>();
                    foreach (var pair in variables) if (pair.Value.Exposed) nestedParents[pair.Key] = pair.Value;
                    foreach (var variable in node.Definition.Variables) if (variable.Exposed) nestedParents[variable.Id] = variable;
                    foreach (var item in node.Overrides)
                    {
                        var variable = node.Definition.Variables.Find(v => v.Id == item.VariableId) ?? node.EffectiveChildren?.Variables.Find(v => v.Id == item.VariableId);
                        if (variable == null) Issue(node.Id, $"Override '{item.VariableId}' no longer exists. Remove or repair it.");
                        else if (item.Value.Type != variable.DefaultValue.Type) Issue(node.Id, $"Override '{variable.Name}' has an incompatible type.");
                    }
                    ValidateGraph(node.EffectiveChildren, nestedParents, path, issues, depth + 1);
                }
                if ((node.Kind == StateNodeKind.GetVariable || node.Kind == StateNodeKind.SetVariable) && Find(node.VariableId) == null) Issue(node.Id, "Missing Blackboard variable. Choose an available variable or expose its parent definition.");
                if (node.Kind == StateNodeKind.Function)
                {
                    var target = graph.FindNode(node.TargetNodeId);
                    var metadata = StateMetadata.For(StateType.Resolve(target?.EffectiveStateType));
                    if (metadata == null || !metadata.Methods.TryGetValue(node.MethodId, out var method)) Issue(node.Id, $"Function '{node.MethodId}' is missing or unsupported. Its bindings have been retained.");
                    else for (int i = 0; i < method.Parameters.Length; i++)
                        if (i < node.Arguments.Count && node.Arguments[i].Type != method.Parameters[i].ParameterType) Issue(node.Id, $"Argument {i + 1} no longer matches '{method.Parameters[i].Name}'.");
                }
                if (node.Kind == StateNodeKind.Convert && StateType.Resolve(node.StateTypeName) == null) Issue(node.Id, "Choose a supported conversion output type.");
                if (node.Kind == StateNodeKind.TriggerTransition && !graph.Connections.Exists(e => e.Kind == StateConnectionKind.Transition && e.Id == node.TransitionId)) Issue(node.Id, "Choose an existing transition to trigger.");
                ports[node.Id] = StateGraphPorts.For(graph, node, Find);
            }
            if (entries != 1) Issue(null, "Each graph must have exactly one Entry node.");
            var edgeIds = new HashSet<string>(); var dataInputs = new HashSet<string>();
            foreach (var edge in graph.Connections)
            {
                if (!edgeIds.Add(edge.Id)) Issue(edge.SourceNodeId, "Duplicate connection identifier.");
                if (!ports.TryGetValue(edge.SourceNodeId ?? "", out var outputs) || !ports.TryGetValue(edge.TargetNodeId ?? "", out var inputs)) { Issue(edge.SourceNodeId, "Connection references a missing node. Reconnect or delete the retained edge."); continue; }
                var output = outputs.Find(p => !p.Input && p.Id == edge.SourcePort);
                var input = inputs.Find(p => p.Input && p.Id == edge.TargetPort);
                if (!StateGraphPorts.Compatible(output, input) || output?.Kind != edge.Kind) Issue(edge.TargetNodeId, "Connection references missing or incompatible ports. Use an explicit conversion for different value types.");
                if (edge.Kind == StateConnectionKind.Value && !dataInputs.Add(edge.TargetNodeId + ":" + edge.TargetPort)) Issue(edge.TargetNodeId, "A value input can have only one connection.");
                if (edge.Kind == StateConnectionKind.Transition)
                {
                    var transition = edge.Transition;
                    if (!string.IsNullOrEmpty(transition.OnTakenNodeId))
                    {
                        if (!ports.TryGetValue(transition.OnTakenNodeId, out var callbacks) || !callbacks.Exists(p => p.Input && p.Kind == StateConnectionKind.Execution)) Issue(edge.SourceNodeId, "Transition callback must reference an executable node in this graph.");
                    }
                    if (transition.Delay < 0 || transition.ExitTime < 0) Issue(edge.SourceNodeId, "Transition delay and exit time cannot be negative.");
                    if (!string.IsNullOrEmpty(transition.ConditionNodeId))
                    {
                        var conditionNode = graph.FindNode(transition.ConditionNodeId);
                        var conditionPort = conditionNode == null ? null : ports[conditionNode.Id].Find(p => !p.Input && p.Id == "value");
                        if (conditionPort?.Type != typeof(bool)) Issue(edge.SourceNodeId, "Transition condition must reference a boolean result node.");
                        if (conditionNode?.Kind == StateNodeKind.Function)
                        {
                            var target = graph.FindNode(conditionNode.TargetNodeId);
                            var metadata = StateMetadata.For(StateType.Resolve(target?.EffectiveStateType));
                            if (metadata != null && metadata.Methods.TryGetValue(conditionNode.MethodId, out var method) && !method.Pure) Issue(conditionNode.Id, "A function used as a condition must declare [StateCallable(Pure = true)].");
                        }
                    }
                    foreach (var condition in transition.Conditions)
                    {
                        if (condition == null) Issue(edge.SourceNodeId, "Transition contains a missing managed condition type.");
                        if (condition is BlackboardStateCondition blackboard && Find(blackboard.VariableId) == null) Issue(edge.SourceNodeId, "Transition condition references a missing Blackboard variable.");
                    }
                }
            }
            var entryNode = graph.Nodes.Find(n => n.Kind == StateNodeKind.Entry);
            if (entryNode != null)
            {
                int starts = graph.Connections.FindAll(e => e.SourceNodeId == entryNode.Id && e.Kind == StateConnectionKind.Transition && e.Transition.Enabled).Count;
                if (starts != 1) Issue(entryNode.Id, "Entry must have exactly one enabled transition to a starting State.");
                var reachable = new HashSet<string> { entryNode.Id };
                bool changed;
                do { changed = false; foreach (var edge in graph.Connections) if (edge.Kind == StateConnectionKind.Transition && edge.Transition.Enabled && (reachable.Contains(edge.SourceNodeId) || graph.FindNode(edge.SourceNodeId)?.Kind == StateNodeKind.AnyState)) changed |= reachable.Add(edge.TargetNodeId); } while (changed);
                foreach (var node in graph.Nodes) if (node.Kind == StateNodeKind.State && !reachable.Contains(node.Id)) Issue(node.Id, $"State '{node.Name}' is unreachable from Entry or Any State.", StateValidationSeverity.Warning);
            }
            CheckCycles(graph, StateConnectionKind.Value, issues);
            CheckCycles(graph, StateConnectionKind.Execution, issues);
            AdditionalValidation?.Invoke(graph, issues);
            path.Remove(graph);
        }
        private static void CheckCycles(StateGraphData graph, StateConnectionKind kind, List<StateValidationIssue> issues)
        {
            var done = new HashSet<string>(); var visiting = new HashSet<string>();
            bool Visit(string id)
            {
                if (visiting.Contains(id)) return true;
                if (!done.Add(id)) return false;
                visiting.Add(id);
                foreach (var edge in graph.Connections) if (edge.Kind == kind && edge.SourceNodeId == id && Visit(edge.TargetNodeId)) return true;
                visiting.Remove(id); return false;
            }
            foreach (var node in graph.Nodes) if (Visit(node.Id)) { issues.Add(new StateValidationIssue { Severity = StateValidationSeverity.Error, GraphId = graph.Id, NodeId = node.Id, Message = $"{kind} cycle detected. Break the cycle; event dispatch can be used for deferred work." }); break; }
        }
    }
}
