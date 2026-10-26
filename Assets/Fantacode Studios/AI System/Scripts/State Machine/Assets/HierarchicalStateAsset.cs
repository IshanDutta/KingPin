using FS_Util;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public class HierarchicalStateAsset : StateAsset
    {
        [SerializeField] new string name = "Sub-State";


        [HideInInspector][SerializeField] StateMachineAsset childStateMachineAsset;

        [HideInInspector][SerializeField] HierarchicalStateBehaviorAsset behavior;
        public HierarchicalStateBehaviorAsset BehaviorAsset => behavior;
        public void SetBehaviorAsset(HierarchicalStateBehaviorAsset value) { behavior = value; }

        public StateMachineAsset ChildStateMachineAsset { get { return childStateMachineAsset; } set { childStateMachineAsset = value; } }

        public override State<AIController> Build(AIController owner)
        {
            var state = new HierarchicalState<AIController>();
            state.SetChildStateMachine(BuildChild(owner));
            state.Name = this.name;

            if (behavior != null)
                state.Behavior = behavior.Build(owner);

            return state;
        }

        public StateMachine<AIController> BuildChild(AIController owner)
        {
            if (childStateMachineAsset == null)
                Debug.LogError($"{name} has not child states");

            return childStateMachineAsset.Build(owner);
        }
        public override string NodeTitle
        {
            get
            {
                if (childStateMachineAsset != null)
                {
                    childStateMachineAsset.Name = name;
                }
                return name;
            }
        }
        public override Color NodeColor => Color.gray;
        public override bool CanCreateMultiple => true;

        public override void DrawInspector()
        {
            base.DrawInspector();
#if UNITY_EDITOR
            HierarchicalStateBehaviorEditorHelper.DrawBehaviorSection(this);
#endif
        }
    }
}
