using FS_Core;
using UnityEngine;

namespace FS_Core
{
    public class TurnToFaceState : State<AIController>
    {
        AIController ai;
        string positionKey;
        float angleTolerance;
        float triggerThreshold;
        float prevAngle;
        float stuckTimer;
        const float StuckThreshold = 0.5f;
        const float StuckAngleDelta = 1f;

        public TurnToFaceState(string positionKey, float angleTolerance, float triggerThreshold)
        {
            this.positionKey = positionKey;
            this.angleTolerance = angleTolerance;
            this.triggerThreshold = triggerThreshold;
        }

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            var dir = GetFacingDir();

            // Complete WITHOUT ever setting the override when there's nothing to face or
            // we're already facing closely enough.
            if (dir.sqrMagnitude < 0.01f || Vector3.Angle(ai.transform.forward, dir) < triggerThreshold)
            {
                Complete();
                return;
            }

            ai.FacingDirOverride = dir;
            prevAngle = Vector3.Angle(ai.transform.forward, dir);
            stuckTimer = 0f;
        }

        public override void Execute()
        {
            if (IsCompleted) return;

            var dir = GetFacingDir();
            if (dir.sqrMagnitude < 0.01f)
            {
                Complete();
                return;
            }

            ai.FacingDirOverride = dir;

            float angle = Vector3.Angle(ai.transform.forward, dir);
            if (angle <= angleTolerance)
            {
                Complete();
                return;
            }

            // Complete if rotation has stalled (angle not decreasing)
            if (Mathf.Abs(prevAngle - angle) < StuckAngleDelta)
                stuckTimer += Time.deltaTime;
            else
                stuckTimer = 0f;

            prevAngle = angle;

            if (stuckTimer >= StuckThreshold)
                Complete();
        }

        public override void Exit()
        {
            ai.FacingDirOverride = null;
        }

        // Clear the override in the SAME frame we finish, so it never leaks onto the
        // hand-off frame where the shooter takes over aiming (Exit() alone is too late).
        void Complete()
        {
            ai.FacingDirOverride = null;
            IsCompleted = true;
        }

        Vector3 GetFacingDir()
        {
            if (!ai.Blackboard.TryGet<Vector3>(positionKey, out var targetPos))
                return Vector3.zero;
            var dir = targetPos - ai.transform.position;
            dir.y = 0;
            return dir.normalized;
        }
    }
}
