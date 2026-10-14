# Visual Stating implementation

Target: Unity 6000.0.60f1, UI Toolkit and the editor-only experimental GraphView API.

## Architecture

The existing `State<T>`, `IRunnable`, and `StateMachine` remain the lifecycle foundation. A graph runner creates private component instances and drives those same APIs. Existing scene-authored player state machines continue to work.

- `Runtime/Scripts/Graph/Data`: versioned graph definitions, reusable state/tree assets, typed values, stable IDs and layout. Assets contain defaults, never live execution state.
- `Runtime/Scripts/Graph/Reflection`: cached state/member discovery and shared port metadata. No editor dependency.
- `Runtime/Scripts/Graph/Execution`: scoped Blackboard instances, hierarchical graph execution, queued events, explicit function execution and bounded diagnostic history.
- `Editor/Scripts/Graph`: replaceable GraphView adapter, native inspectors, asset operations, search, validation and live debugging.
- `Tests/Editor`: meaningful lifecycle, hierarchy, isolation, reflection, serialization and editor-operation coverage.

## Delivery and verification order

1. Typed graph data and lifecycle adapter; three-state executable example.
2. Reflection, Blackboard, value/flow nodes and validation.
3. Nested scopes, linked reusable definitions, independent copies and overrides.
4. Shader Graph-style authoring window, persistence, Undo/Redo and asset extraction.
5. Runtime selection, highlighting, history, breakpoint bridge and build preservation.
6. Unity compilation, automated tests, example generation and documentation.

## Execution contract

One exclusive child is active at each graph level. Parents enter before children; children exit before parents. Updates run parent first. Transition priority is descending, with serialized edge order as the stable tie-breaker. Parent-first interruption is the default; child-first can be selected. A single transition is taken per event or update pass. Delays require continuously valid conditions; exit time is measured in state seconds. Self transitions require explicit permission. Exit completes that graph level.

Execution edges trigger side effects. Function result ports cache their most recent explicit call; only methods marked `[StateCallable(Pure = true)]` may be evaluated as data/conditions. Rendering never invokes state functions. Events are FIFO, bounded, retained while paused and drained on the next update, with recursion/work limits.

Blackboard defaults are copied per runner, including nested mutable objects and collections. Object references intentionally retain their Unity object identity. Nested scopes access parent variables only when exposed. Overrides apply in order: shared definition, graph reference, runner, runtime writes. Member IDs include declaring types and complete method signatures.

GraphView layout, groups, notes and viewport are serialized independently of execution state. Missing types/members/connections are retained and reported, not silently removed. Reusable extraction validates external dependencies before creating an asset. Runtime metadata is available in player builds; the editor build processor supplies stripping preservation for reflected graph types.
