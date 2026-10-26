using FS_Core;
using FS_Util;
using UnityEngine;
using static FS_StealthSystem.StealthBlackboardKeys;

namespace FS_StealthSystem
{
    public class FindSearchPointState : State<AIController>
    {
        public float SearchRadius { get; set; } = 10;


        AIController ai;

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            PersistentGizmos.Clear();

            if (ai.Blackboard.TryGet<Vector3>(SearchPosition, out Vector3 prevSearchPos))
                SearchPositionManager.Release(prevSearchPos);

            TryPickSearchPoint();

            IsCompleted = true;
        }

        void TryPickSearchPoint()
        {
            if (SearchPositionGenerator.TryGetSearchPosition(ai, SearchRadius,
                out Vector3 searchPos, ai.transform.forward))
            {
                ai.Blackboard.Set<Vector3>(SearchPosition, searchPos);
                PersistentGizmos.DrawSphere(searchPos, 0.2f, Color.white);
                return;
            }

            // No hidden point found — fall back to any reachable NavMesh point
            if (SearchPositionGenerator.TryGetRandomReachablePosition(ai, SearchRadius,
                out searchPos))
            {
                ai.Blackboard.Set<Vector3>(SearchPosition, searchPos);
                PersistentGizmos.DrawSphere(searchPos, 0.2f, Color.white);
                return;
            }

            // No search position found. So remove the blackboard key
            ai.Blackboard.Remove(SearchPosition);
        }
    }

}
