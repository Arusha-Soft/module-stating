# Stating — Visual State Machines

Unity 6000.0.60f1. The visual framework extends the existing `AS.Modules.Stating.Core.State<T>` and `StateMachine` APIs. Scene-authored state machines remain available.

## Start here

1. Open `Samples/VisualGraph/Three State Example.unity` and press Play.
2. Use the sample's Speed slider: Idle → Walking at 0.1, Walking → Running at 4. **Reset** sends an event and returns to Idle.
3. Double-click `Three State Machine.asset`, enable **Debug**, and select the scene runner. Active states, transition traces, Blackboard values and history appear in the editor.

For a new graph, use **Assets → Create → State Machine → State Machine Graph**. Double-click it, right-click the canvas, select C# states, and drag cyan transition ports. Connect Entry to the starting state. Assign the graph to a **State Machine Runner** component and assign its Target when the states require a particular `Target` subclass.

The **Three-State Example** creation menu also creates a runner prefab. It preserves an existing unsaved scene; in that case, drag the prefab into your scene. The checked-in sample scene can always be opened directly.

## Editor

The editor follows Shader Graph's native dark UI: Blackboard and Graph Inspector on the left, GraphView canvas in the center, a toolbar and breadcrumbs above, and runtime history below. Panels can be resized. Nodes, groups, notes, connections, viewport and collapsed sections persist.

- Right-click or Space opens searchable state, function, variable, logic, event, reusable and custom-node creation.
- Expand **Fields**, **Functions** and **Lifecycle** on state nodes. Selecting a state uses Unity's `SerializedObject` field inspector, including arrays, nested values and custom PropertyDrawers.
- Transition edges open priority, conditions, boolean-result binding, delay, exit time, event gate, child completion, callback and breakpoint settings.
- Orange ports carry execution; colored value ports carry typed data. Cyan ports carry state transitions. Numeric type changes require an explicit Convert node.
- Drag a Blackboard row to create a Get node. Its context menu adds Get, Set or Compare nodes, duplicates/reorders variables, or deletes a definition.
- Drag a C# state script, reusable state/tree, or complete graph asset onto the canvas. Reusable assets and graph references are linked by default.
- Double-click any state to create/open its internal graph. Breadcrumbs return to parents. The ownership label identifies shared definitions.
- Groups, sticky notes, alignment, multiselection, marquee selection, copy/paste, Ctrl/Cmd+D, Delete, F, Frame All and Undo/Redo use GraphView conventions.
- **Find** searches states, types and functions across nested graphs; the Blackboard has its own variable search. Validation messages focus affected nodes, including nested scopes.

Missing members and invalid connections are retained in serialized data and reported. A connection whose port disappeared may be hidden on the canvas until its signature is repaired; validation still lists it. Graph data does not depend on GraphView and runtime assemblies contain no UnityEditor references.

## C# states and reflection

Existing concrete `State<T>` subclasses can be selected. `SyncState<T>` is a convenient leaf base; `GraphState` is a ready-to-use logging state. Legacy C# trees retain their own child ownership: do not represent the same children a second time in a visual hierarchy.

```csharp
using AS.Modules.Stating.Core;
using AS.Modules.Stating.Graph;
using UnityEngine;

public sealed class MovementState : SyncState<Target>
{
    public float MovementSpeed = 4;
    [SerializeField] private float m_JumpHeight = 2;

    protected override void OnEnterSyncLeaf() { }
    protected override void OnExitSyncLeaf() { }
    protected override void OnUpdate(float dt)
    {
        Target.transform.position += Vector3.forward * MovementSpeed * dt;
    }

    public void Attack(float damage) { Debug.Log(damage); }
    [StateCallable(Pure = true)] public bool CanMove() => MovementSpeed > 0;
    public float CalculateDamage(float multiplier, int level = 1) => multiplier * level;
}
```

Public instance fields and private `[SerializeField]` / supported `[SerializeReference]` fields are discovered automatically. Public supported methods are discovered without attributes. `[StateHidden]` excludes a field or method. Property accessors, framework/Unity lifecycle methods, generic methods, ref/out parameters, Tasks and coroutine signatures are excluded, with unsupported-signature warnings.

Field identities include the declaring type. Method identities include the declaring type and every parameter type, distinguishing overloads. Reflection is cached. Recompilation rebuilds metadata and preserves recoverable configuration and connections.

**Functions have explicit semantics.** Side-effecting methods run through Execute connections, lifecycle outputs, event outputs or transition callbacks. Their result port returns the most recent explicit call's result. Only `[StateCallable(Pure = true)]` methods may run on data evaluation. Rendering never invokes state functions. Function targets must be active states in the current graph. Pure methods should have no side effects because conditions can evaluate them repeatedly.

## Runtime contract

Each runner owns private state components, scoped Blackboards, literal values, function argument defaults, custom node instances and history. Shared assets are definitions. Unity object references deliberately retain their object identity.

- Each graph level has one active child. Parents enter before children; children exit before parents. Updates run parent first; fixed updates use the same hierarchy.
- Higher transition priority wins. Serialized connection order breaks ties. One transition is taken per event pass or update pass. Parent-first interruption is the default; child-first is configurable per graph.
- Entry has exactly one enabled starting transition. Any State edges participate in normal priority ordering. Exit completes the current graph; a parent can wait for child completion.
- Self transitions require Allow Self and run exit/entry again. Resume Child History remembers the previous direct child when a parent re-enters.
- Exit time is elapsed state seconds. Delay measures continuously satisfied conditions and resets when any gate becomes false. Event gates latch in the current state until a transition or state exit clears them, allowing delayed event transitions.
- Events are FIFO and retained while paused. The queue is bounded to 256, with at most 64 events from the start of a tick dispatched in that tick. Events sent by graph execution wait for the next tick. Execution flow is bounded to 256 operations and cycles are validated.
- Pause freezes graph-managed updates, fixed updates, conditions and event dispatch. It does not pause Unity physics or independently authored Unity callbacks/coroutines. Use `OnUpdate`/`OnFixedUpdate` for graph-controlled ticking.
- Debug Step advances one graph update by `Time.fixedDeltaTime`; it does not step Unity's frame or physics. Entry/exit/transition breakpoints pause the Editor at Unity's normal execution boundary and have no release-player behavior.

```csharp
runner.StartMachine();
runner.Blackboard.Set("Speed", 5f);
float speed = runner.Blackboard.Get<float>("Speed");
runner.SendEvent("OnDamaged", 20f);
runner.Pause();
runner.Resume();
runner.RequestTransition(transitionId); // queued; normal gates still apply
runner.StopMachine();
runner.ResetMachine();               // rebuilds private runtime instances
runner.StateChanged += record => Debug.Log(record.Reason);
```

Stop requested from a state callback is deferred until that execution pass finishes. Reset must be called outside graph callbacks. Disabling/destroying a runner stops its graph. Custom typed Targets must be assigned on the runner; the fallback is the GameObject's `Target`, or a new base `Target` when none exists.

## Blackboard and reuse

Supported defaults include bool, int/long/short/byte, float, double, string, Vector2/3/4, Quaternion, Color, enums, Unity object/component references, supported custom serializable classes/structs, and one-dimensional arrays or `List<T>`. Classes use managed-reference serialization; structs and collections preserve typed member values and object references. Polymorphic `[SerializeReference]` fields retain their concrete class instances. Custom managed classes need a parameterless constructor for runtime cloning. Dictionaries, jagged/nested collections, arbitrary System types and unsupported signatures are rejected rather than treated as serializable.

Graph variables are visible to states in that graph. State-local variables belong to that state. Nested graphs have independent tree scopes and may read exposed ancestor variables. IDs avoid name collisions; names are convenient scope-local lookups. Every scope's values are private to the runtime instance.

Save a state or its whole tree through its context menu. Extraction captures exposed external Blackboard defaults as exposed state parameters and keeps host transitions in the host. Unresolved dependencies prevent saving a broken reusable definition.

Linked references follow shared definitions. Field edits on linked nodes are local overrides; exposed parameter overrides have explicit enable toggles. **Edit Shared Definition** intentionally opens the shared asset. **Make This Reference Independent** / **Duplicate as Independent Copy** detach nested reusable definitions too, retaining internal connections and creating independent nested graph identities.

Override precedence is shared defaults → graph reference overrides → runner overrides → runtime writes. Runner overrides can use a stable variable ID or `scopePath:variableId`; qualify the path when the same reusable tree appears more than once. `runner.FindBlackboard(scopePath)` provides scoped runtime access. Graph defaults should use persistent asset references; bind scene objects through runner overrides or gameplay writes.

## Validation, debugging and extensions

Validation checks entry rules, types, signatures, ports, duplicate IDs, missing variables/assets, override types, reachability, execution/value cycles and circular/deep hierarchies. Errors prevent runner startup; warnings preserve recoverable information.

The debugger selects an actual runner, highlights its active hierarchy and recent transitions, watches scoped values, and records entries/exits/events/transitions in a bounded history ring. Recording and deep snapshots are independently optional. History search filters state/event text, selecting records focuses nodes, and runtime editing is an explicit toggle that changes live values rather than asset defaults.

Extend `StateCondition` for custom conditions. Extend serializable `StateLogicNode`, return typed `StatePort` definitions, and implement `IStateValueNode` and/or `IStateExecutionNode` for custom logic. Custom nodes appear in search, have reflected settings, and are cloned per runtime. `StateGraphValidator.AdditionalValidation` supports project-specific validation. Custom state fields use ordinary Unity PropertyDrawers.

Editor extensions can use `StateGraphEditorExtensions.ConfigureNodeView` and `DrawNodeInspector` for project-specific visuals and inspectors. `ActiveStatePaths` exposes complete runtime identities, distinguishing repeated uses of the same reusable tree.

## Player builds and verification

Runtime code and metadata are independent of the Editor. A linker processor preserves reflected graph types. **Tools → ArushaSoft → Stating → Generate Player Bindings** creates `Assets/StatingGenerated/StateGraphAotBindings.cs`, with concrete component factories and value factories for visible graph types. The editor refreshes these bindings when graph assets are saved/imported. IL2CPP builds reject stale bindings before building; regenerate and allow compilation first. Custom types in assemblies unavailable to Assembly-CSharp, or private nested types, need project-owned AOT registration/preservation.

Use Unity Test Runner to run `AS.Modules.Stating.Editor.Tests` in EditMode and `AS.Modules.Stating.Tests` in PlayMode. Tests cover execution, hierarchy, isolation, reflection, typed flow, serialization, Undo/Redo, reuse, validation and native GraphView construction. Verification results and platform limits are in `Documentation/Verification.md`.

Architecture and execution details are in `Documentation/GraphImplementation.md`. GraphView remains experimental and is confined to the editor adapter. See Unity's [GraphView reference](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Experimental.GraphView.GraphView.html) and [managed-reference serialization reference](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeReference.html).
