using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    // ExecuteAlways so lights register outside play mode too — otherwise OnEnable never runs in
    // the editor and visibility reads as pitch dark for any edit-mode tooling or preview.
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    public class StealthLight : MonoBehaviour
    {
        [Tooltip("Ceiling on how visible this light can make a stimulus, before intensity, distance and cone falloff scale it down.")]
        [Range(0f, 1f)]
        [SerializeField] float visibilityWeight = 1f;

        public float VisibilityWeight => visibilityWeight;

        Light cachedLight;

        /// <summary>
        /// The Light this component reads intensity, range and cone angles from.
        /// </summary>
        public Light Light
        {
            get
            {
                if (cachedLight == null)
                    cachedLight = GetComponent<Light>();

                return cachedLight;
            }
        }

        // Lights register themselves rather than being discovered through a physics overlap query.
        // A NonAlloc overlap silently truncates once its buffer fills with level geometry, which
        // drops whichever lights lost the race and makes a lit stimulus read as pitch dark.
        static readonly List<StealthLight> activeLights = new List<StealthLight>();
        public static IReadOnlyList<StealthLight> ActiveLights => activeLights;

        void OnEnable()
        {
            activeLights.Add(this);
        }

        void OnDisable()
        {
            activeLights.Remove(this);
        }
    }
}