using FS_CombatCore;
using FS_Core;
using FS_CoverSystem;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class CoverState : State<AIController>
    {
        private float stateStartTime;
        private float stateDuration;
        private AIController ai;
        private CombatAIController combatAI;
        private ShooterAIController shooterAI;
        private CoverHandler coverHandler;
        private AICoverPointFinder coverPointFinder;

        private float lastCoverCheckTime = 0f;

        private CoverPoint _currentCoverPoint;

        public CoverPoint CurrentCoverPoint
        {
            get => _currentCoverPoint;
            set
            {
                // Release the claim on the old point and claim the new one
                AICoverPointFinder.UnregisterCoverPoint(_currentCoverPoint);
                _currentCoverPoint = value;
                AICoverPointFinder.RegisterCoverPoint(_currentCoverPoint);
            }
        }

        public override void OnEnter(AIController owner)
        {
            ai = owner;

            if (combatAI == null)
            {
                combatAI = ai.GetComponent<CombatAIController>();

                AICoverPointFinder.ResetOccupancy();
            }

            if (shooterAI == null)
                shooterAI = ai.GetComponent<ShooterAIController>();

            if (coverHandler == null)
                coverHandler = ai.GetComponent<CoverHandler>();

            if (coverPointFinder == null)
                coverPointFinder = new AICoverPointFinder(ai, combatAI, shooterAI);

            // Decide how long to stay in cover
            stateStartTime = Time.time;
            stateDuration = Random.Range(shooterAI.MinCoverTime, shooterAI.MaxCoverTime);

            HandleCoverMode();
            if (CurrentCoverPoint?.CoverCollider == null)
            {
                IsCompleted = true;
                return;
            }

            combatAI.NavAgent.speed = shooterAI.CoverApproachSpeed;
        }

        public override void Execute()
        {
            if (IsCompleted) return;

            if (CurrentCoverPoint?.CoverCollider == null)
            {
                IsCompleted = true;
                return;
            }

            combatAI.NavAgent.SetDestination(CurrentCoverPoint.Position);

            if (coverHandler.InCover && combatAI.NavAgent.remainingDistance >
                    combatAI.NavAgent.stoppingDistance + 0.1f)
            {
                coverHandler.GoOutOfCover();
            }

            if (!coverHandler.InCover)
            {

                if (combatAI.NavAgent.remainingDistance <=
                    combatAI.NavAgent.stoppingDistance + 0.1f && !combatAI.NavAgent.pathPending)
                {
                    if (CurrentCoverPoint.PositionOffset != Vector3.zero)
                    {
                        CurrentCoverPoint.PositionOffset = Vector3.zero; // Reset offset after reaching cover
                                                                         // return;
                    }

                    coverHandler.GoToCover();
                }
            }
            else
            {
                Vector3 coverDir = -CurrentCoverPoint.CoverCollider.transform.forward;
                coverDir.y = 0;
                ai.transform.rotation = Quaternion.RotateTowards(ai.transform.rotation,
                    Quaternion.LookRotation(coverDir), Time.deltaTime * 400f);

                Vector3 startPos = ai.transform.position;
                startPos.y = 0.8f;
                int iterations = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (Physics.SphereCast(startPos, 0.1f, ai.transform.forward, out _, 1f, FSSettings.i.ObstacleLayer))
                    {
                        startPos.y += 0.1f;
                        iterations++;
                    }
                    else
                        break;
                }
                combatAI.Animator.SetFloat("CoverType", (1 - (iterations / 10f)) * shooterAI.MaxCoverHeightMultiplier, 0.2f, Time.deltaTime);
            }

            if (combatAI.Fighter.Target == null)
            {
                IsCompleted = true;
                return;
            }

            Vector3 headPos = CurrentCoverPoint.Position;
            Vector3 targetPos = combatAI.Fighter.Target.transform.position + Vector3.up * 2f;


            if (Time.time - lastCoverCheckTime >= 0.5f)
            {
                lastCoverCheckTime = Time.time;
                if (!Physics.Raycast(headPos, targetPos - headPos, out RaycastHit hit, (targetPos - headPos).magnitude, FSSettings.i.ObstacleLayer))
                    HandleCoverMode();
            }

            if (Time.time - stateStartTime >= stateDuration)
            {
                IsCompleted = true;
            }
        }

        public override void Exit()
        {
            CurrentCoverPoint = null;
            coverHandler.GoOutOfCover();
        }

        private void HandleCoverMode()
        {
            CoverPoint newCoverPoint = coverPointFinder.FindBestCoverPoint();

            if (newCoverPoint != CurrentCoverPoint)
                CurrentCoverPoint = newCoverPoint;
        }

        public bool ShouldCover(CombatAIController owner)
        {
            // If too close, don't cover
            if (owner.DistanceToTarget < shooterAI.CoverAvoidDistance)
                return false;

            // If the enemy has clear line of sight, then don't cover
            var origin = ai.transform.position + Vector3.up;
            var targetPos = owner.Fighter.Target.transform.position + Vector3.up * 0.3f;

            if (!Physics.SphereCast(origin, 0.08f, (targetPos - origin).normalized, out RaycastHit hit, Vector3.Distance(targetPos, origin), FSSettings.i.ObstacleLayer))
                return false;

            // PersistentGizmos.DrawRay(origin, (targetPos - origin).normalized, Vector3.Distance(targetPos, origin), Color.red);

            return true;
        }
    }
}
