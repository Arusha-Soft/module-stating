using System;
using AS.Modules.Stating.Graph;

namespace AS.Modules.Stating.Core
{
    public interface IGraphState : IRunnable
    {
        Type TargetType { get; }
        void InitializeTarget(Target target);
        void BindContext(StateContext context);
        void Tick(float deltaTime);
        void FixedTick(float deltaTime);
    }
}
