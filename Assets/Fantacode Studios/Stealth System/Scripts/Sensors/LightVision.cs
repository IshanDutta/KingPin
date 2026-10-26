using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_StealthSystem
{

    public class LightVision : MonoBehaviour
    {
        [Tooltip("Enable light-based concealment; when off, every stimulus reads as fully visible regardless of scene lighting.")]
        [SerializeField] bool useLightVision = false;
        [Tooltip("Draw a ray per light sample in the Scene view.")]
        [ShowIf("useLightVision", true)]
        [SerializeField] bool debugDrawSampleRays = false;

        [ShowIf("useLightVision", true)]
        [Tooltip("Shapes how quickly point lights fade with distance. 0 stays bright all the way to the light's range, higher values approach real inverse-square falloff. Spot lights ignore this — their beam dims by angle only.")]
        [Range(0f, 32f)]
        [SerializeField] float falloffSharpness = 0;

        [Tooltip("Directional intensity that counts as fully lit. Unity measures directional lights in lux — a default sun sits at 1.")]
        [ShowIf("useLightVision", true)]
        [SerializeField] float referenceDirectionalIntensity = 1f;

        [Tooltip("Spot intensity that counts as fully lit. Unity measures spot lights in candela under physical light units, so these sit far above a directional light's lux value.")]
        [ShowIf("useLightVision", true)]
        [SerializeField] float referenceSpotIntensity = 60f;

        [Tooltip("Point intensity that counts as fully lit. Unity measures point lights in candela under physical light units. Point lights are usually authored dimmer than spots, so this normally sits below the spot reference.")]
        [ShowIf("useLightVision", true)]
        [SerializeField] float referencePointIntensity = 40f;

        [Tooltip("Scale each light by how bright its colour reads to the eye, so a deep red lamp conceals better than a white one of equal intensity.")]
        [ShowIf("useLightVision", true)]
        [SerializeField] bool useLightColor = false;


        static LightVision instance;

        void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        /// <summary>
        /// Fully visible when no LightVision exists in the scene — light-based concealment is
        /// an opt-in feature, so its absence must not hide anyone.
        /// </summary>
        public static float GetVisibility(VisualStimulus visualStimulus)
        {
            return instance != null ? instance.ComputeVisibility(visualStimulus) : 1f;
        }

        float ComputeVisibility(VisualStimulus visualStimulus)
        {
            if(!useLightVision) return 1;

            var points = visualStimulus.VisionPoints;
            if (points.Count == 0) return 0f;

            var targetLayer = visualStimulus.gameObject.layer;

            float visibility = 0f;
            foreach (var point in points)
            {
                var worldPos = visualStimulus.transform.TransformPoint(point);
                visibility += SamplePoint(targetLayer, worldPos) / points.Count;
            }

            return Mathf.Clamp01(visibility);
        }

        /// <summary>
        /// Lights combine the way overlapping chances of being spotted do — 1 - Π(1 - contribution)
        /// — so two half-lit sources read as 0.75 instead of a raw sum saturating at 1.
        /// </summary>
        float SamplePoint(int targetLayer, Vector3 pos)
        {
            float unlit = 1f;

            foreach (var stealthLight in StealthLight.ActiveLights)
            {
                if (!stealthLight)
                    continue;

                var light = stealthLight.Light;
                if (light == null || !light.isActiveAndEnabled)
                    continue;

                float contribution = light.type switch
                {
                    LightType.Directional => SampleDirectionalLight(light, stealthLight, pos),
                    LightType.Point => SamplePointLight(light, stealthLight, targetLayer, pos),
                    LightType.Spot => SampleSpotLight(light, stealthLight, targetLayer, pos),

                    // Area lights are baked-only at runtime, so they light nothing to see by
                    _ => 0f
                };

                Accumulate(ref unlit, contribution);
            }

            return 1f - unlit;
        }

        static void Accumulate(ref float unlit, float contribution)
        {
            unlit *= 1f - Mathf.Clamp01(contribution);
        }

        #region Light Sampling

        /// <summary>
        /// Directional light has no position, so distance and cone falloff don't apply — the sun
        /// either reaches this point or something between it and the sky blocks it.
        /// </summary>
        float SampleDirectionalLight(Light light, StealthLight stealthLight, Vector3 pos)
        {
            Vector3 dir = -light.transform.forward;

            if (debugDrawSampleRays)
                Debug.DrawRay(pos, dir * 10f, Color.black);

            // Blocked from the sky = no sun/moon
            if (Physics.Raycast(pos, dir, Mathf.Infinity, FSSettings.i.ObstructionLayer))
                return 0f;

            return LightContribution(light, stealthLight, 1f);
        }

        float SamplePointLight(Light light, StealthLight stealthLight, int targetLayer, Vector3 pos)
        {
            Vector3 toPos = pos - light.transform.position;
            float dist = toPos.magnitude;

            float attenuation = DistanceAttenuation(dist, light.range);
            if (attenuation <= 0f)
                return 0f;

            // Falloff is cheap, the raycast isn't — only pay for it once the point is actually lit
            if (IsOccluded(light.transform.position, toPos, dist, targetLayer))
                return 0f;

            return LightContribution(light, stealthLight, attenuation);
        }

        /// <summary>
        /// Anywhere inside the beam counts as fully lit — distance within the light's range
        /// doesn't dim it. Only the soft edge between the inner and outer angle falls off.
        /// </summary>
        float SampleSpotLight(Light light, StealthLight stealthLight, int targetLayer, Vector3 pos)
        {
            Vector3 toPos = pos - light.transform.position;
            float dist = toPos.magnitude;

            if (dist > light.range)
                return 0f;

            float attenuation = ConeAttenuation(light, toPos, dist);
            if (attenuation <= 0f)
                return 0f;

            if (IsOccluded(light.transform.position, toPos, dist, targetLayer))
                return 0f;

            return LightContribution(light, stealthLight, attenuation);
        }

        #endregion

        #region Light Response

        /// <summary>
        /// The light's authored ceiling, scaled by how bright the light actually is and how much
        /// of it survives distance and cone falloff.
        /// </summary>
        float LightContribution(Light light, StealthLight stealthLight, float attenuation)
        {
            return stealthLight.VisibilityWeight
                 * IntensityFactor(light)
                 * ColorFactor(light)
                 * attenuation;
        }

        /// <summary>
        /// Unity's intensity is unbounded and uses different units per light type, so it's
        /// normalized against a reference brightness rather than used raw.
        /// </summary>
        float IntensityFactor(Light light)
        {
            float reference = light.type switch
            {
                LightType.Directional => referenceDirectionalIntensity,
                LightType.Spot => referenceSpotIntensity,
                LightType.Point => referencePointIntensity,
                _ => 0f
            };

            // A reference of 0 means "don't normalize this type" — the light counts as fully lit
            if (reference <= 0f)
                return 1f;

            return Mathf.Clamp01(light.intensity / reference);
        }

        float ColorFactor(Light light)
        {
            if (!useLightColor)
                return 1f;

            // Rec. 709 luma — how bright the colour reads to the eye, not raw channel strength
            var c = light.color;
            return Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
        }

        /// <summary>
        /// Inverse-square falloff normalized to 0-1 across the light's range. The window term
        /// drives it to exactly 0 at the range boundary so a light never cuts off mid-air.
        /// </summary>
        float DistanceAttenuation(float dist, float range)
        {
            if (range <= 0f || dist >= range)
                return 0f;

            float t = dist / range;

            // Smooth range window, the same shape URP fades a light out with at its boundary
            float window = Mathf.Clamp01(1f - t * t * t * t);
            window *= window;

            return window / (1f + falloffSharpness * t * t);
        }

        /// <summary>
        /// Beam falloff across the spot cone — full strength anywhere inside the inner angle,
        /// easing to nothing at the outer angle, so the beam edge is soft instead of a hard cut.
        /// Full 1 inside the inner cone, a straight ramp down across the penumbra, 0 past the
        /// outer cone. This is the only thing that dims a spot light, so a stimulus standing in
        /// the beam reads as fully lit however far down the beam it is.
        /// </summary>
        float ConeAttenuation(Light light, Vector3 toPos, float dist)
        {
            if (dist <= 0.001f)
                return 1f;

            // Unity stores both angles as full cone widths, so halve them to get the angle
            // measured off the beam axis
            float outerHalf = light.spotAngle * 0.5f;
            float innerHalf = Mathf.Min(light.innerSpotAngle, light.spotAngle) * 0.5f;

            float angle = Vector3.Angle(light.transform.forward, toPos);

            if (angle >= outerHalf)
                return 0f;

            if (angle <= innerHalf)
                return 1f;

            // Straight ramp across the penumbra — how far the point sits between the inner and
            // outer angle is the only thing that matters, measured in degrees rather than cosines
            return (outerHalf - angle) / Mathf.Max(outerHalf - innerHalf, 1e-4f);
        }

        /// <summary>
        /// Casts from the light towards the sampled point. The stimulus' own layer is included in
        /// the cast so hitting the stimulus itself reads as "lit" rather than "blocked".
        /// </summary>
        bool IsOccluded(Vector3 lightPos, Vector3 toPos, float dist, int targetLayer)
        {
            // Light sits on the sampled point — nothing can fit in between
            if (dist <= 0.001f)
                return false;

            Vector3 dir = toPos / dist;

            if (debugDrawSampleRays)
                Debug.DrawRay(lightPos, dir * dist, Color.green);

            LayerMask mask = FSSettings.i.ObstructionLayer | (1 << targetLayer);

            if (!Physics.Raycast(lightPos, dir, out RaycastHit hit, dist, mask))
                return false;

            return hit.transform.gameObject.layer != targetLayer;
        }

        #endregion
    }
}
