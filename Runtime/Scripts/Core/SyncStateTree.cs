namespace AS.Modules.Stating.Core
{
    public abstract class SyncStateTree<T> : StateTree<T> where T : Target
    {
        protected sealed override void OnInitialize()
        {
            OnInitializeSyncStateTree();
        }

        protected sealed override void OnEnter()
        {
            OnEnterSyncTree();
        }

        protected sealed override void OnExit()
        {
            OnExitSyncTree();
        }

        protected virtual void OnInitializeSyncStateTree() { }

        protected abstract void OnEnterSyncTree();

        protected abstract void OnExitSyncTree();
    }
}
