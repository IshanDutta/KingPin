using System.Collections;
using FS_InteractionSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: a service door that stays shut until a specific carryable has been
    /// stashed into a concealment spot, gating progress on "hide the body first". While shut it
    /// carves the NavMesh via a <see cref="NavMeshObstacle"/> so guards path around it.
    /// The stealth system has no dependency on this script.
    /// </summary>
    public class BodyStashGateDemo : MonoBehaviour
    {
        [Tooltip("The carryable that must be stashed before this door opens.")]
        [SerializeField] Carryable requiredBody;

        [Tooltip("Local-space offset the door travels to when it opens.")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, -8.2f, 0f);
        [Tooltip("Speed the door slides open at, in meters per second.")]
        [SerializeField] float openSpeed = 6f;

        [Header("Banner (only the first gate to open should show it)")]
        [Tooltip("If enabled, this gate shows the unlock banner; only the first gate to open ever shows it.")]
        [SerializeField] bool showBanner = true;
        [Tooltip("Text shown in the banner when the door unlocks.")]
        [SerializeField] string bannerMessage = "Body concealed - the service doors have unlocked";
        [Tooltip("Seconds the banner stays on screen.")]
        [SerializeField] float bannerDuration = 2.5f;
        [Tooltip("Color of the banner text.")]
        [SerializeField] Color bannerColor = new Color(0.4f, 0.9f, 0.5f);
        [Tooltip("Font size of the banner text.")]
        [SerializeField] int fontSize = 50;

        static bool bannerShown;

        Vector3 openPos;
        bool opening;

        void Awake()
        {
            openPos = transform.localPosition + openOffset;
            bannerShown = false;   // statics survive a scene reload
        }

        void OnEnable()
        {
            if (requiredBody != null) requiredBody.OnDumpedIntoSpot += Open;
        }

        void OnDisable()
        {
            if (requiredBody != null) requiredBody.OnDumpedIntoSpot -= Open;
        }

        void Open()
        {
            if (opening) return;
            opening = true;

            if (showBanner && !bannerShown)
            {
                bannerShown = true;
                StartCoroutine(ShowBanner());
            }
        }

        void Update()
        {
            if (!opening) return;

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

        IEnumerator ShowBanner()
        {
            var canvasGo = new GameObject("Stash Gate Overlay (Demo)");
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
            text.fontSize = fontSize;
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
