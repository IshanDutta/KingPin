using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: when the stealth situation escalates to Combat, shows a
    /// "detected" message and restarts the scene. The stealth system itself has no
    /// dependency on this script.
    /// </summary>
    public class DetectionRestartDemo : MonoBehaviour
    {
        [Tooltip("Message shown on screen when the guards escalate to combat.")]
        [SerializeField] string message = "You have been detected";
        [Tooltip("Seconds the message is shown before the scene reloads.")]
        [SerializeField] float restartDelay = 3f;
        [Tooltip("Font size of the message text.")]
        [SerializeField] int fontSize = 72;
        [Tooltip("Color of the message text.")]
        [SerializeField] Color textColor = new Color(0.9f, 0.2f, 0.15f);
        [Tooltip("Color of the fullscreen overlay behind the message.")]
        [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Font used for the message. Leave empty for the TMP default.")]
        [SerializeField] TMP_FontAsset font;

        bool triggered;

        void Start()
        {
            if (StealthAIManager.Instance == null)
            {
                Debug.LogWarning("DetectionRestartDemo: no StealthAIManager in scene, disabling.");
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
            if (triggered || type != SituationType.Combat) return;

            triggered = true;
            StartCoroutine(ShowMessageAndRestart());
        }

        IEnumerator ShowMessageAndRestart()
        {
            CreateOverlay();
            yield return new WaitForSeconds(restartDelay);

            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // Works in play mode even when the demo scene is not in Build Settings
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.name);
#endif
        }

        void CreateOverlay()
        {
            var canvasGo = new GameObject("Detection Overlay (Demo)");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.color = backgroundColor;
            var bgRect = bg.rectTransform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var textGo = new GameObject("Message");
            textGo.transform.SetParent(canvasGo.transform, false);
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
        }
    }
}
