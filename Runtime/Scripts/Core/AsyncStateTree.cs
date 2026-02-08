using System.Collections.Generic;

namespace AS.Modules.Stating.Core
{
    public abstract class AsyncStateTree<T> : StateTree<T> where T : Target
    {
        private List<State<T>> m_States = new List<State<T>>();

        protected sealed override void OnInitialize()
        {
            base.OnInitialize();

            AddStates(m_States);

            for (int i = 0; i < m_States.Count; i++)
            {
                m_States[i].Initialize(Target);
            }

            OnInitializeAsyncStateTree();
        }

        protected sealed override void OnEnterTree()
        {
            OnEnterAsyncTree();

            for (int i = 0; i < m_States.Count; i++)
            {
                m_States[i].Enter();
            }
        }

        protected sealed override void OnExitTree()
        {
            OnExitAsyncTree();

            for (int i = 0; i < m_States.Count; i++)
            {
                m_States[i].Exit();
            }
        }

        protected virtual void OnInitializeAsyncStateTree() { }

        protected abstract void AddStates(List<State<T>> states);

        protected abstract void OnEnterAsyncTree();

        protected abstract void OnExitAsyncTree();
    }
}
