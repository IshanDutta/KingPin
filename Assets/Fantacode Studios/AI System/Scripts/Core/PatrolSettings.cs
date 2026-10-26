using UnityEngine;

namespace FS_Core
{
    public class PatrolSettings : MonoBehaviour
    {
        [SerializeField] Transform patrolWaypoints;
        [SerializeField] float patrolSpeed = 2f;

        void Awake()
        {
            var blackboard = GetComponent<AIController>().Blackboard;

            if (patrolWaypoints != null) 
            {
                blackboard.Set("PatrolWaypoints", patrolWaypoints);
                blackboard.Set("PatrolSpeed", patrolSpeed);
            }
        }
    }
}
