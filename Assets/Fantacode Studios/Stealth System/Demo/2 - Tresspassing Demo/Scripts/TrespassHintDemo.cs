using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: after the player has been warned for trespassing and the
    /// situation has died down again, shows a one-shot hint banner suggesting to
    /// look for another way in. The stealth system has no dependency on this script.
    /// </summary>
    public class TrespassHintDemo : MonoBehaviour
    {
        [Tooltip("Hint text shown once the trespass warning has died down.")]
        [SerializeField] string message = "The guards won't let you in. Maybe there's another way around...";
        [Tooltip("Seconds the hint banner stays on screen.")]
        [SerializeField] float duration = 5f;
        [Tooltip("Font size of the hint text.")]
        [SerializeField] int fontSize = 40;
        [Tooltip("Color of the hint text.")]
        [SerializeField] Color textColor = new Color(1f, 0.85f, 0.4f);
        [Tooltip("Color of the banner behind the hint text.")]
        [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("Font used for the hint text. Leave empty for the TMP default.")]
        [SerializeField] TMP_FontAsset font;

        bool wasWarned;
        bool shown;

        void Start()
        {
            if (StealthAIManager.Instance == null)
            {
                Debug.LogWarning("TrespassHintDemo: no StealthAIManager in scene, disabling.");
                enabled = false;
                return;
            }

            StealthAIManager.Instance.OnHighestSituationChanged += OnSituationChanged;
        }

        void OnDestroy()
        {
            if (StealthAIManager.Instance != null)
                StealthAIManager.Instance.OnHighestSituationChanged -= OnSituationChanged;
        }

        void OnSituationChanged(SituationType type)
        {
            if (shown) return;

            if (type == SituationType.TrespassingRestrictedArea)
                wasWarned = true;
            else if (wasWarned && type == SituationType.NoSituation)
            {
                shown = true;
                var banner = CreateBanner();
                Destroy(banner, duration);
            }
        }

        GameObject CreateBanner()
        {
            var canvasGo = new GameObject("Trespass Hint (Demo)");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.color = backgroundColor;
            var bgRect = bg.rectTransform;
            bgRect.anchorMin = new Vector2(0.5f, 1f);
            bgRect.anchorMax = new Vector2(0.5f, 1f);
            bgRect.pivot = new Vector2(0.5f, 1f);
            bgRect.anchoredPosition = new Vector2(0, -60);
            bgRect.sizeDelta = new Vector2(1100, 80);

            var textGo = new GameObject("Message");
            textGo.transform.SetParent(bgGo.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = fontSize;
            text.color = textColor;
            if (font != null) text.font = font;
            text.alignment = TextAlignmentOptions.Center;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return canvasGo;
        }
    }
}
