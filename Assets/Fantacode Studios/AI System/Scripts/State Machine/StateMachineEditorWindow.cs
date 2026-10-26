#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using Object = System.Object;
// Unity 6.5 (6000.5) deprecated Object.GetInstanceID() in favor of GetEntityId(), which
// returns the EntityId struct instead of an int.
#if UNITY_6000_5_OR_NEWER
using NodeInstanceId = UnityEngine.EntityId;
#else
using NodeInstanceId = System.Int32;
#endif

namespace FS_Core
{
    public class StateMachineEditorWindow : EditorWindow
    {
        #region Constants / Window props
        protected const float GRID_SMALL = 20;
        protected const float GRID_LARGE = 100;

        protected const float ZOOM_MIN = 0.4f;
        protected const float ZOOM_MAX = 2.2f;

        protected const float HEADER_HEIGHT = 34f;
        protected const float BORDER_SIZE = 1f;
        protected const float PORT_SIZE = 16f;
        protected const float INSPECTOR_WIDTH_DEFAULT = 320f;
        protected const float TOOLBAR_HEIGHT = 23f;
        protected const float TITLE_BAR_HEIGHT = 50f;

        protected float inspectorWidth = INSPECTOR_WIDTH_DEFAULT;
        protected float parameterCanvasWidth = INSPECTOR_WIDTH_DEFAULT;

        protected const float INSPECTOR_MIN = 220f;
        protected const float PARAMETER_MIN = 150;
        protected const float INSPECTOR_MAX = 520f;
        protected const float PARAMETER_MAX = 300;
        protected const float SPLITTER_WIDTH = 4f;
        #endregion

        #region Appearance
        protected static readonly Color NODE_BG = new Color(0.18f, 0.18f, 0.19f);
        protected static readonly Color NODE_BG_SELECTED = new Color(0.22f, 0.25f, 0.30f);
        protected static readonly Color BORDER = new Color(0.12f, 0.12f, 0.12f);
        protected static readonly Color BORDER_SELECTED = new Color(0.4f, 0.7f, 1f);

        protected readonly Color gridSmallColor = new Color(0.22f, 0.22f, 0.22f, 0.55f);
        protected readonly Color gridLargeColor = new Color(0.25f, 0.25f, 0.25f, 0.85f);

        protected const float EXECUTION_GLOW_ALPHA = 0.45f;
        protected const float EXECUTION_PULSE_SPEED = .25f;
        protected const float EXECUTION_PULSE_MAG = 0.18f;
        protected const float CONNECTION_ANIM_SPEED = 120f;
        protected const float CONNECTION_ARROW_SPACING = 48f;
        protected const int CONNECTION_ARROW_MIN = 2;

        GUIContent focusGUIContent;
        GUIContent shortcutsGUIContent;
        GUIContent gridGUIContent;
        GUIContent minimapGUIContent;
        GUIContent inspectorGUIContent;
        GUIContent parameterGUIContent;

        #endregion

        #region Runtime state
        protected bool showShortcutKeys = true;
        protected bool showGrid = true;
        protected bool showMinimap = true;
        protected bool autoLiveLink = true;
        protected bool showInspector = true;
        protected bool showParameter = true;

        private string shortcutKeysPlayerPrefKey = "StateMachineEditor_showShortcutKeys";
        private string gridPlayerPrefKey = "StateMachineEditor_ShowGrid";
        private string minimapPlayerPrefKey = "StateMachineEditor_ShowMinimap";
        private string autoLiveLinkPlayerPrefKey = "StateMachineEditor_AutoLiveLink";
        private string showInspectorPlayerPrefKey = "StateMachineEditor_ShowInspector";
        private string showParameterPlayerPrefKey = "StateMachineEditor_ShowParameter";

        private bool isBoxSelecting = false;
        private Vector2 boxStart;
        private Vector2 boxEnd;
        [SerializeField] List<StateAsset> multiSelectedNodes = new List<StateAsset>();

        protected Vector2 panOffset;
        protected float zoom = 1f;

        protected bool isResizingInspector;
        protected bool isResizingParameterCanvas;

        protected Vector2 inspectorScrollPos;
        protected Vector2 parameterCanvasScrollPos;
        protected Rect contentRect;
        //protected Vector2 dragOffset;
        [SerializeField] Dictionary<StateAsset, Vector2> dragOffsets = new Dictionary<StateAsset, Vector2>();
        [SerializeField] Dictionary<string, FSSystemInfo> AllInstalledSystems = new Dictionary<string, FSSystemInfo>();

        protected Rect minimapRect;

        protected const float NODE_SPACING_X = 100f;
        protected const float NODE_SPACING_Y = 100f;

        //[SerializeField] StateAsset selectedNode = null;
        protected StateAsset draggingNode = null;
        protected NodeInstanceId selectedNodeInstanceID = default;

        float inspectorContentHeight;
        Vector2 inspectorScroll;
        float conditionRectHeight = 0;

        private List<StateAsset> copiedStateAssets;
        private StateAsset connectionFromNode;
        [SerializeField] StateTransitionAsset selectedTransition;

        public StateMachineAsset stateMachineAsset;
        public StateMachineAsset rootStateMachineAsset;
        public List<StateMachineAsset> stateMachineAssetsStack = new List<StateMachineAsset>();

        // Behavior view mode
        ExecuteBehaviorStateAsset activeBehaviorStateAsset;
        AIBehavior resolvedBehavior;
        #endregion

        #region Instance
        static StateMachineEditorWindow instance;
        public static StateMachineEditorWindow Instance
        {
            get
            {
                if (instance == null)
                    instance = GetWindow<StateMachineEditorWindow>("Graph Editor");
                return instance;
            }
        }
        BlackBoardkeys blackBoardKeys;
        BlackBoardkeys BlackBoardKeys
        {
            get
            {
                if (blackBoardKeys == null)
                {
                    blackBoardKeys = Resources.Load("Data/BlackboardKeyContainer") as BlackBoardkeys;
                }
                return blackBoardKeys;
            }
        }

        // [MenuItem("Tools/FS System/FS AI StateMachine Window")]
        public static void Open() 
        {
            OpenWindow(null);
        }

        public static void OpenWindow(StateMachineAsset asset)
        {
            var window = Instance;
            window.stateMachineAssetsStack.Clear();
            window.SetCurrentStateMachine(asset);
            window.titleContent = new GUIContent("State Machine Editor");
            window.Show();
            window.FocusNode();
            window.Focus();
        }


        #endregion

        #region Lifecycle
        protected void OnEnable()
        {
            panOffset = Vector2.zero;
            zoom = 1f;

            Repaint();

            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;

            if (PlayerPrefs.GetFloat(shortcutKeysPlayerPrefKey, 1) == 1f)
                showShortcutKeys = true;
            else
                showShortcutKeys = false;

            if (PlayerPrefs.GetFloat(gridPlayerPrefKey, 1f) == 1f)
                showGrid = true;
            else
                showGrid = false;

            if (PlayerPrefs.GetFloat(minimapPlayerPrefKey, 1f) == 1f)
                showMinimap = true;
            else
                showMinimap = false;

            if (PlayerPrefs.GetFloat(autoLiveLinkPlayerPrefKey, 1f) == 1f)
                autoLiveLink = true;
            else
                autoLiveLink = false;

            if (PlayerPrefs.GetFloat(showInspectorPlayerPrefKey, 1f) == 1f)
                showInspector = true;
            else
                showInspector = false;

            if (PlayerPrefs.GetFloat(showParameterPlayerPrefKey, 1f) == 1f)
                showParameter = true;
            else
                showParameter = false;

            inspectorWidth = showInspector ? INSPECTOR_WIDTH_DEFAULT : 0;
            parameterCanvasWidth = showParameter ? INSPECTOR_WIDTH_DEFAULT : 0;


            FocusNode();

            if (stateMachineAsset != null)
            {
                foreach (var state in stateMachineAsset.AllStateAssets)
                {
                    if (state == null) return;
                    if (!dragOffsets.ContainsKey(state))
                    {
                        dragOffsets.Add(state, state.NodePosition);
                    }
                }
            }

            FindSystem();
        }

        protected void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;

            PlayerPrefs.SetFloat(shortcutKeysPlayerPrefKey, showShortcutKeys ? 1f : 0f);
            PlayerPrefs.SetFloat(gridPlayerPrefKey, showGrid ? 1f : 0f);
            PlayerPrefs.SetFloat(minimapPlayerPrefKey, showMinimap ? 1f : 0f);
            PlayerPrefs.SetFloat(autoLiveLinkPlayerPrefKey, autoLiveLink ? 1f : 0f);
            PlayerPrefs.SetFloat(showInspectorPlayerPrefKey, showInspector ? 1f : 0f);
            PlayerPrefs.SetFloat(showParameterPlayerPrefKey, showParameter ? 1f : 0f);
        }

        protected void EditorUpdate()
        {
            if (Application.isPlaying)
                Repaint();
        }
        #endregion

        #region GUI / Layout
        protected void OnGUI()
        {
            try
            {

                Event e = Event.current;

                HandleKeyboardShortcuts(e);
                var canvasRect = new Rect(parameterCanvasWidth, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT * 2 - BORDER_SIZE * 2, position.width - inspectorWidth - parameterCanvasWidth, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT * 2 + 2);
                Rect inspectorRect = new Rect(position.width - inspectorWidth, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - BORDER_SIZE * 2, inspectorWidth, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT + 2);
                Rect parameterCanvasRect = new Rect(0, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - BORDER_SIZE * 2, parameterCanvasWidth, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT + 2);
                Rect inspectorCanvasSplitterRect = new Rect(position.width - inspectorWidth - SPLITTER_WIDTH * 0.5f, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT, SPLITTER_WIDTH, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT);
                Rect parameterCanvasSplitterRect = new Rect(parameterCanvasWidth - SPLITTER_WIDTH * 0.5f, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT, SPLITTER_WIDTH, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT);

                HandleCanvasResize(inspectorCanvasSplitterRect, parameterCanvasSplitterRect);

                if (currentAIController?.StateMachineAsset != null && currentAIController.StateMachineAsset != rootStateMachineAsset)
                {
                    SetCurrentStateMachine(currentAIController.StateMachineAsset, true);
                }


                DrawTitleBar();
                DrawToolbar();
                DrawBreadCrumbs(canvasRect);
                if (inspectorWidth > 0)
                    DrawInspector(inspectorRect);
                if (parameterCanvasWidth > 0)
                    DrawParameter(parameterCanvasRect);

                HandlePropertiesPopup(canvasRect);
                GUILayout.BeginArea(canvasRect);
                {
                    contentRect = new Rect(0, 0, canvasRect.width, canvasRect.height);

                    GUI.BeginGroup(contentRect);
                    {
                        HandlePanZoom(e, canvasRect);

                        EditorGUI.DrawRect(contentRect, new Color(0.12f, 0.12f, 0.12f));
                        if (showGrid)
                        {
                            DrawGrid(GRID_SMALL, gridSmallColor, canvasRect);
                            DrawGrid(GRID_LARGE, gridLargeColor, canvasRect);
                        }
                        if (activeBehaviorStateAsset != null)
                        {
                            DrawBehaviorView();
                        }
                        else if (stateMachineAsset != null)
                        {
                            DrawConnectionDrag();
                            DrawTransitions();
                            DrawAllNode();

                            HandleTransitionSelection(e);

                            DrawBoxSelectionVisual();
                        }
                        if (showMinimap)
                            DrawMinimap(canvasRect);

                        if (showShortcutKeys)
                            DrawShortcutHints(canvasRect);
                    }
                    GUI.EndGroup();
                }
                GUILayout.EndArea();
                if (isBoxSelecting)
                {
                    SelectNodesInBox(GetScreenSelectionRect(), e.control);
                }
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (canvasRect.Contains(e.mousePosition))
                    {
                        DeselectNode();
                        e.Use();
                    }
                }
                DrawRectBorder(parameterCanvasRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), 1f);
                DrawRectBorder(canvasRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
                DrawRectBorder(inspectorRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);

                if (Event.current.type == EventType.Repaint)
                    Repaint();


            }
            catch (Exception)
            {
            }
        }

        void DrawAnyStateNode()
        {
            DrawNode(GetAnyState());
        }

        AnyStateAsset GetAnyState()
        {
            if (stateMachineAsset.AnyStateAsset == null)
            {
                stateMachineAsset.AnyStateAsset = CreateInstance<AnyStateAsset>();
                stateMachineAsset.AnyStateAsset.name = stateMachineAsset.AnyStateAsset.NodeTitle;
                stateMachineAsset.AnyStateAsset.SetNodePosition(Vector2.zero);
                stateMachineAsset.AnyStateAsset.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(stateMachineAsset.AnyStateAsset, stateMachineAsset);
                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(this);
            }
            return stateMachineAsset.AnyStateAsset;
        }

        protected void DrawSelectedNodeInspector()
        {
            if (multiSelectedNodes.Count > 0)
            {
                var state = multiSelectedNodes.Last();
                state.DrawInspector();
                if (stateMachineAsset != null)
                    DrawTransitionOrderList(state, stateMachineAsset);
            }
        }

        static ReorderableList transitionOrderList;
        static StateAsset transitionOrderListOwner;
        static StateMachineAsset transitionOrderListSM;

        /// <summary>
        /// Entry point for Inspector editors — resolves the StateMachineAsset that owns the state.
        /// </summary>
        public static void DrawStateTransitionOrderList(StateAsset state)
        {
            if (state == null)
                return;

            var sm = transitionOrderListOwner == state ? transitionOrderListSM : ResolveStateMachineAsset(state);
            if (sm != null)
                DrawTransitionOrderList(state, sm);
        }

        static StateMachineAsset ResolveStateMachineAsset(StateAsset state)
        {
            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<StateMachineAsset>()
                .FirstOrDefault(sm => sm.States.Contains(state) || sm.AnyStateAsset == state);
        }

        static void DrawTransitionOrderList(StateAsset state, StateMachineAsset sm)
        {
            var backing = state is AnyStateAsset ? sm.AnyStateTransitions : sm.Transitions;
            var outgoing = state is AnyStateAsset
                ? backing.Where(t => t != null).ToList()
                : backing.Where(t => t != null && t.FromState == state).ToList();

            if (outgoing.Count == 0)
                return;

            if (transitionOrderList == null || transitionOrderListOwner != state || transitionOrderListSM != sm ||
                !outgoing.SequenceEqual((List<StateTransitionAsset>)transitionOrderList.list))
            {
                transitionOrderListOwner = state;
                transitionOrderListSM = sm;
                transitionOrderList = new ReorderableList(outgoing, typeof(StateTransitionAsset),
                    draggable: true, displayHeader: true, displayAddButton: false, displayRemoveButton: false);

                transitionOrderList.drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, new GUIContent("Transitions", "Evaluated top to bottom, the first transition whose conditions pass wins"));

                transitionOrderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var t = (StateTransitionAsset)transitionOrderList.list[index];
                    string from = t.FromState != null ? t.FromState.NodeTitle : "Any State";
                    string to = t.ToState != null ? t.ToState.NodeTitle : "<Missing To>";
                    EditorGUI.LabelField(rect, $"{from} -> {to}");
                };

                transitionOrderList.onReorderCallback = list =>
                {
                    var backingList = transitionOrderListOwner is AnyStateAsset
                        ? transitionOrderListSM.AnyStateTransitions
                        : transitionOrderListSM.Transitions;
                    ApplyTransitionOrder(transitionOrderListSM, backingList, list.list.Cast<StateTransitionAsset>().ToList());
                };

                transitionOrderList.onSelectCallback = list =>
                {
                    if (instance != null)
                    {
                        instance.selectedTransition = (StateTransitionAsset)list.list[list.index];
                        instance.Repaint();
                    }
                };
            }

            // Keep the row highlight in sync when the transition was selected elsewhere
            // (e.g. in the embedded collection list below)
            if (instance != null)
            {
                int idx = outgoing.IndexOf(instance.selectedTransition);
                if (idx >= 0 && transitionOrderList.index != idx)
                    transitionOrderList.index = idx;
            }

            GUILayout.Space(15);
            transitionOrderList.DoLayoutList();

            DrawSelectedRowTransitionDetails(outgoing, sm);
        }

        static void DrawSelectedRowTransitionDetails(List<StateTransitionAsset> outgoing, StateMachineAsset sm)
        {
            var window = instance;
            if (window == null || window.stateMachineAsset != sm ||
                window.selectedTransition == null || !outgoing.Contains(window.selectedTransition))
                return;

            GUILayout.Space(10);
            // inspectorContentHeight is a two-pass IMGUI measurement from the previous draw;
            // the extra margin covers the header area above the internal scrollview
            float detailHeight = window.inspectorContentHeight + 120f;
            var detailRect = GUILayoutUtility.GetRect(10f, detailHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Layout)
            {
                GUI.BeginGroup(detailRect);
                window.DrawConnectionInspector(new Rect(0, 0, detailRect.width, detailHeight), window.selectedTransition, 12, drawTransitionList: false);
                GUI.EndGroup();
            }
        }

        static void ApplyTransitionOrder(StateMachineAsset sm, List<StateTransitionAsset> backing, List<StateTransitionAsset> ordered)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Reorder Transitions");

            // The ordered list is a filtered view of the backing list — write it back
            // into the slots its items originally occupied, leaving other entries untouched
            var slots = new List<int>();
            for (int i = 0; i < backing.Count; i++)
            {
                if (ordered.Contains(backing[i]))
                    slots.Add(i);
            }
            for (int i = 0; i < slots.Count; i++)
                backing[slots[i]] = ordered[i];

            EditorUtility.SetDirty(sm);
            UpdateStateTransitionCollections(sm);
        }

        static void UpdateStateTransitionCollections(StateMachineAsset sm)
        {
            sm.stateTransitionCollections = sm.AllTransitions.GroupBy(t => (t.FromState, t.ToState)).Select(g => new StateTransitionCollection
            {
                Transitions = g.ToList()
            }).ToList();
        }
        protected void DeselectNode()
        {
            multiSelectedNodes.Clear();
            selectedNodeInstanceID = default;
            Repaint();
        }

        protected void DrawTitleBar()
        {
            Rect titleBarRect = new Rect(0, 0, position.width, TITLE_BAR_HEIGHT);

            DrawGradientBackground(titleBarRect,
                new Color(0.18f, 0.18f, 0.20f),
                new Color(0.16f, 0.16f, 0.18f));

            GUILayout.BeginArea(titleBarRect);
            GUILayout.BeginHorizontal();

            GUILayout.Space(16);
            DrawEditorIcon();
            GUILayout.Space(8);

            GUIStyle titleStyle = new GUIStyle();
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.95f, 0.95f, 0.97f);
            titleStyle.fontSize = 18;
            titleStyle.alignment = TextAnchor.MiddleLeft;

            GUILayout.Label("StateMachine Editor", titleStyle, GUILayout.Height(TITLE_BAR_HEIGHT));

            GUILayout.FlexibleSpace();

            if (stateMachineAsset != null)
            {
                DrawInfoBadge($"StateMachine: {stateMachineAsset.name}");
            }
            else
            {
                DrawWarningBadge("No StateMachine");
            }

            GUILayout.Space(16);

            GUILayout.Space(16);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            DrawRectBorder(new Rect(0, 0, titleBarRect.width, titleBarRect.height), new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
        }

        protected void DrawToolbar()
        {
            Rect toolbarRect = new Rect(0, TITLE_BAR_HEIGHT - BORDER_SIZE, position.width, TOOLBAR_HEIGHT);

            EditorGUI.DrawRect(toolbarRect, new Color(0.19f, 0.19f, 0.21f));
            EditorGUI.DrawRect(new Rect(0, TITLE_BAR_HEIGHT, position.width, 1), new Color(0.25f, 0.25f, 0.27f));
            EditorGUI.DrawRect(new Rect(0, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - 1, position.width, 1), new Color(0.12f, 0.12f, 0.14f));

            var contentRect = new Rect(toolbarRect.x, toolbarRect.y + BORDER_SIZE, toolbarRect.width, toolbarRect.height);
            GUILayout.BeginArea(contentRect);
            GUILayout.BeginHorizontal();


            if (DrawToolbarButton("Focus", SetIconByName("d_ViewToolZoom", ref focusGUIContent)))
            {
                FocusNode();
            }
            float spacing = 2;
            GUILayout.Space(spacing);

            if (DrawToolbarToggle("Shortcuts", showShortcutKeys, icon: SetIconByName("d__Help", ref shortcutsGUIContent)) != showShortcutKeys)
            {
                showShortcutKeys = !showShortcutKeys;
            }

            GUILayout.Space(spacing);

            if (DrawToolbarToggle("Grid", showGrid, icon: SetIconByName("d_GridLayoutGroup Icon", ref gridGUIContent)) != showGrid)
            {
                showGrid = !showGrid;
            }

            GUILayout.Space(spacing);

            if (DrawToolbarToggle("Minimap", showMinimap, icon: SetIconByName("d_SceneViewFx", ref minimapGUIContent)) != showMinimap)
            {
                showMinimap = !showMinimap;
            }

            GUILayout.Space(spacing);

            if (DrawToolbarToggle("Inspector", showInspector, icon: SetIconByName("d_UnityEditor.InspectorWindow", ref inspectorGUIContent)) != showInspector)
            {
                showInspector = !showInspector;
                inspectorWidth = showInspector ? INSPECTOR_WIDTH_DEFAULT : 0;
            }

            GUILayout.Space(spacing);

            if (DrawToolbarToggle("Parameter", showParameter, icon: SetIconByName("d_ToolSettings", ref parameterGUIContent)) != showParameter)
            {
                showParameter = !showParameter;
                parameterCanvasWidth = showParameter ? INSPECTOR_WIDTH_DEFAULT : 0;
            }


            GUILayout.FlexibleSpace();

            DrawZoomIndicator();
            GUILayout.Space(8);

            if (DrawToolbarToggle("Auto Live Link", autoLiveLink) != autoLiveLink)
            {
                autoLiveLink = !autoLiveLink;
                if(autoLiveLink) FocusNode();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            DrawRectBorder(toolbarRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
        }

        GUIContent SetIconByName(string name, ref GUIContent gUIContent)
        {
            if (gUIContent == null)
                gUIContent = EditorGUIUtility.IconContent(name);
            return gUIContent;
        }

        void DrawBreadCrumbs(Rect canvasRect)
        {
            try
            {
                Rect toolbarRect = new Rect(canvasRect.x, canvasRect.y - TOOLBAR_HEIGHT, canvasRect.width, TOOLBAR_HEIGHT + BORDER_SIZE);

                EditorGUI.DrawRect(toolbarRect, new Color(0, 0, 0, .5f));
                DrawRectBorder(toolbarRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);

                if (stateMachineAsset == null) return;

                var contentRect = new Rect(toolbarRect.x, toolbarRect.y + BORDER_SIZE, toolbarRect.width, toolbarRect.height);

                GUILayout.BeginArea(contentRect);
                GUILayout.BeginHorizontal();
                GUILayout.Space(BORDER_SIZE);

                var buttonStyle = new GUIStyle(GUI.skin.label);
                buttonStyle.fontSize = 11;
                buttonStyle.fixedHeight = toolbarRect.height - BORDER_SIZE * 2;
                buttonStyle.normal.textColor = Color.white;
                buttonStyle.hover.textColor = Color.gray;

                bool inBehaviorView = activeBehaviorStateAsset != null;

                for (int i = 0; i < stateMachineAssetsStack.Count; i++)
                {
                    var asset = stateMachineAssetsStack[i];

                    if (i > 0)
                    {
                        GUILayout.Label(">", buttonStyle);
                    }

                    if (i == stateMachineAssetsStack.Count - 1 && !inBehaviorView)
                    {
                        buttonStyle.hover.textColor = Color.white;
                        buttonStyle.normal.textColor = Color.white;
                        buttonStyle.fontStyle = FontStyle.Bold;
                    }
                    else
                        buttonStyle.normal.textColor = new Color(1, 1, 1, .6f);

                    var name = i == 0 ? asset.name : asset.Name;

                    if (GUILayout.Button(name, buttonStyle))
                    {
                        ExitBehaviorView();

                        int removeIndex = i + 1;
                        int removeCount = stateMachineAssetsStack.Count - removeIndex;

                        if (removeCount > 0)
                            stateMachineAssetsStack.RemoveRange(removeIndex, removeCount);

                        SetCurrentStateMachine(asset);
                        DeselectNode();
                        break;
                    }

                }

                if (inBehaviorView)
                {
                    GUILayout.Label(">", buttonStyle);
                    buttonStyle.hover.textColor = Color.white;
                    buttonStyle.normal.textColor = Color.white;
                    buttonStyle.fontStyle = FontStyle.Bold;
                    GUILayout.Label(activeBehaviorStateAsset.NodeTitle, buttonStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();


            }
            catch (Exception)
            {
            }
        }

        void DrawInfoBadge(string text)
        {
            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label);
            badgeStyle.normal.textColor = new Color(0.85f, 0.85f, 0.87f);
            badgeStyle.fontSize = 11;
            badgeStyle.padding = new RectOffset(10, 10, 6, 6);

            Rect badgeRect = GUILayoutUtility.GetRect(new GUIContent(text), badgeStyle);
            badgeRect.x = position.width - badgeRect.width - (TITLE_BAR_HEIGHT - badgeRect.height) * .5f;
            badgeRect.y = (TITLE_BAR_HEIGHT - badgeRect.height) * 0.5f;

            EditorGUI.DrawRect(badgeRect, new Color(0.25f, 0.65f, 1.0f, 0.15f));
            DrawRectBorder(badgeRect, new Color(0.25f, 0.65f, 1.0f, 0.4f), 1);

            GUI.Label(badgeRect, text, badgeStyle);
        }

        void DrawWarningBadge(string text)
        {
            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label);
            badgeStyle.normal.textColor = new Color(1.0f, 0.8f, 0.4f);
            badgeStyle.fontSize = 11;
            badgeStyle.padding = new RectOffset(10, 10, 6, 6);

            Rect badgeRect = GUILayoutUtility.GetRect(new GUIContent(text), badgeStyle);
            badgeRect.x = position.width - badgeRect.width - (TITLE_BAR_HEIGHT - badgeRect.height) * .5f;
            badgeRect.y = (TITLE_BAR_HEIGHT - badgeRect.height) * 0.5f;

            EditorGUI.DrawRect(badgeRect, new Color(1.0f, 0.7f, 0.2f, 0.15f));
            DrawRectBorder(badgeRect, new Color(1.0f, 0.7f, 0.2f, 0.4f), 1);

            GUI.Label(badgeRect, text, badgeStyle);
        }

        protected void DrawInspector(Rect inspectorRect)
        {
            GUILayout.BeginArea(inspectorRect);

            GUILayout.Space(15);

            if (multiSelectedNodes.Count > 0)
            {
                inspectorScrollPos = GUILayout.BeginScrollView(inspectorScrollPos);
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Space(15);
                GUILayout.BeginVertical();

                GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
                headerStyle.fontSize = 15;
                headerStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
                headerStyle.alignment = TextAnchor.MiddleCenter;
                GUILayout.Label("INSPECTOR", headerStyle);

                GUILayout.Space(3);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(inspectorRect.width - 30, 2), new Color(0.3f, 0.3f, 0.3f));
                GUILayout.Space(15);

                DrawSelectedNodeInspector();

                GUILayout.Space(10);

                GUILayout.EndVertical();
                GUILayout.Space(15);
                GUILayout.EndHorizontal();

                GUILayout.EndScrollView();
            }
            else if (selectedTransition != null)
            {
                DrawConnectionInspector(inspectorRect, selectedTransition);
            }
            else
            {

                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();

                GUILayout.Space(10);

                GUIStyle centered = new GUIStyle(GUI.skin.label);
                centered.alignment = TextAnchor.MiddleCenter;
                centered.fontSize = 13;
                centered.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("No Node Selected", centered);

                GUILayout.Space(5);

                centered.fontSize = 11;
                centered.normal.textColor = new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Label("Click a node to view properties", centered);

                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

            }

            GUILayout.EndArea();
        }
        protected void DrawParameter(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(8);
            var borderRect = new Rect(0, 0, rect.width, 36);
            EditorGUI.DrawRect(borderRect, new Color(0, 0, 0, .5f));

            if (BlackBoardKeys == null)
            {
                EditorGUILayout.HelpBox("No Parameter Container Assigned", MessageType.Warning);
                GUILayout.EndArea();
                return;
            }
            GUILayout.BeginHorizontal();
            // Header
            GUIStyle header = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 0, 0, 0),
                fontStyle = FontStyle.Bold
            };
            header.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            header.hover.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUILayout.Label("Parameters", header);
            GUIStyle addButtonStyle = new GUIStyle(EditorStyles.iconButton)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = 25,
                fixedHeight = 18
            };



            var plusIcon = EditorGUIUtility.IconContent("Toolbar Plus");
            if (GUILayout.Button(new GUIContent(plusIcon.image, "Add Parameter"), addButtonStyle))
            {
                GenericMenu menu = new GenericMenu();

                menu.AddItem(new GUIContent("Bool"), false, () =>
                {
                    Undo.RecordObject(BlackBoardKeys, "Add Parameter");
                    string baseName = $"New Bool";
                    string finalName = baseName;
                    int suffix = 1;

                    while (NameExists(finalName))
                    {
                        finalName = $"{baseName}_{suffix}";
                        suffix++;
                    }
                    var newParam = new BlackboardKey
                    {
                        Name = finalName,
                        Type = BlackboardValueType.Bool
                    };
                    BlackBoardKeys.Keys.Add(newParam);
                });

                menu.AddItem(new GUIContent("Float"), false, () =>
                {
                    Undo.RecordObject(BlackBoardKeys, "Add Parameter");

                    string baseName = $"New Float";
                    string finalName = baseName;
                    int suffix = 1;

                    while (NameExists(finalName))
                    {
                        finalName = $"{baseName}_{suffix}";
                        suffix++;
                    }

                    var newParam = new BlackboardKey
                    {

                        Name = finalName,
                        Type = BlackboardValueType.Float
                    };
                    BlackBoardKeys.Keys.Add(newParam);
                });

                menu.ShowAsContext();
            }
            GUILayout.Space(5);
            GUILayout.EndHorizontal();

            DrawRectBorder(borderRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), 1);

            GUILayout.Space(10);

            parameterCanvasScrollPos = GUILayout.BeginScrollView(parameterCanvasScrollPos);


            var keys = BlackBoardKeys.Keys;

            for (int i = 0; i < keys.Count; i++)
            {
                DrawParameterRow(rect.width - 25, keys[i]);
            }

            GUILayout.Space(8);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        bool NameExists(string name)
        {
            if (blackBoardKeys == null) return false;

            for (int i = 0; i < blackBoardKeys.Keys.Count; i++)
            {
                var el = blackBoardKeys.Keys[i];
                string n = el.Name;
                if (n == name) return true;
            }
            return false;
        }
        void DrawParameterRow(float width, BlackboardKey key)
        {
            if (key.Type != BlackboardValueType.Float && key.Type != BlackboardValueType.Bool) return;

            Rect rowRect = GUILayoutUtility.GetRect(width, 36);
            GUI.Box(rowRect, GUIContent.none, EditorStyles.helpBox);

            Rect content = new Rect(rowRect.x + 8, rowRect.y + 8, rowRect.width - 16, rowRect.height - 16);

            bool isRuntime = Application.isPlaying && currentAIController != null;

            if (isRuntime)
            {
                // Runtime layout: Name (45%) + Value (55%)
                Rect nameRect = new Rect(content.x, content.y, content.width * 0.45f, 18);
                EditorGUI.LabelField(nameRect, key.Name);

                Rect valueRect = new Rect(content.x + content.width * 0.45f, content.y, content.width * 0.55f, 18);

                string displayValue = "\u2014";
                if (currentAIController.Blackboard.TryGetDisplayValue(key.Name, out string val))
                    displayValue = val;

                if (key.Type == BlackboardValueType.Bool)
                {
                    bool boolVal = displayValue == "True";
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.Toggle(valueRect, boolVal);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUI.LabelField(valueRect, displayValue);
                }
            }
            else
            {
                // Edit mode layout: Name (60%) + Type (20%) + Delete button
                Rect nameRect = new Rect(content.x, content.y, content.width * 0.6f, 18);
                EditorGUI.BeginChangeCheck();
                EditorGUI.LabelField(nameRect, key.Name);

                // Type Label (Read-only)
                Rect typeRect = new Rect(content.x + content.width * 0.65f, content.y, content.width * 0.2f, 18);
                EditorGUI.LabelField(typeRect, key.Type.ToString(), EditorStyles.centeredGreyMiniLabel);

                // Delete Button
                Rect deleteRect = new Rect(content.x + content.width - 18, content.y + 1, 16, 16);
                GUIStyle removeButtonStyle = new GUIStyle(EditorStyles.iconButton)
                {
                    alignment = TextAnchor.MiddleCenter
                };

                if (GUI.Button(
                        deleteRect,
                        EditorGUIUtility.IconContent("d_winbtn_win_close"),
                        removeButtonStyle))
                {
                    Undo.RecordObject(BlackBoardKeys, "Delete Parameter");
                    blackBoardKeys.Keys.Remove(key);
                    EditorUtility.SetDirty(BlackBoardKeys);
                }
            }

        }

        protected void HandleCanvasResize(Rect inspectorCanvasSplitterRect, Rect parameterCanvasSplitterRect)
        {
            EditorGUIUtility.AddCursorRect(inspectorCanvasSplitterRect, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(parameterCanvasSplitterRect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (inspectorCanvasSplitterRect.Contains(e.mousePosition))
                    {
                        isResizingInspector = true;
                        e.Use();
                    }
                    else if (parameterCanvasSplitterRect.Contains(e.mousePosition))
                    {
                        isResizingParameterCanvas = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isResizingInspector)
                    {
                        inspectorWidth = Mathf.Clamp(
                            position.width - e.mousePosition.x,
                            INSPECTOR_MIN,
                            INSPECTOR_MAX
                        );

                        Repaint();
                        e.Use();
                    }
                    if (isResizingParameterCanvas)
                    {
                        parameterCanvasWidth = Mathf.Clamp(
                            position.width - (position.width - e.mousePosition.x),
                            PARAMETER_MIN,
                            PARAMETER_MAX
                        );

                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (isResizingInspector || isResizingParameterCanvas)
                    {
                        isResizingInspector = false;
                        isResizingParameterCanvas = false;
                        e.Use();
                    }
                    break;
            }
        }
        #endregion

        #region Grid / Pan / Zoom / Minimap / Shortcuts
        protected void HandlePanZoom(Event e, Rect canvasRect)
        {
            Rect localCanvas = new Rect(0, 0, canvasRect.width, canvasRect.height);

            if (!localCanvas.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDrag && (e.button == 2 || (e.alt && e.button == 0)))
            {
                panOffset += e.delta;
                e.Use();
                Repaint();
            }

            if (e.type == EventType.ScrollWheel)
            {
                float oldZoom = zoom;
                zoom = Mathf.Clamp(zoom - e.delta.y * 0.02f, ZOOM_MIN, ZOOM_MAX);

                Vector2 mouse = e.mousePosition;
                Vector2 world = (mouse - panOffset) / oldZoom;
                panOffset = mouse - world * zoom;

                e.Use();
                Repaint();
            }
        }

        protected void DrawGrid(float spacing, Color color, Rect canvasRect)
        {
            Handles.BeginGUI();
            Handles.color = color;

            float scaled = spacing * zoom;
            if (scaled < 8f)
            {
                Handles.EndGUI();
                return;
            }

            Vector2 offset = new(panOffset.x % scaled, panOffset.y % scaled);

            int cols = Mathf.CeilToInt(canvasRect.width / scaled) + 1;
            int rows = Mathf.CeilToInt(canvasRect.height / scaled) + 1;

            for (int i = 0; i < cols; i++)
                Handles.DrawLine(
                    new Vector2(offset.x + i * scaled, 0),
                    new Vector2(offset.x + i * scaled, canvasRect.height)
                );

            for (int j = 0; j < rows; j++)
                Handles.DrawLine(
                    new Vector2(0, offset.y + j * scaled),
                    new Vector2(canvasRect.width, offset.y + j * scaled)
                );

            Handles.EndGUI();
        }

        protected void DrawMinimap(Rect canvasRect)
        {
            if (stateMachineAsset == null || stateMachineAsset.AllStateAssets == null || stateMachineAsset.AllStateAssets.Count == 0)
                return;

            try
            {
                float minimapSize = 180f;
                minimapRect = new Rect(
                    canvasRect.width - minimapSize - 15,
                    canvasRect.height - minimapSize - 15,
                    minimapSize,
                    minimapSize);

                EditorGUI.DrawRect(minimapRect, new Color(0.12f, 0.12f, 0.12f, 0.9f));
                DrawRectBorder(minimapRect, new Color(0.35f, 0.35f, 0.35f), 2f);

                var states = stateMachineAsset.AllStateAssets;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                foreach (var state in states)
                {
                    if (state == null)
                        continue;
                    Rect r = state.NodeRect;
                    minX = Mathf.Min(minX, r.xMin);
                    minY = Mathf.Min(minY, r.yMin);
                    maxX = Mathf.Max(maxX, r.xMax);
                    maxY = Mathf.Max(maxY, r.yMax);
                }

                float worldWidth = Mathf.Max(1f, maxX - minX);
                float worldHeight = Mathf.Max(1f, maxY - minY);

                float padding = 12f;
                float scale = Mathf.Min(
                    (minimapSize - padding * 2) / worldWidth,
                    (minimapSize - padding * 2) / worldHeight);

                Vector2 offset = new Vector2(minimapRect.x + padding, minimapRect.y + padding);

                Handles.BeginGUI();

                Handles.color = new Color(0.6f, 0.7f, 0.9f, 0.5f);

                foreach (var state in states)
                {
                    var transitions = GetTransitions(state);
                    if (transitions == null) continue;

                    Vector2 from = MiniMapPos(state.NodeRect.center, minX, minY, scale, offset);

                    foreach (var target in transitions)
                    {
                        if (target == null) continue;
                        Vector2 to = MiniMapPos(target.NodeRect.center, minX, minY, scale, offset);
                        Handles.DrawLine(from, to);
                    }
                }

                Handles.EndGUI();

                foreach (var state in states)
                {
                    Rect worldRect = state.NodeRect;

                    Vector2 miniPos = MiniMapPos(worldRect.position, minX, minY, scale, offset);
                    Vector2 miniSize = worldRect.size * scale;

                    Rect miniRect = new Rect(miniPos, miniSize);

                    Color nodeColor = new Color(0.4f, 0.4f, 0.4f);

                    if (multiSelectedNodes.Contains(state))
                        nodeColor = new Color(0.4f, 0.7f, 1f);

                    EditorGUI.DrawRect(miniRect, nodeColor);
                }

                Rect viewWorld = GetCurrentViewWorldRect(canvasRect);
                Rect viewMini = new Rect(
                    MiniMapPos(viewWorld.position, minX, minY, scale, offset),
                    viewWorld.size * scale);
            }
            catch (Exception)
            {
            }
        }

        Vector2 MiniMapPos(Vector2 worldPos, float minX, float minY, float scale, Vector2 offset)
        {
            return new Vector2(
                offset.x + (worldPos.x - minX) * scale,
                offset.y + (worldPos.y - minY) * scale);
        }

        Rect GetCurrentViewWorldRect(Rect canvasRect)
        {
            Vector2 topLeft = ScreenToWorld(Vector2.zero);
            Vector2 bottomRight = ScreenToWorld(new Vector2(canvasRect.width, canvasRect.height));
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        IEnumerable<StateAsset> GetTransitions(StateAsset state)
        {
            return null;
        }

        protected void DrawShortcutHints(Rect canvasRect)
        {
            float w = 220;
            float h = 110;
            Rect r = new Rect(8, canvasRect.height - h - 8, w, h);
            EditorGUI.DrawRect(r, new Color(0.06f, 0.06f, 0.06f, 0.9f));
            DrawRectBorder(r, new Color(0.2f, 0.2f, 0.2f), 1f);

            var contentRect = new Rect(r.x + 5, r.y, w, h);
            GUILayout.BeginArea(contentRect);
            GUILayout.Space(6);
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, fontSize = 11 };
            GUILayout.Label("Shortcuts", title);
            GUILayout.Space(4);

            GUIStyle content = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }, fontSize = 11 };
            GUILayout.Label("F : Focus selected / default / AnyState", content);
            GUILayout.Label("Delete : Delete selected node", content);
            //GUILayout.Label("Z : Reset Zoom", content);
            GUILayout.Label("Ctrl + D : Duplicate", content);
            GUILayout.Label("Ctrl + C : Copy", content);
            GUILayout.Label("Ctrl + V : paste", content);
            GUILayout.Space(6);
            GUILayout.EndArea();
        }
        #endregion

        #region Small UI utilities
        protected void DrawRectBorder(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - width, rect.y, width, rect.height), color);
        }

        protected void DrawGradientBackground(Rect rect, Color top, Color bottom)
        {
            Texture2D gradient = new Texture2D(1, 2);
            gradient.SetPixel(0, 0, bottom);
            gradient.SetPixel(0, 1, top);
            gradient.Apply();

            GUI.DrawTexture(rect, gradient, ScaleMode.StretchToFill);
        }

        protected void DrawEditorIcon()
        {
            Rect iconRect = GUILayoutUtility.GetRect(28, 28);
            iconRect.y = (TITLE_BAR_HEIGHT - 28) * 0.5f;

            Vector2 center = iconRect.center;

            Color stateColor = new Color(0.3f, 0.7f, 1f);
            Color activeColor = new Color(1f, 0.85f, 0.3f);
            Color transitionColor = new Color(0.8f, 0.8f, 0.85f);

            float radius = 4f;

            Vector2 s1 = new Vector2(center.x, iconRect.y + 6);
            Vector2 s2 = new Vector2(iconRect.x + 6, iconRect.yMax - 6);
            Vector2 s3 = new Vector2(iconRect.xMax - 6, iconRect.yMax - 6);

            Handles.BeginGUI();

            DrawMiniArrow(s1, s2, transitionColor);
            DrawMiniArrow(s2, s3, transitionColor);
            DrawMiniArrow(s3, s1, transitionColor);

            Handles.color = stateColor;
            Handles.DrawSolidDisc(s2, Vector3.forward, radius);
            Handles.DrawSolidDisc(s3, Vector3.forward, radius);

            Handles.color = activeColor;
            Handles.DrawSolidDisc(s1, Vector3.forward, radius);

            Handles.EndGUI();
        }

        void DrawMiniArrow(Vector2 from, Vector2 to, Color color)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, from, to);

            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float arrowSize = 4f;
            Vector2 tip = to;
            Vector2 baseCenter = to - dir * arrowSize;

            Vector2 a = baseCenter + perp * (arrowSize * 0.5f);
            Vector2 b = baseCenter - perp * (arrowSize * 0.5f);

            Handles.DrawAAConvexPolygon(tip, a, b);
        }

        protected bool DrawToolbarButton(string text, GUIContent icon = null, float width = -1)
        {
            GUIStyle buttonStyle = new GUIStyle(EditorStyles.toolbarButton);
            buttonStyle.fontSize = 11;
            buttonStyle.padding = new RectOffset(10, 10, 1, 1);
            buttonStyle.normal.textColor = new Color(0.85f, 0.85f, 0.87f);
            buttonStyle.fontStyle = FontStyle.Bold;

            GUILayoutOption[] options = width > 0
                ? new[] { GUILayout.Width(width), GUILayout.Height(26) }
                : new[] { GUILayout.Height(26) };

            GUIContent content = icon != null
                ? new GUIContent("", icon.image, text)
                : new GUIContent(text);

            return GUILayout.Button(content, buttonStyle, options);
        }


        protected bool DrawToolbarToggle(string text, bool value, float width = -1, GUIContent icon = null)
        {
            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toolbarButton);
            toggleStyle.fontSize = 11;
            toggleStyle.padding = new RectOffset(10, 10, 1, 1);
            toggleStyle.fontStyle = FontStyle.Bold;

            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = value
                ? new Color(0.3f, 0.7f, 1.0f, 0.6f)
                : new Color(0, 0, 0, .25f);

            GUILayoutOption[] options = width > 0
                ? new[] { GUILayout.Width(width), GUILayout.Height(26) }
                : new[] { GUILayout.Height(26) };

            GUIContent content = icon != null
                ? new GUIContent("", icon.image, text)
                : new GUIContent(text);

            bool result = GUILayout.Toggle(value, content, toggleStyle, options);

            GUI.backgroundColor = oldBG;
            return result;
        }

        protected void DrawZoomIndicator()
        {
            GUIStyle zoomStyle = new GUIStyle(GUI.skin.label);
            zoomStyle.normal.textColor = new Color(0.65f, 0.65f, 0.67f);
            zoomStyle.fontSize = 11;
            zoomStyle.alignment = TextAnchor.MiddleRight;

            float zoomPercent = (zoom / ZOOM_MAX) * 100f;

            GUILayout.Label($"Zoom: {zoomPercent:F0}%", zoomStyle, GUILayout.Width(80));
        }
        #endregion

        #region View Transform Helpers
        protected Rect WorldToScreen(Rect r)
        {
            return new Rect(r.position * zoom + panOffset, r.size * zoom);
        }

        protected Vector2 WorldToScreen(Vector2 p)
        {
            return p * zoom + panOffset;
        }

        protected Vector2 ScreenToWorld(Vector2 p)
        {
            return (p - panOffset) / zoom;
        }
        #endregion

        #region Keyboard shortcuts / Layout / Focus
        protected void HandleKeyboardShortcuts(Event e)
        {
            if (e == null) return;
            if (focusedWindow != this) return;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Delete)
                {
                    DeleteStateOrTransition();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.D && e.modifiers == EventModifiers.Control)
                {
                    if (multiSelectedNodes.Count > 0)
                    {
                        copiedStateAssets = multiSelectedNodes.ToList();
                        DuplicateStatesInPlace(copiedStateAssets);
                    }
                    e.Use();
                }
                else if (e.keyCode == KeyCode.F)
                {
                    FocusNode();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.C)
                {
                    if (multiSelectedNodes.Count > 0 && e.modifiers == EventModifiers.Control)
                    {
                        copiedStateAssets = multiSelectedNodes.ToList();
                        e.Use();
                    }
                }
                else if (e.keyCode == KeyCode.V)
                {
                    if (copiedStateAssets != null && copiedStateAssets.Count > 0 && e.modifiers == EventModifiers.Control)
                    {
                        DuplicateStatesInPlace(copiedStateAssets);
                        e.Use();
                    }
                }
            }
        }

        protected void ApplyLayout(bool focusRoot = true)
        {
            Repaint();
        }

        void FocusNode()
        {
            if (stateMachineAsset != null && stateMachineAsset.States.Count > 0 && stateMachineAsset.InitialStateIndex < stateMachineAsset.States.Count)
            {
                StateAsset node = multiSelectedNodes.Count > 0 ? multiSelectedNodes.First() : stateMachineAsset.States[stateMachineAsset.InitialStateIndex];
                if (node != null)
                {
                    Rect canvasRect = new Rect(parameterCanvasWidth, 0, position.width - inspectorWidth - parameterCanvasWidth,
                        position.height - TOOLBAR_HEIGHT - TITLE_BAR_HEIGHT);
                    Vector2 centerScreen = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);
                    Vector2 rootCenter = node.NodeRect.position + node.NodeRect.size * 0.5f;
                    panOffset = centerScreen - rootCenter * zoom;
                    Repaint();
                }
            }
            else if (stateMachineAsset != null)
            {
                StateAsset node = stateMachineAsset.AnyStateAsset;
                Rect canvasRect = new Rect(parameterCanvasWidth, 0, position.width - inspectorWidth - parameterCanvasWidth,
                       position.height - TOOLBAR_HEIGHT - TITLE_BAR_HEIGHT);
                Vector2 centerScreen = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);
                Vector2 rootCenter = node.NodeRect.position + node.NodeRect.size * 0.5f;
                panOffset = centerScreen - rootCenter * zoom;
                Repaint();
            }
        }

        protected int GetFittedFontSize(string text, float maxWidth, GUIStyle style, int maxFontSize, int minFontSize)
        {
            var content = new GUIContent(text);

            for (int size = maxFontSize; size >= minFontSize; size--)
            {
                style.fontSize = size;

                if (style.CalcSize(content).x <= maxWidth)
                    return size;
            }

            return minFontSize;
        }

        protected string AddSpacesBeforeCapitals(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(input.Length * 2);
            sb.Append(input[0]);

            for (int i = 1; i < input.Length; i++)
            {
                char c = input[i];

                if (char.IsUpper(c) && input[i - 1] != ' ')
                    sb.Append(' ');

                sb.Append(c);
            }

            return sb.ToString();
        }
        #endregion

        #region Node drawing / selection / creation / deletion
        public void DrawAllNode()
        {
            if (stateMachineAsset == null)
            {
                EditorGUILayout.HelpBox("No StateMachine Asset assigned.", MessageType.Warning);
                return;
            }
            var selectedNodes = new List<StateAsset>();
            foreach (var state in stateMachineAsset.States)
            {
                if (state == null) continue;

                if (multiSelectedNodes.Count > 0 && multiSelectedNodes.Contains(state))
                {
                    selectedNodes.Add(state);
                    continue;
                }
                DrawNode(state);
            }
            DrawAnyStateNode();
            foreach (var state in selectedNodes)
            {
                if (state == null) continue;
                DrawNode(state);
            }


            var e = Event.current;
            if (draggingNode == null && e.type == EventType.MouseDown && e.button == 0)
            {
                isBoxSelecting = true;
                boxStart = e.mousePosition;
                boxEnd = e.mousePosition;
            }
        }

        void DrawNode(StateAsset state)
        {
            try
            {
                if (state == null) return;

                bool isSelected = multiSelectedNodes.Contains(state);
                Rect worldRect = state.NodeRect;
                Rect screenRect = WorldToScreen(worldRect);

                var stateMachine = GetSelectedStateMachine();

                if (!Application.isPlaying)
                {
                    if (StateAsset.stateRuntimeRef.Count > 0)
                        StateAsset.stateRuntimeRef = new Dictionary<State<AIController>, StateAsset>();
                    if (StateMachineAsset.stateMachineRuntimeRef.Count > 0)
                        StateMachineAsset.stateMachineRuntimeRef = new Dictionary<StateMachine<AIController>, StateMachineAsset>();
                }
                bool isExecuting = false;
                bool isLeafNode = false;
                if (currentAIController != null && stateMachine != null)
                {
                    var activeStateMachines = stateMachineAsset.GetActiveStateMachineChain(currentAIController);
                    isExecuting = activeStateMachines.Any(s => StateAsset.stateRuntimeRef.ContainsKey(s) && StateAsset.stateRuntimeRef[s] == state);
                    isLeafNode = !isExecuting && stateMachine.GetActiveStateChain().Any(s => StateAsset.stateRuntimeRef.ContainsKey(s) && StateAsset.stateRuntimeRef[s] == state);
                }
                if (autoLiveLink && Application.isPlaying && currentAIController != null)
                {
                    var activeSMChain = stateMachineAsset.GetActiveStateMachineChain(currentAIController);


                    var rootSM = stateMachineAssetsStack.Count > 0 ? stateMachineAssetsStack[0] : null;
                    stateMachineAssetsStack.Clear();
                    // Add root manually since it is not included in the active chain.
                    // This ensures the stack resets correctly when switching state machines at runtime.
                    stateMachineAssetsStack.Add(rootSM);

                    foreach (var s in activeSMChain)
                    {
                        if (!StateAsset.stateRuntimeRef.TryGetValue(s, out var runtime))
                            continue;

                        if (runtime is HierarchicalStateAsset h && h.ChildStateMachineAsset != null)
                        {
                            stateMachineAssetsStack.Add(h.ChildStateMachineAsset);
                        }
                    }

                    // Set active state machine
                    var activeSM =
                        stateMachineAsset.GetActiveStateMachine(currentAIController);

                    if (activeSM != null &&
                        StateMachineAsset.stateMachineRuntimeRef.TryGetValue(activeSM, out var smRuntime))
                    {
                        SetCurrentStateMachine(smRuntime);
                    }

                }


                if (isSelected)
                {
                    Rect selectedRect = new Rect(screenRect.x - 3, screenRect.y - 3, screenRect.width + 6, screenRect.height + 6);
                    EditorGUI.DrawRect(selectedRect, new Color(0.4f, 0.7f, 1f, 0.35f));
                }

                Color borderColor = isSelected ? BORDER_SELECTED : BORDER;
                float borderWidth = isSelected ? 2f : 1f;
                //if (isExecuting)
                //{
                //    Color execColor = new Color(0.173f, 0.302f, 0.486f, 1f);

                //    float pulse = 1f + Mathf.Sin(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED) * EXECUTION_PULSE_MAG;
                //    borderColor = Color.Lerp(borderColor, execColor, 0.85f) * pulse;
                //    borderWidth = 2.5f;

                //    Rect glow = new Rect(screenRect.x - 6, screenRect.y - 6, screenRect.width + 12, screenRect.height + 12);
                //    EditorGUI.DrawRect(glow, new Color(execColor.r, execColor.g, execColor.b, EXECUTION_GLOW_ALPHA * 0.6f));
                //}
                Color defaultStateColor = new Color(0.6118f, 0.3373f, 0.0588f, 1f);
                Color headerColor = stateMachineAsset.States.Count > 0 && stateMachineAsset.States.IndexOf(state) == stateMachineAsset.InitialStateIndex ? defaultStateColor : state.NodeColor;
                //if (isExecuting)
                //{
                //    Color execColor = new Color(0.173f, 0.302f, 0.486f, 1f);
                //    float pulse = 0.5f + 0.5f * (0.5f + Mathf.Sin(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED));
                //    headerColor = Color.Lerp(headerColor, execColor, 0.6f * pulse);
                //}


                headerColor = isExecuting ? new Color(0.173f, 0.302f, 0.486f, 1f) : headerColor;

                EditorGUI.DrawRect(screenRect, headerColor);

                if (isLeafNode)
                {
                    var leafColor = new Color(0.3f, 0.7f, 1f);
                    float t = Mathf.Repeat(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED, 1f);
                    var leafRect = new Rect(screenRect.x + 1, screenRect.y + screenRect.height - screenRect.height * .15f, (screenRect.width - 2) * t, screenRect.height * .15f);
                    EditorGUI.DrawRect(leafRect, leafColor);
                }

                GUIStyle titleStyle = new GUIStyle();
                titleStyle.fontStyle = FontStyle.Bold;
                titleStyle.normal.textColor = Color.white;
                titleStyle.alignment = TextAnchor.MiddleCenter;
                titleStyle.clipping = TextClipping.Clip;

                string formattedTitle = state.NodeTitle;

                int maxFont = Mathf.RoundToInt(13 * zoom);
                int minFont = Mathf.RoundToInt(9 * zoom);

                float padding = 10f;
                float availableWidth = screenRect.width - padding;

                titleStyle.fontSize = GetFittedFontSize(formattedTitle, availableWidth, titleStyle, maxFont, minFont);
                GUI.Label(screenRect, formattedTitle, titleStyle);
                HandleNodeSelectionAndDrag(state, screenRect, screenRect);

            }
            catch (Exception)
            {
            }
        }
        void SelectNode(StateAsset node, bool additive)
        {
            Undo.RegisterCompleteObjectUndo(this, "Select Node");

            Selection.activeObject = node;
            if (!additive)
                multiSelectedNodes.Clear();

            if (!multiSelectedNodes.Contains(node))
                multiSelectedNodes.Add(node);

            selectedTransition = null;
            Repaint();
        }

        void ToggleNodeSelection(StateAsset node)
        {
            if (multiSelectedNodes.Contains(node))
                multiSelectedNodes.Remove(node);
            else
                multiSelectedNodes.Add(node);

            selectedTransition = null;
            Repaint();
        }

        /// <summary>
        /// Deselecting Transitions and State nodes
        /// </summary>
        void DeselectAll()
        {
            multiSelectedNodes.Clear();
            selectedTransition = null;
            Repaint();
        }
        void HandleNodeSelectionAndDrag(StateAsset state, Rect nodeRect, Rect headerRect)
        {
            Event e = Event.current;
            bool additive = e.shift || e.control || e.command;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!headerRect.Contains(e.mousePosition))
                        break;

                    // Double-click: enter hierarchical state
                    if (e.button == 0 && e.clickCount == 2 &&
                        state is HierarchicalStateAsset h)
                    {
                        SetCurrentStateMachine(h.ChildStateMachineAsset);
                        DeselectAll();
                        e.Use();
                        return;
                    }

                    // Double-click: enter behavior view
                    if (e.button == 0 && e.clickCount == 2 &&
                        state is ExecuteBehaviorStateAsset eb)
                    {
                        EnterBehaviorView(eb);
                        DeselectAll();
                        e.Use();
                        return;
                    }

                    // Left click � selection
                    if (e.button == 0)
                    {
                        if (additive)
                        {
                            ToggleNodeSelection(state);
                        }
                        else
                        {
                            SelectNode(state, additive: multiSelectedNodes.Contains(state));
                        }

                        draggingNode = state;
                        foreach (var n in multiSelectedNodes)
                        {
                            dragOffsets[n] = e.mousePosition - WorldToScreen(n.NodePosition);
                        }

                        // Record undo ONCE at drag start
                        if (multiSelectedNodes.Count > 0)
                        {
                            foreach (var n in multiSelectedNodes)
                                Undo.RegisterCompleteObjectUndo(n, "Move Node");
                        }

                        if (connectionFromNode != null)
                            CreateTransition(connectionFromNode, state);

                        e.Use();
                        Repaint();
                    }
                    // Right click � context menu should act on clicked node
                    else if (e.button == 1)
                    {
                        if (!multiSelectedNodes.Contains(state))
                            SelectNode(state, additive: false);

                        if (state != stateMachineAsset)
                            ShowNodePropertiesPopup(state, e.mousePosition);

                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag:
                    if (draggingNode != null && e.button == 0 && !isBoxSelecting)
                    {
                        foreach (var node in multiSelectedNodes)
                        {
                            if (node == null) continue;

                            var dragOffset = dragOffsets.ContainsKey(node) ? dragOffsets[node] : Vector2.zero;
                            Vector2 baseWorldPos = ScreenToWorld(e.mousePosition - dragOffset);
                            node.SetNodePosition(baseWorldPos);
                            EditorUtility.SetDirty(node);
                        }
                        e.Use();
                        Repaint();
                    }
                    else if (isBoxSelecting)
                    {

                        if (contentRect.Contains(e.mousePosition))
                            boxEnd = e.mousePosition;
                        else
                            isBoxSelecting = false;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0)
                    {
                        draggingNode = null;
                        isBoxSelecting = false;
                        e.Use();
                    }
                    break;
            }
        }

        #region Behavior View

        void EnterBehaviorView(ExecuteBehaviorStateAsset asset)
        {
            activeBehaviorStateAsset = asset;
            resolvedBehavior = ResolveBehavior(asset);
        }

        void ExitBehaviorView()
        {
            activeBehaviorStateAsset = null;
            resolvedBehavior = null;
        }

        AIBehavior ResolveBehavior(ExecuteBehaviorStateAsset asset)
        {
            if (asset == null || !Application.isPlaying || currentAIController == null) return null;

            string keyName = asset.BehaviorKeyName;
            if (string.IsNullOrEmpty(keyName)) return null;

            if (currentAIController.Blackboard.TryGet<AIBehavior>(keyName, out var behavior))
                return behavior;

            return null;
        }

        void DrawBehaviorView()
        {
            // Re-resolve each frame to handle play mode transitions
            resolvedBehavior = ResolveBehavior(activeBehaviorStateAsset);

            if (resolvedBehavior == null || resolvedBehavior.NodeCount == 0)
            {
                GUIStyle msgStyle = new GUIStyle(GUI.skin.label);
                msgStyle.fontSize = 14;
                msgStyle.normal.textColor = new Color(1, 1, 1, 0.5f);
                msgStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(contentRect, "AI Behaviors can only be viewed at runtime.", msgStyle);
                return;
            }

            var nodes = resolvedBehavior.Nodes;
            const float NODE_W = 150f;
            const float NODE_H = 35f;
            const float SPACING = 40f;
            const float ARROW_SIZE = 8f;

            float totalWidth = nodes.Count * NODE_W + (nodes.Count - 1) * SPACING;
            float startX = contentRect.width * 0.5f - totalWidth * 0.5f + panOffset.x;
            float startY = contentRect.height * 0.5f - NODE_H * 0.5f + panOffset.y;

            // Resolve runtime node index from the current AI controller's active state chain
            int runtimeNodeIndex = -1;
            bool isRuntimeActive = false;

            if (Application.isPlaying && currentAIController != null)
            {
                var stateMachine = GetSelectedStateMachine();
                if (stateMachine != null)
                {
                    foreach (var s in stateMachine.GetActiveStateChain())
                    {
                        if (s is ExecuteBehaviorState execState
                            && StateAsset.stateRuntimeRef.TryGetValue(s, out var asset)
                            && asset == activeBehaviorStateAsset)
                        {
                            runtimeNodeIndex = execState.CurrentNodeIndex;
                            isRuntimeActive = true;
                            break;
                        }
                    }
                }
            }

            Color baseColor = activeBehaviorStateAsset.NodeColor;
            Color execColor = new Color(0.173f, 0.302f, 0.486f, 1f);
            Color leafBarColor = new Color(0.3f, 0.7f, 1f);

            GUIStyle titleStyle = new GUIStyle();
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.clipping = TextClipping.Clip;
            titleStyle.fontSize = Mathf.RoundToInt(11 * zoom);

            for (int i = 0; i < nodes.Count; i++)
            {
                float x = startX + i * (NODE_W + SPACING);
                Rect nodeRect = new Rect(x * zoom, startY * zoom, NODE_W * zoom, NODE_H * zoom);

                // Determine node color
                Color color = baseColor;
                float alpha = 1f;

                if (isRuntimeActive)
                {
                    if (i == runtimeNodeIndex)
                        color = execColor;
                    else if (i < runtimeNodeIndex)
                        alpha = 0.45f;
                }

                GUI.color = new Color(1, 1, 1, alpha);
                EditorGUI.DrawRect(nodeRect, color);
                GUI.color = Color.white;

                // Leaf node animated bar for active node
                if (isRuntimeActive && i == runtimeNodeIndex)
                {
                    float t = Mathf.Repeat(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED, 1f);
                    var barRect = new Rect(nodeRect.x + 1, nodeRect.y + nodeRect.height - nodeRect.height * 0.15f,
                        (nodeRect.width - 2) * t, nodeRect.height * 0.15f);
                    EditorGUI.DrawRect(barRect, leafBarColor);
                }

                // Node label
                string label = nodes[i].GetType().Name;
                GUI.Label(nodeRect, label, titleStyle);

                // Draw arrow to next node
                if (i < nodes.Count - 1)
                {
                    float arrowStartX = nodeRect.xMax;
                    float arrowEndX = (startX + (i + 1) * (NODE_W + SPACING)) * zoom;
                    float arrowY = nodeRect.y + nodeRect.height * 0.5f;
                    float midX = (arrowStartX + arrowEndX) * 0.5f;

                    // Line
                    Handles.color = new Color(1, 1, 1, 0.4f);
                    Handles.DrawLine(new Vector3(arrowStartX, arrowY), new Vector3(arrowEndX, arrowY));

                    // Arrowhead
                    float s = ARROW_SIZE * zoom;
                    Vector3 tip = new Vector3(midX + s * 0.5f, arrowY);
                    Vector3 top = new Vector3(midX - s * 0.5f, arrowY - s * 0.5f);
                    Vector3 bot = new Vector3(midX - s * 0.5f, arrowY + s * 0.5f);
                    Handles.DrawAAConvexPolygon(tip, top, bot);
                }
            }
        }

        #endregion

        void SetCurrentStateMachine(StateMachineAsset newStateMachineAsset, bool clearStatemachineAssetStack = false)
        {
            if (newStateMachineAsset == null) return;

            ExitBehaviorView();

            if (stateMachineAsset != newStateMachineAsset)
                DeselectAll();

            stateMachineAsset = newStateMachineAsset;
            if (clearStatemachineAssetStack) 
            {
                stateMachineAssetsStack.Clear();
                rootStateMachineAsset = stateMachineAsset;
            }

            if (!stateMachineAssetsStack.Contains(stateMachineAsset))
                stateMachineAssetsStack.Add(stateMachineAsset);

            // FocusNode();
        }

        void HandleConnectionRelease()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseUp && e.button == 0 && connectionFromNode != null)
            {
                connectionFromNode = null;
            }
        }


        void ShowNodePropertiesPopup(StateAsset state, Vector2 mousePosition)
        {
            GenericMenu menu = new GenericMenu();

            if (state.CanSetTranstionStart)
            {
                menu.AddItem(new GUIContent("Make Transition"), false, () =>
                {
                    connectionFromNode = state;
                });
            }
            if (state.CanCreateMultiple)
            {
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    copiedStateAssets = multiSelectedNodes.ToList();
                    if (!copiedStateAssets.Contains(state))
                        copiedStateAssets.Add(state);
                });
                if (copiedStateAssets != null && copiedStateAssets.Count > 0)
                {
                    menu.AddItem(new GUIContent("paste"), false, () =>
                    {
                        DuplicateStates(copiedStateAssets, mousePosition);
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("paste"));
                }
                menu.AddItem(new GUIContent("Duplicate"), false, () =>
                {
                    var selectedStateAssets = multiSelectedNodes.ToList();
                    if (!selectedStateAssets.Contains(state))
                        selectedStateAssets.Add(state);

                    DuplicateStatesInPlace(selectedStateAssets);
                });
            }


            if (state.CanDelete)
            {
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    DeleteStateOrTransition(state);
                });
            }
            if (state.CanSetAsDefault)
            {
                menu.AddItem(new GUIContent("Set As Default State"), false, () =>
                {
                    stateMachineAsset.InitialStateIndex = stateMachineAsset.States.IndexOf(state);
                });
            }
            menu.ShowAsContext();
        }

        void ShowProperties(Vector2 canvasMousePos, Vector2 windowMousePos)
        {
            GenericMenu menu = new GenericMenu();

            if (stateMachineAsset != null)
            {
                menu.AddItem(new GUIContent("Create State"), false, () =>
                {
                    ShowCreateStatePopup(canvasMousePos, windowMousePos);
                });
                menu.AddItem(new GUIContent("Create Sub-Statemachine"), false, () =>
                {
                    CreateStateNode(typeof(HierarchicalStateAsset), canvasMousePos);
                });

                if (copiedStateAssets != null && copiedStateAssets.Count > 0)
                {
                    menu.AddItem(new GUIContent("paste"), false, () =>
                    {
                        DuplicateStates(copiedStateAssets, canvasMousePos);
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("paste"));
                }
            }
            menu.ShowAsContext();
        }

        void HandlePropertiesPopup(Rect canvasRect)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseUp && e.button == 1)
            {
                Vector2 windowMousePos = e.mousePosition;
                // Convert from window space to canvas-relative space
                Vector2 canvasMousePos = windowMousePos - canvasRect.position;
                ShowProperties(canvasMousePos, windowMousePos);
            }
        }

        void DuplicateStatesInPlace(List<StateAsset> states)
        {
            if (states == null || states.Count == 0) return;
            Vector2 referencePos = states[0].NodePosition;
            // Place copies at a small offset from their original positions
            DuplicateStates(states, WorldToScreen(referencePos + new Vector2(50, 50)));
        }

        void DuplicateStates(List<StateAsset> states, Vector2 pos = default)
        {
            if (states == null || states.Count == 0) return;

            Vector2 targetWorldPos = ScreenToWorld(pos);
            StateAsset reference = states[0];
            Vector2 referencePos = reference.NodePosition;

            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Duplicate States");

            // First pass: duplicate all states and build old->new map
            var stateMap = new Dictionary<StateAsset, StateAsset>();
            foreach (var state in states)
            {
                if (state == null || !state.CanCreateMultiple) continue;

                var newState = Instantiate(state);
                Undo.RegisterCreatedObjectUndo(newState, "Duplicate States");

                newState.name = newState.NodeTitle;
                Vector2 offset = state.NodePosition - referencePos;
                newState.SetNodePosition(targetWorldPos + offset);

                if (newState is HierarchicalStateAsset newHierarchical &&
                    state is HierarchicalStateAsset sourceHierarchical &&
                    sourceHierarchical.ChildStateMachineAsset != null)
                {
                    var newChildSM = DeepCopyStateMachine(sourceHierarchical.ChildStateMachineAsset);
                    newHierarchical.ChildStateMachineAsset = newChildSM;
                }

                newState.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(newState, stateMachineAsset);
                stateMachineAsset.States.Add(newState);
                stateMap[state] = newState;
            }

            // Second pass: copy transitions where both endpoints are in the copied set
            var copiedSet = new HashSet<StateAsset>(states);
            foreach (var transition in stateMachineAsset.Transitions.ToList())
            {
                if (transition.FromState != null && transition.ToState != null &&
                    copiedSet.Contains(transition.FromState) && copiedSet.Contains(transition.ToState))
                {
                    var newTransition = Instantiate(transition);
                    newTransition.name = transition.name;
                    newTransition.FromState = stateMap[transition.FromState];
                    newTransition.ToState = stateMap[transition.ToState];

                    // Deep copy conditions
                    for (int j = 0; j < newTransition.Conditions.Count; j++)
                    {
                        if (newTransition.Conditions[j] != null)
                        {
                            var copiedCondition = Instantiate(newTransition.Conditions[j]);
                            copiedCondition.name = newTransition.Conditions[j].name;
                            AssetDatabase.AddObjectToAsset(copiedCondition, stateMachineAsset);
                            newTransition.Conditions[j] = copiedCondition;
                        }
                    }

                    newTransition.hideFlags = HideFlags.HideInHierarchy;

                    Undo.RegisterCreatedObjectUndo(newTransition, "Duplicate States");
                    AssetDatabase.AddObjectToAsset(newTransition, stateMachineAsset);
                    stateMachineAsset.Transitions.Add(newTransition);
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(stateMachineAsset);
            GetStateTransitionCollections();
            Repaint();
        }

        StateMachineAsset DeepCopyStateMachine(StateMachineAsset source)
        {
            var newSM = Instantiate(source);
            newSM.name = source.name;
            newSM.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(newSM, stateMachineAsset);

            // Map original states to their copies for remapping transitions
            var stateMap = new Dictionary<StateAsset, StateAsset>();

            // Deep copy all states (including the AnyStateAsset)
            for (int i = 0; i < newSM.States.Count; i++)
            {
                var originalState = source.States[i];
                var copiedState = Instantiate(originalState);
                copiedState.name = originalState.name;

                if (copiedState is HierarchicalStateAsset copiedHierarchical &&
                    originalState is HierarchicalStateAsset originalHierarchical &&
                    originalHierarchical.ChildStateMachineAsset != null)
                {
                    copiedHierarchical.ChildStateMachineAsset = DeepCopyStateMachine(originalHierarchical.ChildStateMachineAsset);
                }

                copiedState.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copiedState, stateMachineAsset);
                stateMap[originalState] = copiedState;
                newSM.States[i] = copiedState;
            }

            // Deep copy AnyStateAsset
            if (source.AnyStateAsset != null)
            {
                var copiedAnyState = Instantiate(source.AnyStateAsset);
                copiedAnyState.name = source.AnyStateAsset.name;
                copiedAnyState.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copiedAnyState, stateMachineAsset);
                stateMap[source.AnyStateAsset] = copiedAnyState;
                newSM.AnyStateAsset = copiedAnyState;
            }

            // Deep copy transitions with remapped state references
            DeepCopyTransitions(newSM.Transitions, source.Transitions, stateMap);
            DeepCopyTransitions(newSM.AnyStateTransitions, source.AnyStateTransitions, stateMap);

            // Deep copy transition collections
            for (int i = 0; i < newSM.stateTransitionCollections.Count; i++)
            {
                var newCollection = new StateTransitionCollection();
                foreach (var originalTransition in source.stateTransitionCollections[i].Transitions)
                {
                    // Find the already-copied transition that corresponds to this original
                    var allSourceTransitions = source.Transitions.Concat(source.AnyStateTransitions).ToList();
                    var allNewTransitions = newSM.Transitions.Concat(newSM.AnyStateTransitions).ToList();
                    int idx = allSourceTransitions.IndexOf(originalTransition);
                    if (idx >= 0 && idx < allNewTransitions.Count)
                        newCollection.Transitions.Add(allNewTransitions[idx]);
                }
                newSM.stateTransitionCollections[i] = newCollection;
            }

            return newSM;
        }

        void DeepCopyTransitions(List<StateTransitionAsset> targetList, List<StateTransitionAsset> sourceList, Dictionary<StateAsset, StateAsset> stateMap)
        {
            for (int i = 0; i < targetList.Count; i++)
            {
                var originalTransition = sourceList[i];
                var copiedTransition = Instantiate(originalTransition);
                copiedTransition.name = originalTransition.name;

                // Remap state references to the copied states
                if (copiedTransition.FromState != null && stateMap.TryGetValue(copiedTransition.FromState, out var newFrom))
                    copiedTransition.FromState = newFrom;
                if (copiedTransition.ToState != null && stateMap.TryGetValue(copiedTransition.ToState, out var newTo))
                    copiedTransition.ToState = newTo;

                // Deep copy condition sub-assets
                for (int j = 0; j < copiedTransition.Conditions.Count; j++)
                {
                    if (copiedTransition.Conditions[j] != null)
                    {
                        var copiedCondition = Instantiate(copiedTransition.Conditions[j]);
                        copiedCondition.name = copiedTransition.Conditions[j].name;
                        copiedCondition.hideFlags = HideFlags.HideInHierarchy;
                        AssetDatabase.AddObjectToAsset(copiedCondition, stateMachineAsset);
                        copiedTransition.Conditions[j] = copiedCondition;
                    }
                }

                copiedTransition.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copiedTransition, stateMachineAsset);
                targetList[i] = copiedTransition;
            }
        }

        void ShowCreateStatePopup(Vector2 canvasMousePos, Vector2 windowMousePos)
        {
            var stateOptions = TypeCache.GetTypesWithAttribute<AIStateModuleAttribute>()
                .Where(t => t.IsSubclassOf(typeof(StateAsset)) && !t.IsAbstract)
                .Select(t =>
                {
                    var attr = t.GetCustomAttribute<AIStateModuleAttribute>();
                    return (type: t, module: attr.ModuleName, name: attr.DisplayName);
                })
                .OrderBy(e => e.module).ThenBy(e => e.name)
                .ToList();

            var dropdown = new StateSearchDropdown(
                new AdvancedDropdownState(),
                stateOptions,
                (selectedType) => CreateStateNode(selectedType, canvasMousePos)
            );

            dropdown.Show(new Rect(windowMousePos, Vector2.zero));
        }
        void CreateStateNode(Type stateType, Vector2 mousePosition)
        {
            if (stateMachineAsset == null) return;

            StateAsset newNode = CreateInstance(stateType) as StateAsset;
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Create Transition");
            Undo.RegisterCreatedObjectUndo(newNode, "Create State Node");

            if (newNode is HierarchicalStateAsset hierarchicalStateAsset)
            {
                hierarchicalStateAsset.ChildStateMachineAsset = CreateInstance<StateMachineAsset>();
                hierarchicalStateAsset.ChildStateMachineAsset.hideFlags = HideFlags.HideInHierarchy;
                hierarchicalStateAsset.ChildStateMachineAsset.Name = name;
                hierarchicalStateAsset.ChildStateMachineAsset.name = "StateMachine";

                AssetDatabase.AddObjectToAsset(hierarchicalStateAsset.ChildStateMachineAsset, stateMachineAsset);
                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(hierarchicalStateAsset.ChildStateMachineAsset);
            }
            newNode.name = newNode.NodeTitle;
            newNode.SetNodePosition(ScreenToWorld(mousePosition));
            stateMachineAsset.States.Add(newNode);

            newNode.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(newNode, stateMachineAsset);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(stateMachineAsset);
            SelectNode(newNode, false);
            Repaint();
        }
        void DeleteStateOrTransition(StateAsset state = null, bool showWarning = true)
        {
            var deleteAssetTYpeString = selectedTransition != null ? "Transition" : "State";
            if (!showWarning || EditorUtility.DisplayDialog($"Delete {deleteAssetTYpeString}",
                  $"Are you sure you want to delete the {deleteAssetTYpeString}?",
                  "Delete", "Cancel"))
            {

                if (state != null)
                {
                    DeleteState(state, stateMachineAsset);
                }
                else if (selectedTransition != null)
                {
                    DeleteTransition(selectedTransition, stateMachineAsset, true, false);
                }
                if (multiSelectedNodes.Count > 0)
                {
                    for (int i = multiSelectedNodes.Count - 1; i >= 0; i--)
                    {
                        DeleteState(multiSelectedNodes[i], stateMachineAsset);
                    }
                }
            }
        }
        void DeleteState(StateAsset state, StateMachineAsset stateMachineAsset, bool forceDelete = false)
        {
            if (state == null || (!state.CanDelete && !forceDelete)) return;
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Delete State Node");
            Undo.RegisterCompleteObjectUndo(state, "Delete State Node");

            if (state is HierarchicalStateAsset hierarchical)
            {
                DeleteStateMachine(hierarchical.ChildStateMachineAsset);
            }


            if (multiSelectedNodes.Contains(state))
                multiSelectedNodes.Remove(state);
            stateMachineAsset.States.Remove(state);
            var transitions = GetTransitionsRelatedToState(state, stateMachineAsset);
            foreach (var transition in transitions)
            {
                DeleteTransition(transition, stateMachineAsset, true, false);
            }

            Undo.DestroyObjectImmediate(state);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(stateMachineAsset);
            Repaint();
        }

        void DeleteStateMachine(StateMachineAsset stateMachineAsset)
        {
            if (stateMachineAsset == null) return;

            foreach (var state in stateMachineAsset.AllStateAssets)
            {
                DeleteState(state, stateMachineAsset, true);
            }
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Delete Statemachine");
            Undo.DestroyObjectImmediate(stateMachineAsset);
            AssetDatabase.SaveAssets();
            Repaint();
        }
        void DeleteTransition(StateTransitionAsset transitionAsset, StateMachineAsset stateMachineAsset, bool deleteCollection = true, bool showWarning = true)
        {
            if (!showWarning || EditorUtility.DisplayDialog("Delete Transition",
                  $"Are you sure you want to delete the transition?",
                  "Delete", "Cancel"))
            {

                if (selectedTransition == transitionAsset)
                {
                    selectedTransition = null;
                }


                if (deleteCollection)
                {
                    var collection = stateMachineAsset.stateTransitionCollections
                        .FirstOrDefault(c => c.Transitions.Contains(transitionAsset));

                    if (collection != null)
                    {
                        // copy to avoid modifying while iterating
                        var transitions = collection.Transitions.ToList();

                        foreach (var t in transitions)
                        {
                            RemoveTransition(t);
                        }

                        stateMachineAsset.stateTransitionCollections.Remove(collection);
                    }
                }
                else
                {
                    RemoveTransition(transitionAsset);
                }

                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(stateMachineAsset);
                GetStateTransitionCollections();
                Repaint();
            }
        }
        void RemoveTransition(StateTransitionAsset transition)
        {
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Delete Transition");
            Undo.RegisterCompleteObjectUndo(transition, "Delete Transition");

            if (transition.FromState != null)
                Undo.RecordObject(transition.FromState, "Delete Transition");
            if (transition.ToState != null)
                Undo.RecordObject(transition.ToState, "Delete Transition");

            stateMachineAsset.Transitions.Remove(transition);
            stateMachineAsset.AnyStateTransitions.Remove(transition);
            foreach (var condition in transition.Conditions)
            {
                if (condition != null)
                    Undo.DestroyObjectImmediate(condition);
            }
            transition.Conditions.Clear();
            Undo.DestroyObjectImmediate(transition);
        }


        List<StateTransitionAsset> GetTransitionsRelatedToState(StateAsset state, StateMachineAsset stateMachineAsset)
        {
            List<StateTransitionAsset> transitionAssets = new List<StateTransitionAsset>();
            foreach (var transition in stateMachineAsset.AllTransitions)
            {
                if (transition.FromState == state || transition.ToState == state)
                    transitionAssets.Add(transition);
            }
            return transitionAssets;
        }

        void CreateTransition(StateAsset fromState, StateAsset toAsset)
        {
            if (fromState != toAsset && toAsset.CanSetTranstionEnd && fromState.CanSetTranstionStart)
            {
                StateTransitionAsset transition = CreateInstance<StateTransitionAsset>();

                Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Create Transition");
                Undo.RegisterCreatedObjectUndo(transition, "Create Transition");

                transition.name = $"{fromState.NodeTitle} To {toAsset.NodeTitle}";
                transition.FromState = fromState;
                transition.ToState = toAsset;

                if (fromState is AnyStateAsset)
                {
                    stateMachineAsset.AnyStateTransitions.Add(transition);
                }
                else
                {
                    stateMachineAsset.Transitions.Add(transition);
                }

                transition.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(transition, stateMachineAsset);
                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(stateMachineAsset);
                Repaint();
                GetStateTransitionCollections();
            }
            connectionFromNode = null;
        }

        void DrawConnectionDrag()
        {
            if (connectionFromNode == null)
                return;
            StateAsset state = connectionFromNode;
            Vector2 startPoint = WorldToScreen(state.NodeRect.center);
            Vector2 endPoint = Event.current.mousePosition;
            var mag = (startPoint - endPoint);
            var capPoint = endPoint + mag * .5f;
            float thickness = Mathf.Clamp(4f * zoom, 2.5f, 10f);
            Vector3[] seg = new Vector3[] { startPoint, endPoint };
            Handles.color = new Color(1, 1, 1f, .8f);
            Handles.DrawAAPolyLine(thickness, seg);
            DrawConnectionEndpoints(startPoint, capPoint, new Color(1, 1, 1f, 1));
            HandleConnectionRelease();
            if (Event.current.type == EventType.Repaint)
                Repaint();
        }
        void DrawConnectionEndpoints(Vector2 p0, Vector2 p1, Color color)
        {
            Vector2 dir = (p1 - p0).normalized;
            if (dir == Vector2.zero) dir = Vector2.up;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float arrowLen = Mathf.Clamp(10 * zoom, 6f, 30);
            float arrowWidth = Mathf.Clamp(10 * zoom, 4f, 30);

            Vector2 tip = p1;
            Vector2 baseCenter = p1 - dir * arrowLen;
            Vector2 a1 = baseCenter - perp * (arrowWidth * 0.5f);
            Vector2 a2 = baseCenter + perp * (arrowWidth * 0.5f);
            var defColor = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(new[] { (Vector3)tip, (Vector3)a1, (Vector3)a2 });
            Handles.color = defColor;
        }
        #endregion

        #region Transitions drawing / selection / Transition Inspector
        void HandleTransitionSelection(Event e)
        {
            if (e.type != EventType.MouseDown || e.button != 0)
                return;

            Vector2 mouse = e.mousePosition;

            StateTransitionAsset closest = null;
            float closestDist = float.MaxValue;

            foreach (var transition in stateMachineAsset.AllTransitions)
            {
                var offs = new Vector2(
                    transition.FromState.NodeRect.center.y > transition.ToState.NodeRect.center.y ? 7 : -7, 0);

                Vector2 start = WorldToScreen(transition.FromState.NodeRect.center - offs);
                Vector2 end = WorldToScreen(transition.ToState.NodeRect.center - offs);

                float distanceToLine = HandleUtility.DistancePointLine(mouse, start, end);

                Vector2 capPoint = Vector2.Lerp(start, end, 0.5f);
                float distanceToCap = Vector2.Distance(mouse, capPoint);

                float dist = Mathf.Min(distanceToLine, distanceToCap);
                if (dist < 15 && dist < closestDist)
                {
                    closestDist = dist;
                    closest = transition;
                }
            }

            if (closest != null)
            {
                SelectTransition(closest);
                Repaint();
                e.Use();
                return;
            }

            selectedTransition = null;
        }

        void SelectTransition(StateTransitionAsset transition)
        {
            Undo.RegisterCompleteObjectUndo(this, "Select Transition");

            Selection.activeObject = transition;

            selectedTransition = transition;
            multiSelectedNodes.Clear();
        }

        void DrawTransitions()
        {
            foreach (var transition in stateMachineAsset.stateTransitionCollections)
            {
                var offs = new Vector2(transition.Transitions.First().FromState.NodeRect.center.y > transition.Transitions.First().ToState.NodeRect.center.y ? 7 : -7,
                    transition.Transitions.First().FromState.NodeRect.center.x < transition.Transitions.First().ToState.NodeRect.center.x ? 5 : -5);
                Vector2 startPoint = WorldToScreen(transition.Transitions.First().FromState.NodeRect.center - offs);
                Vector2 endPoint = WorldToScreen(transition.Transitions.First().ToState.NodeRect.center - offs);
                Vector2 capPoint = Vector2.Lerp(startPoint, endPoint, 0.5f);
                var a = (endPoint - startPoint).normalized * Mathf.Clamp(10 * zoom, 6f, 30);
                capPoint += a * .5f;
                float thickness = Mathf.Clamp(4f * zoom, 2.5f, 10f);

                bool isSelected = transition.Transitions.Contains(selectedTransition);

                Color lineColor = isSelected
                    ? new Color(0.4196f, 0.6980f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.8f);

                Handles.color = lineColor;
                Handles.DrawAAPolyLine(thickness, startPoint, endPoint);
                DrawConnectionEndpoints(startPoint, capPoint, lineColor);

                var collection = stateMachineAsset.stateTransitionCollections.FirstOrDefault(c => c.Transitions.Contains(transition.Transitions.First()));
                if (collection != null)
                {
                    if (collection.Transitions.Count > 1)
                    {
                        capPoint += a;
                        DrawConnectionEndpoints(startPoint, capPoint, lineColor);
                        capPoint -= a * 2;
                        DrawConnectionEndpoints(startPoint, capPoint, lineColor);
                    }
                }
            }
        }

        public static void DrawSelectedtransitionInspector(Rect rect)
        {
            instance.DrawConnectionInspector(rect, instance.selectedTransition, 12);
        }

        void DrawConnectionInspector(Rect inspectorRect, StateTransitionAsset transition, float conditionRectXpadding = 0, bool drawTransitionList = true)
        {
            // Safety checks
            if (stateMachineAsset == null || transition == null)
            {
                EditorGUI.LabelField(new Rect(inspectorRect.x + 8, inspectorRect.y + 8, inspectorRect.width - 16, 20),
                    "No transition selected or state machine asset is null.");
                return;
            }

            var collection = stateMachineAsset.stateTransitionCollections
                .FirstOrDefault(c => c.Transitions.Contains(transition));

            // Basic layout constants
            const float padding = 8f;
            const float iconSize = 30f;
            const float headerHeight = 26f;
            const float rowHeight = 25;
            const float rowSpacing = 4f;
            const float rightButtonWidth = 20f;
            const float addRemoveBtnHeight = 18;
            const float maxListHeight = 220f;
            inspectorRect.x = 0;
            inspectorRect.y = 0;

            // Styles
            var titleStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            var subStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft };
            var smallRightStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

            // Top area
            Rect topRect = new Rect(inspectorRect.x + padding, inspectorRect.y + padding, inspectorRect.width - padding * 2, iconSize);
            GUIContent transitionIcon = EditorGUIUtility.IconContent("AnimatorStateTransition Icon");
            Rect iconRect = new Rect(topRect.x, topRect.y, iconSize, iconSize);
            GUI.Label(iconRect, transitionIcon);


            // Title and subtitle
            string fromTitle = transition.FromState != null ? transition.FromState.NodeTitle : "<Missing From>";
            string toTitle = transition.ToState != null ? transition.ToState.NodeTitle : "<Missing To>";
            string headerText = $"{fromTitle} -> {toTitle}";
            string collectionCountText = collection != null ? $"{collection.Transitions.Count} Transitions" : "No collection";

            Rect titleRect = new Rect(iconRect.xMax + 8f, iconRect.y, topRect.width - iconSize - 8f, 16f);
            GUI.Label(titleRect, headerText, titleStyle);
            Rect subtitleRect = new Rect(titleRect.x, titleRect.y + 16f, titleRect.width, 14f);
            GUI.Label(subtitleRect, collectionCountText, subStyle);

            // Thin separator line
            Rect lineRect = new Rect(inspectorRect.x, topRect.yMax + 8f, inspectorRect.width, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0f, 0f, 0f, 0.6f));

            inspectorScroll = GUI.BeginScrollView(
                new Rect(0, lineRect.yMax, inspectorRect.width, inspectorRect.height - padding),
                inspectorScroll,
                new Rect(0, lineRect.yMax, inspectorRect.width, inspectorContentHeight + lineRect.yMax)
            );

            // Guard if no collection
            if (collection == null)
            {
                Rect helpRect = new Rect(inspectorRect.x + padding, lineRect.yMax + 8f, inspectorRect.width - padding * 2, 40f);
                EditorGUI.HelpBox(helpRect, "This transition does not belong to any collection.", MessageType.Info);
                return;
            }

            // Calculate list area
            float listStartY = lineRect.yMax + 12f;
            Rect transitionBGRect;
            if (drawTransitionList)
            {
                float desiredListHeight = collection.Transitions.Count * (rowHeight + rowSpacing) + headerHeight + 8f;
                float listHeight = Mathf.Clamp(desiredListHeight, headerHeight + 8f, maxListHeight);
                Rect collectionRect = new Rect(inspectorRect.x + padding, listStartY, inspectorRect.width - padding * 2, listHeight);

                // Header box + inner background
                Rect collectionHeaderRect = new Rect(collectionRect.x, collectionRect.y, collectionRect.width, headerHeight);
                EditorGUI.HelpBox(collectionHeaderRect, "", MessageType.None);
                collectionHeaderRect = new Rect(collectionRect.x + padding, collectionRect.y, collectionRect.width, headerHeight);
                GUI.Label(collectionHeaderRect, "Transitions");
                Rect innerRect = new Rect(collectionRect.x, collectionHeaderRect.yMax, collectionRect.width, collectionRect.height - headerHeight);
                EditorGUI.DrawRect(innerRect, new Color(0f, 0f, 0f, 0.02f));

                transitionBGRect = new Rect(innerRect.x, collectionRect.y, innerRect.width, collection.Transitions.Count * (rowHeight) + headerHeight + padding);

                // Rows
                float rowX = innerRect.x + 1;
                float rowW = innerRect.width - 2;
                EditorGUI.HelpBox(transitionBGRect, "", MessageType.None);
                EditorGUI.HelpBox(transitionBGRect, "", MessageType.None);
                for (int i = 0; i < collection.Transitions.Count; i++)
                {
                    var t = collection.Transitions[i];
                    Rect rowRect = new Rect(rowX, innerRect.y + padding * .5f + i * (rowHeight), rowW, rowHeight);

                    bool isSelected = selectedTransition == t;
                    bool isHover = rowRect.Contains(Event.current.mousePosition);

                    // Selection / hover highlight
                    if (isSelected)
                        EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.90f, 0.85f)); // animator blue
                    else if (isHover)
                        EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.035f));

                    // Click handling (left mouse)
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowRect.Contains(Event.current.mousePosition))
                    {
                        selectedTransition = t;
                        Repaint();
                        Event.current.Use();
                    }

                    // Row label (left)
                    string rowLabel = $"{(t.FromState != null ? t.FromState.NodeTitle : "<Missing>")} -> {(t.ToState != null ? t.ToState.NodeTitle : "<Missing>")}";
                    Rect rowLabelRect = new Rect(rowRect.x + 4f, rowRect.y + 2f, rowRect.width - rightButtonWidth - 8f, rowRect.height - 4f);
                    GUI.Label(rowLabelRect, rowLabel);
                }
            }
            else
            {
                transitionBGRect = new Rect(inspectorRect.x + padding, lineRect.yMax, inspectorRect.width - padding * 2, 0f);
            }
            if (selectedTransition != null && collection.Transitions.Count > 0)
            {
                var toggleStyle = new GUIStyle(EditorStyles.toggle) { alignment = TextAnchor.UpperLeft };
                Rect transitionOnCompleteToggle = new Rect(inspectorRect.x + padding * 2, transitionBGRect.yMax + padding * 2, inspectorRect.width * .5f, 18f);
                selectedTransition.TransitionOnComplete = GUI.Toggle(transitionOnCompleteToggle, selectedTransition.TransitionOnComplete,
                    new GUIContent("Transition On Complete", "If true, then the transition will happen as soon as the from state is complete"), toggleStyle);
            }



            Rect removeBtnRect = new Rect(inspectorRect.x + inspectorRect.width - padding * 2 - rightButtonWidth, transitionBGRect.yMax, rightButtonWidth, 18f);
            if (drawTransitionList && GUI.Button(removeBtnRect, new GUIContent("-", "Remove selected transition")))
            {
                if (selectedTransition != null && collection.Transitions.Contains(selectedTransition))
                {
                    DeleteTransition(selectedTransition, stateMachineAsset, false);
                }
                else
                {
                    DeleteTransition(transition, stateMachineAsset, false);
                }
                Repaint();
            }
            // Bottom separator and remove button
            float bottomY = removeBtnRect.yMax + padding * 3;
            Rect bottomSep = new Rect(inspectorRect.x, bottomY + 6f, inspectorRect.width, 1f);
            EditorGUI.DrawRect(bottomSep, new Color(0f, 0f, 0f, 0.6f));
            var conditionHeaderBGRect = new Rect(inspectorRect.x + padding, bottomSep.yMax + padding, inspectorRect.width - padding * 2, headerHeight);
            var conditionHeaderLableRect = new Rect(inspectorRect.x + 2 * padding, bottomSep.yMax + padding, inspectorRect.width - padding * 2, headerHeight);

            if (selectedTransition != null)
            {
                EditorGUI.DrawRect(conditionHeaderBGRect, new Color(0f, 0f, 0f, 0.5f));
                GUI.Label(conditionHeaderLableRect, "Conditions");



                // Add condition button
                GUIStyle addButtonStyle = new GUIStyle(EditorStyles.iconButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    fixedWidth = 25,
                    fixedHeight = 18
                };
                var addButtonrect = new Rect(inspectorRect.x + inspectorRect.width - padding * 2 - rightButtonWidth, conditionHeaderBGRect.y + 4, rightButtonWidth, addRemoveBtnHeight);
                var plusIcon = EditorGUIUtility.IconContent("Toolbar Plus");
                if (GUI.Button(addButtonrect, new GUIContent(plusIcon.image, "Add Condition"), addButtonStyle))
                {
                    GenericMenu menu = new GenericMenu();

                    menu.AddItem(new GUIContent("Bool"), false, () =>
                    {
                        Undo.RecordObject(BlackBoardKeys, "Add Condition");

                        if (selectedTransition != null && collection.Transitions.Contains(selectedTransition))
                        {
                            var condition = CreateCondtionAsset(typeof(BlackboardBoolConditionAsset));
                            Undo.RegisterCompleteObjectUndo(selectedTransition, "Add Condition");
                            selectedTransition.Conditions.Add(condition);
                        }
                        Repaint();
                    });

                    menu.AddItem(new GUIContent("Float"), false, () =>
                    {
                        Undo.RecordObject(BlackBoardKeys, "Add Parameter");

                        if (selectedTransition != null && collection.Transitions.Contains(selectedTransition))
                        {
                            var condition = CreateCondtionAsset(typeof(BlackboardFloatConditionAsset));
                            Undo.RegisterCompleteObjectUndo(selectedTransition, "Add Condition");
                            selectedTransition.Conditions.Add(condition);
                        }
                        Repaint();
                    });

                    menu.AddItem(new GUIContent("Trigger"), false, () =>
                    {
                        Undo.RecordObject(BlackBoardKeys, "Add Condition");

                        if (selectedTransition != null && collection.Transitions.Contains(selectedTransition))
                        {
                            var condition = CreateCondtionAsset(typeof(BlackboardTriggerConditionAsset));
                            Undo.RegisterCompleteObjectUndo(selectedTransition, "Add Condition");
                            selectedTransition.Conditions.Add(condition);
                        }
                        Repaint();
                    });

                    menu.AddItem(new GUIContent("IsSet"), false, () =>
                    {
                        Undo.RecordObject(BlackBoardKeys, "Add Condition");

                        if (selectedTransition != null && collection.Transitions.Contains(selectedTransition))
                        {
                            var condition = CreateCondtionAsset(typeof(BlackboardIsSetConditionAsset));
                            Undo.RegisterCompleteObjectUndo(selectedTransition, "Add Condition");
                            selectedTransition.Conditions.Add(condition);
                        }
                        Repaint();
                    });

                    menu.ShowAsContext();
                }





                conditionRectHeight = 0;
                foreach (var c in selectedTransition.Conditions)
                {
                    var conditionsRect = new Rect(conditionHeaderBGRect.x, conditionHeaderBGRect.yMax + conditionRectHeight, conditionHeaderBGRect.width, conditionRectHeight);
                    conditionRectHeight += c.DrawInspector(conditionsRect, conditionRectXpadding) + headerHeight + 2;
                    removeBtnRect = new Rect(conditionsRect.x + conditionsRect.width - rightButtonWidth - 4.5f, conditionsRect.y + 4.5f, rightButtonWidth, addRemoveBtnHeight);
                    if (GUI.Button(removeBtnRect, new GUIContent("-", "Remove selected transition")))
                    {
                        Undo.RegisterCompleteObjectUndo(selectedTransition, "Remove Condition");

                        selectedTransition.Conditions.Remove(c);
                        DeleteConditionAsset(c);
                        break;
                    }
                }
            }
            inspectorContentHeight = conditionRectHeight + (conditionHeaderBGRect.yMax - lineRect.y) - padding + addRemoveBtnHeight;

            GUI.EndScrollView();
        }

        void GetStateTransitionCollections()
        {
            UpdateStateTransitionCollections(stateMachineAsset);
        }

        public BlackboardConditionAsset CreateCondtionAsset(Type conditionType)
        {
            BlackboardConditionAsset conditionNode = CreateInstance(conditionType) as BlackboardConditionAsset;
            if (conditionNode == null)
            {
                Debug.LogError("Failed to create Condition: " + conditionType);
                return null;
            }
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Add Condition");
            Undo.RegisterCreatedObjectUndo(conditionNode, "Add Condition");
            conditionNode.name = conditionType.Name;

            if (stateMachineAsset != null)
            {
                conditionNode.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(conditionNode, stateMachineAsset);
            }

            EditorUtility.SetDirty(stateMachineAsset);
            AssetDatabase.SaveAssets();

            return conditionNode;
        }

        void DeleteConditionAsset(BlackboardConditionAsset bTConditionAsset)
        {
            Undo.RegisterCompleteObjectUndo(stateMachineAsset, "Remove Condition");
            Undo.DestroyObjectImmediate(bTConditionAsset);
            EditorUtility.SetDirty(stateMachineAsset);
            AssetDatabase.SaveAssets();
        }

        private void SelectNodesInBox(Rect selectionRect, bool additive)
        {
            if (!additive)
                multiSelectedNodes.Clear();

            foreach (var s in stateMachineAsset.AllStateAssets)
            {
                if (s == null) continue;

                Rect nodeRect = WorldToScreen(s.NodeRect);
                if (selectionRect.Overlaps(nodeRect, true))
                {
                    if (!multiSelectedNodes.Contains(s))
                        multiSelectedNodes.Add(s);
                }
            }
        }

        private Rect GetScreenSelectionRect()
        {
            Rect r = new Rect(boxStart.x, boxStart.y, boxEnd.x - boxStart.x, boxEnd.y - boxStart.y);

            if (r.width < 0) { r.x += r.width; r.width = -r.width; }
            if (r.height < 0) { r.y += r.height; r.height = -r.height; }

            return r;
        }


        private void DrawBoxSelectionVisual()
        {
            if (isBoxSelecting)
            {
                Rect rect = GetScreenSelectionRect();
                EditorGUI.DrawRect(rect, new Color(0.3f, 0.6f, 1f, 0.15f));

                Handles.BeginGUI();
                Handles.color = new Color(0.3f, 0.6f, 1f, 0.9f);
                Handles.DrawAAPolyLine(2,
                    new Vector3(rect.xMin, rect.yMin),
                    new Vector3(rect.xMax, rect.yMin),
                    new Vector3(rect.xMax, rect.yMax),
                    new Vector3(rect.xMin, rect.yMax),
                    new Vector3(rect.xMin, rect.yMin));
                Handles.EndGUI();
            }

        }

        #endregion

        #region Utility

        public void FindSystem()
        {
            AllInstalledSystems.Clear();
            FieldInfo[] fields = typeof(FSSystemsSetup).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var field in fields)
            {
                // Check if the field's type is FSSystem
                if (field.FieldType == typeof(FSSystemInfo))
                {
                    // Get the value of the field and cast it to FSSystem
                    FSSystemInfo system = field.GetValue(this) as FSSystemInfo;

                    if (system != null)
                    {
                        AllInstalledSystems.TryAdd(field.Name, system);
                    }
                }
            }
        }

        AIController currentAIController;
        StateMachine<AIController> GetSelectedStateMachine()
        {
            if (currentAIController != null || Selection.activeGameObject != null)
            {
                if (currentAIController == null)
                    currentAIController = Selection.activeGameObject.GetComponent<AIController>();

                if (currentAIController != null && currentAIController.StateMachine != null)
                {
                    return currentAIController.StateMachine;
                }
            }
            return null;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null)
            {
                return;
            }
            var aiController = Selection.activeGameObject.GetComponent<AIController>();
            if (aiController != null)
            {
                if (aiController.StateMachineAsset != null)
                    SetCurrentStateMachine(aiController.StateMachineAsset, true);

                var stateMachine = aiController.StateMachine;
                // if (stateMachine != null)
                {
                    currentAIController = aiController;
                }
            }
        }

        #endregion
    }

    [InitializeOnLoad]
    public static class StateMachineWindowOpener
    {
        static StateMachineWindowOpener()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
        }

        private static void OnProjectItemGUI(string guid, Rect selectionRect)
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown &&
                e.clickCount == 2 &&
                selectionRect.Contains(e.mousePosition))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path); 

                if (obj is StateMachineAsset stateMachine)
                {
                    StateMachineEditorWindow.OpenWindow(stateMachine);
                    e.Use();
                }
            }
        }
    }

    public class StateSearchDropdown : AdvancedDropdown
    {
        Action<Type> onSelect;
        List<(Type type, string module, string name)> entries;

        public StateSearchDropdown(
            AdvancedDropdownState state,
            List<(Type type, string module, string name)> stateEntries,
            Action<Type> onSelect) : base(state)
        {
            this.entries = stateEntries;
            this.onSelect = onSelect;
            minimumSize = new Vector2(280, 350);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Create State");
            var groups = new Dictionary<string, AdvancedDropdownItem>();

            foreach (var entry in entries)
            {
                if (!groups.TryGetValue(entry.module, out var group))
                {
                    group = new AdvancedDropdownItem(entry.module);
                    groups[entry.module] = group;
                    root.AddChild(group);
                }
                group.AddChild(new StateItem(entry.type, entry.name));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is StateItem stateItem)
                onSelect?.Invoke(stateItem.Type);
        }

        class StateItem : AdvancedDropdownItem
        {
            public Type Type;

            public StateItem(Type type, string name) : base(name)
            {
                Type = type;
            }
        }
        [CustomEditor(typeof(StateTransitionAsset))]
        public class GlobalInspectorInjector : Editor
        {
            public override void OnInspectorGUI()
            {
                if (StateMachineEditorWindow.Instance == null)
                    return;
                Rect inspectorRect = GUILayoutUtility.GetRect(0, 0, 0, 1000);
                inspectorRect.width = EditorGUIUtility.currentViewWidth;

                StateMachineEditorWindow.DrawSelectedtransitionInspector(inspectorRect);
            }
        }

    }
}
#endif
