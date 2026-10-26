using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_InteractionSystem
{
    /// <summary>
    /// Where the carrier stands (and faces) to start a pickup, expressed in the carried object's
    /// body-canonical space — the same frame InteractionPoint offsets use: origin at the hips,
    /// +Z toward the feet for a lying character (horizontal-forward for props). Authored by hand:
    /// enable it, then drag the magenta scene-view handle on the CarryHotspot (play mode with the
    /// body lying gives the truest frame, and edits persist — the data lives on this asset).
    /// </summary>
    [System.Serializable]
    public class CarryStandPoint
    {
        [Tooltip("Use this stand point. When off, the CarryHotspot falls back to its manual " +
                 "interactionPoints list for this pose.")]
        public bool enabled;

        [Tooltip("Stand position relative to the body pivot, in body-canonical space (Y is ignored — " +
                 "the stand position is ground-snapped at runtime).")]
        public Vector3 positionOffset = new Vector3(0.6f, 0f, 0f);

        [Tooltip("Facing (degrees) relative to the body-canonical forward.")]
        public float facingDirection = 90f;
    }

    /// <summary>
    /// ScriptableObject that defines the type and full configuration of a carryable object.
    /// Create one asset per logical category (e.g. "DeadBody", "WoodenBox").
    /// The asset reference is used as the type identity by <see cref="ConcealmentHotspot"/>:
    /// objects sharing the same CarryableData asset are considered the same carryable type.
    /// </summary>
    [CreateAssetMenu(fileName = "CarryableData", menuName = "Interaction System/Carryable Data")]
    public class CarryableData : ScriptableObject
    {
        [Header("Socket")]
        [Tooltip("Which socket on the carrier this object attaches to.")]
        public CarrySocketType carrySocket;

        [Tooltip("Seat the object at the position offset once it attaches. Disable to keep the " +
                 "position the pickup animation produced (e.g. a limp body driven entirely by " +
                 "animation). With both this and Apply Rotation Offset off, the object is left " +
                 "exactly where the animation puts it.")]
        public bool applyPositionOffset = true;

        [Tooltip("Force the rotation offset once the object attaches. Disable to keep the rotation " +
                 "the pickup animation produced — e.g. a body draped over the shoulder, where forcing " +
                 "a rotation fights the animation.")]
        public bool applyRotationOffset = true;

        [Tooltip("Local position offset applied relative to the socket transform.")]
        [ShowIf(LogicOperator.And, "applyPositionOffset", true, "useCharacterPosePickup", false)]
        public Vector3 carrySocketPosOffset;

        [Tooltip("Local rotation offset (Euler angles) applied relative to the socket transform.")]
        [ShowIf(LogicOperator.And, "applyRotationOffset", true, "useCharacterPosePickup", false)]
        public Vector3 carrySocketRotOffset;

        // Character-pose carries use a separate offset per pose: the face-up and face-down pose clips
        // rest on the socket at different body points, so a single offset can't serve both.
        [Tooltip("Socket position offset when carrying a character lying face up (on back).")]
        [ShowIf(LogicOperator.And, "applyPositionOffset", true, "useCharacterPosePickup", true)]
        public Vector3 carrySocketPosOffsetFaceUp;

        [Tooltip("Socket rotation offset (Euler) when carrying a character lying face up (on back).")]
        [ShowIf(LogicOperator.And, "applyRotationOffset", true, "useCharacterPosePickup", true)]
        public Vector3 carrySocketRotOffsetFaceUp;

        [Tooltip("Socket position offset when carrying a character lying face down.")]
        [ShowIf(LogicOperator.And, "applyPositionOffset", true, "useCharacterPosePickup", true)]
        public Vector3 carrySocketPosOffsetFaceDown;

        [Tooltip("Socket rotation offset (Euler) when carrying a character lying face down.")]
        [ShowIf(LogicOperator.And, "applyRotationOffset", true, "useCharacterPosePickup", true)]
        public Vector3 carrySocketRotOffsetFaceDown;

        [Tooltip("Seconds to smoothly ease the object to its carry socket offset when picked up. 0 = snap instantly.")]
        [ShowIf(LogicOperator.Or, "applyPositionOffset", true, "applyRotationOffset", true)]
        [Min(0f)]
        public float carrySocketMoveDuration = 0.35f;


        [Header("Carrier Animations")]

        [Tooltip("Looping animation played on the carrier while holding this object.")]
        public AnimGraphClipInfo carryIdleClip;

        [Tooltip("Avatar mask the carry idle clip is played on (which body part it drives).")]
        public Mask carryIdleClipMask = Mask.UpperBody;

        [Tooltip("Animation played on the carrier when placing the object down gently.")]
        public AnimGraphClipInfo dropClip;

        [Tooltip("When enabled, this object can be thrown by the carrier.")]
        public bool isThrowable = true;

        [Tooltip("Animation played on the carrier when throwing the object with force.")]
        [ShowIf("isThrowable", true)]
        public AnimGraphClipInfo throwClip;

        [Tooltip("Animation played on the carried object itself when it is set down. " +
                 "When assigned, the object's ragdoll / release fires when this clip completes " +
                 "rather than at a normalised-time threshold.")]
        public AnimGraphClipInfo objectDropClip;


        [Header("Pickup")]
        [Space(5)]
        [Tooltip("When true, the system detects whether the character is lying face-up or face-down " +
                 "and selects the appropriate pickup animation below. Falls back to carryingClip if unassigned.")]
        public bool useCharacterPosePickup;

        [ShowIf("useCharacterPosePickup", false)]
        [Tooltip("Animation played on the carrier when picking this object up.")]
        public AnimGraphClipInfo carryingClip;

        [ShowIf("useCharacterPosePickup", false)]
        [Tooltip("Animation played on a secondary target actor when picking up (e.g. a rack reacting). " +
               "If assigned and a target carried object is provided on the CarryHotspot, the carrier's " +
               "clip fires concurrently while this clip is awaited.")]
        public AnimGraphClipInfo targetClip;

        

       

        [Tooltip("Pickup animation used when the character is lying on their back (face up).")]
        [ShowIf("useCharacterPosePickup", true)]
        public AnimGraphClipInfo carryingClipFaceUp;

        [Tooltip("Pickup animation used when the character is lying on their front (face down).")]
        [ShowIf("useCharacterPosePickup", true)]
        public AnimGraphClipInfo carryingClipFaceDown;

        [Tooltip("Target clip played on the carried character when lying face up.")]
        [ShowIf("useCharacterPosePickup", true)]
        public AnimGraphClipInfo targetClipFaceUp;

        [Tooltip("Target clip played on the carried character when lying face down.")]
        [ShowIf("useCharacterPosePickup", true)]
        public AnimGraphClipInfo targetClipFaceDown;

        [Space(10)]

        [Tooltip("Normalised time within interactionClip at which the object attaches to the carrier.")]
        [Range(0f, 1f)]
        public float carryStartNormalizedTime;


        [Header("Stand Points (per pose)")]
        [Tooltip("Where the carrier stands to start the pickup, in the body-canonical frame. Enable, " +
                 "then position by dragging the magenta scene-view handle on the CarryHotspot.")]
        [ShowIf("useCharacterPosePickup", false)]
        public CarryStandPoint standPoint = new CarryStandPoint();

        // Character-pose carries stand differently for each pose — the two pickup animations approach
        // the body from their own side — so each pose carries its own point, like the socket offsets.
        [Tooltip("Stand point used when the character is lying face up (on back).")]
        [ShowIf("useCharacterPosePickup", true)]
        public CarryStandPoint standPointFaceUp = new CarryStandPoint();

        [Tooltip("Stand point used when the character is lying face down.")]
        [ShowIf("useCharacterPosePickup", true)]
        public CarryStandPoint standPointFaceDown = new CarryStandPoint();


        [Header("Dump")]
        [Space(5)]
        [Tooltip("When true, this object can be dumped into a ConcealmentSpot.")]
        public bool canBeDumped;

        [Tooltip("Animation played on the carried object itself when it is dumped into a concealment spot. " +
                 "Requires an ActionExecutor on the carried object. Plays concurrently with the player's dump animation.")]
        [ShowIf("canBeDumped", true)]
        public AnimGraphClipInfo objectDumpClip;

        [Tooltip("Animation played on the player (carrier) when dumping this object into a concealment spot.")]
        [ShowIf("canBeDumped", true)]
        public AnimGraphClipInfo playerDumpClip;


        [Header("Release Timing")]

        [Tooltip("Normalised time within dropClip at which the object detaches from the carrier and is set down.")]
        [Range(0f, 1f)]
        public float dropStartNormalizedTime;

        [Tooltip("Normalised time within throwClip at which the object detaches from the carrier and throw force is applied.")]
        [ShowIf("isThrowable", true)]
        [Range(0f, 1f)]
        public float throwStartNormalizedTime;


        [Header("Physics")]

        [Tooltip("Controls the horizontal force applied to the object when throwing. Higher values increase forward throw distance.")]
        [ShowIf("isThrowable", true)]
        public float throwForce = 10f;

        [Tooltip("Adds upward impulse to the object when thrown. Higher values make the object arc higher.")]
        [ShowIf("isThrowable", true)]
        public float throwHeight = 2f;
    }
}
