using UnityEngine;

namespace AS.Modules.Stating.Core
{
    public class StateMachineComponent<T> : MonoBehaviour where T : Target
    {
        [SerializeField] private T m_Target;
        [SerializeField] private bool m_RunInAwake = true;
        [SerializeField] private State<T> m_StartState;

        private StateMachine m_Machine;

        private void OnEnable()
        {
            
        }
        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            m_StartState.Initialize(m_Target);
            if (m_RunInAwake)
            {
                Run();
            }
        }

        public void Run()
        {
            Switch(m_StartState);
        }

        public void Switch(IRunnable runnable)
        {
            m_Machine.SwitchState(runnable);
        }

        private void OnApplicationQuit() //TODO: this one is quick fix, we must more checks on this in free time.
        {
            Switch(null);
        }

        private void OnDisable()
        {
            //Switch(null);
        }
    }
}
