using FS_Core;
using FS_LocomotionSystem;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Util
{
    public class AIUtil
    {
        public static bool HasLineOfSight(Transform target, Vector3 eyePosition, float distOffset = 0f, float heightOffset = 0f)
        {
            Vector3 targetPoint = GetRaycastTargetPos(target) + Vector3.up * heightOffset;
            Vector3 dir = targetPoint - eyePosition;
            float dist = dir.magnitude;
            dir /= dist;

            // Include the target's own layer so the cast stops at its surface. Otherwise the cast travels through the 
            // target's volume to its center, and anything inside that volume would falsely count as obstruction.
            int layerMask = FSSettings.i.ObstructionLayer | (1 << target.gameObject.layer);
            if (Physics.SphereCast(eyePosition, 0.1f, dir, out var hit, dist - distOffset, layerMask))
            {
                if (hit.transform.IsChildOf(target) || target.IsChildOf(hit.transform))
                    return true;

                // Debug.DrawLine(eyePosition, hit.point, Color.red);
                return false;
            }

            return true;
        }

        public static bool HasLineOfSight(Vector3 targetPos, Vector3 eyePosition, float distOffset = 0f)
        {
            Vector3 dir = targetPos - eyePosition;
            float dist = dir.magnitude;
            dir /= dist;

            bool isObstructed = Physics.SphereCast(eyePosition, 0.1f, dir, out var hit, dist - distOffset, FSSettings.i.ObstructionLayer);
            if (isObstructed)
            {
                // Debug.DrawLine(eyePosition, hit.point, Color.red);
                return false;
            }

            // nothing hit -> clear line
            // Debug.DrawLine(eyePosition, eyePosition + dir * dist, Color.green);
            return true;
        }

        public static bool HasReachedDestination(NavMeshAgent agent)
        {
            if (agent.pathPending)
                return false;

            if (agent.remainingDistance > agent.stoppingDistance)
                return false;

            if (agent.hasPath && agent.velocity.sqrMagnitude != 0f)
                return false;

            return true;
        }

        static Vector3 GetRaycastTargetPos(Transform target)
        {
            var character = target.GetComponent<LocomotionCore>();
            if (character == null)
                return target.position;

            return target.position + Vector3.up * character.RaycastTargetHeight;
        }
    }
}
