using System.Collections;
using FS_Core;
using UnityEngine;
using UnityEngine.AI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: puts a character into the state a corpse should already be in when the
    /// level opens — health at zero and the ragdoll simulating — so it is immediately available
    /// to the carry mechanic. The stealth system has no dependency on this script.
    /// </summary>
    public class DeadBodyStartDemo : MonoBehaviour
    {
        [SerializeField] Damagable damagable;
        [SerializeField] ActiveRagdollSystem ragdoll;
         [SerializeField] NavMeshAgent agent;
          [SerializeField] CharacterController characterController;

        [Tooltip("Let the ragdoll fall and settle before the body can be picked up.")]
        [SerializeField] bool settleOnStart = true;

        void Reset()
        {
            damagable = GetComponent<Damagable>();
            ragdoll = GetComponent<ActiveRagdollSystem>();
            agent = GetComponent<NavMeshAgent>();
            characterController = GetComponent<CharacterController>();
        }

        IEnumerator Start()
        {
            yield return null;

            if (damagable == null) damagable = GetComponent<Damagable>();
            if (ragdoll == null) ragdoll = GetComponent<ActiveRagdollSystem>();

            // Dead before anything else reads the character, so combat/AI logic never treats it as alive.
            if (damagable != null) damagable.TakeDamage(damagable.CurrentHealth);

            // ActiveRagdollSystem.Start() forces every bone kinematic, so activating in the same
            // frame would be undone. Waiting one frame lets that run first.

            if (ragdoll == null) yield break;

            if (!ragdoll.HasRagdollBones)
            {
                Debug.LogWarning($"{name}: no ragdoll bones — run the Ragdoll Builder on this character.", this);
                yield break;
            }

            ragdoll.BlendToRagdoll(false); 
            if(agent != null)
                agent.enabled =false;
            if(characterController != null)
                characterController.enabled =false;
        }
    }
}
