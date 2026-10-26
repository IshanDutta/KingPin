using UnityEditor;
using UnityEngine;

namespace FS_InteractionSystem
{
    /// <summary>
    /// Play-mode tuning tool for a carried object's carry-socket offset. While the object is being
    /// carried, select it and drag the Move / Rotate handles in the Scene view to position it in the
    /// carrier's socket. For character-pose carries the handle edits the offset of the pose you are
    /// currently carrying (Face Up / Face Down), so you tune each pose independently. Edits write to
    /// the shared <c>CarryableData</c> asset — which persists through play mode — and preview live.
    /// </summary>
    [CustomEditor(typeof(Carryable))]
    public class CarryableEditor : Editor
    {
        // Draw through duringSceneGui (like the concealment spot) so the Move/Rotate handles and the
        // label stay visible even when the Scene view's Gizmos toggle is OFF. Registered only while
        // the carryable is selected (editor OnEnable/OnDisable).
        void OnEnable()  { SceneView.duringSceneGui += DuringSceneGui; }
        void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            Tools.hidden = false;   // always restore Unity's transform tool on deselect
        }

        void DuringSceneGui(SceneView sceneView)
        {
            var carryable = target as Carryable;
            if (carryable == null) return;

            // Tuning is a play-mode task: it needs the live socket + resolved pose.
            if (!Application.isPlaying || !carryable.IsCarrying || !carryable.AppliesAnyOffset)
            {
                Tools.hidden = false;
                return;
            }

            var socket = carryable.transform.parent;   // the carrier's socket while carried
            if (socket == null || carryable.data == null)
            {
                Tools.hidden = false;
                return;
            }

            // Hide Unity's default transform gizmo so it doesn't sit on top of the carry-offset handle
            // (both are at the object's location, making the offset handle impossible to grab).
            Tools.hidden = true;

            sceneView.Repaint();                        // track the socket as the carrier moves

            // Read/write the offset for the pose currently being carried (routed by the accessors).
            Vector3 worldPos = socket.TransformPoint(carryable.carrySocketPosOffset);
            Quaternion worldRot = socket.rotation * Quaternion.Euler(carryable.carrySocketRotOffset);

            EditorGUI.BeginChangeCheck();
            worldPos = Handles.PositionHandle(worldPos, worldRot);
            worldRot = Handles.RotationHandle(worldRot, worldPos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(carryable.data, "Tune Carry Socket Offset");
                carryable.SetCarrySocketOffset(
                    socket.InverseTransformPoint(worldPos),
                    (Quaternion.Inverse(socket.rotation) * worldRot).eulerAngles);
                EditorUtility.SetDirty(carryable.data);

                // Only snap the body to the offset WHILE dragging the handle (live tuning preview).
                // Doing it every frame would hard-pin the body and override the runtime placement /
                // smoothing whenever the object is simply selected — which reads as a snap.
                carryable.ApplyCarryPlacement();
            }

            Handles.color = Color.cyan;
            // With Apply Rotation Offset off, the rotation handle still writes the offset but nothing
            // consumes it, so the drag has no visible effect on the carried object. Say so, or it
            // reads as broken.
            Handles.Label(worldPos + Vector3.up * 0.15f,
                $"Carry Offset — {carryable.CurrentPoseName}" +
                (carryable.applyRotationOffset ? "" : "   (rotation: not applied)"));
        }

        public override void OnInspectorGUI()
        {
            var carryable = (Carryable)target;

            if (Application.isPlaying && carryable.IsCarrying && carryable.AppliesAnyOffset)
                EditorGUILayout.HelpBox(
                    $"Carrying ({carryable.CurrentPoseName}) — drag the Move / Rotate handles in the " +
                    "Scene view to set this pose's carry socket offset. Face-up and face-down are tuned " +
                    "separately. Writes to the CarryableData asset (persists after play)." +
                    (carryable.applyRotationOffset
                        ? ""
                        : "\n\nApply Rotation Offset is off: the rotation offset is not applied to the " +
                          "object, so rotating the handle has no visible effect."), MessageType.Info);
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode, pick this object up, then select it to drag the carry socket " +
                    "offset in the Scene view. Character carries tune each pose (Face Up / Face Down) " +
                    "separately. Or edit CarryableData ▸ Carry Socket …Offset fields directly.",
                    MessageType.None);

                if (carryable.data != null)
                {
                    var d = carryable.data;
                    string status = d.useCharacterPosePickup
                        ? $"Stand points: Face Up {(d.standPointFaceUp.enabled ? "✓" : "—")}   " +
                          $"Face Down {(d.standPointFaceDown.enabled ? "✓" : "—")}"
                        : $"Stand point: {(d.standPoint.enabled ? "✓" : "—")}";
                    EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        "Position stand points by selecting the CarryHotspot and dragging the magenta handle.",
                        EditorStyles.miniLabel);
                }
            }

            DrawDefaultInspector();
        }
    }
}
