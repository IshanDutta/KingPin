using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public class HostileArea : MonoBehaviour, ISuspicionFactor
    {
        [Tooltip("If true, the entire scene is treated as hostile and the player is registered on enable without needing a trigger collider.")]
        [SerializeField] bool coversEntireScene;

        static readonly HashSet<HostileArea> activeAreas = new();
        static bool anyCoversEntireScene;

        bool playerInside;
        SuspicionProfile profile;
        new Collider collider;

        public bool IsActive => playerInside;
        public bool IsInstant => true;
        public bool WarnsFirst => false;

        void Awake()
        {
            collider = GetComponent<Collider>();
        }

        void OnEnable()
        {
            activeAreas.Add(this);
            if (coversEntireScene)
            {
                anyCoversEntireScene = true;

                // No trigger overlap will happen — find and register with the player directly
                var p = FindAnyObjectByType<SuspicionProfile>();
                if (p != null)
                {
                    profile = p;
                    playerInside = true;
                    profile.AddFactor(this);
                }
            }
        }

        void OnDisable()
        {
            activeAreas.Remove(this);
            if (coversEntireScene)
            {
                anyCoversEntireScene = false;

                if (profile != null)
                {
                    profile.RemoveFactor(this);
                    playerInside = false;
                    profile = null;
                }
            }
        }

        public static bool ContainsPosition(Vector3 pos)
        {
            if (anyCoversEntireScene)
                return true;

            foreach (var area in activeAreas)
            {
                if (area.collider != null && area.collider.ClosestPoint(pos) == pos)
                    return true;
            }
            return false;
        }

        void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponent<SuspicionProfile>();
            if (p == null) return;

            profile = p;
            playerInside = true;
            profile.AddFactor(this);
        }

        void OnTriggerExit(Collider other)
        {
            var p = other.GetComponent<SuspicionProfile>();
            if (p == null || p != profile) return;

            playerInside = false;
            profile.RemoveFactor(this);
            profile = null;
        }
    }
}
