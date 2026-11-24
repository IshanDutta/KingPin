using FS_CombatCore;
using FS_Core;
using FS_Shooter;
using FS_Util;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class ShooterAIController : MonoBehaviour
    {
        [Header("Shoot Settings")]
        [Tooltip("When enabled, the shoot state completes after the min/max shoot time elapses")]
        [SerializeField] private bool useShootDuration = true;

        [Tooltip("Minimum time (seconds) to remain in shooting")]
        [SerializeField] private float minShootTime = 3f;

        [Tooltip("Maximum time (seconds) to remain in shooting")]
        [SerializeField] private float maxShootTime = 6f;

        [Tooltip("How far (in meters) the AI's shots can stray from the point it aims at. 0 means perfectly accurate, higher values make the AI miss more often. Shots also become more accurate at close range regardless of this value")]
        [SerializeField] private float aimError = 0.5f;

        [Tooltip("When enabled, the AI waits a random interval between shots instead of firing at the weapon's rate. Useful for slow, deliberate shooters like archers")]
        [SerializeField] private bool hasFireInterval = false;

        [Tooltip("Minimum seconds between shots")]
        [ShowIf("hasFireInterval", true)]
        [SerializeField] private float minFireInterval = 3f;

        [Tooltip("Maximum seconds between shots")]
        [ShowIf("hasFireInterval", true)]
        [SerializeField] private float maxFireInterval = 6f;

        [Header("Charged Shot Settings")]
        [Tooltip("When enabled, charged weapons (bows and the like) are drawn and held before release, and the shot's force scales with how long it was drawn — the same way the player's hold-to-charge works. Weapons not marked as charged are unaffected")]
        [SerializeField] private bool useChargedShots = true;

        [Tooltip("Minimum seconds the AI holds the draw before releasing a charged shot")]
        [ShowIf("useChargedShots", true)]
        [SerializeField] private float minChargeTime = 1f;

        [Tooltip("Maximum seconds the AI holds the draw before releasing a charged shot. Longer draws hit harder, up to the weapon's full force")]
        [ShowIf("useChargedShots", true)]
        [SerializeField] private float maxChargeTime = 1.5f;

        [Header("Cover Settings")]

        [Tooltip("Minimum seconds to remain in cover")]
        [SerializeField] private float minCoverTime = 1f;

        [Tooltip("Maximum seconds to remain in cover")]
        [SerializeField] private float maxCoverTime = 3f;

        [Header("Cover Movement")]
        [Tooltip("Speed at which the AI approaches its cover point")]
        [SerializeField] private float coverApproachSpeed = 3.5f;

        [SerializeField] private float maxCoverHeightMultiplier = 3f;

        [Header("Cover Scoring Weights")]
        [SerializeField] private float distanceWeight = 1f;
        [SerializeField] private float distanceToTargetWeight = 1f;
        [SerializeField] private float occupantWeight = 10f;

        [Header("Cover Distance Thresholds")]
        [Tooltip("Avoid taking cover when too close to the target")]
        [SerializeField] private float coverAvoidDistance = 2f;

        [Tooltip("Covers below this distance won't be chosen")]
        [SerializeField] private float minCoverDistance = 2f;

        [Tooltip("Range to search for cover colliders")]
        [SerializeField] private float coverRange = 10f;

        [Tooltip("When disabled, cover availability checks are skipped and the AI will keep shooting instead of taking cover. Useful for levels without cover areas")]
        [SerializeField] private bool shouldCheckForCover = true;

        [Tooltip("Minimum interval (seconds) between cover availability physics checks, calls in between return the cached value")]
        [SerializeField] private float coverCheckInterval = 0.5f;

        [Header("Clear Shot Settings")]
        [Tooltip("Fallback height above the AI's feet from which the firing line is checked when no weapon is equipped. With a weapon equipped, the actual muzzle position is used instead")]
        [SerializeField] private float clearShotCheckHeight = 1.4f;

        [Tooltip("Seconds vision and the firing line can disagree (target visible but shot blocked, or clear shot but no LOS) before the AI advances toward the target to fix its position")]
        [SerializeField] private float clearShotRepositionDelay = 2f;

        [Tooltip("Radius of the clearance corridor required along the firing line. Bullets scatter around the aim center (aim noise + spread), so a hairline gap that only clears the exact center line isn't a real shot")]
        [SerializeField] private float clearShotClearance = 0.25f;

        [Tooltip("Movement speed while advancing from the shoot state to fix its position (matches the flank walk speed by default)")]
        [SerializeField] private float advanceSpeed = 2f;

        private AIController aiController;
        private CombatAIController combatAI;
        private ShooterFighter shooter;
        private AICoverPointFinder coverPointFinder;
        private float lastCoverCheckTime = float.NegativeInfinity;
        private bool cachedCoverAvailable;

        public bool ShouldCheckForCover { get => shouldCheckForCover; set => shouldCheckForCover = value; }

        // Whether a valid cover point currently exists near the AI, used to decide between taking cover and continuing to shoot.
        public bool IsCoverAvailable
        {
            get
            {
                if (!shouldCheckForCover)
                    return false;

                if (combatAI.Fighter.Target == null)
                    return false;

                if (Time.time - lastCoverCheckTime < coverCheckInterval)
                    return cachedCoverAvailable;

                lastCoverCheckTime = Time.time;
                cachedCoverAvailable = coverPointFinder.FindBestCoverPoint() != null;
                return cachedCoverAvailable;
            }
        }

        // Heights above the target position (feet level) sampled by CanSeeTargetPosition —
        // roughly the feet, waist and head of the space the target occupies.
        static readonly float[] targetPositionSampleHeights = { 0.5f, 1f, 1.5f };

        /// <summary>
        /// Whether the AI can see any part of the space around the target position —
        /// GetTargetPosition(), i.e. the last seen position when UseLastSeenPosition is enabled,
        /// the target's actual position otherwise. This is the suppression gate: shooting at a
        /// position is only plausible when some part of that space is actually visible. 
        /// </summary>
        public bool CanSeeTargetPosition()
        {
            if (combatAI.Fighter.Target == null)
                return false;

            Vector3 basePos = combatAI.GetTargetPosition();
            Vector3 origin = shooter != null && shooter.CurrentShooterWeaponObject != null
                ? shooter.CurrentShooterWeaponObject.ammoSpawnPoint.position
                : transform.position + Vector3.up * clearShotCheckHeight;

            foreach (float height in targetPositionSampleHeights)
            {
                Vector3 point = basePos + Vector3.up * height;
                Vector3 toPoint = point - origin;
                float dist = toPoint.magnitude;
                if (dist < 0.001f)
                    return true;

                if (!Physics.Raycast(origin, toPoint / dist, dist, FSSettings.i.ObstacleLayer))
                {
                    // Debug.DrawLine(origin, point, Color.cyan);
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            aiController = GetComponent<AIController>();
            combatAI = GetComponent<CombatAIController>();
            shooter = GetComponent<ShooterFighter>();
            coverPointFinder = new AICoverPointFinder(aiController, combatAI, this);
        }

        private void Start()
        {
            // If we get an attack while and the target is not visible then turn FlankRequested to true since the AI should try
            // Flank and gain visiblilty on the target
            combatAI.Fighter.OnGotHit += (FighterCore attacker, Vector3 hitPoint, float hittingTime, bool isBlockedHit, HitType hitType) =>
            {
                if (attacker == null || attacker != CombatAIManager.i.Player || combatAI.Fighter.Target != attacker)
                    return;

                if (combatAI.IsTargetVisible)
                    return;

                if (ShooterAIManager.Instance != null
                    && Vector3.Distance(transform.position, attacker.transform.position) < ShooterAIManager.Instance.ImmediateFlankDistanceThreshold)
                    aiController.Blackboard.Set<bool>(ShooterBlackboardKeys.FlankRequested, true);
            };
        }

        /// <summary>
        /// The aim point validated by the last HasClearShot() call, null when no candidate was clear.
        /// </summary>
        public Vector3? ClearShotAimPoint { get; private set; }

        /// <summary>
        /// Whether a bullet fired from ammo spwans point of the gun would reach the target. 
        /// </summary>
        public bool HasClearShot()
        {
            ClearShotAimPoint = null;

            var target = combatAI.Fighter.Target;
            if (target == null)
                return false;

            // Cast from AmmoSpawnPoint -> Target
            Vector3 origin = shooter != null && shooter.CurrentShooterWeaponObject != null
                ? shooter.CurrentShooterWeaponObject.ammoSpawnPoint.position
                : transform.position + Vector3.up * clearShotCheckHeight;

            // Chest first (biggest target). If the chest is covered, fall back to the head center — the
            // clearance sphere only fits past a cover lip when the whole head and neck are exposed
            if (HasClearShotTo(origin, ShooterFighter.GetTargetAimCenter(target)))
                return true;

            var head = target.animator != null && target.animator.isHuman
                ? target.animator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            return head != null && HasClearShotTo(origin, head.position);
        }

        bool HasClearShotTo(Vector3 origin, Vector3 aimPoint)
        {
            Vector3 toAimPoint = aimPoint - origin;
            float dist = toAimPoint.magnitude;

            // SphereCast instead of Raycast since bullets scatter around the aim point. The cast stops one radius short of the 
            // target so the sphere doesn't clip geometry directly behind the target (e.g. the target pressed against a wall).
            bool clear = dist < 0.001f
                || !Physics.SphereCast(new Ray(origin, toAimPoint / dist), clearShotClearance,
                    dist - clearShotClearance, FSSettings.i.ObstacleLayer);

            // Debug.DrawLine(origin, aimPoint, clear ? Color.green : Color.red);
            if (clear)
                ClearShotAimPoint = aimPoint;
            return clear;
        }

        // Shoot
        public bool UseDuration => useShootDuration;
        public float MinShootTime => minShootTime;
        public float MaxShootTime => maxShootTime;
        public float AimError => aimError;
        public bool HasFireInterval => hasFireInterval;
        public float MinFireInterval => minFireInterval;
        public float MaxFireInterval => maxFireInterval;
        public bool UseChargedShots => useChargedShots;
        public float MinChargeTime => minChargeTime;
        public float MaxChargeTime => maxChargeTime;
        public float ClearShotRepositionDelay => clearShotRepositionDelay;
        public float AdvanceSpeed => advanceSpeed;

        // Cover
        public float MinCoverTime => minCoverTime;
        public float MaxCoverTime => maxCoverTime;
        public float CoverApproachSpeed => coverApproachSpeed;
        public float MaxCoverHeightMultiplier => maxCoverHeightMultiplier;
        public float DistanceWeight => distanceWeight;
        public float DistanceToTargetWeight => distanceToTargetWeight;
        public float OccupantWeight => occupantWeight;
        public float CoverAvoidDistance => coverAvoidDistance;
        public float MinCoverDistance => minCoverDistance;
        public float CoverRange => coverRange;
    }
}
