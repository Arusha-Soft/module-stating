using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    [CreateAssetMenu(menuName = "State Machine/Reusable State", fileName = "Reusable State")]
    public class ReusableStateAsset : ScriptableObject
    {
        public StateNodeData Definition = new StateNodeData();
    }
}
