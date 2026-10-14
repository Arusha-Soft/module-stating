using System;
using System.Collections.Generic;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    public enum StateNodeKind { State, Entry, Exit, AnyState, Constant, GetVariable, SetVariable, Compare, And, Or, Not, Function, ReceiveEvent, SendEvent, TriggerTransition, Convert, Log, Add, Subtract, Multiply, Divide, Custom }
    public enum StateConnectionKind { Transition, Execution, Value }
    public enum StateComparison { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }
    public enum StateConditionMode { All, Any }
    public enum StateTransitionOrder { ParentFirst, ChildFirst }
    public enum StateVariableScope { Graph, State, Tree, Instance }
    public enum StateHistoryKind { Enter, Exit, Transition, Event, Error }

    [Serializable]
    public sealed class StateFieldValue
    {
        public string MemberId;
        public StateValue Value = new StateValue();
    }
    [Serializable]
    public sealed class StateVariable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Variable";
        public string Category = "Default";
        [TextArea] public string Description;
        public StateVariableScope Scope;
        public bool Exposed;
        public StateValue DefaultValue = new StateValue();
    }
    [Serializable]
    public sealed class StateVariableOverride
    {
        [Tooltip("Stable variable ID. For runner overrides, an optional graph/node path followed by ':' and the ID disambiguates repeated trees.")]
        public string VariableId;
        public StateValue Value = new StateValue();
    }
    [Serializable]
    public sealed class StateGraphData
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "State Machine";
        [TextArea] public string Description;
        public StateTransitionOrder TransitionOrder;
        public List<StateNodeData> Nodes = new List<StateNodeData>();
        public List<StateConnection> Connections = new List<StateConnection>();
        public List<StateVariable> Variables = new List<StateVariable>();
        public List<StateGroupData> Groups = new List<StateGroupData>();
        public List<StateNoteData> Notes = new List<StateNoteData>();
        public Vector3 ViewPosition;
        public Vector3 ViewScale = Vector3.one;
        public StateNodeData FindNode(string id) => Nodes.Find(n => n.Id == id);
        public static StateGraphData Create(string name = "State Machine")
        {
            var graph = new StateGraphData { Name = name };
            graph.Nodes.Add(new StateNodeData { Name = "Entry", Kind = StateNodeKind.Entry, Position = new Rect(40, 160, 140, 80) });
            return graph;
        }
    }
    [Serializable]
    public sealed class StateNodeData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "State";
        public StateNodeKind Kind;
        public Rect Position = new Rect(300, 160, 260, 180);
        public Color Color = new Color(0.22f, 0.42f, 0.52f);
        public Texture2D Icon;
        public bool Expanded = true;
        public bool FieldsExpanded;
        public bool FunctionsExpanded;
        public bool LifecycleExpanded;
        [TextArea] public string Description;
        public string StateTypeName;
        public bool IsLinked;
        public ReusableStateAsset Template;
        public List<StateFieldValue> Fields = new List<StateFieldValue>();
        public List<StateVariable> Variables = new List<StateVariable>();
        public List<StateVariableOverride> Overrides = new List<StateVariableOverride>();
        [SerializeReference] public StateGraphData Children;
        public bool IsSubGraphLinked;
        public StateMachineGraph SubGraphAsset;
        public bool ResumeChildren;
        public bool BreakOnEnter;
        public bool BreakOnExit;
        public string VariableId;
        public string TargetNodeId;
        public string MethodId;
        public string EventName = "Event";
        public string TransitionId;
        public StateComparison Comparison;
        public StateValue Value = new StateValue();
        public List<StateValue> Arguments = new List<StateValue>();
        [SerializeReference] public StateLogicNode Custom;
        public StateNodeData Definition => IsLinked && Template != null ? Template.Definition : this;
        public string EffectiveStateType => Definition.StateTypeName;
        public StateGraphData EffectiveChildren => Definition.IsSubGraphLinked ? Definition.SubGraphAsset?.Graph : Definition.Children;
    }
    [Serializable]
    public sealed class StateConnection
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string SourceNodeId;
        public string SourcePort = "out";
        public string TargetNodeId;
        public string TargetPort = "in";
        public StateConnectionKind Kind;
        public StateTransition Transition = new StateTransition();
    }
    [Serializable]
    public sealed class StateTransition
    {
        public bool Enabled = true;
        public int Priority;
        [Min(0)] public float Delay;
        [Min(0)] public float ExitTime;
        public bool AllowSelf;
        public bool RequireChildCompletion;
        public bool Breakpoint;
        public string EventName;
        public string ConditionNodeId;
        public StateConditionMode ConditionMode;
        [SerializeReference] public List<StateCondition> Conditions = new List<StateCondition>();
        public string OnTakenNodeId;
        [TextArea] public string Description;
    }
    [Serializable]
    public sealed class StateGroupData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "Group";
        public Rect Position = new Rect(100, 100, 400, 300);
        public Color Color = new Color(0.3f, 0.3f, 0.3f, 0.2f);
        public List<string> NodeIds = new List<string>();
    }
    [Serializable]
    public sealed class StateNoteData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "Note";
        [TextArea] public string Contents;
        public Rect Position = new Rect(100, 100, 240, 160);
        public Color Color = new Color(0.55f, 0.48f, 0.25f);
    }
    [Serializable]
    public abstract class StateCondition
    {
        public abstract bool Evaluate(StateContext context);
    }
    [Serializable]
    public sealed class BlackboardStateCondition : StateCondition
    {
        public string VariableId;
        public StateComparison Comparison;
        public StateValue Value = StateValue.From(true);
        public override bool Evaluate(StateContext context) => StateOperations.Compare(context.Blackboard.Get(VariableId), Value.Get(), Comparison);
    }
}
