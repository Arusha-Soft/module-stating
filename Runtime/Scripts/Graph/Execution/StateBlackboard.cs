using System;
using System.Collections.Generic;

namespace AS.Modules.Stating.Graph
{
    public sealed class StateBlackboard
    {
        private sealed class Slot
        {
            public StateVariable Definition;
            public object Value;
        }
        private readonly Dictionary<string, Slot> m_Ids = new Dictionary<string, Slot>();
        private readonly Dictionary<string, Slot> m_Names = new Dictionary<string, Slot>();
        private readonly bool m_RequireExposedParent;
        public StateBlackboard Parent { get; }
        public string Path { get; }
        public event Action<string, object> Changed;

        public StateBlackboard(IEnumerable<StateVariable> definitions, StateBlackboard parent = null, string path = "", bool requireExposedParent = true)
        {
            Parent = parent; Path = path; m_RequireExposedParent = requireExposedParent;
            foreach (StateVariable definition in definitions)
            {
                if (m_Ids.ContainsKey(definition.Id) || m_Names.ContainsKey(definition.Name)) throw new InvalidOperationException($"Duplicate Blackboard variable '{definition.Name}' in {path}.");
                var slot = new Slot { Definition = definition, Value = definition.DefaultValue.CreateRuntimeValue() };
                m_Ids.Add(definition.Id, slot); m_Names.Add(definition.Name, slot);
            }
        }
        private bool Find(string id, bool fromChild, out Slot slot)
        {
            if ((m_Ids.TryGetValue(id, out slot) || m_Names.TryGetValue(id, out slot)) && (!fromChild || slot.Definition.Exposed)) return true;
            slot = null;
            return Parent != null && Parent.Find(id, fromChild || m_RequireExposedParent, out slot);
        }
        public bool Contains(string id) => Find(id, false, out _);
        public Type GetValueType(string id) => Find(id, false, out Slot slot) ? slot.Definition.DefaultValue.Type : null;
        public object Get(string id)
        {
            if (!Find(id, false, out Slot slot)) throw new KeyNotFoundException($"Blackboard variable '{id}' is unavailable in '{Path}'. Expose parent variables to access them from children.");
            return slot.Value;
        }
        public T Get<T>(string id) => (T)Get(id);
        public bool TryGet<T>(string id, out T value)
        {
            if (Find(id, false, out Slot slot) && slot.Value is T typed) { value = typed; return true; }
            value = default; return false;
        }
        public void Set<T>(string id, T value) => Set(id, (object)value);
        public void Set(string id, object value)
        {
            if (!Find(id, false, out Slot slot)) throw new KeyNotFoundException($"Missing Blackboard variable '{id}' in '{Path}'.");
            Type type = slot.Definition.DefaultValue.Type;
            if ((value == null && type.IsValueType) || (value != null && !type.IsInstanceOfType(value))) throw new ArgumentException($"Variable '{slot.Definition.Name}' expects {type.Name}, received {value?.GetType().Name ?? "null"}.");
            slot.Value = value;
            Changed?.Invoke(slot.Definition.Id, value);
        }
        public void ApplyOverrides(IEnumerable<StateVariableOverride> overrides, bool strict = false)
        {
            if (overrides == null) return;
            foreach (var item in overrides)
            {
                string id = item.VariableId;
                if (string.IsNullOrEmpty(id)) continue;
                int split = id.LastIndexOf(':');
                if (split >= 0) { if (id.Substring(0, split) != Path) continue; id = id.Substring(split + 1); }
                // Overrides apply only to this scope, never mutate an exposed ancestor by accident.
                if (m_Ids.TryGetValue(id, out Slot slot) || m_Names.TryGetValue(id, out slot)) Set(slot.Definition.Id, item.Value.CreateRuntimeValue());
                else if (strict) throw new KeyNotFoundException($"Override '{id}' does not exist in '{Path}'.");
            }
        }
        public IEnumerable<StateVariable> Definitions
        {
            get { foreach (Slot slot in m_Ids.Values) yield return slot.Definition; }
        }
        public Dictionary<string, object> Snapshot()
        {
            var values = new Dictionary<string, object>();
            foreach (var pair in m_Ids) values[pair.Key] = StateType.Clone(pair.Value.Value);
            return values;
        }
    }

    public sealed class StateContext
    {
        public StateMachineRunner Runner { get; }
        public StateBlackboard Blackboard { get; }
        public string NodeId { get; }
        public string Path { get; }
        public StateContext(StateMachineRunner runner, StateBlackboard blackboard, string nodeId, string path)
        {
            Runner = runner; Blackboard = blackboard; NodeId = nodeId; Path = path;
        }
        public void SendEvent(string name, object payload = null) => Runner.SendEvent(name, payload);
        public T Get<T>(string id) => Blackboard.Get<T>(id);
        public void Set<T>(string id, T value) => Blackboard.Set(id, value);
    }

    public static class StateOperations
    {
        public static bool Compare(object left, object right, StateComparison comparison)
        {
            if (comparison == StateComparison.Equal) return Equals(left, right);
            if (comparison == StateComparison.NotEqual) return !Equals(left, right);
            if (left == null || right == null || left.GetType() != right.GetType() || !(left is IComparable comparable)) return false;
            int result = comparable.CompareTo(right);
            switch (comparison)
            {
                case StateComparison.Greater: return result > 0;
                case StateComparison.Less: return result < 0;
                case StateComparison.GreaterOrEqual: return result >= 0;
                case StateComparison.LessOrEqual: return result <= 0;
                default: return false;
            }
        }
        public static object ConvertValue(object value, Type type)
        {
            if (value == null) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (type.IsInstanceOfType(value)) return value;
            if (type.IsEnum) return Enum.ToObject(type, System.Convert.ToInt64(value));
            return System.Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        }
        public static object Arithmetic(object left, object right, StateNodeKind kind)
        {
            if (left == null || right == null || left.GetType() != right.GetType()) throw new InvalidOperationException("Arithmetic inputs must have the same numeric type.");
            if (StateType.IsInteger(left.GetType()))
            {
                long x = System.Convert.ToInt64(left), y = System.Convert.ToInt64(right);
                long integer = kind == StateNodeKind.Add ? checked(x + y) : kind == StateNodeKind.Subtract ? checked(x - y) : kind == StateNodeKind.Multiply ? checked(x * y) : x / y;
                return System.Convert.ChangeType(integer, left.GetType());
            }
            double a = System.Convert.ToDouble(left), b = System.Convert.ToDouble(right);
            if (kind == StateNodeKind.Divide && b == 0) throw new DivideByZeroException();
            double value = kind == StateNodeKind.Add ? a + b : kind == StateNodeKind.Subtract ? a - b : kind == StateNodeKind.Multiply ? a * b : a / b;
            return System.Convert.ChangeType(value, left.GetType());
        }
    }
}
