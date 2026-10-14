using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AS.Modules.Stating.Core;

namespace AS.Modules.Stating.Graph
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class StateCallableAttribute : Attribute
    {
        public bool Pure { get; set; }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public sealed class StateHiddenAttribute : Attribute { }

    public sealed class StateMethod
    {
        public readonly string Id;
        public readonly MethodInfo Method;
        public readonly ParameterInfo[] Parameters;
        public readonly bool Pure;
        public StateMethod(MethodInfo method)
        {
            Method = method; Parameters = method.GetParameters();
            Id = MethodId(method);
            Pure = method.GetCustomAttribute<StateCallableAttribute>()?.Pure ?? false;
        }
        public static string MethodId(MethodInfo method)
        {
            var names = new List<string>();
            foreach (ParameterInfo parameter in method.GetParameters()) names.Add(StateType.Id(parameter.ParameterType));
            return $"{method.DeclaringType.FullName}::{method.Name}({string.Join(";", names)})";
        }
    }
    public sealed class StateMetadata
    {
        private static readonly Dictionary<Type, StateMetadata> s_Cache = new Dictionary<Type, StateMetadata>();
        public readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
        public readonly Dictionary<string, StateMethod> Methods = new Dictionary<string, StateMethod>();
        public readonly List<string> Warnings = new List<string>();
        public static StateMetadata For(Type type)
        {
            if (type == null) return null;
            if (!s_Cache.TryGetValue(type, out var metadata)) { metadata = new StateMetadata(type); s_Cache[type] = metadata; }
            return metadata;
        }
        public static void ClearCache() => s_Cache.Clear();
        private StateMetadata(Type type)
        {
            foreach (FieldInfo field in StateType.SerializedFields(type))
            {
                if (field.IsDefined(typeof(StateHiddenAttribute))) continue;
                if (StateType.FieldIsSupported(field)) Fields[StateType.FieldId(field)] = field;
                else Warnings.Add($"Field {field.Name}: {field.FieldType.Name} is not a supported serialized type.");
            }
            for (Type current = type; current != null && current != typeof(UnityEngine.MonoBehaviour); current = current.BaseType)
            {
                if (current.IsGenericType && (current.GetGenericTypeDefinition() == typeof(State<>) || current.GetGenericTypeDefinition() == typeof(SyncState<>) || current.GetGenericTypeDefinition() == typeof(AsyncState<>) || current.GetGenericTypeDefinition() == typeof(SyncStateTree<>) || current.GetGenericTypeDefinition() == typeof(AsyncStateTree<>))) continue;
                foreach (MethodInfo method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName || method.IsDefined(typeof(StateHiddenAttribute)) || IsLifecycle(method.Name)) continue;
                    bool supported = !method.IsGenericMethod && method.ReturnType != typeof(IEnumerator) && !typeof(Task).IsAssignableFrom(method.ReturnType) &&
                        (method.ReturnType == typeof(void) || StateType.IsSupported(method.ReturnType));
                    foreach (ParameterInfo parameter in method.GetParameters()) supported &= !parameter.IsOut && !parameter.ParameterType.IsByRef && StateType.IsSupported(parameter.ParameterType);
                    if (supported) { var metadata = new StateMethod(method); Methods[metadata.Id] = metadata; }
                    else Warnings.Add($"Method {method.Name}: generic, ref/out, asynchronous or unsupported signature is excluded.");
                }
            }
        }
        private static bool IsLifecycle(string name) => name == "Awake" || name == "Start" || name == "Update" || name == "FixedUpdate" || name == "LateUpdate" || name == "OnEnable" || name == "OnDisable" || name == "OnDestroy" || name == "OnApplicationQuit" || name == "Enter" || name == "Exit" || name == "Initialize" || name == "InitializeTarget" || name == "BindContext" || name == "Tick" || name == "FixedTick" || name == "OnGUI" || name == "OnValidate" || name == "Reset" || name.StartsWith("OnTrigger", StringComparison.Ordinal) || name.StartsWith("OnCollision", StringComparison.Ordinal) || name.StartsWith("OnMouse", StringComparison.Ordinal) || name.StartsWith("OnDrawGizmos", StringComparison.Ordinal) || name.StartsWith("OnAnimator", StringComparison.Ordinal) || name.StartsWith("OnApplication", StringComparison.Ordinal) || name.StartsWith("OnRender", StringComparison.Ordinal);
    }
}
