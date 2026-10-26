using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: always-visible objective line in the top-left corner.
    /// Swaps to the fallback objective once the player has been warned for
    /// trespassing and the situation has died down (same moment the hint banner
    /// shows). The stealth system has no dependency on this script.
    /// </summary>
    public class ObjectiveHUDDemo : MonoBehaviour
    {
        [Tooltip("Objective line shown from scene start.")]
        [SerializeField] string initialObjective = "Enter the house without alerting the guards";
        [Tooltip("Objective shown once the player has been warned for trespassing and the situation has died down.")]
        [SerializeField] string fallbackObjective = "Find another way into the compound";
        [Tooltip("Font size of the objective text.")]
        [SerializeField] int fontSize = 26;
        [Tooltip("Color of the objective text.")]
        [SerializeField] Color textColor = Color.black;
        [Tooltip("Font used for the objective text. Leave empty for the TMP default.")]
        [SerializeField] TMP_FontAsset font;

        TMP_Text objectiveText;
        bool wasWarned;
        bool swapped;

        void Start()
        {
            CreateHUD();
            objectiveText.text = "Objective: " + initialObjective;

            if (StealthAIManager.Instance != null)
                StealthAIManager.Instance.OnHighestSituationChanged += OnSituationChanged;
        }

        void OnDestroy()
        {
            if (StealthAIManager.Instance != null)
                StealthAIManager.Instance.OnHighestSituationChanged -= OnSituationChanged;
        }

        void OnSituationChanged(SituationType type)
        {
            if (swapped) return;

            if (type == SituationType.TrespassingRestrictedArea)
                wasWarned = true;
            else if (wasWarned && type == SituationType.NoSituation)
            {
                swapped = true;
                objectiveText.text = "Objective: " + fallbackObjective;
            }
        }

        void CreateHUD()
        {
            var canvasGo = new GameObject("Objective HUD (Demo)");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var bgGo = new GameObject("Container");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(1f, 1f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.pivot = new Vector2(1f, 1f);
            bgRect.anchoredPosition = new Vector2(-20, -20);
            bgRect.sizeDelta = new Vector2(760, 52);

            var textGo = new GameObject("Objective");
            textGo.transform.SetParent(bgGo.transform, false);
            objectiveText = textGo.AddComponent<TextMeshProUGUI>();
            objectiveText.fontSize = fontSize;
            objectiveText.color = textColor;
            objectiveText.fontStyle = FontStyles.Bold;
            if (font != null) objectiveText.font = font;
            objectiveText.alignment = TextAlignmentOptions.MidlineRight;
            var textRect = objectiveText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16, 0);
            textRect.offsetMax = new Vector2(-16, 0);
        }
    }
}
