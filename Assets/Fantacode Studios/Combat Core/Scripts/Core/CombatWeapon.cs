using FS_Core;
using FS_LocomotionSystem;
using UnityEngine;

namespace FS_CombatCore
{
    public class CombatWeapon : EquippableItem
    {
        [Tooltip("Bitmask of damage type(s) this weapon deals. Matched against the target's allowed damage types.")]
        [DamageTypeDropdown]
        [SerializeField] int weaponDamageType = ~0;

        [Tooltip("Data for reactions associated with the weapon.")]
        [SerializeField] ReactionsData reactionData;

        [Tooltip("Indicates if the weapon can block attacks.")]
        [SerializeField] bool canBlock;

        [Tooltip("The animation clip used for blocking with the attacks.")]
        [SerializeField] AnimationClip blocking;

        [Tooltip("The percentage of damage taken when getting hit while blocking.")]
        [Range(0, 100)]
        [SerializeField] float blockedDamage = 25f;

        [Tooltip("Data for reactions when blocking with the weapon.")]
        [SerializeField] ReactionsData blockReactionData;

        [Tooltip("Indicates if the locomotion movement speed should be overridden.")]
        [SerializeField] bool overrideMoveSpeed;

        [Tooltip("Movement speed while the character is in combat mode(Locked into a target).")]
        [SerializeField] float combatMoveSpeed = 2f;

        [Tooltip("2D locomotion clip set for directional movement in combat mode. If assigned, overrides base locomotion with per-weapon 2D blending.")]
        [SerializeField] LocomotionClipSet2D locomotionClipSet2D;

        [Tooltip("Indicates if the fighter can dodge.")]
        [SerializeField] bool overrideDodge;
        [SerializeField] DodgeData dodgeData;

        [Tooltip("Indicates if the fighter can roll.")]
        [SerializeField] bool overrideRoll;
        [SerializeField] DodgeData rollData;

        /// <summary>
        /// Returns the weapon's damage type bitmask.
        /// A value of 0 (Nothing) means no damage type is assigned and bypasses type filtering on the target.
        /// </summary>
        public int GetDamageType() => weaponDamageType;
        
        public ReactionsData ReactionData => reactionData;
        public bool CanBlock => canBlock;
        public AnimationClip Blocking => blocking;
        public float BlockedDamage => blockedDamage;
        public ReactionsData BlockReactionData => blockReactionData;
        public bool OverrideMoveSpeed => overrideMoveSpeed;
        public float CombatMoveSpeed => combatMoveSpeed;
        public LocomotionClipSet2D LocomotionClipSet2D => locomotionClipSet2D;
        public bool OverrideDodge => overrideDodge;
        public DodgeData DodgeData => dodgeData;
        public bool OverrideRoll => overrideRoll;
        public DodgeData RollData => rollData;
        public virtual HitType HitType => HitType.Any;
    }
}
