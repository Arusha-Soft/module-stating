using System;
using System.Collections;
using AS.Modules.Stating.Graph;
using UnityEngine;

namespace AS.Modules.Stating.Samples
{
    /// <summary>Standalone regression check; enabled only by the --stating-smoke command-line flag.</summary>
    public sealed class GraphPlayerSmokeCheck : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartCheck()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--stating-smoke") < 0) return;
            new GameObject("Stating Standalone Verification").AddComponent<GraphPlayerSmokeCheck>().StartCoroutine(Check());
        }
        private static IEnumerator Check()
        {
            yield return null;
            int exit = 0;
            try
            {
                var runner = FindFirstObjectByType<StateMachineRunner>();
                if (runner == null || !runner.IsRunning || runner.Graph.Graph.FindNode(runner.ActiveStateId)?.Name != "Idle") throw new InvalidOperationException("Initial Idle state did not start.");
                runner.Blackboard.Set("Speed", 1f); runner.Tick(0.02f);
                if (runner.Graph.Graph.FindNode(runner.ActiveStateId)?.Name != "Walking") throw new InvalidOperationException("Walking transition failed.");
                runner.Blackboard.Set("Speed", 5f); runner.Tick(0.02f);
                if (runner.Graph.Graph.FindNode(runner.ActiveStateId)?.Name != "Running") throw new InvalidOperationException("Running transition failed.");
                runner.Blackboard.Set("Speed", 0f); runner.SendEvent("Reset"); runner.Tick(0.02f);
                if (runner.Graph.Graph.FindNode(runner.ActiveStateId)?.Name != "Idle") throw new InvalidOperationException("Reset event failed.");
                Debug.Log("STATING_STANDALONE_SMOKE_PASSED");
            }
            catch (Exception exception) { Debug.LogException(exception); exit = 1; }
            Application.Quit(exit);
        }
    }
}
