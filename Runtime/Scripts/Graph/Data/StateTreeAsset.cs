using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    [CreateAssetMenu(menuName = "State Machine/Reusable State Tree", fileName = "State Tree")]
    public sealed class StateTreeAsset : ReusableStateAsset
    {
        private void Reset() { Definition.Children = StateGraphData.Create("State Tree"); }
    }
}
