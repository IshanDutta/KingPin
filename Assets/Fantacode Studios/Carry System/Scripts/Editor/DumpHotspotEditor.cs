using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FS_InteractionSystem
{
    /// <summary>
    /// Scene + inspector editor for <see cref="DumpHotspot"/>.
    ///
    /// Scene: the interaction points — where the carrier stands to dump — come from
    /// <see cref="InteractionHotspotEditor"/>, drawn with the same solid markers, facing arrows and
    /// Move / Rotate handles every other hotspot uses, through <c>SceneView.duringSceneGui</c> so
    /// they survive the Scene view's Gizmos toggle being off.
    ///
    /// Inspector: fields are grouped into collapsible, colour-accented sections matching the other
    /// hotspot editors, with a live Contents readout in play mode. Disable After Completion and
    /// Disable Object After Completion are deliberately not shown: a dump spot forces both off in
    /// Awake because it closes by filling up, so exposing them would offer settings that never take
    /// effect.
    /// </summary>
    [CustomEditor(typeof(DumpHotspot), true)]
    public class DumpHotspotEditor : InteractionHotspotEditor
    {
        // ── Accent colours ───────────────────────────────────────────────────────────
        // Amber for the dump, matching the Dump section of the occupancy editors.
        protected static readonly Color DumpAccent    = new Color(0.95f, 0.82f, 0.28f);
        protected static readonly Color NeutralAccent = new Color(0.62f, 0.67f, 0.76f);

        // Forced in DumpHotspot.Awake — claimed so they never surface in "Advanced", never drawn.
        static readonly string[] HiddenProps = { "disableHotspotAfterCompletion", "disableObjectAfterCompletion" };

        readonly HashSet<string> drawn = new HashSet<string>();
        readonly Dictionary<string, bool> sectionOpen = new Dictionary<string, bool>();
        GUIStyle titleStyle, boldFoldout;

        // ══ Inspector ════════════════════════════════════════════════════════════════

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            drawn.Clear();
            drawn.Add("m_Script");   // hidden — the title card names the component instead
            Claim(HiddenProps);      // claimed but never drawn

            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(prevLabelWidth, 168f);

            DrawTitleCard();

            DrawDumpSection();

            Section("Interaction", NeutralAccent,
                    "interactionLabel", "alignToHotspot", "snapToPosition",
                    "useStoppingDistance", "stoppingDistance", "interactionPoints");

            Section("Events", NeutralAccent, "OnStartInteractionEvent", "OnEndInteractionEvent");

            DrawAdvanced();
            DrawContents();

            EditorGUIUtility.labelWidth = prevLabelWidth;
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// What this spot accepts and what its lid does, with the capacity spelled out — "-1" reading
        /// as unlimited and an empty type list as "anything carryable" are the two settings most
        /// easily misread as "nothing".
        /// </summary>
        void DrawDumpSection()
        {
            var comp = target as DumpHotspot;

            string[] props = { "dumpCapacity", "allowedTypes", "playOpenAnimationOnDump",
                               "dumpOpenTarget", "dumpOpenClip" };
            Claim(props);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Header("Dump", DumpAccent, null, true))
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField(
                        "Only a character carrying something this spot accepts can see or use it — " +
                        "with empty hands there is no prompt at all.",
                        EditorStyles.wordWrappedMiniLabel);
                    DrawProps(props);

                    if (comp != null)
                    {
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField(
                            $"Holds {(comp.dumpCapacity < 0 ? "any number of objects" : $"{comp.dumpCapacity} object(s)")}, " +
                            (comp.allowedTypes == null || comp.allowedTypes.Count == 0
                                ? "of any carryable type."
                                : $"of {comp.allowedTypes.Count} listed type(s)."),
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            EditorGUILayout.Space(2);
        }

        // What the spot is holding. Authored empty and filled at runtime, so it is a readout rather
        // than a setting — shown in both modes because a spot pre-filled in a scene is worth seeing.
        void DrawContents()
        {
            var comp = target as DumpHotspot;
            if (comp == null) return;

            drawn.Add("storedObjects");

            SectionBody("Contents", NeutralAccent, true, () =>
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField("Stored",
                        comp.dumpCapacity < 0
                            ? $"{comp.StoredCount} (unlimited)"
                            : $"{comp.StoredCount} / {comp.dumpCapacity}{(comp.IsFull ? "  — full" : "")}");

                    foreach (var stored in comp.StoredObjects)
                        EditorGUILayout.LabelField("   •", stored != null ? stored.name : "(missing)");
                }
            });

            if (Application.isPlaying) Repaint();
        }

        // ── Layout building blocks ────────────────────────────────────────────────────

        void DrawTitleCard()
        {
            titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(target.GetType().Name), titleStyle);
                EditorGUILayout.LabelField("A place carried objects go — a dumpster, a boot, a chest.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// A collapsible, boxed section listing serialized fields. The names are CLAIMED immediately
        /// (so they can never fall through to "Advanced") but only DRAWN when the section is expanded.
        /// </summary>
        protected void Section(string title, Color accent, params string[] propNames)
        {
            Claim(propNames);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Header(title, accent, null, true))
                {
                    EditorGUILayout.Space(2);
                    DrawProps(propNames);
                }
            }
            EditorGUILayout.Space(2);
        }

        /// <summary>A collapsible, boxed section with custom body content (claims no fields).</summary>
        protected void SectionBody(string title, Color accent, bool defaultOpen, System.Action body)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Header(title, accent, null, defaultOpen))
                {
                    EditorGUILayout.Space(2);
                    body();
                }
            }
            EditorGUILayout.Space(2);
        }

        // Header row: accent bar + bold foldout on the left, optional enable toggle on the right.
        bool Header(string title, Color accent, SerializedProperty enableProp, bool defaultOpen)
        {
            boldFoldout ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

            Rect r = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 3f, 3f, r.height - 5f), accent);

            if (!sectionOpen.TryGetValue(title, out bool open)) open = defaultOpen;
            float toggleW = enableProp != null ? 20f : 0f;
            open = EditorGUI.Foldout(new Rect(r.x + 9f, r.y, r.width - 9f - toggleW, r.height),
                                     open, title, true, boldFoldout);
            sectionOpen[title] = open;

            if (enableProp != null)
            {
                var tr = new Rect(r.xMax - 16f, r.y + 1f, 16f, r.height);
                enableProp.boolValue = EditorGUI.Toggle(tr, enableProp.boolValue);
            }
            return open;
        }

        /// <summary>Marks property names as owned by a section so "Advanced" never re-lists them.</summary>
        protected void Claim(string[] propNames)
        {
            foreach (var n in propNames) drawn.Add(n);
        }

        /// <summary>Draws the named properties (missing ones skipped); claiming happens separately.</summary>
        protected void DrawProps(string[] propNames)
        {
            EditorGUI.indentLevel++;
            foreach (var n in propNames)
            {
                var p = serializedObject.FindProperty(n);
                if (p != null) EditorGUILayout.PropertyField(p, true);
            }
            EditorGUI.indentLevel--;
        }

        // Everything no section claimed, under a collapsed "Advanced" box — so a field added to the
        // hotspot later is never silently missing from the inspector.
        void DrawAdvanced()
        {
            var rest = new List<SerializedProperty>();
            var it = serializedObject.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script" || it.name == "storedObjects" || drawn.Contains(it.name)) continue;
                rest.Add(it.Copy());
            }
            if (rest.Count == 0) return;

            SectionBody("Advanced", NeutralAccent, false, () =>
            {
                EditorGUI.indentLevel++;
                foreach (var p in rest) EditorGUILayout.PropertyField(p, true);
                EditorGUI.indentLevel--;
            });
        }
    }
}
