using FS_Core;
using FS_LocomotionSystem;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_CoverSystem
{
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class CoverHandler : MonoBehaviour
    {
        public float colliderHeightInCover = 1.2f;
        public Vector3 colliderCenterInCover = new Vector3(0, 0.6f, 0);

        CharacterController characterController;
        LocomotionCore locomotionCore;

        Animator animator;
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            locomotionCore = GetComponent<LocomotionCore>();
            animator = GetComponent<Animator>();
        }

        private void Start()
        {
            controllerOriginalHeight = characterController.height;
            controllerOriginalCenter = characterController.center;
        }

        public bool InCover { get; private set; }

        private void Update()
        {
            if (InCover) { 
            AdjustControllerForCover();
            }
        }

        // BlockLocomotion/UnblockLocomotion are a shared counter across every system on the
        // character, so cover must contribute at most one block, ever. Entering twice used to
        // push the count to 2; the single matching exit then left it at 1 and locomotion stayed
        // blocked for the rest of the game — the character could not walk, only root-motion
        // actions like a roll still worked.
        public void GoToCover()
        {
            if (InCover) return;

            InCover = true;

            locomotionCore.BlockLocomotion();

            animator.SetBool(AnimatorParameters.coverMode, true);
            animator.CrossFade(AnimationNames.LocomotionToCover, .2f);
            //AdjustControllerForCover();
        }

        public void GoOutOfCover()
        {
            // Called from several places that cannot know whether this character is actually in
            // cover (the shooter stepping out to aim, AI state exit, system teardown). Without
            // this guard the unmatched UnblockLocomotion cancels somebody else's block.
            if (!InCover) return;

            InCover = false;

            locomotionCore.UnblockLocomotion();

            if (characterController.enabled)
                characterController.SimpleMove(Vector3.zero);
            animator.SetBool(AnimatorParameters.coverMode, false);
        }

        Vector3 controllerOriginalCenter;
        float controllerOriginalHeight;
        void AdjustControllerForCover()
        {
            if (characterController == null) return;

            characterController.height = Mathf.Lerp(controllerOriginalHeight, colliderHeightInCover, animator.GetFloat("CoverType"));
            characterController.center = Vector3.Lerp(controllerOriginalCenter, colliderCenterInCover, animator.GetFloat("CoverType"));
        }

        void ResetControllerAdjustments()
        {
            if (characterController == null) return;

            characterController.height = controllerOriginalHeight;
            characterController.center = controllerOriginalCenter;
        }
    }
}
