# Verification — 2026-10-14

The module was copied into an isolated Unity project under the workspace's ignored `Temp/StatingVerification` directory. The original gameplay scenes and project settings were not used or changed for these checks.

| Check | Result |
| --- | --- |
| Unity 6000.0.60f1 runtime/editor compilation | Passed |
| Runtime compilation without UNITY_EDITOR | Passed |
| EditMode tests | 30 passed, 0 failed |
| PlayMode tests | 1 passed, 0 failed |
| Native GraphView window construction with Direct3D 11 | Passed |
| Windows x64 Mono Development build with Low managed stripping | Passed |
| Executing that standalone player with `--stating-smoke` | Passed; exit code 0 |
| Sample asset, prefab and scene script/reference GUIDs | Verified |
| Tracked diff whitespace checks | Passed |

The standalone check started Idle, changed Speed to reach Walking and Running, and dispatched Reset to return to Idle. The player logged `STATING_STANDALONE_SMOKE_PASSED`.

The automated tests cover lifecycle/update/fixed-update execution, transition priority and tie ordering, continuous delay gates, paused event queues, parent/child ordering and interruption, child Exit completion, mutable Blackboard and literal isolation, reference overrides, explicit function execution/results, safe stop requests during entry callbacks, repeatable startup, bounded history, repeated-tree runtime paths, reflected fields/overloads/signature exclusion, typed ports, graph/managed-value serialization, Undo/Redo, independent copies, linked extraction, missing metadata retention, circular references, value cycles, selection nesting and native editor construction.

Test result XML and complete Unity/build/player logs remain in `Temp/StatingVerification` in the working project. These generated verification artifacts are not part of the module.

## Practical limits

- The installed Windows player support contains Mono variations, with no Windows IL2CPP variation. AOT factory generation and linker integration are implemented, but an IL2CPP executable was not built or run. Install the matching Windows IL2CPP support and run a target build before shipping IL2CPP.
- Automated window construction and serialized editor operations do not replace manual gesture and visual QA of every authoring workflow. The editor uses the installed native GraphView implementation; no claim of pixel-identical Shader Graph rendering is made.
- The original project's full gameplay/build suite was not run. Compatibility was checked by keeping the existing lifecycle APIs, compiling the module against its installed Unity assemblies, and testing it in a minimal player project.
- Existing obsolete-tree and unused serialized-field compiler warnings remain. They did not prevent compilation or the standalone build.

## Reproduce

Use Unity Test Runner for the `AS.Modules.Stating.Editor.Tests` EditMode assembly and `AS.Modules.Stating.Tests` PlayMode assembly. A graphics device is required for the EditorWindow test; do not pass `-nographics` for that test.

For command-line runs, use Unity's `-batchmode -projectPath ... -runTests -testPlatform EditMode` or `PlayMode`, with `-testResults` and `-logFile` paths. Limit a full-project test run to the Stating test namespaces if only module verification is wanted.

In an isolated verification project, `StateGraphExamples.GenerateVerificationAssets` creates the graph/prefab/scene and player bindings. After allowing the generated scripts to compile, `StateGraphExamples.BuildVerificationPlayer` builds the sample at `Build/StatingSmoke.exe`. Run it with `-batchmode -nographics --stating-smoke -logFile ...`. This build helper changes the isolated project's backend/stripping settings and is intended for verification, not production build orchestration.
