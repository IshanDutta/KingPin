using System.Collections.Generic;
using System.Text;
using FS_Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: shows what every guard currently knows — awareness level, whether they
    /// have locked on, and the shared situation the stealth system has escalated to.
    ///
    /// Awareness climbs at (vision zone rate) x (distance) x (motion) x (light visibility), so
    /// putting the number on screen next to the light readout makes the whole detection chain
    /// legible. The stealth system has no dependency on this script.
    /// </summary>
    public class StealthDetectionHUDDemo : MonoBehaviour
    {
        [Tooltip("Seconds between readout updates.")]
        [SerializeField] float refreshInterval = 0.1f;
        [Tooltip("Guards farther than this from the player are left off the list, in meters.")]
        [SerializeField] float listRange = 45f;

        [Header("Style")]
        [Tooltip("Readout color for unaware guards and a calm situation.")]
        [SerializeField] Color calmColor = new Color(0.45f, 0.9f, 0.55f);
        [Tooltip("Readout color while a guard is gaining awareness or a suspicion is active.")]
        [SerializeField] Color suspiciousColor = new Color(1f, 0.82f, 0.3f);
        [Tooltip("Readout color once a guard has spotted or engaged the player.")]
        [SerializeField] Color alertColor = new Color(1f, 0.45f, 0.3f);

        TMP_Text readout;
        readonly StringBuilder sb = new StringBuilder();
        readonly List<StealthAIController> guards = new List<StealthAIController>();
        float timer;
        Transform player;

        void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            RefreshGuardList();
            BuildHUD();
        }

        void RefreshGuardList()
        {
            guards.Clear();
            guards.AddRange(FindObjectsByType<StealthAIController>(FindObjectsSortMode.None));
        }

        void Update()
        {
            if (readout == null) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = refreshInterval;

            var situation = StealthAIManager.Instance != null
                ? StealthAIManager.Instance.HighestSituationType
                : SituationType.NoSituation;

            Color sitCol = situation == SituationType.NoSituation ? calmColor
                         : situation >= SituationType.AlertInvestigation ? alertColor : suspiciousColor;

            sb.Clear();
            sb.Append("<b>AI DETECTION</b>   <color=#").Append(ColorUtility.ToHtmlStringRGB(sitCol)).Append('>')
              .Append(situation).Append("</color>");
            sb.Append("\n<size=62%>awareness = vision zone x distance x motion x light</size>");

            int shown = 0;
            foreach (var g in guards)
            {
                if (g == null) continue;
                if (player != null && Vector3.Distance(player.position, g.transform.position) > listRange) continue;

                var aw = g.GetComponent<AwarenessSystem>();
                float a = aw == null ? 0f : aw.CurrentAwareness;
                var ai = g.GetComponent<AIController>();
                bool locked = ai != null && ai.Target != null;

                Color c = locked || a >= 0.999f ? alertColor : a > 0.05f ? suspiciousColor : calmColor;
                string state = locked ? "ENGAGED" : a >= 0.999f ? "SPOTTED" : a > 0.05f ? "NOTICING" : "unaware";
                string own = g.CurrentSituation == null ? "-" : g.CurrentSituation.Type.ToString();

                sb.Append("\n<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>')
                  .Append(Bar(a)).Append("  ").Append(Mathf.RoundToInt(a * 100f).ToString().PadLeft(3)).Append("%  ")
                  .Append(state).Append("</color>")
                  .Append("  <size=58%>").Append(g.name).Append("  [").Append(own).Append("]</size>");
                shown++;
            }
            if (shown == 0) sb.Append("\n<size=62%>no guards nearby</size>");
            readout.text = sb.ToString();
        }

        static string Bar(float v)
        {
            int filled = Mathf.RoundToInt(Mathf.Clamp01(v) * 10f);
            return "<mspace=0.55em>" + new string('#', filled) + new string('.', 10 - filled) + "</mspace>";
        }

        void BuildHUD()
        {
            var canvasGo = new GameObject("AI Detection HUD (Demo)");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            var pr = img.rectTransform;
            pr.anchorMin = new Vector2(1f, 1f);
            pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(1f, 1f);
            pr.anchoredPosition = new Vector2(-24f, -24f);
            pr.sizeDelta = new Vector2(720f, 210f);

            var textGo = new GameObject("Readout");
            textGo.transform.SetParent(panel.transform, false);
            readout = textGo.AddComponent<TextMeshProUGUI>();
            readout.fontSize = 24f;
            readout.color = Color.white;
            readout.alignment = TextAlignmentOptions.TopLeft;
            readout.richText = true;
            var tr = readout.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(18f, 14f);
            tr.offsetMax = new Vector2(-18f, -12f);
        }
    }
}
