# Changelog

## Visual graph framework — 2026-10-14

- Added executable ScriptableObject graphs and private GameObject runners on the existing state lifecycle.
- Added the GraphView authoring window, typed ports, automatic field/method discovery and native field inspectors.
- Added scoped typed Blackboards, conditions, explicit function/data/flow nodes, events and deterministic transitions.
- Added nested graphs, linked reusable states/trees/graphs, parameter overrides and independent extraction/copies.
- Added Undo/Redo, serialized layout, groups, notes, search, runtime highlighting, bounded history, snapshots and Editor breakpoints.
- Added custom condition/node extension APIs and generated AOT factories/linker preservation.
- Added runtime, serialization and editor tests, plus a playable three-state graph, runner prefab and scene.
- Fixed the existing StateMachine change event and active-child cleanup when a legacy state tree exits. Restricted the editor assembly to Editor builds and repaired test assembly references.
