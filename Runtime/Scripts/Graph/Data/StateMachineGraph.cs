using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    [CreateAssetMenu(menuName = "State Machine/State Machine Graph", fileName = "State Machine")]
    public sealed class StateMachineGraph : ScriptableObject
    {
        public StateGraphData Graph = StateGraphData.Create();
    }
}
