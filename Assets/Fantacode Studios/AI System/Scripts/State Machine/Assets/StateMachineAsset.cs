using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "State Machine/Create new state machine")]
    public class StateMachineAsset : ScriptableObject
    {
        [SerializeField] List<StateAsset> states = new List<StateAsset>();
        [SerializeField] int initialStateIndex;
        [SerializeField] List<StateTransitionAsset> transitions = new List<StateTransitionAsset>();
        [SerializeField] List<StateTransitionAsset> anyStateTransitions = new List<StateTransitionAsset>();
        [SerializeField] AnyStateAsset anyStateAsset;
        public List<StateTransitionCollection> stateTransitionCollections = new List<StateTransitionCollection>(); // helper for editor
        public List<StateAsset> States => states;
        public string Name { get; set; } = "Base";
        public int InitialStateIndex
        {
            get
            {
                if (initialStateIndex >= states.Count && initialStateIndex != 0)
                {
                    initialStateIndex = states.Count - 1;
                }
                return initialStateIndex;
            }
            set { initialStateIndex = value; }
        }
        public List<StateTransitionAsset> Transitions => transitions;
        public List<StateTransitionAsset> AnyStateTransitions => anyStateTransitions;
        public List<StateTransitionAsset> AllTransitions => transitions.Concat(anyStateTransitions).ToList();
        public List<StateAsset> AllStateAssets => states.Concat(new List<StateAsset>() { anyStateAsset }).ToList();

        public static Dictionary<StateMachine<AIController>, StateMachineAsset> stateMachineRuntimeRef = new Dictionary<StateMachine<AIController>, StateMachineAsset>();

        public AnyStateAsset AnyStateAsset { get { return anyStateAsset; } set { anyStateAsset = value; } }

        public StateMachine<AIController> Build(AIController owner)
        {
            List<State<AIController>> builtStates = new List<State<AIController>>();
            Dictionary<StateAsset, State<AIController>> stateMap = new();
            foreach (var stateAsset in AllStateAssets)
            {
                if (stateAsset == null) continue;

                var builtState = stateAsset.Build(owner);
                builtStates.Add(builtState);
                stateMap[stateAsset] = builtState;

                if (builtState != null)
                    //builtState.SetAsset(stateAsset);
                    StateAsset.stateRuntimeRef.Add(builtState, stateAsset);
            }

            var stateMachine = new StateMachine<AIController>(owner, owner.Blackboard, builtStates,
                builtStates[initialStateIndex]);

            foreach (var transitionAsset in transitions)
            {
                var transition = transitionAsset.Build(owner, stateMap);
                stateMachine.AddTransition(transition);
            }

            foreach (var anyTransitionAsset in anyStateTransitions)
            {
                var anyTransition = anyTransitionAsset.Build(owner, stateMap);
                stateMachine.AddAnyStateTransition(anyTransition);
            }

            if (stateMachine != null)
                //stateMachine.SetAsset(this);

                stateMachineRuntimeRef.Add(stateMachine, this);

            return stateMachine;
        }

        public StateMachine<AIController> GetActiveStateMachine(AIController aIController)
        {
            var current = aIController.StateMachine;
            while (current != null)
            {
                if (current.CurrentState is HierarchicalState<AIController> parent &&
                    parent.ChildStateMachine != null)
                    current = parent.ChildStateMachine;
                else
                    break;
            }

            return current;
        }

        public List<State<AIController>> GetActiveStateMachineChain(AIController aIController)
        {
            List<State<AIController>> chain = new List<State<AIController>>();
            var currentChain = aIController.StateMachine.GetActiveStateChain().ToList();
            for (int i = 0; i < currentChain.Count - 1; i++)
            {
                chain.Add(currentChain[i]);
            }
            return chain;
        }
    }
    [Serializable]
    public class StateTransitionCollection
    {
        public List<StateTransitionAsset> Transitions = new List<StateTransitionAsset>();
    }
}
