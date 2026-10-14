using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace AS.Modules.Stating.Graph
{
    /// <summary>Generated player bindings provide concrete generic calls for IL2CPP/AOT.</summary>
    [Preserve]
    public static class StateAotRegistry
    {
        private static readonly Dictionary<Type, Func<GameObject, MonoBehaviour>> s_States = new Dictionary<Type, Func<GameObject, MonoBehaviour>>();
        private static readonly Dictionary<Type, Func<object>> s_Values = new Dictionary<Type, Func<object>>();
        [Preserve] public static void RegisterState<T>() where T : MonoBehaviour => s_States[typeof(T)] = go => go.AddComponent<T>();
        [Preserve] public static void RegisterValue<T>(Func<T> factory) => s_Values[typeof(T)] = () => factory();
        internal static MonoBehaviour AddState(GameObject obj, Type type) => s_States.TryGetValue(type, out var factory) ? factory(obj) : (MonoBehaviour)obj.AddComponent(type);
        internal static object Create(Type type) => s_Values.TryGetValue(type, out var factory) ? factory() : Activator.CreateInstance(type, true);
    }
}
