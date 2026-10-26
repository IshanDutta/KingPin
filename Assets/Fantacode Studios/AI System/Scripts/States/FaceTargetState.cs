using FS_Core;
using UnityEngine;

namespace FS_Core
{
    public class FaceTargetState : State<AIController>
    {
        AIController ai;
        string targetKey;
        Transform target;

        public FaceTargetState(string targetKey)
        {
            this.targetKey = targetKey;
        }

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ai.Blackboard.TryGet(targetKey, out target);
            UpdateFacing();
        }

        public override void Execute()
        {
            if (target == null) return;

            UpdateFacing();
        }

        public override void Exit()
        {
            ai.FacingDirOverride = null;
        }

        void UpdateFacing()
        {
            if (target == null) return;

            var toTarget = target.position - ai.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
                ai.FacingDirOverride = toTarget.normalized;
        }
    }
}
