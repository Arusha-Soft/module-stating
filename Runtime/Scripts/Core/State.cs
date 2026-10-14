using UnityEngine;

namespace AS.Modules.Stating.Core
{
    public abstract class State<T> : MonoBehaviour, IGraphState where T : Target
    {
        protected T Target { get; private set; }
        protected AS.Modules.Stating.Graph.StateContext Context { get; private set; }
        public System.Type TargetType => typeof(T);

        public void InitializeTarget(Target target)
        {
            if (!(target is T typedTarget)) throw new System.ArgumentException($"{GetType().Name} requires a {typeof(T).Name} target.");
            Initialize(typedTarget);
        }

        public void BindContext(AS.Modules.Stating.Graph.StateContext context) { Context = context; }
        public virtual void Tick(float deltaTime) { OnUpdate(deltaTime); }
        public virtual void FixedTick(float deltaTime) { OnFixedUpdate(deltaTime); }

        private bool m_IsInitialized = false;

        public void Initialize(T target)
        {
            if(m_IsInitialized) return;

            Target = target;
            m_IsInitialized = true;
            OnInitialize();
        }

        public void Enter()
        {
            OnEnter();
        }

        public void Exit()
        {
            OnExit();
        }

        protected virtual void OnInitialize() { }

        protected virtual void OnEnable() { }

        protected virtual void OnDisable() { }

        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnFixedUpdate(float deltaTime) { }

        protected abstract void OnEnter();

        protected abstract void OnExit();
    }
}
