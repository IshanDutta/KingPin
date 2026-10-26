using FS_Core;
using FS_LocomotionSystem;
using UnityEngine;

namespace FS_Core
{
    public static partial class AnimatorParameters
    {
        //public static int coverMode = Animator.StringToHash("CoverMode");
    }

    public static partial class AnimationNames
    {
        public static string LocomotionToCover = "LocomotionToCover";
    }
}

namespace FS_CoverSystem
{
    /// <summary>
    /// Result of a cover probe. <see cref="found"/> is the only valid way to test for cover —
    /// never compare <see cref="standPosition"/> against <see cref="Vector3.zero"/>, the world
    /// origin is a legal place to stand.
    /// </summary>
    public struct CoverHit
    {
        /// <summary>True when a usable cover was found.</summary>
        public bool found;

        /// <summary>Collider hit at the bottom of the cover.</summary>
        public Collider collider;

        /// <summary>Point on the cover face, at the lowest probed height.</summary>
        public Vector3 point;

        /// <summary>Outward facing normal of the cover face, flattened to the horizontal plane.</summary>
        public Vector3 normal;

        /// <summary>Height of the top of the cover above the character's feet.</summary>
        public float height;

        /// <summary>Where the character should stand to be behind this cover.</summary>
        public Vector3 standPosition;
    }

    [RequireComponent(typeof(CoverInputManager))]
    public class CoverController : SystemBase, ICoverProvider
    {
        const string CoverLayerName = "Cover";
        const string CoverTag = "Cover";

        // Cached component references
        private EnvironmentScanner environmentScanner;
        private LocomotionCore locomotion;
        private ICharacter playerCharacter;
        private CharacterController characterController;
        private PlayerController playerController;
        private CoverHandler coverHandler;
        private Animator animator;
        private CoverInputManager coverInputManager;

        public bool enableCover = true;

        [SerializeField]
        private float coverCheckDistance = 4f;
        [SerializeField]
        private float playerHeightForCover = 2.6f;
        [SerializeField, Tooltip("Only accept obstacles on the \"Cover\" layer (or tagged \"Cover\") as cover.")]
        private bool OnlyCoverInCoverTags;

        [Header("Cover Detection")]
        [SerializeField, Tooltip("Radius of the probe spheres used to find a cover surface.")]
        private float probeRadius = 0.2f;
        [SerializeField, Tooltip("Probes start this far behind the character so a cover it is already touching is still found.")]
        private float probeBackOffset = 0.2f;
        [SerializeField, Tooltip("Height above the feet of the lowest probe. A cover must be solid from here upwards.")]
        private float probeStartHeight = 0.3f;
        [SerializeField, Tooltip("Vertical distance between probes.")]
        private float probeHeightStep = 0.15f;
        [SerializeField, Tooltip("Probing stops here. Anything taller simply counts as full height cover.")]
        private float maxProbeHeight = 2.2f;
        [SerializeField, Tooltip("An obstacle shorter than this is a step, not a cover.")]
        private float minCoverHeight = 1f;
        [SerializeField, Tooltip("How far a surface may tilt from vertical and still be cover. Keeps ramps, slopes and the ground out.")]
        private float maxSurfaceTiltAngle = 35f;
        [SerializeField, Tooltip("Probes at different heights may differ by at most this much in depth before they count as two different surfaces (a crate in front of a wall).")]
        private float surfaceDepthTolerance = 0.4f;
        [SerializeField, Tooltip("Probes at different heights may differ by at most this angle in facing and still be the same cover face.")]
        private float surfaceContinuityAngle = 45f;
        [SerializeField, Tooltip("Gap left between the character's capsule and the cover face when standing at it.")]
        private float coverStandGap = 0.05f;
        [SerializeField, Tooltip("Speed used to run to the detected cover.")]
        private float coverApproachSpeed = 4f;
        [SerializeField, Tooltip("Extra distance beyond the character's capsule at which the approach counts as having reached the cover face.")]
        private float coverArrivalTolerance = 0.25f;
        [SerializeField, Tooltip("Give up approaching after this long, so an unreachable cover can never pin the character against a wall.")]
        private float approachTimeout = 4f;
        [SerializeField, Tooltip("Give up when the character has not got closer to the cover face for this long.")]
        private float approachStuckTimeout = 0.6f;
        [SerializeField, Tooltip("Draw the cover probes in the scene view while this object is selected and playing.")]
        private bool drawCoverProbes;

        // Reused so the per-frame probes don't allocate
        readonly RaycastHit[] castBuffer = new RaycastHit[16];

        // Approach state
        CoverHit approachCover;
        bool approachHasCover;
        float approachTime;
        float approachStuckTime;
        float approachClosestDistance;

        static int coverLayerIndex = -2;    // -2 = unresolved, -1 = the layer does not exist
        static int coverTagState = -1;      // -1 = unresolved, 0 = missing, 1 = exists

        // Public properties to store the last cover position and forward direction
        public Vector3 LastCoverPosition { get; set; }
        public Vector3 LastCoverForward { get; set; }
        public bool WasInCover { get; set; }

        public override SystemState State => SystemState.Cover;
        public override float Priority => 4;

        public bool ChaseMode { get; set; }
        public bool CustomMode { get; set; }

        private void Start()
        {
            environmentScanner = GetComponent<EnvironmentScanner>();
            locomotion = GetComponent<LocomotionCore>();
            playerCharacter = GetComponent<ICharacter>();
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();
            coverHandler = GetComponent<CoverHandler>();
            animator = GetComponent<Animator>();
            coverInputManager = GetComponent<CoverInputManager>();
            if (coverInputManager == null)
                coverInputManager = gameObject.AddComponent<CoverInputManager>();

            LastCoverPosition = transform.position;

            // Subscribe to the state exit event
            OnStateExited += HandleStateExit;
        }

        private void HandleStateExit()
        {
            coverHandler.GoOutOfCover();
            LastCoverPosition = transform.position;
        }

        public override void HandleUpdate()
        {
            if (!enableCover) return;
            var coverKeyPressed = coverInputManager != null && coverInputManager.CoverPressed;

            if (!coverHandler.InCover && !ChaseMode)
            {
                if (coverKeyPressed)
                {
                    StartChaseToCover();
                }
            }
            else
            {
                if (coverKeyPressed && !ChaseMode)
                {
                    StopCover();
                    return;
                }

                // Try to align to the cover if not yet in proper cover mode
                if (ChaseMode)
                {
                    if (!UpdateApproach())
                        return;     // still running to the cover, or the approach was cancelled

                    StartCover();
                    ChaseMode = false;
                }
                else
                {
                    locomotion.SetCurrentVelocity(0, Vector3.zero);
                }

                // Update the equipped system if available
                if(!animator.IsInTransition(0) && !ChaseMode)
                    playerController.CurrentEquippedSystem?.HandleUpdate();

                // The equipped system may have stepped out of cover in the call above (aiming a
                // weapon or a throwable). Running another frame of cover movement/rotation on a
                // character that just left cover snaps it back against the wall.
                if (!coverHandler.InCover)
                    return;

                // Custom mode bypasses default cover movement updates
                //if (CustomMode)
                //    return; 

                UpdateCoverAnimationAndMovement();
            }
        }
        /// <summary>
        /// Updates the cover animation and adjusts movement while in cover mode.
        /// </summary>
        private void UpdateCoverAnimationAndMovement()
        {
            // Define the starting position for the sphere cast
            Vector3 startPos = transform.position - transform.forward * 0.2f;
            startPos.y += 0.8f;

            int castIterations;
            for (castIterations = 0; castIterations < 10; castIterations++)
            {
                //GizmosExtend.drawSphereCast(startPos, 0.1f, transform.forward, 1f, Color.blue);
                // Same geometry as before, so the CoverType curve is unchanged; the cast now
                // rejects triggers, the character's own colliders and Unity's zeroed
                // initial-overlap hit. Tilt is not enforced here - it would change the measured
                // height at the top edge of a cover, and with it the tuned crouch blend.
                if (CastForCover(startPos, transform.forward, 1f, 0.1f, false, out _))
                {
                    startPos.y += 0.1f;
                }
                else
                {
                    break;
                }
            }

            // Determine cover type value based on the number of iterations
            float coverTypeValue = (1f - (castIterations / 10f)) * playerHeightForCover;
            playerCharacter.Animator.SetFloat("CoverType", coverTypeValue, 0.2f, Time.deltaTime);

            startPos = transform.position;
            startPos.y += 0.8f;
            // The alignment normal must come from a wall, not from a ramp or the floor - a
            // near-horizontal normal flattens to near-zero and yields a meaningless facing. On
            // failure the normal is zeroed exactly as before, which leaves the rotation alone.
            if (!CastForCover(startPos, transform.forward, coverCheckDistance, 0.1f, true, out RaycastHit coverHit))
            {
                coverHit.normal = Vector3.zero;
            }

            // Flatten the hit normal to the horizontal plane
            Vector3 adjustedNormal = new Vector3(coverHit.normal.x, 0, coverHit.normal.z);
            Quaternion targetRotation;
            if (adjustedNormal.sqrMagnitude > 0.0001f)
            {
                targetRotation = Quaternion.LookRotation(-adjustedNormal);
            }
            else
            {
                targetRotation = transform.rotation; // fallback to current rotation if normal is zero
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * 100f);

            // Calculate the movement direction along the cover
            float movDir = Vector3.Dot(transform.right, playerCharacter.MoveDir);
            Vector3 sideCastOrigin = startPos + (movDir * transform.right).normalized * 0.5f;
            //GizmosExtend.drawSphereCast(sideCastOrigin, 0.1f, transform.forward, 1f, Color.blue);

            if (CastForCover(sideCastOrigin, -adjustedNormal, 1f, 0.1f, false, out _))
            {
                playerCharacter.Animator.SetFloat("CoverDir", movDir, 0.25f, Time.deltaTime);
                characterController.Move(playerCharacter.Animator.deltaPosition + (-adjustedNormal * Time.deltaTime) + Vector3.down);
            }
            else
            {
                playerCharacter.Animator.SetFloat("CoverDir", 0f, 0.1f, Time.deltaTime);
                characterController.Move(-adjustedNormal * Time.deltaTime + Vector3.down);
            }
        }
        public Vector3 StartChaseToCover()
        {
            if (!TryFindCover(coverCheckDistance, out CoverHit cover))
                return Vector3.zero;

            playerCharacter.FocusSystem(this);

            approachCover = cover;
            approachHasCover = true;
            approachTime = 0f;
            approachStuckTime = 0f;
            approachClosestDistance = float.MaxValue;

            ChaseMode = true;
            locomotion.SetCrouching(false);

            return cover.standPosition;
        }

        /// <summary>
        /// Drives the run to the cover. Returns true once the character has reached it.
        /// </summary>
        bool UpdateApproach()
        {
            // Keep the target fresh while the probe still sees a cover, but never cancel on a
            // single empty frame — probes get noisy exactly where the approach ends, at the wall.
            if (TryFindCover(coverCheckDistance, out CoverHit cover))
            {
                approachCover = cover;
                approachHasCover = true;
            }
            else if (!approachHasCover)
            {
                CancelApproach();
                return false;
            }

            // Arrival is measured perpendicular to the cover face, not as the distance to the one
            // point the probe returned. That point sits wherever the cast happened to cross the
            // wall, so approaching off-axis leaves it well off to the side: the character reaches
            // the wall, cannot close the lateral gap because the wall blocks it, and a
            // point-distance test never completes — leaving it running on the spot against the
            // wall instead of taking cover.
            float standDistance = (characterController != null ? characterController.radius : 0.3f) + coverStandGap;
            float distanceToFace = Vector3.Dot(transform.position - approachCover.point, approachCover.normal);

            // The tolerance also has to cover one frame of travel so a long frame cannot overshoot.
            float tolerance = Mathf.Max(coverArrivalTolerance, coverApproachSpeed * Time.deltaTime * 1.5f);
            if (distanceToFace <= standDistance + tolerance)
                return true;

            // Progress guard. Anything that leaves the cover unreachable — a blocked path, an
            // inside corner, a target that keeps sliding along the wall — has to release the
            // character rather than pin it against the geometry.
            approachTime += Time.deltaTime;
            if (distanceToFace < approachClosestDistance - 0.01f)
            {
                approachClosestDistance = distanceToFace;
                approachStuckTime = 0f;
            }
            else
            {
                approachStuckTime += Time.deltaTime;
            }

            if (approachTime >= approachTimeout || approachStuckTime >= approachStuckTimeout)
            {
                CancelApproach();
                return false;
            }

            Vector3 target = approachCover.standPosition;
            target.y = transform.position.y;
            locomotion.ReachDestination(target, coverApproachSpeed);
            return false;
        }

        void CancelApproach()
        {
            ChaseMode = false;
            approachHasCover = false;
            playerCharacter.UnFocusSystem(this);
        }

        #region Cover detection

        /// <summary>
        /// Checks for a viable cover position ahead of the character.
        /// Legacy shim: returns <see cref="Vector3.zero"/> when nothing was found. Prefer
        /// <see cref="TryFindCover"/> — it reports failure explicitly instead of through a value
        /// that is also a perfectly legal place to stand.
        /// </summary>
        public Vector3 CheckCover(float checkDistance = 0)
        {
            return TryFindCover(checkDistance <= 0 ? coverCheckDistance : checkDistance, out CoverHit cover)
                ? cover.standPosition
                : Vector3.zero;
        }

        /// <summary>
        /// Probes the obstacle ahead of the character and reports whether it is usable cover.
        ///
        /// The probes climb the obstacle from <see cref="probeStartHeight"/> upwards, and the
        /// surface only qualifies when it is
        ///   * one continuous face over that whole climb — consistent depth and consistent
        ///     facing, which is what stops a low crate from being measured as the wall metres
        ///     behind it,
        ///   * at least <see cref="minCoverHeight"/> tall,
        ///   * close enough to vertical to hide behind, and
        ///   * facing the character rather than clipped at an edge.
        /// </summary>
        public bool TryFindCover(float maxDistance, out CoverHit cover)
        {
            cover = default;

            Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();

            Vector3 feet = transform.position;
            Vector3 probeOrigin = feet - direction * probeBackOffset;
            float castDistance = maxDistance + probeBackOffset;

            float step = Mathf.Max(0.02f, probeHeightStep);
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt((maxProbeHeight - probeStartHeight) / step) + 1);

            RaycastHit baseHit = default;
            bool baseFound = false;
            float topHeight = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float height = probeStartHeight + i * step;
                if (height > maxProbeHeight) break;

                if (!CastForCover(probeOrigin + Vector3.up * height, direction, castDistance, probeRadius, true, out RaycastHit hit))
                    break;      // free space at this height — the top of the obstacle

                if (!baseFound)
                {
                    // The lowest blocked probe defines the surface being measured. It has to face
                    // us; if it does not we clipped an edge rather than hitting a face.
                    if (Vector3.Dot(Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized, direction) > -0.2f)
                        return false;

                    baseHit = hit;
                    baseFound = true;
                }
                else
                {
                    // Continuity, not collider identity: covers are routinely split across
                    // colliders, and one collider can present several faces. A probe at a very
                    // different depth or facing a different way is a different surface.
                    if (Mathf.Abs(hit.distance - baseHit.distance) > surfaceDepthTolerance) break;
                    if (Vector3.Angle(hit.normal, baseHit.normal) > surfaceContinuityAngle) break;
                }

                topHeight = height;
            }

            if (!baseFound) return false;

            // The last blocked probe puts the top somewhere in
            // [topHeight - probeRadius, topHeight - probeRadius + step); take the middle.
            float coverHeight = Mathf.Max(0f, topHeight - probeRadius + step * 0.5f);
            if (coverHeight < minCoverHeight) return false;

            Vector3 normal = Vector3.ProjectOnPlane(baseHit.normal, Vector3.up);
            if (normal.sqrMagnitude < 0.0001f) return false;
            normal.Normalize();

            // The old code handed back hit.point — a point *on* the wall — as the place to run
            // to, which left the stopping distance to whatever the 0.5 m arrival check happened
            // to produce. Push it out by the capsule instead.
            float radius = characterController != null ? characterController.radius : 0.3f;

            cover.found = true;
            cover.collider = baseHit.collider;
            cover.point = baseHit.point;
            cover.normal = normal;
            cover.height = coverHeight;
            cover.standPosition = new Vector3(baseHit.point.x, feet.y, baseHit.point.z) + normal * (radius + coverStandGap);
            return true;
        }

        /// <summary>
        /// Sphere cast that drops everything a cover probe must never accept: trigger volumes,
        /// the character's own colliders, Unity's zeroed initial-overlap hit, optionally
        /// non-vertical surfaces, and anything not marked as cover when that filter is on.
        /// Returns the closest surviving hit rather than whatever came back first, so one bad
        /// collider in front of a real cover no longer hides it.
        /// </summary>
        bool CastForCover(Vector3 origin, Vector3 direction, float distance, float radius, bool requireVerticalSurface, out RaycastHit result)
        {
            result = default;

            if (direction.sqrMagnitude < 0.0001f) return false;     // e.g. a zeroed cover normal
            direction.Normalize();

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, castBuffer, distance,
                                                   ObstacleMask, QueryTriggerInteraction.Ignore);
            float closest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castBuffer[i];
                if (hit.collider == null) continue;

                // Unity reports distance 0 with an undefined point (0,0,0) and normal when the
                // sweep starts already overlapping a collider. That hit carries no usable data.
                if (hit.distance <= 0f) continue;

                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (requireVerticalSurface && !IsVerticalSurface(hit.normal)) continue;
                if (!PassesCoverFilter(hit.collider)) continue;
                if (hit.distance >= closest) continue;

                closest = hit.distance;
                result = hit;
                found = true;
            }

            return found;
        }

        LayerMask ObstacleMask
        {
            get
            {
                if (environmentScanner != null) return environmentScanner.ObstacleLayer;
                if (FSSettings.i != null) return FSSettings.i.ObstacleLayer;
                return Physics.DefaultRaycastLayers;
            }
        }

        bool IsVerticalSurface(Vector3 normal)
        {
            return Mathf.Abs(normal.y) <= Mathf.Sin(Mathf.Clamp(maxSurfaceTiltAngle, 0f, 89f) * Mathf.Deg2Rad);
        }

        bool PassesCoverFilter(Collider col)
        {
            if (!OnlyCoverInCoverTags) return true;

            // The original compared only the tag. This project marks covers with the "Cover"
            // *layer* and defines no such tag, so the filter rejected every obstacle and turned
            // cover off completely. Layer first, tag second.
            int layer = ResolveCoverLayer();
            if (layer >= 0 && col.gameObject.layer == layer) return true;

            return CoverTagExists() && col.CompareTag(CoverTag);
        }

        static int ResolveCoverLayer()
        {
            if (coverLayerIndex == -2)
                coverLayerIndex = LayerMask.NameToLayer(CoverLayerName);
            return coverLayerIndex;
        }

        /// <summary>
        /// CompareTag logs an error for tags the project does not define, so probe once and cache.
        /// </summary>
        static bool CoverTagExists()
        {
            if (coverTagState < 0)
            {
                try
                {
                    GameObject.FindGameObjectsWithTag(CoverTag);
                    coverTagState = 1;
                }
                catch (UnityException)
                {
                    coverTagState = 0;
                }
            }
            return coverTagState == 1;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawCoverProbes || !Application.isPlaying) return;

            Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            Vector3 probeOrigin = transform.position - direction * probeBackOffset;
            bool found = TryFindCover(coverCheckDistance, out CoverHit cover);
            Gizmos.color = found ? Color.green : Color.red;

            float step = Mathf.Max(0.02f, probeHeightStep);
            for (float h = probeStartHeight; h <= maxProbeHeight; h += step)
            {
                Vector3 start = probeOrigin + Vector3.up * h;
                Gizmos.DrawWireSphere(start, probeRadius);
                Gizmos.DrawLine(start, start + direction * (coverCheckDistance + probeBackOffset));
            }

            if (!found) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(cover.standPosition, 0.15f);
            Gizmos.DrawRay(cover.point, cover.normal);

            Vector3 faceAtFeet = new Vector3(cover.point.x, transform.position.y, cover.point.z);
            Gizmos.DrawLine(faceAtFeet, faceAtFeet + Vector3.up * cover.height);
        }

        #endregion

        bool previousCameraAlignment;
        public void StartCover()
        {
            // Already covering and already in control — a redundant entry would restart the
            // LocomotionToCover crossfade mid-pose. The Shooter reaches here on OnStopAim
            // whenever WasInCover is still set, which happens if cover was taken *during* the
            // aim it is returning from.
            if (coverHandler.InCover && playerController.FocusedScript == this)
            {
                ChaseMode = false;
                WasInCover = false;
                return;
            }

            playerCharacter.FocusSystem(this);
            coverHandler.GoToCover();
            locomotion.SetRaycastHeightOverride(0.8f);

            // Nothing is owed a "return to cover" any more — we are in it.
            WasInCover = false;
            //previousCameraAlignment = playerController.AlignTargetWithCameraForward;
            //playerController.AlignTargetWithCameraForward = false;
        }

        public void StopCover()
        {
            coverHandler.GoOutOfCover();
            locomotion.SetRaycastHeightOverride(null);
            playerCharacter.UnFocusSystem(this);
            //playerController.AlignTargetWithCameraForward = previousCameraAlignment;
        }

        #region ICoverProvider

        public bool IsInCover => coverHandler != null && coverHandler.InCover;

        /// <summary>
        /// Steps out of cover for an action that cover blocks (aiming a throwable, etc.).
        /// Mirrors what the Shooter does when the player starts aiming from cover.
        /// </summary>
        public void ExitCoverForAction()
        {
            if (!IsInCover) return;

            LastCoverPosition = transform.position;
            LastCoverForward = transform.forward;
            CustomMode = false;
            WasInCover = true;
            StopCover();
        }

        /// <summary>
        /// Steps back into the cover left by <see cref="ExitCoverForAction"/>, provided the
        /// player hasn't moved away from it in the meantime.
        /// </summary>
        public void ReturnToCoverAfterAction()
        {
            CustomMode = false;
            WasInCover = false;

            // Another system took over during the action (parkour, swimming, ...) — it owns the
            // character now, so don't pull focus back into cover from underneath it.
            if (playerController.FocusedScript != null) return;

            // Only return if the player is still standing against a cover.
            if (!TryFindCover(0.5f, out _)) return;

            StartCover();
        }

        #endregion

    }

    /// <summary>
    /// Extension method to modify vector components.
    /// </summary>
    public static class VectorExtensions
    {
        /// <summary>
        /// Returns a copy of the vector with the y component replaced.
        /// </summary>
        public static Vector3 SetY(this Vector3 v, float newY)
        {
            return new Vector3(v.x, newY, v.z);
        }
    }
}
