using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AS.Modules.Stating.Editor.Graph
{
    internal sealed class StateElementResizeHandle : VisualElement
    {
        private readonly GraphElement m_Element;
        private readonly Action<Rect> m_Commit;
        private Vector2 m_Start;
        private Rect m_Original;
        private bool m_Dragging;
        internal StateElementResizeHandle(GraphElement element, Action<Rect> commit)
        {
            m_Element = element; m_Commit = commit; tooltip = "Resize";
            style.position = Position.Absolute; style.right = 1; style.bottom = 1; style.width = 16; style.height = 16;
            var grip = new Label("◢") { pickingMode = PickingMode.Ignore }; grip.style.color = new Color(0.65f, 0.65f, 0.65f); Add(grip);
            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                m_Dragging = true; m_Start = evt.mousePosition; m_Original = m_Element.GetPosition(); this.CaptureMouse(); evt.StopPropagation();
            });
            RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!m_Dragging) return;
                float scale = m_Element.GetFirstAncestorOfType<GraphView>()?.viewTransform.scale.x ?? 1;
                Vector2 size = m_Original.size + (evt.mousePosition - m_Start) / scale; size.x = Mathf.Max(200, size.x); size.y = Mathf.Max(120, size.y);
                m_Element.style.minWidth = size.x; m_Element.style.minHeight = size.y; m_Element.SetPosition(new Rect(m_Original.position, size)); evt.StopPropagation();
            });
            RegisterCallback<MouseUpEvent>(evt =>
            {
                if (!m_Dragging) return;
                m_Dragging = false; this.ReleaseMouse(); m_Commit(m_Element.GetPosition()); evt.StopPropagation();
            });
        }
    }
}
