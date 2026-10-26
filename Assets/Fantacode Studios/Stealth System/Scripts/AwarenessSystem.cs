using UnityEngine;

namespace FS_StealthSystem
{
    public class AwarenessSystem : MonoBehaviour
    {
        [Header("Awareness Settings")]
        [Tooltip("Awareness lost per second while no stimulus is visible.")]
        [SerializeField] float decayRate = 0.15f;           // awareness decrease per second when not seen

        [Header("Detection Settings")]
        [Tooltip("Awareness gained per second while the stimulus is in the primary vision zone (before distance/motion multipliers).")]
        [SerializeField] float primaryDetectionSpeed = 0.80f;
        [Tooltip("Awareness gained per second while the stimulus is only in the peripheral vision zone.")]
        [SerializeField] float peripheralDetectionSpeed = 0.2f;
        [Tooltip("Distance in meters within which detection is near-instant (10x rate).")]
        [SerializeField] float instantDetectionDistance = 3f;
        [Tooltip("Distance in meters at which the close-range detection boost has fully faded back to 1x.")]
        [SerializeField] float distanceMultiplyThreshold = 6f;
        [Tooltip("Detection rate multiplier just outside instant range, fading to 1x at the threshold distance.")]
        [SerializeField] float distanceMultiplier = 2f;

        [Tooltip("Observed stimulus speed (m/s) below which motion doesn't speed up detection. Keep above walk speed.")]
        [SerializeField] float motionRampStartSpeed = 2.5f;
        [Tooltip("Observed stimulus speed (m/s) at which the motion multiplier reaches its max. Around sprint speed.")]
        [SerializeField] float motionRampEndSpeed = 6f;
        [Tooltip("Detection rate multiplier at motionRampEndSpeed. Must exceed sprint/walk speed ratio so sprinting through vision isn't safer than walking.")]
        [SerializeField] float maxMotionMultiplier = 4f;

        [Tooltip("Flat awareness added per sound heard from a source the AI can already see (e.g. sprint footsteps).")]
        [SerializeField] float soundAwarenessBump = 0.3f;

        public bool PauseDecay { get; set; }

        public float CurrentAwareness { get; private set; }

        private void Start()
        {
            AwarenessUIManager.i.RegisterCharacter(this);
        }

        /// <summary>
        /// Per-tick vision update — the rate depends on the vision zone, the distance to the
        /// stimulus and how well lit it is. Applies fixedDeltaTime (call from FixedUpdate flow).
        ///
        /// Light visibility crossfades between seeing and not seeing instead of only scaling the
        /// gain: a stimulus that reads as half lit is only made out half of the time, so half the
        /// tick builds awareness and the other half decays it. Scaling the gain alone left the
        /// meter frozen at whatever it had reached the moment a stimulus stepped out of a light,
        /// and it only began falling once the stimulus left the vision volume altogether.
        /// Fully lit (and any project without light vision) still gains at the unscaled rate.
        /// </summary>
        public void UpdateAwarenessFromVision(VisionInfo visionInfo, float multiplier = 1f)
        {
            float baseRate = (visionInfo.Zone == VisionZone.Primary) ? primaryDetectionSpeed : peripheralDetectionSpeed;
            float distanceMult = 1f;

            var stimulusPos = visionInfo.Stimulus.transform.position;
            var distance = Vector3.Distance(transform.position, stimulusPos);
            if (distance <= instantDetectionDistance)
                distanceMult = 10;
            else if (distance < distanceMultiplyThreshold)
            {
                float t = (distance - instantDetectionDistance) / (distanceMultiplyThreshold - instantDetectionDistance);
                distanceMult = Mathf.Lerp(distanceMultiplier, 1, t);
            }

            float motionMult = GetMotionMultiplier(visionInfo.Stimulus, stimulusPos);
            float visibility = LightVision.GetVisibility(visionInfo.Stimulus);

            float gain = baseRate * distanceMult * motionMult * multiplier * visibility;

            // Darkness decays at the same rate as an empty vision volume, so walking out of a
            // light reads the same as walking out of sight
            float loss = PauseDecay ? 0f : decayRate * (1f - visibility);

            CurrentAwareness = Mathf.Clamp01(CurrentAwareness + (gain - loss) * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Flat bump for a sound heard from a source the AI can already see —
        /// the noise confirms suspicion instead of starting an investigation.
        /// </summary>
        public void IncreaseAwarenessFromSound(VisualStimulus visualStimulus)
        {
            IncreaseAwareness(soundAwarenessBump, LightVision.GetVisibility(visualStimulus));
        }

        // Observed-motion tracking — fast-moving stimuli draw the eye and are detected faster
        VisualStimulus lastSeenStimulus;
        Vector3 lastSeenPos;
        float lastSeenTime;

        float GetMotionMultiplier(VisualStimulus stimulus, Vector3 position)
        {
            float mult = 1f;
            float elapsed = Time.time - lastSeenTime;

            // Speed is only measurable if the same stimulus was also seen on the previous tick
            if (stimulus == lastSeenStimulus && elapsed > 0f && elapsed <= Time.fixedDeltaTime * 2f)
            {
                Vector3 delta = position - lastSeenPos;
                delta.y = 0f;
                float speed = delta.magnitude / elapsed;
                mult = Mathf.Lerp(1f, maxMotionMultiplier,
                    Mathf.InverseLerp(motionRampStartSpeed, motionRampEndSpeed, speed));
            }

            lastSeenStimulus = stimulus;
            lastSeenPos = position;
            lastSeenTime = Time.time;

            return mult;
        }

        /// <summary>
        /// Raw amount, no time scaling — entry point for other senses (e.g. audio stimuli).
        /// </summary>
        public void IncreaseAwareness(float amount, float lightVisionMultiplier)
        {
            CurrentAwareness = Mathf.Clamp01(CurrentAwareness + amount * lightVisionMultiplier);
        }

        public void DecayAwareness()
        {
            if (PauseDecay) return;
            CurrentAwareness = Mathf.Clamp01(CurrentAwareness - decayRate * Time.fixedDeltaTime);
        }

        public void SetAwareness(float newAwareness)
        {
            CurrentAwareness = Mathf.Clamp01(newAwareness);
        }

        public bool IsAware => CurrentAwareness == 1;
    }
}
