using System;
using System.Collections.Generic;
using AS.Modules.Stating.Core;
using AS.Modules.Stating.Graph;
using UnityEngine;

namespace AS.Modules.Stating.Tests
{
    [Serializable]
    public class GraphProbeValue
    {
        public List<int> Values = new List<int> { 1, 2 };
        public GameObject Reference;
    }
    public sealed class GraphProbeState : State<Target>
    {
        public static readonly List<string> Calls = new List<string>();
        public string Label;
        public float MovementSpeed = 2;
        [SerializeField] private float m_JumpHeight = 3;
        [NonSerialized] public int InvocationCount;
        protected override void OnEnter() { Calls.Add(Label + ".Enter"); }
        protected override void OnExit() { Calls.Add(Label + ".Exit"); }
        protected override void OnUpdate(float deltaTime) { Calls.Add(Label + ".Update"); }
        protected override void OnFixedUpdate(float deltaTime) { Calls.Add(Label + ".Fixed"); }
        public float Attack(float damage, int level = 1) { InvocationCount++; return damage * level; }
        [StateCallable(Pure = true)] public bool CanMove() => MovementSpeed > 0 && m_JumpHeight >= 0;
        public void Overload(int value) { }
        public void Overload(float value) { }
        [StateHidden] public void Hidden() { }
        public void Unsupported(ref int value) { }
        public void StopRunner() => Context.Runner.StopMachine();
    }
}
