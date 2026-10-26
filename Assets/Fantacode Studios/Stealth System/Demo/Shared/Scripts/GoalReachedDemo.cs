using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: shows a success message when the player enters the goal
    /// trigger, then pauses the game after a short delay. Optionally also watches the
    /// guards — when the situation escalates to detection it shows a failure message
    /// and restarts the scene. The stealth system has no dependency on this script.
    /// </summary>
    public class GoalReachedDemo : MonoBehaviour
    {
        [Tooltip("Message shown when the player reaches the goal.")]
        [SerializeField] string message = "You reached the goal!";
        [Tooltip("Seconds the success message is shown before the game pauses (or restarts).")]
        [SerializeField] float pauseDelay = 3f;
        [Tooltip("If enabled, the scene reloads after the delay instead of pausing the game.")]
        [SerializeField] bool restartOnComplete;
        [Tooltip("Font size used for all overlay messages.")]
        [SerializeField] int fontSize = 72;
        [Tooltip("Color of the success message text.")]
        [SerializeField] Color textColor = new Color(0.3f, 0.85f, 0.35f);
        [Tooltip("Color of the fullscreen overlay behind the messages.")]
        [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Font used for overlay messages. Leave empty for the TMP default.")]
        [SerializeField] TMP_FontAsset font;

        [Tooltip("If enabled, the goal only succeeds while no guard situation of Trespassing or higher is active; otherwise a warning is shown instead.")]
        [SerializeField] bool requireUndetected;
        [Tooltip("Message shown when the player reaches the goal while a guard situation is active.")]
        [SerializeField] string warningMessage = "You must reach here without being detected!";
        [Tooltip("Seconds the warning message stays on screen.")]
        [SerializeField] float warningDuration = 2.5f;
        [Tooltip("Color of the warning message text.")]
        [SerializeField] Color warningColor = new Color(1f, 0.65f, 0.15f);

        [Header("Detection")]
        [Tooltip("If enabled, the scene restarts when the guards detect the player. Leave off in scenes that already have a DetectionRestartDemo, or both would fire.")]
        [SerializeField] bool restartOnDetection;
        [Tooltip("Situation type that counts as 'detected'. Any situation at this level or above triggers the restart.")]
        [SerializeField] SituationType detectionSituation = SituationType.Combat;
        [Tooltip("Message shown when the guards detect the player.")]
        [SerializeField] string detectionMessage = "You have been detected";
        [Tooltip("Seconds the detection message is shown before the scene reloads.")]
        [SerializeField] float detectionRestartDelay = 3f;
        [Tooltip("Color of the detection message text.")]
        [SerializeField] Color detectionColor = new Color(0.9f, 0.2f, 0.15f);

        bool triggered;
        GameObject activeWarning;

        void Start()
        {
            // safety: a previous run may have paused the game before a reload
            Time.timeScale = 1f;

            if (!restartOnDetection) return;

            if (StealthAIManager.Instance == null)
            {
                Debug.LogWarning("GoalReachedDemo: no StealthAIManager in scene, restart on detection disabled.", this);
                restartOnDetection = false;
                return;
            }

            StealthAIManager.Instance.OnHighestSituationChanged += OnSituationChanged;
        }

        void OnDestroy()
        {
            if (StealthAIManager.Instance != null)
                StealthAIManager.Instance.OnHighestSituationChanged -= OnSituationChanged;
        }

        void OnTriggerEnter(Collider other) => HandlePlayer(other);
        void OnTriggerStay(Collider other) => HandlePlayer(other);

        void HandlePlayer(Collider other)
        {
            if (triggered || !other.CompareTag("Player")) return;

            if (requireUndetected && StealthAIManager.Instance != null
                && StealthAIManager.Instance.HighestSituationType >= SituationType.TrespassingRestrictedArea)
            {
                if (activeWarning == null)
                {
                    activeWarning = CreateOverlay(warningMessage, warningColor, "Goal Warning (Demo)");
                    Destroy(activeWarning, warningDuration);
                }
                return;
            }

            triggered = true;
            if (activeWarning != null) Destroy(activeWarning);
            StartCoroutine(ShowMessageAndPause());
        }

        // ── Detection ────────────────────────────────────────────────────

        void OnSituationChanged(SituationType type)
        {
            if (triggered || !restartOnDetection || type < detectionSituation) return;

            triggered = true;
            if (activeWarning != null) Destroy(activeWarning);
            StartCoroutine(ShowMessageAndRestart());
        }

        IEnumerator ShowMessageAndRestart()
        {
            CreateOverlay(detectionMessage, detectionColor, "Detection Overlay (Demo)");
            yield return new WaitForSeconds(detectionRestartDelay);

            RestartScene();
        }

        // ── Goal reached ─────────────────────────────────────────────────

        IEnumerator ShowMessageAndPause()
        {
            CreateOverlay(message, textColor, "Goal Overlay (Demo)");
            yield return new WaitForSeconds(pauseDelay);

            if (restartOnComplete)
                RestartScene();
            else
                Time.timeScale = 0f;
        }

        void RestartScene()
        {
            Time.timeScale = 1f;

            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // Works in play mode even when the demo scene is not in Build Settings
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.name);
#endif
        }

        GameObject CreateOverlay(string overlayText, Color overlayColor, string overlayName)
        {
            var canvasGo = new GameObject(overlayName);
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
            text.text = overlayText;
            text.fontSize = fontSize;
            text.color = overlayColor;
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
