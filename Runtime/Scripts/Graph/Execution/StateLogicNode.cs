using System;
using System.Collections.Generic;

namespace AS.Modules.Stating.Graph
{
    /// <summary>Serialized custom node definition. Each runner receives a private copy for execution.</summary>
    [Serializable]
    public abstract class StateLogicNode
    {
        public abstract IEnumerable<StatePort> GetPorts();
    }
    public interface IStateValueNode
    {
        object Evaluate(string outputPort, StateContext context, Func<string, object> input);
    }
    public interface IStateExecutionNode
    {
        void Execute(StateContext context, Func<string, object> input);
    }
}
