using AS.Modules.Stating.Core;
using System.Collections.Generic;

namespace AS.Modules.Stating
{
    [System.Obsolete("StateTree class is deprecated, Some of the functions are may change in next version, use AsyncStateTree instead.")]
    
    public abstract class StateTree<T> : State<T> where T : Target
    {
        private StateMachine m_Machine;
        private List<State<T>> m_SubStates = new List<State<T>>();

        protected State<T> CurrentState { private set; get; }

        protected override void OnInitialize()
        {
            AddSubStates(m_SubStates);

            for (int i = 0; i < m_SubStates.Count; i++)
            {
                m_SubStates[i].Initialize(Target);
            }

            OnInitializeStateTree();
        }

        protected override void OnEnter()
        {
            OnEnterTree();
        }

        protected override void OnExit()
        {
            OnExitTree();
        }

        protected void Switch(State<T> subState)
        {
            if (CurrentState == subState)
            {
                return;
            }

            m_Machine.SwitchState(subState);
            CurrentState = subState;
        }

        protected virtual void OnInitializeStateTree() { }

        /// <summary>
        /// Adds the sub-states of the current state to the specified collection.
        /// </summary>
        /// <remarks>
        /// Implementations should add all direct sub-states of the current state to the provided
        /// <paramref name="states"/> list. These sub-states represent mutually-exclusive modes that the
        /// tree can switch between using <see cref="Switch(State{T})"/>. The list may already contain
        /// other states when this method is called.
        /// </remarks>
        /// <param name="states">A list to which the sub-states of this state will be added. Must not be null.</param>
        protected abstract void AddSubStates(List<State<T>> states);

        protected abstract void OnEnterTree();

        protected abstract void OnExitTree();
    }
}
