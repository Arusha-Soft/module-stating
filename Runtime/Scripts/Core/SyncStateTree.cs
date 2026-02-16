namespace AS.Modules.Stating.Core
{
    public abstract class SyncStateTree<T> : StateTree<T> where T : Target
    {
        protected sealed override void OnInitialize()
        {
            base.OnInitialize();
            OnInitializeSyncStateTree();
        }

        protected sealed override void OnEnterTree()
        {
            OnEnterSyncTree();
        }

        protected sealed override void OnExitTree()
        {
            OnExitSyncTree();
        }

        protected virtual void OnInitializeSyncStateTree() { }

        protected abstract void OnEnterSyncTree();

        protected abstract void OnExitSyncTree();
    }
}
