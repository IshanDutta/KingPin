using System.Collections;
using FS_InteractionSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: a door that opens once the player has stayed concealed inside a specific
    /// hiding spot long enough for the patrol to walk past. Gates progress on "get in the bin and
    /// wait". While shut it carves the NavMesh via a <see cref="NavMeshObstacle"/> so guards path
    /// around it. The stealth system has no dependency on this script.
    /// </summary>
    public class HideGateDemo : MonoBehaviour
    {
        [Tooltip("The hiding spot the player has to occupy.")]
        [SerializeField] EnterExitHotspot requiredSpot;

        [Tooltip("Seconds the player must stay concealed. Timed so a patrol lap passes the bin.")]
        [SerializeField] float requiredHideSeconds = 3f;

        [Tooltip("Local-space offset the door travels to when it opens.")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, -8.2f, 0f);
        [Tooltip("Speed the door slides open at, in meters per second.")]
        [SerializeField] float openSpeed = 6f;

        [Header("Banner")]
        [Tooltip("Text shown in the banner when the door opens.")]
        [SerializeField] string bannerMessage = "Guard walked past - shutter open";
        [Tooltip("Seconds the banner stays on screen.")]
        [SerializeField] float bannerDuration = 2.5f;
        [Tooltip("Color of the banner text.")]
        [SerializeField] Color bannerColor = new Color(0.5f, 0.85f, 1f);

        Vector3 openPos;
        float hiddenFor;
        bool opening;

        /// <summary>How far through the hide the player is, 0-1. Handy for a progress readout.</summary>
        public float Progress => requiredHideSeconds <= 0f ? 1f : Mathf.Clamp01(hiddenFor / requiredHideSeconds);

        void Awake() => openPos = transform.localPosition + openOffset;

        void Update()
        {
            if (!opening)
            {
                if (requiredSpot == null) return;

                // Only counts while actually inside the spot; stepping out pauses rather than resets,
                // so a nervous player is not punished for bailing once.
                if (requiredSpot.IsOccupied)
                {
                    hiddenFor += Time.deltaTime;
                    if (hiddenFor >= requiredHideSeconds) Open();
                }
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

        void Open()
        {
            opening = true;
            StartCoroutine(ShowBanner());
        }

        IEnumerator ShowBanner()
        {
            var canvasGo = new GameObject("Hide Gate Overlay (Demo)");
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
