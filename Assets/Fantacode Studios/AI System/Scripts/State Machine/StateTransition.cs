using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class StateTransition<T>
    {
        State<T> fromState;
        State<T> toState;
        bool transitionOnComplete = true;
        List<IBlackboardCondition> conditions;

        public State<T> FromState => fromState;
        public State<T> ToState => toState;

        public StateTransition(State<T> fromState, State<T> toState, bool transitionOnComplete, List<IBlackboardCondition> conditions)
        {
            this.fromState = fromState;
            this.toState = toState;
            this.transitionOnComplete = transitionOnComplete;
            this.conditions = conditions;
        }

        public bool CanTransition(Blackboard blackboard, bool isComplete=false)
        {
            if (transitionOnComplete && !isComplete)
                return false;

            if (!transitionOnComplete && (conditions == null || conditions.Count == 0))
                return false;

            if (conditions != null)
            {
                foreach (var condition in conditions)
                {
                    if (!condition.Evaluate(blackboard))
                        return false;
                }
            }
            return true;
        }

        public void ExecutePostTransitionActions(Blackboard blackboard)
        {
            if (conditions == null) return;
            foreach (var condition in conditions)
            {
                if (condition is IPostTransitionCondition post)
                    post.OnTransitioned(blackboard);
            }
        }
    }

}
