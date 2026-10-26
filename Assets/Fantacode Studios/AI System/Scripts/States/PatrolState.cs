using FS_Util;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class PatrolState : State<AIController>
    {
        AIController ai;
        PatrolStateContext ctx;
        List<Transform> waypoints;
        int currentWaypoint;
        bool initialized = false;

        public override void OnEnter(AIController owner)
        {
            ai = owner;
            ctx = Context as PatrolStateContext;

            BuildWaypointList();

            if (waypoints == null || waypoints.Count == 0)
            {
                IsCompleted = true;
                return;
            }

            // Warp before calling set destination
            ai.NavAgent.Warp(ai.NavAgent.transform.position);

            ai.NavAgent.speed = ResolveMoveSpeed();
            ai.NavAgent.stoppingDistance = ctx.ArrivalThreshold;
            
            // If the AI goes out of patrol state due to other states like getting hit, then we don't want to restart the patrolling
            if (!initialized) 
            {
                initialized = true;
                currentWaypoint = 0;
                ai.NavAgent.SetDestination(waypoints[currentWaypoint].position);
            }
        }

        float ResolveMoveSpeed()
        {
            if (ctx.TakeMoveSpeedFromBlackboard
                && ctx.MoveSpeedKey != null
                && ai.Blackboard.TryGet<float>(ctx.MoveSpeedKey.Name, out float speed))
                return speed;
            return ctx.MoveSpeed;
        }

        public override void Execute()
        {
            if (waypoints == null || waypoints.Count == 0) return;

            if (AIUtil.HasReachedDestination(ai.NavAgent))
            {
                int next = currentWaypoint + 1;
                if (next < waypoints.Count)
                {
                    currentWaypoint = next;
                    ai.NavAgent.SetDestination(waypoints[currentWaypoint].position);
                }
                else if (ctx.Loop)
                {
                    currentWaypoint = 0;
                    ai.NavAgent.SetDestination(waypoints[currentWaypoint].position);
                }
                else
                {
                    ai.NavAgent.ResetPath();
                    IsCompleted = true;
                }
            }
        }

        public override void Exit()
        {
            ai?.NavAgent.ResetPath();
        }

        void BuildWaypointList()
        {
            if (!ai.Blackboard.TryGet<Transform>(ctx.WaypointsKey.Name, out Transform network)
                || network == null || network.childCount == 0)
            {
                waypoints = null;
                return;
            }

            waypoints = new List<Transform>();
            foreach (Transform child in network)
                waypoints.Add(child);
        }
    }

    public class PatrolStateContext
    {
        public BlackboardKey WaypointsKey { get; set; }
        public float MoveSpeed { get; set; } = 2f;
        public bool TakeMoveSpeedFromBlackboard { get; set; }
        public BlackboardKey MoveSpeedKey { get; set; }
        public float ArrivalThreshold { get; set; } = 0.5f;
        public bool Loop { get; set; } = true;
    }
}
