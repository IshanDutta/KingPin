 using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;


namespace FS_ShooterSystem
{
    [DefaultExecutionOrder(1)]
    public class BowHelper : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Transform representing the default position where the bowstring is held when not drawn")]
        Transform holdTransform;

        [SerializeField]
        [Tooltip("Line renderer component representing the top half of the bowstring")]
        LineRenderer topLine;

        [SerializeField]
        [Tooltip("Line renderer component representing the bottom half of the bowstring")]
        LineRenderer bottomLine;

        [SerializeField]
        [Tooltip("Local offset from the right hand position where the string should be held when drawn")]
        Vector3 stringHoldingOffset;

        [SerializeField]
        [Tooltip("Local offset from the right hand position where the arrow should be positioned")]
        Vector3 arrowHoldingOffset;

        [SerializeField]
        [Tooltip("Rotation offset applied to the arrow during the reload animation")]
        Vector3 arrowHoldingRotationOffsetDuringReload = new Vector3(-100, 240, 45);

        [SerializeField]
        Transform arrowForwardReference;

        float stringSmoothing = 30f;

        ShooterWeaponObject bowWeapon;
        Animator animator;

        Transform rightHandBone;
        Vector3 currentStringPosition;

        private void OnEnable()
        {
            bowWeapon = GetComponent<ShooterWeaponObject>();
            if (bowWeapon == null) return;

            bowWeapon.OnEquip += HandleEquip;
            bowWeapon.OnSetAmmo += HandleSetAmmo;
            bowWeapon.dontBreakAimingWhileReload = true;

            // ShooterFighter.SetWeapon deactivates the weapon object before it raises OnEquip,
            // so this component is disabled when that event fires and never receives it.
            // Resolve the bones directly on enable instead of waiting for the next equip.
            HandleEquip();
        }

        private void OnDisable()
        {
            if (bowWeapon == null) return;

            bowWeapon.OnEquip -= HandleEquip;
            bowWeapon.OnSetAmmo -= HandleSetAmmo;
        }

        void HandleEquip()
        {
            if (bowWeapon.ParentShooterFighter == null) return;

            animator = bowWeapon.ParentShooterFighter.GetComponent<Animator>();
            rightHandBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }

        void HandleSetAmmo()
        {
            var ammoRoot = GetNockedAmmoRoot();
            if (ammoRoot == null || rightHandBone == null) return;

            // SetAmmo fires from the reload clip's update callback, i.e. before this frame's
            // LateUpdate — pose the arrow immediately so it is never seen at a stale rotation.
            ammoRoot.rotation = GetReloadArrowRotation();
        }

        /// <summary>
        /// Root transform of the arrow currently sitting on the bow, or null when there isn't
        /// one. An arrow only counts as nocked while its root is still parented to the weapon's
        /// ammo spawn point. ShooterFighter.ExecuteShooting unparents the arrow on release, but
        /// the weapon keeps CurrentLoadedAmmo pointing at the fired arrow until the next
        /// SetAmmo, and the pool clears ReadyToPerform/IsHit as soon as that arrow is released.
        /// Without the parent check a spent arrow gets dragged back onto the hand, or sent to
        /// the world origin by the idle branch below assigning localPosition on a root that no
        /// longer has a parent.
        /// </summary>
        Transform GetNockedAmmoRoot()
        {
            var ammo = bowWeapon.CurrentLoadedAmmo;
            if (ammo == null || ammo.ReadyToPerform || ammo.IsHit) return null;

            var ammoRoot = ammo.transform.parent;
            return ammoRoot != null && ammoRoot.parent == bowWeapon.ammoSpawnPoint ? ammoRoot : null;
        }

        /// <summary>
        /// Carry orientation held for the whole reload: the authored offset applied in right-hand
        /// space, so the arrow stays locked to the draw hand through the entire animation.
        /// Rebuilt from the live bone every frame rather than eased toward a target — an eased
        /// rotation lags whenever the hand turns faster than the ease rate, which the AI does
        /// constantly (ShooterFighter.UpdateAimingTargets snap-rotates the whole body toward the
        /// target every frame, and NavMesh steering turns it mid-draw).
        /// </summary>
        Quaternion GetReloadArrowRotation()
        {
            return rightHandBone.rotation * Quaternion.Euler(arrowHoldingRotationOffsetDuringReload);
        }

        /// <summary>
        /// Orientation for a drawn arrow whose nock sits at <paramref name="arrowPosition"/>, so
        /// the shaft runs from the draw hand to the bow.
        /// </summary>
        Quaternion GetNockedArrowRotation(Vector3 arrowPosition)
        {
            if (arrowForwardReference == null) return transform.rotation;

            var direction = arrowForwardReference.position - arrowPosition;
            if (direction.sqrMagnitude < 0.000001f) return transform.rotation;

            // Roll about the shaft follows the bow rather than world up. Assigning
            // Transform.forward implies LookRotation(direction, Vector3.up), which goes
            // degenerate when aiming steeply up or down and leaves the arrow rolled off the
            // rest whenever the bow itself is rolled.
            return Quaternion.LookRotation(direction, transform.up);
        }

        private void LateUpdate()
        {
            var shooter = bowWeapon != null ? bowWeapon.ParentShooterFighter : null;

            // The fighter can be assigned after this component enabled (weapon object active in
            // the scene before its first SetWeapon) — resolve the bones as soon as it appears.
            if (shooter != null && rightHandBone == null)
                HandleEquip();

            if (shooter == null || rightHandBone == null)
            {
                topLine.SetPosition(0, topLine.transform.position);
                topLine.SetPosition(1, holdTransform.position);
                bottomLine.SetPosition(0, bottomLine.transform.position);
                bottomLine.SetPosition(1, holdTransform.position);
                return;
            }

            var targetStringPos = shooter.IsAiming && !shooter.IsLoadingAmmo && !shooter.IsReloading
                            ? rightHandBone.position + rightHandBone.TransformDirection(stringHoldingOffset)
                            : holdTransform.position;
            if (shooter.IsReloading)
                currentStringPosition = Vector3.Lerp(currentStringPosition, targetStringPos, Time.deltaTime * stringSmoothing);
            else
                currentStringPosition = targetStringPos;

            topLine.SetPosition(0, topLine.transform.position);
            topLine.SetPosition(1, currentStringPosition);
            bottomLine.SetPosition(0, bottomLine.transform.position);
            bottomLine.SetPosition(1, currentStringPosition);

            var ammoRoot = GetNockedAmmoRoot();
            if (ammoRoot == null) return;

            if (shooter.IsAiming || shooter.IsReloading)
            {
                ammoRoot.position = rightHandBone.position + rightHandBone.TransformDirection(arrowHoldingOffset);

                // Reloading holds the authored carry offset in hand space; once the draw is done
                // the arrow points down the shaft at the bow. Both are rebuilt from live
                // transforms each frame, so neither can drift behind a fast-turning hand.
                ammoRoot.rotation = shooter.IsReloading
                    ? GetReloadArrowRotation()
                    : GetNockedArrowRotation(ammoRoot.position);
            }
            else
            {
                ammoRoot.localPosition = Vector3.zero;
                ammoRoot.localRotation = Quaternion.identity;
            }
        }
    }
}
