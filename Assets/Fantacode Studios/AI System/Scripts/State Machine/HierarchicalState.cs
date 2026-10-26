using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_Core
{

    public class HierarchicalState<T> : State<T>, IChildStateMachineOwner<T>
    {
        public StateMachine<T> ChildStateMachine { get; private set; }
        public HierarchicalStateBehavior<T> Behavior { get; set; }

        public void SetChildStateMachine(StateMachine<T> stateMachine)
        {
            ChildStateMachine = stateMachine;
        }

        public sealed override void OnEnter(T owner)
        {
            Owner = owner;

            ChildStateMachine.EnterInitialState();
            OnEntered();
            Behavior?.OnStateEnter(Owner, this);
        }

        public sealed override void Execute()
        {
            OnExecuted();
            ChildStateMachine.Execute();
            Behavior?.OnStateExecute(Owner, this);

            if (ChildStateMachine.CurrentState != null && ChildStateMachine.CurrentState.IsCompleted &&
                !ChildStateMachine.HasValidTransition())
                IsCompleted = true;
        }

        public sealed override void Exit()
        {
            ChildStateMachine.CurrentState?.Exit();
            ChildStateMachine.ClearCurrentState();
            OnExited();
            Behavior?.OnStateExit(Owner, this);
        }

        public virtual void OnEntered() { }
        public virtual void OnExecuted() { }
        public virtual void OnExited() { }
    }
}
