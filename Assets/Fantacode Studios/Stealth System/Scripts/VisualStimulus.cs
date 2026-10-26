using System.Collections;
using System.Collections.Generic;
using FS_InteractionSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_StealthSystem
{
    public enum VisualStimulusType
    {
        SuspiciousObject,    // Open door, moved object, broken glass
        AlertObject,         // Dead body, blood
        Player,              // Player
        Threat               // Enemy AI from other factions
    }

    public class VisualStimulus : MonoBehaviour
    {
        [Tooltip("Type of stimulus")]
        [SerializeField] VisualStimulusType stimulusType;

        [Tooltip("How strongly this stimulus contributes to awareness")]
        [Range(0f, 1f)]
        [SerializeField] float importance = 1f;

        [Tooltip("Optional tag added to dialogue bark queries for this stimulus")]
        [SerializeField] string dialogueBarkTag;

        public bool Resolved { get; set; } = false;

        EnterExitInteractionHandler enterExitInteractionHandler;

        /// <summary>Fully hidden inside a ConcealmentHotspot (e.g. a garbage box).</summary>
        public bool IsConcealed => enterExitInteractionHandler != null && enterExitInteractionHandler.IsInsideSpot;

        /// <summary>Entering or inside a ConcealmentHotspot — the hotspot analog of HidingArea.IsInsideAnyArea.</summary>
        public bool IsInsideEnterExitSpot => enterExitInteractionHandler != null && enterExitInteractionHandler.CurrentSpot != null;

        void Awake()
        {
            enterExitInteractionHandler = GetComponent<EnterExitInteractionHandler>();
        }

        public VisualStimulusType Type => stimulusType;
        public float Importance => importance;
        public float TypePriority => (int)stimulusType;
        public string DialogueBarkTag => dialogueBarkTag;

        // Each call returns a new stimulus object. Stimuli are held by situations and controllers, so a shared mutable 
        // instance would leak the live position to everyone holding it (e.g. tracking a player hidden in a HidingArea). 
        // Resolution persists across snapshots via the Resolved flag on this component.
        public Stimulus GetStimulus()
        {
            return new Stimulus()
            {
                modality = StimulusModality.Visual,
                priority = stimulusType switch
                {
                    VisualStimulusType.SuspiciousObject => StimulusPriority.Low,
                    VisualStimulusType.AlertObject => StimulusPriority.High,
                    VisualStimulusType.Player => StimulusPriority.Critical,
                    VisualStimulusType.Threat => StimulusPriority.Critical,
                    _ => StimulusPriority.Low
                },
                importance = Importance,
                source = gameObject,
                position = transform.position,
                resolved = Resolved
            };
        }

        [Tooltip("Raises the point AI line-of-sight raycasts aim at. Set for objects whose pivot sits at ground level (e.g. dead bodies) so the cast targets their visible mass instead of grazing the floor.")]
        [SerializeField] float raycastHeightOffset = 0f;
        public float RaycastHeightOffset => raycastHeightOffset;

        [Tooltip("Local-space points on this object that AI vision raycasts target for line-of-sight checks.")]
        [SerializeField] List<Vector3> visionPoints = new List<Vector3>() { Vector3.zero };
        public List<Vector3> VisionPoints => visionPoints;

        public void FixedUpdate()
        {
            //print(LightVision.GetVisibility(this));
        }

        public void SetStimulusType(VisualStimulusType stimulusType)
        {
            this.stimulusType = stimulusType;
        }
    }


#if UNITY_EDITOR

    [CustomEditor(typeof(VisualStimulus))]
    public class VisionPointsGizmos : Editor
    {

        private void OnEnable()
        {

            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        int selectedPointIndex = -1;
        private void OnSceneGUI(SceneView sceneView)
        {
            VisualStimulus visualStimulus = (VisualStimulus)target;
            Transform t = visualStimulus.transform;

            for (int i = 0; i < visualStimulus.VisionPoints.Count; i++)
            {
                Vector3 worldPos = t.TransformPoint(visualStimulus.VisionPoints[i]);

                Handles.color = Color.red;

                float size = HandleUtility.GetHandleSize(worldPos) * 0.15f;
                if (Handles.Button(
                    worldPos,
                    Quaternion.identity,
                    size,
                    size,
                    Handles.SphereHandleCap))
                {
                    selectedPointIndex = i;
                    GUI.changed = true;
                }

                if (selectedPointIndex == i)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldPos = Handles.PositionHandle(worldPos, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(visualStimulus, "Move Vision Point");
                        visualStimulus.VisionPoints[i] = t.InverseTransformPoint(newWorldPos);
                        EditorUtility.SetDirty(visualStimulus);
                    }
                }
            }
        }
    }

#endif
}
