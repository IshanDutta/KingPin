using System.Collections.Generic;
using UnityEngine;
namespace FS_Core
{

    public abstract class State<T>
    {

        protected T Owner;
        protected object Context;

        public bool IsCompleted { get; protected set; }

        public string Name { get; set; }

        public void Enter(T owner)
        {
            IsCompleted = false;
            Owner = owner;
            OnEnter(owner);
        }

        public virtual void OnEnter(T owner) { }

        public virtual void Execute() { }

        public virtual void Exit() { }

        public void SetContext(object context)
        {
            Context = context;
        }
    }
}
