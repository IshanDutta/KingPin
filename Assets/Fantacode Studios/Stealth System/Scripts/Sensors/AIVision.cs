using FS_Core;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FS_Util;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS_StealthSystem
{

    public class AIVision : MonoBehaviour
    {
        [Header("Primary Vision Settings")]
        [Tooltip("Width in meters of the primary vision volume at the AI's position.")]
        [SerializeField] float primaryStartWidth = 3f;
        [Tooltip("Width in meters of the primary vision volume at half its range.")]
        [SerializeField] float primaryMidWidth = 6f;
        [Tooltip("Width in meters of the primary vision volume at its far end.")]
        [SerializeField] float primaryEndWidth = 3f;
        
        [Tooltip("How far ahead in meters the primary vision volume reaches.")]
        [SerializeField] float primaryRange = 10f;
        [Tooltip("Height in meters of the primary vision volume above the character's feet.")]
        [SerializeField] float primaryHeight = 2f;
        [Tooltip("Extends the volume below the character's feet, so an AI posted at a height can see targets below it.")]
        [SerializeField] float primaryHeightBelow = 0f;

        [Header("Peripheral Vision Settings")]
        [Tooltip("Width in meters of the peripheral vision volume at the AI's position.")]
        [SerializeField] float peripheralStartWidth = 5f;
        [Tooltip("Width in meters of the peripheral vision volume at half its range.")]
        [SerializeField] float peripheralMidWidth = 10f;
        [Tooltip("Width in meters of the peripheral vision volume at its far end.")]
        [SerializeField] float peripheralEndWidth = 5f;

        [Tooltip("How far ahead in meters the peripheral vision volume reaches; keep it larger than the primary range so it wraps the primary volume.")]
        [SerializeField] float peripheralRange = 20f;
        [Tooltip("Height in meters of the peripheral vision volume above the character's feet.")]
        [SerializeField] float peripheralHeight = 3f;
        [Tooltip("Extends the volume below the character's feet, so an AI posted at a height can see targets below it.")]
        [SerializeField] float peripheralHeightBelow = 0f;

        [Tooltip("Height in meters above the feet that line-of-sight checks are cast from.")]
        [SerializeField] float eyeHeight = 1.8f;

        [Header("Gizmos")]
        [Tooltip("Draw the vision volumes in the scene view. Independent of the scene view Gizmos toggle.")]
        [SerializeField] bool debugDrawVisionVolume = false;

        [Tooltip("Fill color of the primary vision gizmo; the alpha controls how translucent the faces are.")]
        [SerializeField, ShowIf("debugDrawVisionVolume", true)] Color primaryFill = new Color(1f, 0.6f, 0.2f, 0.12f);
        [Tooltip("Outline color of the primary vision gizmo wireframe.")]
        [SerializeField, ShowIf("debugDrawVisionVolume", true)] Color primaryOutline = new Color(1f, 0.6f, 0.2f, 1f);
        [Tooltip("Fill color of the peripheral vision gizmo; the alpha controls how translucent the faces are.")]
        [SerializeField, ShowIf("debugDrawVisionVolume", true)] Color peripheralFill = new Color(0.2f, 0.6f, 1f, 0.08f);
        [Tooltip("Outline color of the peripheral vision gizmo wireframe.")]
        [SerializeField, ShowIf("debugDrawVisionVolume", true)] Color peripheralOutline = new Color(0.2f, 0.6f, 1f, 1f);

        public float EyeHeight => eyeHeight;

        StealthAIController stealthAIController;
        VisionVolume primaryVolume;
        VisionVolume pheripheralVolume;

        private void Start()
        {
            stealthAIController = GetComponent<StealthAIController>();

            primaryVolume = GenerateVisionSensor(primaryStartWidth, primaryMidWidth, primaryEndWidth, primaryRange, primaryHeight,
                primaryHeightBelow, primaryFill, primaryOutline, "Primary Vision Sensor");

            pheripheralVolume = GenerateVisionSensor(peripheralStartWidth, peripheralMidWidth, peripheralEndWidth, peripheralRange, peripheralHeight, 
                peripheralHeightBelow, peripheralFill, peripheralOutline, "Pheripheral Vision Sensor");
        }

        VisionVolume GenerateVisionSensor(float startWidth, float midWidth, float endWidth, float range, float height,
            float heightBelow, Color fillColor, Color outlineColor, string name="Vision Sensor")
        {
            var visionVolume = Instantiate(StealthSettings.i.VisionVolumePrefab, transform);
            visionVolume.transform.localPosition = Vector3.zero;

            var visionMesh = VisionMeshCreator.Create(startWidth, midWidth, endWidth, range, height, heightBelow, name);

            var meshFilter = visionVolume.gameObject.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = visionMesh;

            var collider = visionVolume.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = visionMesh;
            collider.convex = true;
            collider.isTrigger = true;

            visionVolume.SetShape(startWidth, midWidth, endWidth, range, height, heightBelow);
            visionVolume.GizmoFillColor = fillColor;
            visionVolume.GizmoOutlineColor = outlineColor;
            visionVolume.DrawGizmos = debugDrawVisionVolume;

            return visionVolume;
        }

        private void FixedUpdate()
        {
            var visionInfo = CheckVision();

            stealthAIController.HandleVision(visionInfo);
        }


        public VisionInfo CheckVision(bool requireLOS = true)
        {
            Vector3 eyePosition = transform.position + Vector3.up * eyeHeight;

            // Primary Vision has priority
            foreach (var stimulus in primaryVolume.Stimuli)
            {
                if (stimulus == null || stimulus.Resolved) continue;

                if (!requireLOS || AIUtil.HasLineOfSight(stimulus.transform, eyePosition, heightOffset: stimulus.RaycastHeightOffset))
                {
                    return new VisionInfo()
                    {
                        Zone = VisionZone.Primary,
                        Stimulus = stimulus,
                        IsHidden = stimulus.IsConcealed || HidingArea.IsHidden(stimulus)
                    };
                }
            }

            // Peripheral Vision
            foreach (var stimulus in pheripheralVolume.Stimuli)
            {
                if (stimulus == null || stimulus.Resolved) continue;

                if (!requireLOS || AIUtil.HasLineOfSight(stimulus.transform, eyePosition, heightOffset: stimulus.RaycastHeightOffset))
                {
                    return new VisionInfo()
                    {
                        Zone = VisionZone.Peripheral,
                        Stimulus = stimulus,
                        IsHidden = stimulus.IsConcealed || HidingArea.IsHidden(stimulus)
                    };
                }
            }

            return null;
        }


#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep the generated volumes in sync when the gizmo settings are edited while playing.
            SyncGizmoSettings(primaryVolume, primaryFill, primaryOutline);
            SyncGizmoSettings(pheripheralVolume, peripheralFill, peripheralOutline);
        }

        void SyncGizmoSettings(VisionVolume volume, Color fillColor, Color outlineColor)
        {
            if (volume == null) return;

            volume.DrawGizmos = debugDrawVisionVolume;
            volume.GizmoFillColor = fillColor;
            volume.GizmoOutlineColor = outlineColor;
        }

        /// <summary>
        /// Edit time preview of the volumes that get generated on Start. Called by VisionVolumeGizmoDrawer,
        /// not by OnDrawGizmos, so it survives the scene view Gizmos toggle being off.
        /// </summary>
        public void DrawVisionVolumePreview()
        {
            if (!debugDrawVisionVolume) return;

            // Peripheral first: it wraps the primary volume, so the primary reads on top of it.
            VisionGizmos.DrawVolume(transform, peripheralStartWidth, peripheralMidWidth, peripheralEndWidth,
                peripheralRange, peripheralHeight, peripheralHeightBelow, peripheralFill, peripheralOutline);

            VisionGizmos.DrawVolume(transform, primaryStartWidth, primaryMidWidth, primaryEndWidth,
                primaryRange, primaryHeight, primaryHeightBelow, primaryFill, primaryOutline);
        }
#endif
    }

}
