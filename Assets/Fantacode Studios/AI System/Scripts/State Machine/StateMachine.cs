using System;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{

    public class StateMachine<T>
    {
        public State<T> CurrentState { get; private set; }

        Dictionary<State<T>, List<StateTransition<T>>> transitions = new();
        List<StateTransition<T>> anyStateTransitions = new();

        T _owner;
        Blackboard _blackboard;
        public List<State<T>> States { get; private set; }
        public State<T> InitialState { get; private set; }

        public StateMachine(T owner, Blackboard blackboard, List<State<T>> states,
            State<T> initialState = null)
        {
            _owner = owner;
            _blackboard = blackboard;
            States = states;
            InitialState = initialState;
        }

        public Func<bool> CanExecute = () => true;

        public void ChangeState(State<T> newState)
        {
            // Debug.Log($"[StateChange] '{CurrentState?.Name ?? "(none)"}' -> '{newState?.Name ?? "(none)"}'");
            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState?.Enter(_owner);
        }

        public void EnterInitialState()
        {
            ChangeState(InitialState);
        }

        public void ClearCurrentState()
        {
            CurrentState = null;
        }

        public void Execute()
        {
            if (!CanExecute())
                return;

            if (CurrentState == null)
                return;

            CurrentState.Execute();

            CheckAnyStateTransitions();
            CheckTransitionsFrom(CurrentState);
        }

        void CheckAnyStateTransitions()
        {
            foreach (var transition in anyStateTransitions)
            {
                if (transition.CanTransition(_blackboard) && CurrentState != transition.ToState)
                {
                    ChangeState(transition.ToState);
                    transition.ExecutePostTransitionActions(_blackboard);
                    break;
                }
            }
        }

        void CheckTransitionsFrom(State<T> fromState)
        {
            if (fromState == null || !transitions.ContainsKey(fromState))
                return;

            foreach (var transition in transitions[fromState])
            {
                if (transition.CanTransition(_blackboard, fromState.IsCompleted))
                {
                    ChangeState(transition.ToState);
                    transition.ExecutePostTransitionActions(_blackboard);
                    break;
                }
            }
        }

        public bool HasValidTransition()
        {
            if (CurrentState == null)
                return false;

            if (transitions.ContainsKey(CurrentState))
            {
                foreach (var transition in transitions[CurrentState])
                {
                    if (transition.CanTransition(_blackboard, CurrentState.IsCompleted))
                        return true;
                }
            }
            return false;
        }

        public void AddTransition(StateTransition<T> t)
        {
            if (!transitions.ContainsKey(t.FromState))
                transitions[t.FromState] = new List<StateTransition<T>>();

            transitions[t.FromState].Add(t);
        }

        public void AddAnyStateTransition(StateTransition<T> t)
        {
            anyStateTransitions.Add(t);
        }

        public IEnumerable<State<T>> GetActiveStateChain()
        {
            var current = CurrentState;

            while (current != null)
            {
                yield return current;

                if (current is IChildStateMachineOwner<T> parent)
                    current = parent.ChildStateMachine?.CurrentState;
                else
                    break;
            }
        }

    }
}