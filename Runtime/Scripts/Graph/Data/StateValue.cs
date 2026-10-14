using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AS.Modules.Stating.Graph
{
    /// <summary>A Unity-serializable, typed value. Unity object references retain their identity.</summary>
    [Serializable]
    public sealed class StateValue
    {
        public string TypeName = StateType.Id(typeof(float));
        public bool IsNull;
        public bool Boolean;
        public long Integer;
        public double Number;
        public string Text;
        public Vector4 Vector;
        public UnityEngine.Object Object;
        [SerializeReference] public object Managed;
        [SerializeReference] public List<StateValue> Items;
        public List<string> MemberNames;

        public Type Type => StateType.Resolve(TypeName);

        public static StateValue Default(Type type)
        {
            return From(type.IsValueType ? Activator.CreateInstance(type) : null, type);
        }

        public static StateValue From(object value, Type declaredType = null)
        {
            return Capture(value, declaredType ?? value?.GetType() ?? typeof(string), new HashSet<object>(), 0);
        }

        private static StateValue Capture(object value, Type type, HashSet<object> path, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("Value nesting exceeds 32 levels.");
            var result = new StateValue { TypeName = StateType.Id(type), IsNull = value == null };
            if (value == null) return result;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) result.Object = (UnityEngine.Object)value;
            else if (type == typeof(bool)) result.Boolean = (bool)value;
            else if (type == typeof(string)) result.Text = (string)value;
            else if (type.IsEnum || StateType.IsInteger(type)) result.Integer = Convert.ToInt64(value);
            else if (type == typeof(float) || type == typeof(double)) result.Number = Convert.ToDouble(value);
            else if (type == typeof(Vector2)) { var v = (Vector2)value; result.Vector = new Vector4(v.x, v.y); }
            else if (type == typeof(Vector3)) { var v = (Vector3)value; result.Vector = new Vector4(v.x, v.y, v.z); }
            else if (type == typeof(Vector4)) result.Vector = (Vector4)value;
            else if (type == typeof(Quaternion)) { var v = (Quaternion)value; result.Vector = new Vector4(v.x, v.y, v.z, v.w); }
            else if (type == typeof(Color)) { var v = (Color)value; result.Vector = new Vector4(v.r, v.g, v.b, v.a); }
            else
            {
                if (!type.IsValueType && !path.Add(value)) throw new InvalidOperationException("Circular default values are not supported.");
                result.Items = new List<StateValue>();
                if (value is IList list)
                {
                    Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                    foreach (object item in list) result.Items.Add(Capture(item, element, path, depth + 1));
                }
                else
                {
                    // Store classes using Unity's managed-reference serialization, and structs field by field.
                    if (!type.IsValueType)
                    {
                        result.Managed = value;
                        result.Items = null;
                    }
                    else
                    {
                        result.MemberNames = new List<string>();
                        foreach (FieldInfo field in StateType.SerializedFields(type))
                        {
                            result.MemberNames.Add(StateType.FieldId(field));
                            result.Items.Add(Capture(field.GetValue(value), field.FieldType, path, depth + 1));
                        }
                    }
                }
                if (!type.IsValueType) path.Remove(value);
            }
            return result;
        }

        public object Get()
        {
            Type type = Type;
            if (type == null) throw new InvalidOperationException($"Missing value type '{TypeName}'.");
            if (IsNull) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return Object;
            if (type == typeof(bool)) return Boolean;
            if (type == typeof(string)) return Text;
            if (type.IsEnum) return Enum.ToObject(type, Integer);
            if (StateType.IsInteger(type)) return Convert.ChangeType(Integer, type);
            if (type == typeof(float)) return (float)Number;
            if (type == typeof(double)) return Number;
            if (type == typeof(Vector2)) return new Vector2(Vector.x, Vector.y);
            if (type == typeof(Vector3)) return new Vector3(Vector.x, Vector.y, Vector.z);
            if (type == typeof(Vector4)) return Vector;
            if (type == typeof(Quaternion)) return new Quaternion(Vector.x, Vector.y, Vector.z, Vector.w);
            if (type == typeof(Color)) return new Color(Vector.x, Vector.y, Vector.z, Vector.w);
            if (Managed != null) return Managed;
            if (type.IsArray)
            {
                Array array = Array.CreateInstance(type.GetElementType(), Items?.Count ?? 0);
                for (int i = 0; i < array.Length; i++) array.SetValue(Items[i].Get(), i);
                return array;
            }
            object instance = StateAotRegistry.Create(type);
            if (instance is IList list)
            {
                if (Items != null) foreach (StateValue item in Items) list.Add(item.Get());
            }
            else if (Items != null && MemberNames != null)
            {
                var fields = StateType.SerializedFields(type);
                for (int i = 0; i < MemberNames.Count; i++)
                    foreach (FieldInfo field in fields)
                        if (StateType.FieldId(field) == MemberNames[i]) { field.SetValue(instance, Items[i].Get()); break; }
            }
            return instance;
        }

        public object CreateRuntimeValue() => StateType.Clone(Get());
    }

    public static class StateType
    {
        private static readonly Dictionary<string, Type> s_Types = new Dictionary<string, Type>();
        private static readonly Dictionary<Type, FieldInfo[]> s_Fields = new Dictionary<Type, FieldInfo[]>();
        public static string Id(Type type) => type == null ? "" : $"{type.FullName}, {type.Assembly.GetName().Name}";
        public static Type Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (s_Types.TryGetValue(id, out Type type)) return type;
            type = Type.GetType(id, false);
            // Do not cache missing types: the editor can load/recompile assemblies later.
            if (type != null) s_Types[id] = type;
            return type;
        }
        public static bool IsInteger(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
        public static string FieldId(FieldInfo field) => $"{field.DeclaringType.FullName}::{field.Name}";
        public static bool IsSupported(Type type) => IsSupported(type, new HashSet<Type>());
        private static bool IsSupported(Type type, HashSet<Type> path)
        {
            if (type == null || type.IsPointer || type.ContainsGenericParameters) return false;
            if (type == typeof(bool) || IsInteger(type) || type == typeof(float) || type == typeof(double) || type == typeof(string) || type.IsEnum ||
                type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) || type == typeof(Color) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;
            if (type.IsArray) return type.GetArrayRank() == 1 && !typeof(IList).IsAssignableFrom(type.GetElementType()) && IsSupported(type.GetElementType(), path);
            if (type.IsGenericType) return type.GetGenericTypeDefinition() == typeof(List<>) && !typeof(IList).IsAssignableFrom(type.GetGenericArguments()[0]) && IsSupported(type.GetGenericArguments()[0], path);
            if (type.Namespace != null && (type.Namespace == "System" || type.Namespace.StartsWith("System."))) return false;
            if (type.Assembly.GetName().Name.StartsWith("UnityEngine")) return false;
            if (!type.IsSerializable || type.IsAbstract || type.IsInterface || typeof(Delegate).IsAssignableFrom(type)) return false;
            if (!type.IsValueType && type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null) return false;
            if (!path.Add(type)) return true;
            foreach (FieldInfo field in SerializedFields(type)) if (!FieldIsSupported(field, path)) return false;
            path.Remove(type);
            return true;
        }
        public static bool FieldIsSupported(FieldInfo field) => FieldIsSupported(field, new HashSet<Type>());
        private static bool FieldIsSupported(FieldInfo field, HashSet<Type> path) => (field.IsDefined(typeof(SerializeReference)) && (field.FieldType.IsInterface || field.FieldType.IsAbstract)) || IsSupported(field.FieldType, path);
        public static FieldInfo[] SerializedFields(Type type)
        {
            if (s_Fields.TryGetValue(type, out var result)) return result;
            var fields = new List<FieldInfo>();
            for (Type current = type; current != null && current != typeof(MonoBehaviour) && current != typeof(ScriptableObject) && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField)) || field.IsDefined(typeof(SerializeReference)))) fields.Add(field);
            result = fields.ToArray(); s_Fields[type] = result; return result;
        }
        public static object Clone(object value) => Clone(value, new Dictionary<object, object>(), 0);
        private static object Clone(object value, Dictionary<object, object> seen, int depth)
        {
            if (value == null || value is UnityEngine.Object || value is string) return value;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return value;
            if (depth > 64) throw new InvalidOperationException("Managed Blackboard value nesting exceeds 64 levels.");
            if (!type.IsValueType && seen.TryGetValue(value, out object existing)) return existing;
            if (value is Array array)
            {
                Array copy = Array.CreateInstance(type.GetElementType(), array.Length); seen[value] = copy;
                for (int i = 0; i < array.Length; i++) copy.SetValue(Clone(array.GetValue(i), seen, depth + 1), i);
                return copy;
            }
            object result = StateAotRegistry.Create(type);
            if (!type.IsValueType) seen[value] = result;
            if (value is IList list) foreach (object item in list) ((IList)result).Add(Clone(item, seen, depth + 1));
            else foreach (FieldInfo field in SerializedFields(type)) field.SetValue(result, Clone(field.GetValue(value), seen, depth + 1));
            return result;
        }
    }
}
