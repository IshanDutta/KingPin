using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Core
{
    public class BTMoveToPosAction : IBTNode
    {
        readonly Vector3 targetPos;
        readonly NavMeshAgent navAgent;

        bool isMoving = false;

        public BTMoveToPosAction(Vector3 targetPos, NavMeshAgent navAgent) 
        { 
            this.targetPos = targetPos;
            this.navAgent = navAgent;
        }

        public BTExecutionStatus Tick()
        {
            if (!isMoving)
            {
                navAgent.SetDestination(targetPos);
                isMoving = true;
            }

            if (navAgent.remainingDistance < navAgent.stoppingDistance)
            {
                isMoving = false;
                return BTExecutionStatus.SUCCESS;
            }

            return BTExecutionStatus.RUNNING;
        }
        public bool IsRunning { get; set; }
    }

}
