using System;
using System.Collections;
using AS.Modules.Stating.Graph;
using UnityEditor;
using UnityEngine;

namespace AS.Modules.Stating.Editor.Graph
{
    internal static class StateValueGUI
    {
        internal static void Draw(string label, StateValue value)
        {
            if (value == null || value.Type == null) { EditorGUILayout.HelpBox(label + ": missing value type. Stored data is retained.", MessageType.Error); return; }
            EditorGUI.BeginChangeCheck();
            object edited = DrawObject(label, value.Get(), value.Type, 0);
            if (EditorGUI.EndChangeCheck())
            {
                var copy = StateValue.From(edited, value.Type);
                value.IsNull = copy.IsNull; value.Boolean = copy.Boolean; value.Integer = copy.Integer; value.Number = copy.Number; value.Text = copy.Text;
                value.Vector = copy.Vector; value.Object = copy.Object; value.Managed = copy.Managed; value.Items = copy.Items; value.MemberNames = copy.MemberNames;
            }
        }
        internal static object DrawObject(string label, object value, Type type, int depth)
        {
            if (depth > 12) { EditorGUILayout.LabelField(label, "Maximum inspector nesting reached"); return value; }
            if (type == typeof(bool)) return EditorGUILayout.Toggle(label, value is bool b && b);
            if (type == typeof(int)) return EditorGUILayout.IntField(label, value == null ? 0 : (int)value);
            if (StateType.IsInteger(type)) return Convert.ChangeType(EditorGUILayout.LongField(label, value == null ? 0 : Convert.ToInt64(value)), type);
            if (type == typeof(float)) return EditorGUILayout.FloatField(label, value == null ? 0 : (float)value);
            if (type == typeof(double)) return EditorGUILayout.DoubleField(label, value == null ? 0 : (double)value);
            if (type == typeof(string)) return EditorGUILayout.TextField(label, (string)value ?? "");
            if (type == typeof(Vector2)) return EditorGUILayout.Vector2Field(label, value == null ? Vector2.zero : (Vector2)value);
            if (type == typeof(Vector3)) return EditorGUILayout.Vector3Field(label, value == null ? Vector3.zero : (Vector3)value);
            if (type == typeof(Vector4)) return EditorGUILayout.Vector4Field(label, value == null ? Vector4.zero : (Vector4)value);
            if (type == typeof(Quaternion)) { var q = value == null ? Quaternion.identity : (Quaternion)value; var v = EditorGUILayout.Vector4Field(label, new Vector4(q.x, q.y, q.z, q.w)); return new Quaternion(v.x, v.y, v.z, v.w); }
            if (type == typeof(Color)) return EditorGUILayout.ColorField(label, value == null ? Color.white : (Color)value);
            if (type.IsEnum) return EditorGUILayout.EnumPopup(label, (Enum)(value ?? Activator.CreateInstance(type)));
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return EditorGUILayout.ObjectField(label, (UnityEngine.Object)value, type, false);
            string key = "Stating.ValueFoldout." + type.FullName + "." + label + "." + depth;
            bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, true), label, true); SessionState.SetBool(key, open);
            if (!open) return value;
            using (new EditorGUI.IndentLevelScope())
            {
                if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)))
                {
                    Type elementType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                    IList list = (IList)value;
                    int previous = list?.Count ?? 0;
                    int count = Mathf.Clamp(EditorGUILayout.DelayedIntField("Size", previous), 0, 1024);
                    if (list == null || count != previous)
                    {
                        IList replacement = type.IsArray ? (IList)Array.CreateInstance(elementType, count) : (IList)Activator.CreateInstance(type);
                        for (int i = 0; i < count; i++)
                        {
                            object item = i < previous ? list[i] : elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                            if (type.IsArray) replacement[i] = item; else replacement.Add(item);
                        }
                        list = replacement;
                    }
                    for (int i = 0; i < list.Count; i++) list[i] = DrawObject("Element " + i, list[i], elementType, depth + 1);
                    return list;
                }
                if (value == null)
                {
                    if (!type.IsAbstract && !type.IsInterface && GUILayout.Button("Create " + type.Name)) { GUI.changed = true; return Activator.CreateInstance(type, true); }
                    if (type.IsAbstract || type.IsInterface) EditorGUILayout.HelpBox("Choose a concrete serializable type using the variable's Type control.", MessageType.Info);
                    return null;
                }
                foreach (var field in StateType.SerializedFields(value.GetType()))
                    field.SetValue(value, DrawObject(ObjectNames.NicifyVariableName(field.Name), field.GetValue(value), field.FieldType, depth + 1));
            }
            return value;
        }
    }
}
