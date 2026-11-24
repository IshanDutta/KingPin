using FS_CombatCore;
using FS_Core;
using FS_Shooter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_ShooterSystem
{
    [RequireComponent(typeof(AIManager))]
    public class ShooterAIManager : MonoBehaviour
    {
        [SerializeField] bool shouldFlank = true;

        [Tooltip("Seconds after the whole squad loses sight of the target before one AI is sent to flank it")]
        [SerializeField] float timeToStartFlanking = 4f;

        [Tooltip("Seconds an individual AI can stay without LOS while the squad still has contact before it repositions to regain a firing line. Keep this above the max cover time so normal cover ducking doesn't count as being blocked")]
        [SerializeField] float timeToRepositionWhileEngaged = 4f;

        [Tooltip("When an AI loses LOS while the target is within this distance, it flanks immediately instead of waiting for the timers — someone who ducks behind cover right in front of the AI has a near-certain position, so the suppress-then-flank caution doesn't apply")]
        [SerializeField] float immediateFlankDistanceThreshold = 3f;

        float timeSinceSightLost = 0f;

        // Per-AI time without LOS while the squad still has contact (cleared when contact is fully lost)
        readonly Dictionary<CombatAIController, float> noVisualTimes = new Dictionary<CombatAIController, float>();

        // Per-AI visibility from the previous frame, to detect the losing-sight transition
        readonly Dictionary<CombatAIController, bool> wasVisible = new Dictionary<CombatAIController, bool>();

        FighterCore player;
        List<CombatAIController> enemiesInRange = new List<CombatAIController>();

        public static ShooterAIManager Instance { get; private set; }

        public float ImmediateFlankDistanceThreshold => immediateFlankDistanceThreshold;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            player = CombatAIManager.i.Player;
        }


        private void Update()
        {
            if (!shouldFlank) return;

            // Find enemies in range
            enemiesInRange.Clear();
            foreach (var combatAI in CombatAIManager.i.RangedAIList)
            {
                if (combatAI.Fighter.Target == player && !combatAI.Fighter.IsDead)
                    enemiesInRange.Add(combatAI);
            }

            if (enemiesInRange.Count == 0) return;

            HandleCloseRangeSightLoss();

            // Two Situations:
            //  - Atleast one AI has LOS: Then AIs without LOS should reposition themselves after a while
            //  - No AIs have LOS to Target: Shoot at the lastseen position for a while and then pick one AI to flank
            if (enemiesInRange.Any(e => e.IsTargetVisible))
            {
                timeSinceSightLost = 0f;

                // Squad shares live intel: keep the shared last seen position fresh while anyone has eyes on the target
                AIManager.Instance.LastKnownTargetPos = player.transform.position;

                RepositionAIsWithoutVisual();
            }
            else
            {
                noVisualTimes.Clear();
                timeSinceSightLost += Time.deltaTime;
            }

            if (timeSinceSightLost > timeToStartFlanking)
            {
                var enemyToFlank = SelectEnemyToFlank(enemiesInRange);
                enemyToFlank?.AIController.Blackboard.Set<bool>(ShooterBlackboardKeys.FlankRequested, true);

                timeSinceSightLost = 0f;
            }
        }

        // Sends AIs that haven't had LOS for a while to reposition (FlankState) while the squad still has contact. 
        // The timer resets whenever the AI regains visual, so an AI cycling between shoot and cover never 
        // accumulates enough time, only one that's genuinely stuck without an angle does.
        void RepositionAIsWithoutVisual()
        {
            foreach (var enemy in enemiesInRange)
            {
                if (enemy.IsTargetVisible)
                {
                    noVisualTimes.Remove(enemy);
                    continue;
                }

                noVisualTimes.TryGetValue(enemy, out float time);
                time += Time.deltaTime;

                enemy.AIController.Blackboard.TryGet<bool>(ShooterBlackboardKeys.FlankRequested, out bool alreadyRequested);
                if (time >= timeToRepositionWhileEngaged && !alreadyRequested && CanFlankToTargetPosition(enemy))
                {
                    enemy.AIController.Blackboard.Set<bool>(ShooterBlackboardKeys.FlankRequested, true);
                    time = 0f;
                }

                noVisualTimes[enemy] = time;
            }
        }

        // Response urgency scales with certainty: an AI that loses LOS while the target is right in front of 
        // it (ducked behind cover at close range) has a near-certain position, so it moves in immediately instead of
        // waiting for the suppress-then-flank timers, which model the genuinely-uncertain far case.
        void HandleCloseRangeSightLoss()
        {
            foreach (var enemy in enemiesInRange)
            {
                bool visible = enemy.IsTargetVisible;
                wasVisible.TryGetValue(enemy, out bool wasPreviouslyVisible);

                if (wasPreviouslyVisible && !visible
                    && enemy.DistanceToTarget < immediateFlankDistanceThreshold
                    && CanFlankToTargetPosition(enemy))
                {
                    enemy.AIController.Blackboard.Set<bool>(ShooterBlackboardKeys.FlankRequested, true);
                }

                wasVisible[enemy] = visible;
            }
        }

        CombatAIController SelectEnemyToFlank(List<CombatAIController> enemies)
        {
            // Don't flank while the player is mid-action (e.g. a takedown or other synced animation)
            if (player.IsBusy)
                return null;

            return enemies.Where(e => CanFlankToTargetPosition(e))
                .OrderBy(e => DistanceToFlankDestination(e)).FirstOrDefault();
        }

        // Distance to the position a flank would actually walk to: the last seen position (when enabled)
        // plus the overshoot FlankState pushes past it, along the approach line. Including the overshoot
        // keeps CanFlankToTargetPosition honest for an AI standing right on the last seen position — it
        // is not “already there”, the pocket beyond still needs sweeping.
        float DistanceToFlankDestination(CombatAIController enemy)
        {
            return Vector3.Distance(enemy.transform.position, enemy.GetTargetPosition())
                + AIManager.Instance.StimulusOvershootDistance;
        }

        // Whether a flank to this AI's destination is worth requesting:
        //   • Not already there — Otherwise, FlankState would complete on the same frame (and create a snap in the aim)
        //   • Within the AI's movement range.
        bool CanFlankToTargetPosition(CombatAIController enemy)
        {
            float distance = DistanceToFlankDestination(enemy);
            return distance > enemy.NavAgent.stoppingDistance + 0.5f && distance < enemy.GetMovementRange();
        }
    }
}
