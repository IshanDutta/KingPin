using FS_StealthSystem;
using FS_Core;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FS_Util;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

namespace FS_StealthSystem
{

    public class VisionVolume : MonoBehaviour
    {
        [Tooltip("Draw this volume in the scene view. Independent of the scene view Gizmos toggle.")]
        [SerializeField] bool drawGizmos = false;
        [Tooltip("Fill color of the volume gizmo; the alpha controls how translucent the faces are.")]
        [SerializeField] Color gizmoFillColor = new Color(1f, 0.6f, 0.2f, 0.12f);
        [Tooltip("Outline color of the volume gizmo wireframe.")]
        [SerializeField] Color gizmoOutlineColor = new Color(1f, 0.6f, 0.2f, 1f);

        public List<VisualStimulus> Stimuli { get; private set; } = new List<VisualStimulus>();

        public bool DrawGizmos { get => drawGizmos; set => drawGizmos = value; }
        public Color GizmoFillColor { get => gizmoFillColor; set => gizmoFillColor = value; }
        public Color GizmoOutlineColor { get => gizmoOutlineColor; set => gizmoOutlineColor = value; }

        // Dimensions of the mesh this volume was generated with, kept so it can draw its own outline.
        float startWidth, midWidth, endWidth, range, height, heightBelow;
        bool hasShape;

        private void Awake()
        {
            gameObject.layer = LayerMask.NameToLayer("VisionSensor");
        }

        /// <summary>Records the dimensions used to build this volume's collision mesh, for gizmo drawing.</summary>
        public void SetShape(float startWidth, float midWidth, float endWidth, float range, float height, float heightBelow)
        {
            this.startWidth = startWidth;
            this.midWidth = midWidth;
            this.endWidth = endWidth;
            this.range = range;
            this.height = height;
            this.heightBelow = heightBelow;
            hasShape = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (LayersUtil.LayerMaskContainsLayer(StealthSettings.i.VisualStimulusLayer, other.gameObject.layer))
            {
                if (other.TryGetComponent<VisualStimulus>(out VisualStimulus stimulus))
                {
                    InsertStimulusByPriority(stimulus);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (LayersUtil.LayerMaskContainsLayer(StealthSettings.i.VisualStimulusLayer, other.gameObject.layer))
            {
                if (other.TryGetComponent<VisualStimulus>(out VisualStimulus stimulus))
                {
                    Stimuli.Remove(stimulus);
                }
            }
        }

        void InsertStimulusByPriority(VisualStimulus stimulus)
        {
            // insert based on priority
            int index = Stimuli.FindIndex(s => s.TypePriority <= stimulus.TypePriority);

            if (index < 0)
                Stimuli.Add(stimulus);
            else
                Stimuli.Insert(index, stimulus);
        }

#if UNITY_EDITOR
        static readonly List<VisionVolume> activeVolumes = new List<VisionVolume>();
        public static IReadOnlyList<VisionVolume> ActiveVolumes => activeVolumes;

        private void OnEnable() => activeVolumes.Add(this);
        private void OnDisable() => activeVolumes.Remove(this);

        public void DrawGizmo()
        {
            if (!drawGizmos || !hasShape) return;

            VisionGizmos.DrawVolume(transform, startWidth, midWidth, endWidth, range, height, heightBelow,
                gizmoFillColor, gizmoOutlineColor);
        }
#endif
    }

#if UNITY_EDITOR

    /// <summary>
    /// Shared drawing for vision volumes: translucent depth tested faces plus a wireframe outline.
    /// Kept separate from the components so both the runtime volumes and the AIVision edit time preview
    /// render the same shape.
    /// </summary>
    public static class VisionGizmos
    {
        // Vertex layout matches VisionMeshCreator: the volume sits on the character's feet and rises to
        // height, so the outline lines up with the collider that actually does the detection.
        //
        //   0..3   start cross section (z = 0)
        //   4..7   middle cross section (z = range / 2)
        //   8..11  end cross section (z = range)
        //
        // Each face is a planar quad wound around its perimeter, split at the middle section because the
        // volume changes width there.
        static readonly int[][] faces =
        {
            new[] { 0, 1, 5, 4 },   new[] { 4, 5, 9, 8 },      // bottom
            new[] { 2, 6, 7, 3 },   new[] { 6, 10, 11, 7 },    // top
            new[] { 0, 4, 6, 2 },   new[] { 4, 8, 10, 6 },     // left
            new[] { 1, 3, 7, 5 },   new[] { 5, 7, 11, 9 },     // right
            new[] { 0, 2, 3, 1 },                              // start cap
            new[] { 8, 9, 11, 10 },                            // end cap
        };

        static readonly Vector3[] quad = new Vector3[4];
        static readonly Vector3[] verts = new Vector3[12];

        public static void DrawVolume(Transform space, float startWidth, float midWidth, float endWidth,
            float range, float height, float heightBelow, Color fillColor, Color outlineColor)
        {
            SetVertices(space, startWidth, midWidth, endWidth, range, height, heightBelow);

            var previousZTest = Handles.zTest;

            // Depth tested, so level geometry occludes the volume and the faces layer up where the shape
            // is deepest. That is what gives it a readable sense of volume rather than a flat outline.
            Handles.zTest = CompareFunction.LessEqual;

            if (fillColor.a > 0f)
            {
                Handles.color = fillColor;
                foreach (var face in faces)
                {
                    for (int i = 0; i < quad.Length; i++)
                        quad[i] = verts[face[i]];

                    Handles.DrawAAConvexPolygon(quad);
                }
            }

            Handles.color = outlineColor;
            DrawWireframe();

            // Faint pass over the occluded parts, so the volume stays traceable through walls.
            Handles.zTest = CompareFunction.Greater;
            Handles.color = new Color(outlineColor.r, outlineColor.g, outlineColor.b, outlineColor.a * 0.15f);
            DrawWireframe();

            Handles.zTest = previousZTest;
        }

        static void SetVertices(Transform space, float startWidth, float midWidth, float endWidth,
            float range, float height, float heightBelow)
        {
            float mid = range * 0.5f;
            float bottom = -heightBelow;

            // Start
            verts[0] = new Vector3(-startWidth * 0.5f, bottom, 0f);
            verts[1] = new Vector3(startWidth * 0.5f, bottom, 0f);
            verts[2] = new Vector3(-startWidth * 0.5f, height, 0f);
            verts[3] = new Vector3(startWidth * 0.5f, height, 0f);

            // Middle
            verts[4] = new Vector3(-midWidth * 0.5f, bottom, mid);
            verts[5] = new Vector3(midWidth * 0.5f, bottom, mid);
            verts[6] = new Vector3(-midWidth * 0.5f, height, mid);
            verts[7] = new Vector3(midWidth * 0.5f, height, mid);

            // End
            verts[8] = new Vector3(-endWidth * 0.5f, bottom, range);
            verts[9] = new Vector3(endWidth * 0.5f, bottom, range);
            verts[10] = new Vector3(-endWidth * 0.5f, height, range);
            verts[11] = new Vector3(endWidth * 0.5f, height, range);

            for (int i = 0; i < verts.Length; i++)
                verts[i] = space.TransformPoint(verts[i]);
        }

        static void DrawWireframe()
        {
            DrawRect(0);  // Start
            DrawRect(8);  // End

            Connect(0, 4);
            Connect(4, 8);
        }

        static void DrawRect(int i)
        {
            Handles.DrawLine(verts[i + 0], verts[i + 1]);
            Handles.DrawLine(verts[i + 1], verts[i + 3]);
            Handles.DrawLine(verts[i + 3], verts[i + 2]);
            Handles.DrawLine(verts[i + 2], verts[i + 0]);
        }

        static void Connect(int a, int b)
        {
            Handles.DrawLine(verts[a + 0], verts[b + 0]);
            Handles.DrawLine(verts[a + 1], verts[b + 1]);
            Handles.DrawLine(verts[a + 2], verts[b + 2]);
            Handles.DrawLine(verts[a + 3], verts[b + 3]);
        }
    }

    /// <summary>
    /// Draws the vision volumes from SceneView.duringSceneGui rather than OnDrawGizmos, so they stay
    /// visible when the scene view Gizmos toggle is off.
    /// </summary>
    [InitializeOnLoad]
    static class VisionVolumeGizmoDrawer
    {
        static readonly List<AIVision> aiVisions = new List<AIVision>();

        static VisionVolumeGizmoDrawer()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;

            EditorApplication.hierarchyChanged -= RefreshAIVisions;
            EditorApplication.hierarchyChanged += RefreshAIVisions;

            // Scenes aren't loaded yet while InitializeOnLoad runs.
            EditorApplication.delayCall += RefreshAIVisions;
        }

        static void RefreshAIVisions()
        {
            aiVisions.Clear();
            aiVisions.AddRange(UnityEngine.Object.FindObjectsByType<AIVision>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        static void OnSceneGui(SceneView sceneView)
        {
            // The generated volumes are what actually detect stimuli, so they take over once they exist.
            foreach (var volume in VisionVolume.ActiveVolumes)
            {
                if (volume != null)
                    volume.DrawGizmo();
            }

            if (Application.isPlaying) return;

            foreach (var aiVision in aiVisions)
            {
                if (aiVision != null)
                    aiVision.DrawVisionVolumePreview();
            }
        }
    }

#endif
}
