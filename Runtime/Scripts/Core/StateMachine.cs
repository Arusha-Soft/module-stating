using System;

namespace AS.Modules.Stating.Core
{
    public struct StateMachine
    {
        public event Action<IRunnable, IRunnable> OnChangeState;

        private IRunnable m_CurrentState;
        public IRunnable CurrentState => m_CurrentState;

        public void SwitchState(IRunnable runnable)
        {
            if(runnable == m_CurrentState)
            {
                return;
            }

            IRunnable previous = m_CurrentState;
            previous?.Exit();
            m_CurrentState = runnable;
            runnable?.Enter();
            OnChangeState?.Invoke(previous, runnable);
        }
    }
}
