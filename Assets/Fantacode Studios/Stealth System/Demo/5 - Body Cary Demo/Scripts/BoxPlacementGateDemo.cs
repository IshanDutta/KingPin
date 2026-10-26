using System.Collections;
using FS_InteractionSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: a door that opens once a specific carryable has been set down inside a
    /// marked pad. Gates progress on "carry the box to the pad and drop it". While shut it carves
    /// the NavMesh via a <see cref="NavMeshObstacle"/> so guards path around it.
    /// The stealth system has no dependency on this script.
    /// </summary>
    public class BoxPlacementGateDemo : MonoBehaviour
    {
        [Tooltip("The carryable that has to be placed on the pad.")]
        [SerializeField] Carryable requiredBox;

        [Tooltip("Trigger volume marking where the box must end up.")]
        [SerializeField] Collider placementZone;

        [Tooltip("Local-space offset the door travels to when it opens.")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, -8.2f, 0f);
        [Tooltip("Speed the door slides open at, in meters per second.")]
        [SerializeField] float openSpeed = 6f;

        [Header("Pad feedback")]
        [Tooltip("Renderer whose emission color shows whether the pad is still waiting for the box.")]
        [SerializeField] Renderer padRenderer;
        [Tooltip("Emission color of the pad while it is waiting for the box.")]
        [SerializeField] Color emptyColor = new Color(1.5f, 1.1f, 0.2f);
        [Tooltip("Emission color of the pad once the box is in place.")]
        [SerializeField] Color filledColor = new Color(0.25f, 1.5f, 0.45f);

        [Header("Banner")]
        [Tooltip("Text shown in the banner when the door opens.")]
        [SerializeField] string bannerMessage = "Box in place - the shutter has opened";
        [Tooltip("Seconds the banner stays on screen.")]
        [SerializeField] float bannerDuration = 2.5f;
        [Tooltip("Color of the banner text.")]
        [SerializeField] Color bannerColor = new Color(0.4f, 0.9f, 0.5f);

        Vector3 openPos;
        bool opening;
        Material padMat;

        void Awake()
        {
            openPos = transform.localPosition + openOffset;
            if (padRenderer != null)
                padMat = Application.isPlaying ? padRenderer.material : padRenderer.sharedMaterial;
            SetPad(false);
        }

        void Update()
        {
            if (!opening)
            {
                if (IsBoxPlaced()) Open();
                return;
            }

            transform.localPosition = Vector3.MoveTowards(transform.localPosition, openPos, openSpeed * Time.deltaTime);

            if ((transform.localPosition - openPos).sqrMagnitude < 0.0001f)
            {
                var col = GetComponent<Collider>();
                if (col != null) col.enabled = false;

                var obstacle = GetComponent<NavMeshObstacle>();
                if (obstacle != null) obstacle.enabled = false;   // stop carving so the doorway becomes walkable

                enabled = false;
            }
        }

        /// <summary>The box counts as placed once it is inside the pad and no longer in the player's hands.</summary>
        bool IsBoxPlaced()
        {
            if (requiredBox == null || placementZone == null) return false;
            if (requiredBox.IsCarrying) return false;
            return placementZone.bounds.Contains(requiredBox.transform.position);
        }

        void Open()
        {
            opening = true;
            SetPad(true);
            StartCoroutine(ShowBanner());
        }

        void SetPad(bool filled)
        {
            if (padMat == null) return;
            padMat.EnableKeyword("_EMISSION");
            padMat.SetColor("_EmissionColor", filled ? filledColor : emptyColor);
        }

        IEnumerator ShowBanner()
        {
            var canvasGo = new GameObject("Box Gate Overlay (Demo)");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var textGo = new GameObject("Message");
            textGo.transform.SetParent(canvasGo.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = bannerMessage;
            text.fontSize = 50;
            text.color = bannerColor;
            text.alignment = TextAlignmentOptions.Center;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.12f);
            rect.anchorMax = new Vector2(1f, 0.28f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            yield return new WaitForSeconds(bannerDuration);
            if (canvasGo != null) Destroy(canvasGo);
        }
    }
}
