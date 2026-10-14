using System;
using System.Collections.Generic;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    public static class StateGraphCopy
    {
        public static StateGraphData Graph(StateGraphData source) => JsonUtility.FromJson<StateGraphData>(JsonUtility.ToJson(source));
        public static StateNodeData Node(StateNodeData source) => JsonUtility.FromJson<StateNodeData>(JsonUtility.ToJson(source));
        public static StateNodeData Independent(StateNodeData source)
        {
            return Independent(source, new HashSet<StateGraphData>());
        }
        private static StateNodeData Independent(StateNodeData source, HashSet<StateGraphData> path)
        {
            var copy = Node(source.Definition);
            copy.Id = source.Id; copy.Name = source.Name; copy.Position = source.Position;
            copy.Color = source.Color; copy.Icon = source.Icon != null ? source.Icon : copy.Icon;
            if (!string.IsNullOrEmpty(source.Description)) copy.Description = source.Description;
            copy.Expanded = source.Expanded; copy.FieldsExpanded = source.FieldsExpanded; copy.FunctionsExpanded = source.FunctionsExpanded; copy.LifecycleExpanded = source.LifecycleExpanded;
            copy.BreakOnEnter = source.BreakOnEnter; copy.BreakOnExit = source.BreakOnExit;
            copy.IsLinked = false; copy.Template = null;
            copy.ResumeChildren = source.ResumeChildren;
            if (source.IsLinked)
            {
                foreach (var field in source.Fields)
                {
                    copy.Fields.RemoveAll(f => f.MemberId == field.MemberId);
                    copy.Fields.Add(JsonUtility.FromJson<StateFieldValue>(JsonUtility.ToJson(field)));
                }
                foreach (var item in source.Overrides)
                {
                    var variable = copy.Variables.Find(v => v.Id == item.VariableId) ?? copy.Children?.Variables.Find(v => v.Id == item.VariableId);
                    if (variable != null) variable.DefaultValue = JsonUtility.FromJson<StateValue>(JsonUtility.ToJson(item.Value));
                }
            }
            if (source.EffectiveChildren != null)
            {
                var childSource = source.EffectiveChildren;
                if (!path.Add(childSource)) throw new InvalidOperationException("Cannot copy a circular State Tree reference.");
                copy.Children = Graph(childSource); copy.Children.Id = Guid.NewGuid().ToString("N");
                copy.Children.Nodes.Clear();
                var ids = new Dictionary<string, string>();
                foreach (var child in childSource.Nodes)
                {
                    var childCopy = Independent(child, path); childCopy.Id = Guid.NewGuid().ToString("N"); ids[child.Id] = childCopy.Id; copy.Children.Nodes.Add(childCopy);
                }
                foreach (var edge in copy.Children.Connections) { edge.SourceNodeId = ids[edge.SourceNodeId]; edge.TargetNodeId = ids[edge.TargetNodeId]; }
                foreach (var edge in copy.Children.Connections)
                {
                    if (ids.TryGetValue(edge.Transition.ConditionNodeId ?? "", out string condition)) edge.Transition.ConditionNodeId = condition;
                    if (ids.TryGetValue(edge.Transition.OnTakenNodeId ?? "", out string callback)) edge.Transition.OnTakenNodeId = callback;
                }
                foreach (var child in copy.Children.Nodes) if (ids.TryGetValue(child.TargetNodeId ?? "", out string target)) child.TargetNodeId = target;
                foreach (var group in copy.Children.Groups) for (int i = 0; i < group.NodeIds.Count; i++) if (ids.TryGetValue(group.NodeIds[i], out string id)) group.NodeIds[i] = id;
                path.Remove(childSource);
                copy.IsSubGraphLinked = false; copy.SubGraphAsset = null;
                foreach (var item in source.Overrides)
                {
                    var variable = copy.Children.Variables.Find(v => v.Id == item.VariableId);
                    if (variable != null) variable.DefaultValue = JsonUtility.FromJson<StateValue>(JsonUtility.ToJson(item.Value));
                }
            }
            return copy;
        }
        public static StateGraphData Selection(StateGraphData source, HashSet<string> selected, Vector2 offset)
        {
            var copy = Graph(source);
            copy.Nodes.RemoveAll(n => !selected.Contains(n.Id));
            copy.Connections.RemoveAll(e => !selected.Contains(e.SourceNodeId) || !selected.Contains(e.TargetNodeId));
            copy.Groups.RemoveAll(g => !g.NodeIds.Exists(selected.Contains));
            copy.Notes.Clear();
            copy.Variables.Clear();
            var map = new Dictionary<string, string>();
            foreach (var node in copy.Nodes) { string previous = node.Id; node.Id = Guid.NewGuid().ToString("N"); map[previous] = node.Id; node.Position.position += offset; }
            foreach (var edge in copy.Connections)
            {
                string previous = edge.Id; edge.Id = Guid.NewGuid().ToString("N"); map[previous] = edge.Id;
                edge.SourceNodeId = map[edge.SourceNodeId]; edge.TargetNodeId = map[edge.TargetNodeId];
            }
            foreach (var edge in copy.Connections)
            {
                if (map.TryGetValue(edge.Transition.ConditionNodeId ?? "", out var condition)) edge.Transition.ConditionNodeId = condition;
                if (map.TryGetValue(edge.Transition.OnTakenNodeId ?? "", out var callback)) edge.Transition.OnTakenNodeId = callback;
            }
            foreach (var node in copy.Nodes)
            {
                if (map.TryGetValue(node.TargetNodeId ?? "", out var target)) node.TargetNodeId = target;
                if (map.TryGetValue(node.TransitionId ?? "", out var transition)) node.TransitionId = transition;
            }
            foreach (var group in copy.Groups)
            {
                group.Id = Guid.NewGuid().ToString("N"); group.Position.position += offset;
                group.NodeIds.RemoveAll(id => !map.ContainsKey(id));
                for (int i = 0; i < group.NodeIds.Count; i++) group.NodeIds[i] = map[group.NodeIds[i]];
            }
            return copy;
        }
    }
}
