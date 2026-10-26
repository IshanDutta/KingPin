using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace FS_Core
{
    public class StateTransitionAsset : ScriptableObject
    {
        [SerializeField] StateAsset fromState;
        [SerializeField] StateAsset toState;
        [SerializeField] bool transitionOnComplete;
        [SerializeField] List<BlackboardConditionAsset> conditions = new List<BlackboardConditionAsset>();

        public StateAsset FromState { get { return fromState; } set { fromState = value; } }
        public StateAsset ToState { get { return toState; } set { toState = value; } }
        public bool TransitionOnComplete { get { return transitionOnComplete; } set { transitionOnComplete = value; } }
        public List<BlackboardConditionAsset> Conditions { get { return conditions; } set { conditions = value; } }

        public StateTransition<AIController> Build(AIController owner, Dictionary<StateAsset, State<AIController>> stateMap)
        {
            // From state can be null to indicate any state
            var fromStateBuilt = fromState != null ? stateMap[fromState] : null;

            return new StateTransition<AIController>(fromStateBuilt, stateMap[toState],
                transitionOnComplete, conditions.Select(c => c.Build()).ToList());
        }
    }
}
