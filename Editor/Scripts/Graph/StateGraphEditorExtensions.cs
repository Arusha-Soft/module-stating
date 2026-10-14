using System;
using AS.Modules.Stating.Graph;
using UnityEditor.Experimental.GraphView;

namespace AS.Modules.Stating.Editor.Graph
{
    public static class StateGraphEditorExtensions
    {
        /// <summary>Customize an editor node after its typed ports and standard sections have been created.</summary>
        public static event Action<Node, StateNodeData> ConfigureNodeView;
        /// <summary>Return true to supply the selected node's inspector. Custom drawers own their Undo/dirty handling.</summary>
        public static event Func<StateNodeData, bool> DrawNodeInspector;
        internal static void Configure(Node view, StateNodeData data) => ConfigureNodeView?.Invoke(view, data);
        internal static bool DrawInspector(StateNodeData data)
        {
            if (DrawNodeInspector == null) return false;
            foreach (Func<StateNodeData, bool> drawer in DrawNodeInspector.GetInvocationList()) if (drawer(data)) return true;
            return false;
        }
    }
}
