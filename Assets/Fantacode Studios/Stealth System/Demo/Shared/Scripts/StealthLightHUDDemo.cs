using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: a live readout of the player's light visibility, the value
    /// <see cref="LightVision.GetVisibility"/> hands to the AI.
    ///
    /// Visibility is a straight multiplier on how fast awareness climbs, so 0 means a guard
    /// looking right at you gains nothing and 1 means full detection speed. Showing it on
    /// screen is the only way to actually watch the light system work.
    /// The stealth system has no dependency on this script.
    /// </summary>
    public class StealthLightHUDDemo : MonoBehaviour
    {
        [Tooltip("Player stimulus to sample. Found automatically from the Player tag when empty.")]
        [SerializeField] VisualStimulus target;

        [Tooltip("List the nearest lights and what each is doing.")]
        [SerializeField] bool showLightBreakdown = true;
        [Tooltip("Maximum number of nearby lights listed in the breakdown.")]
        [SerializeField] int maxLightsListed = 4;
        [Tooltip("Lights farther than this from the player are left off the breakdown, in meters.")]
        [SerializeField] float listRadius = 25f;

        [Header("Thresholds")]
        [Tooltip("Visibility (0-1) at or below which the readout shows HIDDEN.")]
        [SerializeField] float hiddenBelow = 0.05f;
        [Tooltip("Visibility (0-1) at or above which the readout shows EXPOSED.")]
        [SerializeField] float exposedAbove = 0.6f;

        [Header("Style")]
        [Tooltip("Readout and bar color while the player counts as hidden.")]
        [SerializeField] Color hiddenColor = new Color(0.35f, 0.9f, 0.5f);
        [Tooltip("Readout and bar color while the player is partially lit.")]
        [SerializeField] Color partialColor = new Color(1f, 0.8f, 0.25f);
        [Tooltip("Readout and bar color while the player counts as exposed.")]
        [SerializeField] Color exposedColor = new Color(1f, 0.35f, 0.3f);

        TMP_Text readout;
        Image barFill;
        readonly StringBuilder sb = new StringBuilder();
        readonly List<StealthLight> nearby = new List<StealthLight>();

        void Start()
        {
            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.GetComponent<VisualStimulus>();
            }
            BuildHUD();
        }

        void Update()
        {
            if (readout == null) return;

            if (target == null)
            {
                readout.text = "<b>LIGHT VISIBILITY</b>\n<size=60%>waiting for player / LightVision</size>";
                return;
            }

            float v = LightVision.GetVisibility(target);
            Color c = v <= hiddenBelow ? hiddenColor : v >= exposedAbove ? exposedColor : partialColor;
            string state = v <= hiddenBelow ? "HIDDEN" : v >= exposedAbove ? "EXPOSED" : "PARTIALLY LIT";

            barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
            barFill.color = c;

            sb.Clear();
            sb.Append("<b>LIGHT VISIBILITY</b>   <color=#")
              .Append(ColorUtility.ToHtmlStringRGB(c)).Append('>')
              .Append(Mathf.RoundToInt(v * 100f)).Append("%  ").Append(state).Append("</color>");
            sb.Append("\n<size=62%>0 = a guard gains no awareness   ·   100 = detected at full speed</size>");

            if (showLightBreakdown)
            {
                CollectNearby();
                for (int i = 0; i < nearby.Count && i < maxLightsListed; i++)
                {
                    var sl = nearby[i];
                    var l = sl.Light;
                    var motion = sl.GetComponent<StealthLightMotion>();
                    sb.Append("\n<size=62%>· ").Append(sl.name)
                      .Append("  [").Append(l.type).Append("]")
                      .Append("  weight ").Append(sl.VisibilityWeight.ToString("0.00"))
                      .Append("  ").Append(l.isActiveAndEnabled ? "ON" : "OFF");
                    if (motion != null) sb.Append("  (").Append(motion.StateLabel).Append(')');
                    sb.Append("</size>");
                }
            }
            readout.text = sb.ToString();
        }

        void CollectNearby()
        {
            nearby.Clear();
            var pos = target.transform.position;
            foreach (var sl in StealthLight.ActiveLights)
            {
                if (sl == null || sl.Light == null) continue;
                if (sl.Light.type != LightType.Directional &&
                    Vector3.Distance(pos, sl.transform.position) > listRadius) continue;
                nearby.Add(sl);
            }
            nearby.Sort((a, b) =>
                Vector3.Distance(pos, a.transform.position).CompareTo(Vector3.Distance(pos, b.transform.position)));
        }

        void BuildHUD()
        {
            var canvasGo = new GameObject("Light Visibility HUD (Demo)");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.55f);
            var pr = panelImg.rectTransform;
            pr.anchorMin = new Vector2(0f, 1f);
            pr.anchorMax = new Vector2(0f, 1f);
            pr.pivot = new Vector2(0f, 1f);
            pr.anchoredPosition = new Vector2(24f, -24f);
            pr.sizeDelta = new Vector2(680f, 190f);

            var textGo = new GameObject("Readout");
            textGo.transform.SetParent(panel.transform, false);
            readout = textGo.AddComponent<TextMeshProUGUI>();
            readout.fontSize = 26f;
            readout.color = Color.white;
            readout.alignment = TextAlignmentOptions.TopLeft;
            readout.richText = true;
            var tr = readout.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(18f, 34f);
            tr.offsetMax = new Vector2(-18f, -12f);

            // bar across the bottom of the panel
            var barBg = new GameObject("Bar BG");
            barBg.transform.SetParent(panel.transform, false);
            var bg = barBg.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.14f);
            var br = bg.rectTransform;
            br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(1f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.anchoredPosition = new Vector2(0f, 12f);
            br.sizeDelta = new Vector2(-36f, 14f);

            var fill = new GameObject("Bar Fill");
            fill.transform.SetParent(barBg.transform, false);
            barFill = fill.AddComponent<Image>();
            barFill.color = hiddenColor;
            var fr = barFill.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0f, 1f);
            fr.pivot = new Vector2(0f, 0.5f);
            fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
        }
    }
}
