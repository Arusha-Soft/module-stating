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

        /// <summary>
        /// Adds custom state objects to the provided collection.
        /// </summary>
        /// <remarks>
        /// Implementations should populate the provided <paramref name="states"/> list with any states that
        /// should be initialized and entered automatically with the tree. These are not "sub-states" you switch
        /// between; they are concurrent/persistent states managed for the tree's lifetime.
        /// </remarks>
        /// <param name="states">A list to which new state objects should be added. The list is provided by the caller and may already contain existing states.</param>
        protected abstract void AddStates(List<State<T>> states);

        protected abstract void OnEnterAsyncTree();

        protected abstract void OnExitAsyncTree();
    }
}
