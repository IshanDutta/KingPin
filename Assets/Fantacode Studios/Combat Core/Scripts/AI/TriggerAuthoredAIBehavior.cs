using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_CombatCore
{
    // Keep existing enum names and values so old Inspector settings survive.
    public enum TriggerType { OnPlayerEnter, OnPlayerExit, OnStart }
    public enum AuthoredTargetSource { TriggeringObject, AssignedObject }

    public class TriggerAuthoredAIBehavior : MonoBehaviour
    {
        [SerializeField] TriggerType triggerType;
        [SerializeField] List<CombatAIController> enemies;
        [SerializeField] bool setTarget = true;
        [SerializeField] FighterCore target; // Existing FighterCore reference, retained.

        [Header("Player / Monster Targeting")]
        [SerializeField] bool acceptPlayer = true;
        [SerializeField] bool acceptMonster = true;
        [SerializeField] string monsterTag = "Monster";
        [SerializeField] LayerMask monsterLayers;
        [SerializeField] AuthoredTargetSource targetSource = AuthoredTargetSource.TriggeringObject;
        [Tooltip("Optional explicit monster or player. Also used by OnStart.")]
        [SerializeField] GameObject assignedTarget;
        [Header("Monster Priority")]
        [Tooltip("Visible monsters always outrank the player. Uses independent shooting for both targets.")]
        [SerializeField] bool prioritizeMonster = true;
        [Tooltip("Allow soldiers to shoot the player when no monster is visible.")]
        [SerializeField] bool fallbackToPlayer = true;
        [SerializeField] bool debugTrigger = true;
        bool triggering;

        private void Awake()
        {
            // OnStart means this is a scene-start configuration, not a physics event.
            // Set the mode before any enemy Start, including enemies enabled later.
            if (triggerType != TriggerType.OnStart || !setTarget ||
                !prioritizeMonster || !acceptMonster || enemies == null) return;

            GameObject monster = assignedTarget;
            GameObject player = null;
            GameObject actor = GetAssignedTarget();
            if (actor != null)
            {
                if (actor.CompareTag("Player")) player = actor;
                else if (monster == null) monster = actor;
            }

            foreach (var combatAI in enemies)
            {
                if (combatAI == null) continue;
                combatAI.PrepareAuthoredMonsterPriority(monster, player,
                    monsterTag, monsterLayers, acceptPlayer && fallbackToPlayer);
            }
        }

        private void Start()
        {
            if (triggerType == TriggerType.OnStart)
                BeginTrigger(GetAssignedTarget());
        }

        private void OnTriggerEnter(Collider other)
        {
            if (triggerType == TriggerType.OnPlayerEnter)
                HandleCollider(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (triggerType == TriggerType.OnPlayerExit)
                HandleCollider(other);
        }

        private GameObject GetAssignedTarget()
        {
            if (assignedTarget != null) return assignedTarget;
            return target != null ? target.gameObject : null;
        }

        private void HandleCollider(Collider other)
        {
            if (triggering || other == null) return;
            GameObject actor = ResolveActor(other);
            if (actor == null) return;
            if (prioritizeMonster && acceptMonster) { BeginTrigger(actor); return; }
            BeginTrigger(targetSource == AuthoredTargetSource.AssignedObject
                ? GetAssignedTarget() : actor);
        }

        private GameObject ResolveActor(Collider other)
        {
            if (acceptMonster && prioritizeMonster && assignedTarget != null &&
                (other.transform == assignedTarget.transform || other.transform.IsChildOf(assignedTarget.transform)))
                return assignedTarget;
            // Walk upward so colliders on the hands, feet, etc. can activate the trigger.
            Transform matchedByLayer = null;
            for (Transform t = other.transform; t != null; t = t.parent)
            {
                if (acceptPlayer && t.CompareTag("Player")) return t.gameObject;
                if (acceptMonster && !string.IsNullOrEmpty(monsterTag) && t.tag == monsterTag)
                    return t.gameObject;
                if (acceptMonster && (monsterLayers.value & (1 << t.gameObject.layer)) != 0)
                    matchedByLayer = t;
            }
            if (matchedByLayer == null) return null;

            // Prefer the assigned root when the collider belongs to that monster.
            GameObject explicitTarget = GetAssignedTarget();
            if (explicitTarget != null && (other.transform == explicitTarget.transform ||
                other.transform.IsChildOf(explicitTarget.transform)))
                return explicitTarget;

            return matchedByLayer.gameObject;
        }

        private void BeginTrigger(GameObject actor)
        {
            if (triggering) return;
            if (setTarget && actor == null && !(prioritizeMonster && acceptMonster))
            {
                Debug.LogWarning("[Authored AI] No target assigned. Assign a target GameObject for OnStart or AssignedObject mode.", this);
                return;
            }
            triggering = true;
            StartCoroutine(TriggerAuthoredBehavior(actor));
        }

        private IEnumerator TriggerAuthoredBehavior(GameObject actor)
        {
            if (enemies == null || enemies.Count == 0)
            {
                Debug.LogWarning("[Authored AI] No enemies assigned.", this);
                triggering = false;
                yield break;
            }

            // Activate everyone first, then allow Awake/Start to run before accessing combat.
            foreach (var combatAI in enemies)
                if (combatAI != null && !combatAI.gameObject.activeSelf)
                    combatAI.gameObject.SetActive(true);

            yield return null;

            int activated = 0;
            foreach (var combatAI in enemies)
            {
                if (combatAI == null || !combatAI.gameObject.activeInHierarchy) continue;
                if (combatAI.Damagable != null && combatAI.Damagable.CurrentHealth <= 0) continue;

                if (setTarget)
                {
                    if (prioritizeMonster && acceptMonster)
                    {
                        // In priority mode the explicit assigned object is the monster.
                        // If the monster itself activated the trigger, use its root as well.
                        GameObject monster = assignedTarget;
                        GameObject player = null;
                        if (actor != null)
                        {
                            if (actor.CompareTag("Player")) player = actor;
                            else if (monster == null) monster = actor;
                        }
                        if (!combatAI.SetAuthoredMonsterPriority(monster, player,
                            monsterTag, monsterLayers, acceptPlayer && fallbackToPlayer)) continue;
                    }
                    else if (!combatAI.TrySetAuthoredTarget(actor)) continue;
                }
                else
                {
                    if (!combatAI.isActiveAndEnabled || combatAI.AIController == null ||
                        !combatAI.AIController.isActiveAndEnabled) continue;
                    combatAI.AIController.Blackboard.Set(CombatCoreBlackboardKeys.TriggerAuthoredBehavior, true);
                }
                activated++;
                if (debugTrigger)
                    Debug.Log("[Authored AI] " + combatAI.name + " activated" +
                        (setTarget ? (prioritizeMonster && acceptMonster ? " with monster priority" : " against " + (actor != null ? actor.name : "none")) : "") + ".", combatAI);
            }

            if (activated > 0) Destroy(gameObject); // Retain original one-shot behavior.
            else
            {
                Debug.LogWarning("[Authored AI] No enemies could be activated. Check earlier errors and scene managers.", this);
                triggering = false;
            }
        }
    }
}
