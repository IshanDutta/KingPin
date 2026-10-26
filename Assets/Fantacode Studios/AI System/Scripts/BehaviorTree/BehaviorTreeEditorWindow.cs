#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
    public enum BTExecutionState
    {
        Failure,
        Success
    }

    public enum LayoutMode
    {
        Vertical,
        Horizontal
    }

    public class BehaviorTreeEditorWindow : EditorWindow
    {
        const float GRID_SMALL = 20f;
        const float GRID_LARGE = 100f;

        const float ZOOM_MIN = 0.4f;
        const float ZOOM_MAX = 2.2f;

        const float HEADER_HEIGHT = 34f;
        const float BORDER_SIZE = 1;
        const float PORT_SIZE = 16f;
        const float INSPECTOR_WIDTH = 320f;
        const float TOOLBAR_HEIGHT = 23;
        const float TITLE_BAR_HEIGHT = 50;

        float inspectorWidth = 320f;

        const float INSPECTOR_MIN = 220f;
        const float INSPECTOR_MAX = 520f;
        const float SPLITTER_WIDTH = 4f;

        bool isResizingInspector;

        static readonly Color NODE_BG = new(0.18f, 0.18f, 0.19f);
        static readonly Color NODE_BG_SELECTED = new(0.22f, 0.25f, 0.30f);
        static readonly Color BORDER = new(0.12f, 0.12f, 0.12f);
        static readonly Color BORDER_SELECTED = new(0.4f, 0.7f, 1f);

        private readonly Color gridSmallColor = new Color(0.22f, 0.22f, 0.22f, 0.55f);
        private readonly Color gridLargeColor = new Color(0.25f, 0.25f, 0.25f, 0.85f);

        const float EXECUTION_GLOW_ALPHA = 0.45f;
        const float EXECUTION_PULSE_SPEED = 2.2f;
        const float EXECUTION_PULSE_MAG = 0.18f;
        const float CONNECTION_ANIM_SPEED = 120f;
        const float CONNECTION_ARROW_SPACING = 48f;
        const int CONNECTION_ARROW_MIN = 2;

        private bool autoLayoutEnabled = false;
        private LayoutMode autoLayoutDirection = LayoutMode.Vertical;

        private bool showShortcutHints = true;
        private bool showGrid = true;

        private BTNodeAsset rootNode;

        Vector2 panOffset;
        private float zoom = 1f;

        private BTNodeAsset draggingNode = null;
        private BTNodeAsset selectedNode = null;
        private Vector2 dragOffset;

        private Vector2 inspectorScrollPos;
        private SerializedObject selectedNodeSerializedObject;

        BTNodeAsset connectionFromNode;
        BTNodeAsset pendingParentForCreation;
        Vector2 connectionMousePos;
        bool isDraggingConnection;
        Vector2 newNodeCreatePosition;

        private List<BTNodeAsset> allNodes = new List<BTNodeAsset>();

        private bool showMinimap = true;
        private Rect minimapRect;

        private const float NODE_SPACING_X = 100;
        private const float NODE_SPACING_Y = 100f;

        // ============ UNDO/REDO SUPPORT ============
        private NodeInstanceId selectedNodeInstanceID = default;


        static BehaviorTreeEditorWindow instance;
        public static BehaviorTreeEditorWindow Instance
        {
            get
            {
                if (instance == null)
                    instance = GetWindow<BehaviorTreeEditorWindow>("Behavior Tree");
                return instance;
            }
        }

        public static void OpenWindow(BTNodeAsset root)
        {
            var w = GetWindow<BehaviorTreeEditorWindow>("Behavior Tree");
            w.minSize = new Vector2(900, 520);
            w.rootNode = root;
            w.RebuildRuntimeState();
            w.FocusNode(w.rootNode);
            instance = w;
        }

        void OnEnable()
        {
            panOffset = Vector2.zero;
            zoom = 1f;

            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;

            RebuildRuntimeState();
            CollectAllNodes();

            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= EditorUpdate;
        }

        // ============ CRITICAL FIX: Proper Undo Handler ============
        void OnUndoRedo()
        {
            // After Undo/Redo, we need to rebuild everything from the serialized state
            RebuildRuntimeState();
            Repaint();
        }

        void RebuildRuntimeState()
        {
            if (rootNode == null) return;

            // Collect all nodes from asset (this will find restored nodes too)
            CollectAllNodesFromAsset();

            // Rebuild parent links from serialized children
            RebuildParentLinks();

            // Restore selected node
            RestoreSelectedNode();

            // Refresh display
            CollectAllNodes();


            AssetDatabase.SaveAssetIfDirty(rootNode);
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        void CollectAllNodesFromAsset()
        {
            if (rootNode == null) return;

            // Get all sub-assets of the root node
            string assetPath = AssetDatabase.GetAssetPath(rootNode);
            if (string.IsNullOrEmpty(assetPath)) return;

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            allNodes.Clear();

            // Find the actual root node reference (it might have changed after Undo)
            foreach (var asset in allAssets)
            {
                if (asset is BTNodeAsset node)
                {
                    // Update root reference if needed
                    if (node.Parent == null && node != rootNode && node.name == rootNode.name)
                    {
                        rootNode = node;
                    }
                }
            }
        }

        void RebuildParentLinks()
        {
            if (rootNode == null) return;
            RebuildParentLinksRecursive(rootNode, null);
        }

        void RebuildParentLinksRecursive(BTNodeAsset node, BTNodeAsset parent)
        {
            if (node == null) return;

            node.Parent = parent;

            if (node is BTConditionDecoratorAsset decorator && decorator.Child != null)
            {
                decorator.Child.Parent = decorator;
                RebuildParentLinksRecursive(decorator.Child, decorator);
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    if (child != null)
                    {
                        RebuildParentLinksRecursive(child, node);
                    }
                }
            }
        }

        void RestoreSelectedNode()
        {
            if (selectedNodeInstanceID == default)
            {
                selectedNode = null;
                selectedNodeSerializedObject = null;
                return;
            }

            selectedNode = FindNodeByInstanceID(rootNode, selectedNodeInstanceID);

            if (selectedNode != null)
            {
                selectedNodeSerializedObject = new SerializedObject(selectedNode);
            }
            else
            {
                selectedNodeInstanceID = default;
            }
        }

        // Wraps the version-specific id accessor so call sites stay clean.
#if UNITY_6000_5_OR_NEWER
        static NodeInstanceId GetNodeId(UnityEngine.Object obj) => obj.GetEntityId();
#else
        static NodeInstanceId GetNodeId(UnityEngine.Object obj) => obj.GetInstanceID();
#endif

        BTNodeAsset FindNodeByInstanceID(BTNodeAsset node, NodeInstanceId instanceID)
        {
            if (node == null) return null;

            if (GetNodeId(node) == instanceID)
                return node;

            if (node is BTConditionDecoratorAsset decorator && decorator.Child != null)
            {
                var found = FindNodeByInstanceID(decorator.Child, instanceID);
                if (found != null) return found;
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    var found = FindNodeByInstanceID(child, instanceID);
                    if (found != null) return found;
                }
            }

            return null;
        }

        void EditorUpdate()
        {
            if (Application.isPlaying)
                Repaint();
        }

        void OnGUI()
        {
            Event e = Event.current;

            HandleKeyboardShortcuts(e);

            Rect canvasRect = new Rect(0, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - BORDER_SIZE * 2, position.width - inspectorWidth, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT);

            Rect inspectorRect = new Rect(position.width - inspectorWidth, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - BORDER_SIZE * 2, inspectorWidth, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT);

            Rect splitterRect = new Rect(position.width - inspectorWidth - SPLITTER_WIDTH * 0.5f, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT, SPLITTER_WIDTH, position.height - TITLE_BAR_HEIGHT - TOOLBAR_HEIGHT);

            HandleInspectorResize(splitterRect);
            DrawRectBorder(new Rect(0, 0, position.width, position.height), new Color(0.08f, 0.08f, 0.08f), 1f);

            DrawTitleBar();
            DrawToolbar();

            GUILayout.BeginArea(canvasRect);
            {
                GUI.BeginGroup(new Rect(0, 0, canvasRect.width, canvasRect.height));
                {
                    HandlePanZoom(e, canvasRect);

                    EditorGUI.DrawRect(new Rect(0, 0, canvasRect.width, canvasRect.height), new Color(0.12f, 0.12f, 0.12f));
                    if (showGrid)
                    {
                        DrawGrid(GRID_SMALL, gridSmallColor, canvasRect);
                        DrawGrid(GRID_LARGE, gridLargeColor, canvasRect);
                    }

                    if (rootNode != null)
                    {
                        DrawConnectionsRecursive(rootNode);

                        DrawAllNodes();
                        DrawConnectionDrag();

                        if (showMinimap)
                            DrawMinimap(canvasRect);

                        if (showShortcutHints)
                            DrawShortcutHints(canvasRect);
                    }
                }
                GUI.EndGroup();
            }
            GUILayout.EndArea();
            DrawRectBorder(canvasRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);

            DrawInspector(inspectorRect);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Rect localCanvasRect = new Rect(0, 0, canvasRect.width, canvasRect.height);
                if (localCanvasRect.Contains(e.mousePosition) && !IsOverNode(e.mousePosition))
                {
                    DeselectNode();
                    e.Use();
                }
            }

            if (Event.current.type == EventType.Repaint)
                Repaint();
        }

        #region Toolbar & Header
        void DrawTitleBar()
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

            GUILayout.Label("Behavior Tree Editor", titleStyle, GUILayout.Height(TITLE_BAR_HEIGHT));

            GUILayout.FlexibleSpace();

            if (rootNode != null)
            {
                DrawInfoBadge($"Root: {rootNode.name}");
            }
            else
            {
                DrawWarningBadge("No Root Node");
            }

            GUILayout.Space(16);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            DrawRectBorder(new Rect(0, 0, titleBarRect.width, titleBarRect.height), new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
        }

        void DrawToolbar()
        {
            Rect toolbarRect = new Rect(0, TITLE_BAR_HEIGHT - BORDER_SIZE, position.width, TOOLBAR_HEIGHT);

            EditorGUI.DrawRect(toolbarRect, new Color(0.19f, 0.19f, 0.21f));
            EditorGUI.DrawRect(new Rect(0, TITLE_BAR_HEIGHT, position.width, 1), new Color(0.25f, 0.25f, 0.27f));
            EditorGUI.DrawRect(new Rect(0, TITLE_BAR_HEIGHT + TOOLBAR_HEIGHT - 1, position.width, 1), new Color(0.12f, 0.12f, 0.14f));

            var contentRect = new Rect(toolbarRect.x, toolbarRect.y + BORDER_SIZE, toolbarRect.width, toolbarRect.height);
            GUILayout.BeginArea(contentRect);
            GUILayout.BeginHorizontal();

            GUILayout.Space(12);

            DrawToolbarSection(() =>
            {
                if (DrawToolbarButton("Focus Root", EditorGUIUtility.IconContent("d_ViewToolZoom"), 100))
                {
                    FocusNode(rootNode);
                }
            });

            DrawToolbarSeparator();

            DrawToolbarSection(() =>
            {
                GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
                labelStyle.normal.textColor = new Color(0.75f, 0.75f, 0.77f);
                labelStyle.fontSize = 11;
                labelStyle.fontStyle = FontStyle.Bold;

                GUILayout.Label("Layout : ", labelStyle, GUILayout.Width(45));

                if (DrawToolbarButton("Vertical", autoLayoutDirection == LayoutMode.Vertical))
                {
                    autoLayoutDirection = LayoutMode.Vertical;
                    ApplyLayout();
                }

                GUILayout.Space(4);

                if (DrawToolbarButton("Horizontal", autoLayoutDirection == LayoutMode.Horizontal))
                {
                    autoLayoutDirection = LayoutMode.Horizontal;
                    ApplyLayout();
                }
            });

            DrawToolbarSeparator();

            DrawToolbarSection(() =>
            {
                bool newAutoLayout = DrawToolbarToggle("Auto Layout", autoLayoutEnabled);
                if (newAutoLayout != autoLayoutEnabled)
                {
                    autoLayoutEnabled = newAutoLayout;
                    if (autoLayoutEnabled)
                        ApplyLayout();
                }

                GUILayout.Space(8);

                if (DrawToolbarToggle("Hints", showShortcutHints) != showShortcutHints)
                {
                    showShortcutHints = !showShortcutHints;
                }

                GUILayout.Space(8);

                if (DrawToolbarToggle("Grid", showGrid) != showGrid)
                {
                    showGrid = !showGrid;
                }
            });

            GUILayout.FlexibleSpace();

            DrawZoomIndicator();
            GUILayout.Space(8);

            if (DrawToolbarToggle("Minimap", showMinimap, 70) != showMinimap)
            {
                showMinimap = !showMinimap;
            }

            GUILayout.Space(12);

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            DrawRectBorder(toolbarRect, new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
        }

        void DrawGradientBackground(Rect rect, Color top, Color bottom)
        {
            Texture2D gradient = new Texture2D(1, 2);
            gradient.SetPixel(0, 0, bottom);
            gradient.SetPixel(0, 1, top);
            gradient.Apply();

            GUI.DrawTexture(rect, gradient, ScaleMode.StretchToFill);
        }

        void DrawEditorIcon()
        {
            Rect iconRect = GUILayoutUtility.GetRect(24, 24);
            iconRect.y = (TITLE_BAR_HEIGHT - 24) * 0.5f;

            Color iconColor = new Color(0.3f, 0.7f, 1.0f);

            EditorGUI.DrawRect(new Rect(iconRect.x + 10, iconRect.y + 12, 4, 10), iconColor);
            EditorGUI.DrawRect(new Rect(iconRect.x + 6, iconRect.y + 8, 12, 3), iconColor);
            EditorGUI.DrawRect(new Rect(iconRect.x + 4, iconRect.y + 4, 16, 3), iconColor);
        }

        void DrawInfoBadge(string text)
        {
            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label);
            badgeStyle.normal.textColor = new Color(0.85f, 0.85f, 0.87f);
            badgeStyle.fontSize = 11;
            badgeStyle.padding = new RectOffset(10, 10, 6, 6);

            Rect badgeRect = GUILayoutUtility.GetRect(new GUIContent(text), badgeStyle);
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
            badgeRect.y = (TITLE_BAR_HEIGHT - badgeRect.height) * 0.5f;

            EditorGUI.DrawRect(badgeRect, new Color(1.0f, 0.7f, 0.2f, 0.15f));
            DrawRectBorder(badgeRect, new Color(1.0f, 0.7f, 0.2f, 0.4f), 1);

            GUI.Label(badgeRect, text, badgeStyle);
        }

        void DrawToolbarSection(System.Action content)
        {
            GUILayout.BeginHorizontal();
            content?.Invoke();
            GUILayout.EndHorizontal();
        }

        void DrawToolbarSeparator()
        {
            GUILayout.Space(8);
            Rect sepRect = GUILayoutUtility.GetRect(1, TOOLBAR_HEIGHT);
            EditorGUI.DrawRect(sepRect, new Color(0.3f, 0.3f, 0.32f));
            GUILayout.Space(8);
        }

        bool DrawToolbarButton(string text, GUIContent icon = null, float width = -1)
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
                ? new GUIContent(text, icon.image)
                : new GUIContent(text);

            return GUILayout.Button(content, buttonStyle, options);
        }

        bool DrawToolbarButton(string text, bool isActive)
        {
            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = isActive
                ? new Color(0.3f, 0.7f, 1.0f, 0.5f)
                : new Color(0.25f, 0.25f, 0.27f);

            bool result = DrawToolbarButton(text);

            GUI.backgroundColor = oldBG;
            return result;
        }

        bool DrawToolbarToggle(string text, bool value, float width = -1)
        {
            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toolbarButton);
            toggleStyle.fontSize = 11;
            toggleStyle.padding = new RectOffset(10, 10, 1, 1);
            toggleStyle.fontStyle = FontStyle.Bold;

            Color oldBG = GUI.backgroundColor;
            GUI.backgroundColor = value
                ? new Color(0.3f, 0.7f, 1.0f, 0.6f)
                : new Color(0.25f, 0.25f, 0.27f);

            GUILayoutOption[] options = width > 0
                ? new[] { GUILayout.Width(width), GUILayout.Height(26) }
                : new[] { GUILayout.Height(26) };

            bool result = GUILayout.Toggle(value, text, toggleStyle, options);

            GUI.backgroundColor = oldBG;
            return result;
        }

        void DrawZoomIndicator()
        {
            GUIStyle zoomStyle = new GUIStyle(GUI.skin.label);
            zoomStyle.normal.textColor = new Color(0.65f, 0.65f, 0.67f);
            zoomStyle.fontSize = 11;
            zoomStyle.alignment = TextAnchor.MiddleRight;

            float zoomPercent = (zoom / ZOOM_MAX) * 100f;

            GUILayout.Label($"Zoom: {zoomPercent:F0}%", zoomStyle, GUILayout.Width(80));
        }

        #endregion

        #region Layout

        void ApplyLayout(bool focusRootNode = true)
        {
            if (rootNode == null) return;

            Undo.RegisterCompleteObjectUndo(rootNode, "Auto Layout");
            RecordAllNodesForUndo("Auto Layout");

            CollectAllNodes();

            if (autoLayoutDirection == LayoutMode.Vertical)
                ApplyAutoLayoutVertical(rootNode);
            else
                ApplyAutoLayoutHorizontal(rootNode);

            if (focusRootNode)
                FocusNode(rootNode);
        }

        void RecordAllNodesForUndo(string undoName)
        {
            if (rootNode == null) return;

            CollectAllNodes();
            foreach (var node in allNodes)
            {
                if (node != null)
                {
                    Undo.RegisterCompleteObjectUndo(node, undoName);
                }
            }
        }

        void FocusNode(BTNodeAsset node)
        {
            if (node != null)
            {
                Rect canvasRect = new Rect(0, 0, position.width - INSPECTOR_WIDTH,
                    position.height - TOOLBAR_HEIGHT - TITLE_BAR_HEIGHT);
                Vector2 centerScreen = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);
                Vector2 rootCenter = node.NodeRect.position + node.NodeRect.size * 0.5f;
                panOffset = centerScreen - rootCenter * zoom;
                Repaint();
            }
        }

        void ApplyAutoLayoutVertical(BTNodeAsset root)
        {
            if (root == null) return;
            var sizes = new Dictionary<BTNodeAsset, float>();
            ComputeSubtreeWidthVertical(root, sizes);
            float startX = 0f;
            LayoutVerticalRecursive(root, startX, 0f, sizes);
        }

        float ComputeSubtreeWidthVertical(BTNodeAsset node, Dictionary<BTNodeAsset, float> sizes)
        {
            if (node == null) return 0f;
            if (node.Children == null || node.Children.Count == 0)
            {
                float w = Mathf.Max(node.NodeRect.width, 120f);
                sizes[node] = w;
                return w;
            }

            float total = 0f;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                float cw = ComputeSubtreeWidthVertical(child, sizes);
                total += cw;
            }
            total += NODE_SPACING_X * (node.Children.Count - 1);
            sizes[node] = Mathf.Max(total, node.NodeRect.width);
            return sizes[node];
        }

        void LayoutVerticalRecursive(BTNodeAsset node, float centerX, float y, Dictionary<BTNodeAsset, float> sizes)
        {
            if (node == null) return;
            float w = sizes.ContainsKey(node) ? sizes[node] : node.NodeRect.width;
            node.SetNodePosition(new Vector2(centerX - node.NodeRect.width * 0.5f, y));
            EditorUtility.SetDirty(node);

            if (node.Children == null || node.Children.Count == 0)
                return;

            float totalWidth = 0f;
            for (int i = 0; i < node.Children.Count; i++)
                totalWidth += sizes[node.Children[i]];
            totalWidth += NODE_SPACING_X * (node.Children.Count - 1);

            float startX = centerX - totalWidth * 0.5f;
            float cur = startX;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var spacing = node.Parent != null && node.Parent is BTConditionDecoratorAsset ? 0 : NODE_SPACING_Y;
                var child = node.Children[i];
                float childW = sizes[child];
                float childCenter = cur + childW * 0.5f;
                float childY = y + spacing + node.NodeRect.height;
                LayoutVerticalRecursive(child, childCenter, childY, sizes);
                cur += childW + NODE_SPACING_X;
            }
        }

        void ApplyAutoLayoutHorizontal(BTNodeAsset root)
        {
            if (root == null) return;
            var sizes = new Dictionary<BTNodeAsset, float>();
            ComputeSubtreeHeightHorizontal(root, sizes);
            LayoutHorizontalRecursive(root, 0f, 0f, sizes);
        }

        float ComputeSubtreeHeightHorizontal(BTNodeAsset node, Dictionary<BTNodeAsset, float> sizes)
        {
            if (node == null) return 0f;
            if (node.Children == null || node.Children.Count == 0)
            {
                float h = Mathf.Max(node.NodeRect.height, 48f);
                sizes[node] = h;
                return h;
            }
            float total = 0f;
            for (int i = 0; i < node.Children.Count; i++)
            {
                total += ComputeSubtreeHeightHorizontal(node.Children[i], sizes);
            }
            total += NODE_SPACING_Y * (node.Children.Count - 1);
            sizes[node] = Mathf.Max(total, node.NodeRect.height);
            return sizes[node];
        }

        void LayoutHorizontalRecursive(BTNodeAsset node, float x, float centerY, Dictionary<BTNodeAsset, float> sizes)
        {
            if (node == null) return;
            node.SetNodePosition(new Vector2(x, centerY - node.NodeRect.height * 0.5f));
            EditorUtility.SetDirty(node);

            if (node.Children == null || node.Children.Count == 0) return;

            float totalHeight = 0f;
            for (int i = 0; i < node.Children.Count; i++)
                totalHeight += sizes[node.Children[i]];
            totalHeight += NODE_SPACING_Y * (node.Children.Count - 1);

            float startY = centerY - totalHeight * 0.5f;
            float cur = startY;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var spacing = node.Parent != null && node.Parent is BTConditionDecoratorAsset ? 0 : NODE_SPACING_X;
                var child = node.Children[i];
                float ch = sizes[child];
                float childCenterY = cur + ch * 0.5f;
                float childX = x + spacing + node.NodeRect.width;
                LayoutHorizontalRecursive(child, childX, childCenterY, sizes);
                cur += ch + NODE_SPACING_Y;
            }
        }

        #endregion

        #region Node

        void CollectAllNodes()
        {
            allNodes.Clear();
            if (rootNode != null)
                CollectNodesRecursive(rootNode);
        }

        void CollectNodesRecursive(BTNodeAsset node)
        {
            if (node == null) return;
            allNodes.Add(node);
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                    CollectNodesRecursive(child);
            }
        }

        void DrawAllNodes()
        {
            if (rootNode == null) return;

            CollectAllNodes();

            foreach (var node in allNodes)
            {
                if (node != selectedNode)
                {
                    if (node.Parent is BTConditionDecoratorAsset)
                        continue;
                    DrawNode(node);
                }
            }

            if (selectedNode != null && allNodes.Contains(selectedNode))
            {
                if (selectedNode.Parent is BTConditionDecoratorAsset)
                    DrawNode(selectedNode.Parent);
                else
                    DrawNode(selectedNode);
            }
        }

        void DrawNode(BTNodeAsset node, bool draggable = true)
        {
            if (node == null) return;

            bool isSelected = (node == selectedNode);
            Rect worldRect = node.NodeRect;
            Rect screenRect = WorldToScreen(worldRect);

            Rect header = new Rect(screenRect.x, screenRect.y, screenRect.width, HEADER_HEIGHT * zoom);
            Rect content = new Rect(screenRect.x, screenRect.y + header.height, screenRect.width, screenRect.height - header.height);

            bool isExecuting = Application.isPlaying && (node == rootNode || (node.Node != null && node.Node.IsRunning && node.Parent != null && (node.Parent == rootNode || (node.Parent.Node != null && node.Parent.Node.IsRunning == true))));

            if (isSelected)
            {
                Rect glowRect = new Rect(screenRect.x - 3, screenRect.y - 3, screenRect.width + 6, screenRect.height + 6);
                EditorGUI.DrawRect(glowRect, new Color(0.4f, 0.7f, 1f, 0.35f));
            }

            EditorGUI.DrawRect(screenRect, isSelected ? NODE_BG_SELECTED : NODE_BG);

            Color borderColor = isSelected ? BORDER_SELECTED : BORDER;
            float borderWidth = isSelected ? 2f : 1f;
            if (isExecuting)
            {
                Color execColor = new Color(1f, 0.8f, 0.2f);
                float pulse = 1f + Mathf.Sin(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED) * EXECUTION_PULSE_MAG;
                borderColor = Color.Lerp(borderColor, execColor, 0.85f) * pulse;
                borderWidth = 2.5f;

                Rect glow = new Rect(screenRect.x - 6, screenRect.y - 6, screenRect.width + 12, screenRect.height + 12);
                EditorGUI.DrawRect(glow, new Color(execColor.r, execColor.g, execColor.b, EXECUTION_GLOW_ALPHA * 0.6f));
            }
            DrawRectBorder(screenRect, borderColor, borderWidth);

            Color headerColor = node.NodeColor;
            if (isExecuting)
            {
                Color execColor = new Color(1f, 0.85f, 0.3f);
                float pulse = 0.5f + 0.5f * (0.5f + Mathf.Sin(Time.realtimeSinceStartup * EXECUTION_PULSE_SPEED));
                headerColor = Color.Lerp(headerColor, execColor, 0.6f * pulse);
            }
            EditorGUI.DrawRect(header, headerColor);

            GUIStyle titleStyle = new GUIStyle();
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.clipping = TextClipping.Clip;

            string formattedTitle = AddSpacesBeforeCapitals(node.NodeTitle);

            int maxFont = Mathf.RoundToInt(13 * zoom);
            int minFont = Mathf.RoundToInt(9 * zoom);

            float padding = 10f;
            float availableWidth = header.width - padding;

            titleStyle.fontSize = GetFittedFontSize(formattedTitle, availableWidth, titleStyle, maxFont, minFont);

            GUI.Label(header, formattedTitle, titleStyle);

            Rect inputPortRect = default;
            Rect outputPortRect = default;

            float portSize = PORT_SIZE * zoom;
            if (autoLayoutDirection == LayoutMode.Vertical)
            {
                inputPortRect = new Rect(
                    screenRect.x + (screenRect.width - portSize) * 0.5f,
                    screenRect.y - portSize * 0.5f,
                    portSize,
                    portSize
                );

                outputPortRect = new Rect(
                    screenRect.x + (screenRect.width - portSize) * 0.5f,
                    screenRect.y + screenRect.height - portSize * 0.5f,
                    portSize,
                    portSize
                );
            }
            else
            {
                inputPortRect = new Rect(
                    screenRect.x - portSize * 0.5f,
                    screenRect.y + (screenRect.height - portSize) * 0.5f,
                    portSize,
                    portSize
                );

                outputPortRect = new Rect(
                    screenRect.x + screenRect.width - portSize * 0.5f,
                    screenRect.y + (screenRect.height - portSize) * 0.5f,
                    portSize,
                    portSize
                );
            }

            Vector2 mouse = Event.current.mousePosition;
            bool hoverOutput = outputPortRect.Contains(mouse);
            bool activeOutput = isDraggingConnection && connectionFromNode == node;

            if (draggable && node.CanCreateChildNodes && ((node.Parent == null || (!(node.Parent is BTConditionDecoratorAsset) &&
                !(node is BTConditionDecoratorAsset))) || (node is BTConditionDecoratorAsset bTConditionDecoratorAsset && bTConditionDecoratorAsset.Child != null && bTConditionDecoratorAsset.Child.CanCreateChildNodes)))
            {
                DrawPortOutputSphere(outputPortRect, hoverOutput, activeOutput);

                if (node is BTConditionDecoratorAsset bt1 && bt1.Child != null && bt1.Child.CanCreateChildNodes)
                    HandleConnectionPort(bt1.Child, outputPortRect);
                else
                    HandleConnectionPort(node, outputPortRect);
            }

            HandleNodeSelectionAndDrag(node, screenRect, header, draggable);

            if (node is BTConditionDecoratorAsset bt)
            {
                DrawConditionDecoratorNodeChild(bt, content);
            }
        }

        void DrawConditionDecoratorNodeChild(BTConditionDecoratorAsset decorator, Rect rect)
        {
            var child = decorator.Child;
            if (child == null) return;
            var worldRect = child.NodeRect;
            var screenRect = WorldToScreen(worldRect);
            rect = new Rect(rect.x + (rect.width - screenRect.width) * .5f, rect.y + (rect.height - screenRect.height) * .5f, child.NodeRect.width, child.NodeRect.height);
            var newPos = ScreenToWorld(rect.position);
            child.SetNodePosition(newPos);
            DrawNode(child, false);
        }

        void DrawRectBorder(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - width, rect.y, width, rect.height), color);
        }

        void DrawPortOutputSphere(Rect r, bool hover, bool active)
        {
            Vector2 center = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
            float radius = Mathf.Max(4f * zoom, r.width * 0.35f);

            Color baseColor = new Color(0.4f, 0.85f, 1f);
            Color hoverColor = new Color(0.7f, 1f, 1f);
            Color activeColor = new Color(1f, 0.9f, 0.3f);

            Color fill = hover ? hoverColor : baseColor;
            if (active) fill = activeColor;

            Handles.BeginGUI();
            Color old = Handles.color;
            Handles.color = fill;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            Handles.color = old;
            Handles.EndGUI();
        }

        bool IsOverNode(Vector2 mousePos)
        {
            foreach (var node in allNodes)
            {
                Rect worldRect = node.NodeRect;
                Rect screenRect = WorldToScreen(worldRect);
                if (screenRect.Contains(mousePos))
                    return true;
            }
            return false;
        }

        void HandleNodeSelectionAndDrag(BTNodeAsset node, Rect nodeRect, Rect headerRect, bool drag = true)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (headerRect.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            if (!autoLayoutEnabled && drag)
                            {
                                draggingNode = node;
                                Undo.RegisterCompleteObjectUndo(node, "Move Node");
                            }

                            SelectNode(node);
                            dragOffset = e.mousePosition - nodeRect.position;
                            e.Use();
                            Repaint();
                        }
                        else if (e.button == 1 && node != rootNode)
                        {
                            e.Use();
                            Repaint();
                            ShowNodePropertiesPopup(node, e.mousePosition);
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (draggingNode == node)
                    {
                        var newPos = ScreenToWorld(e.mousePosition - dragOffset);
                        node.SetNodePosition(newPos);
                        EditorUtility.SetDirty(node);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && draggingNode == node)
                    {
                        draggingNode = null;
                        e.Use();
                    }
                    break;
            }
        }

        void SelectNode(BTNodeAsset node)
        {
            selectedNode = node;
            selectedNodeInstanceID = node != null ? GetNodeId(node) : default;
            Repaint();
        }

        void DeselectNode()
        {
            selectedNode = null;
            selectedNodeInstanceID = default;
            selectedNodeSerializedObject = null;
            Repaint();
        }

        #endregion

        #region Inspector

        void DrawInspector(Rect inspectorRect)
        {
            GUILayout.BeginArea(inspectorRect);
            EditorGUI.DrawRect(new Rect(0, 0, inspectorRect.width, inspectorRect.height), new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(new Rect(0, 0, 2, inspectorRect.height), new Color(0.25f, 0.25f, 0.26f));
            EditorGUI.DrawRect(new Rect(2, 0, 1, inspectorRect.height), new Color(0.1f, 0.1f, 0.1f));

            GUILayout.Space(15);

            if (selectedNode == null)
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
            else
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

                GUIStyle valueStyle = new GUIStyle(GUI.skin.label);
                valueStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
                valueStyle.fontStyle = FontStyle.Bold;
                valueStyle.fontSize = 15;
                valueStyle.alignment = TextAnchor.MiddleLeft;
                GUILayout.Label(selectedNode.NodeTitle, valueStyle);

                GUILayout.Space(10);
                selectedNode.DrawInspector();

                //if (selectedNode is BTConditionDecoratorAsset bTConditionDecoratorAsset)
                //{
                //    if (bTConditionDecoratorAsset.Condition is BTAndConditionAsset || bTConditionDecoratorAsset.Condition is BTOrConditionAsset)
                //    {
                //        var currentRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                //        var r = new Rect(currentRect.xMax - 40, currentRect.y, 40, 25);
                //        GUIStyle listButtonStyle = new GUIStyle(GUI.skin.button);
                //        listButtonStyle.fontStyle = FontStyle.Bold;
                //        listButtonStyle.fontSize = 10;
                //        listButtonStyle.alignment = TextAnchor.MiddleCenter;
                //        listButtonStyle.normal.textColor = Color.green;

                //        if (GUI.Button(r, new GUIContent("Bool"), listButtonStyle))
                //        {
                //            var conditionAsset = CreateCondtionAsset(typeof(BTBoolConditionAsset));
                //            bTConditionDecoratorAsset.Condition.AddCondition(conditionAsset);
                //        }
                //        r.x -= 45;
                //        if (GUI.Button(r, new GUIContent("Float"), listButtonStyle))
                //        {
                //            var conditionAsset = CreateCondtionAsset(typeof(BTFloatConditionAsset));
                //            bTConditionDecoratorAsset.Condition.AddCondition(conditionAsset);
                //        }
                //        r.x -= 45;
                //        if (GUI.Button(r, new GUIContent("AND"), listButtonStyle))
                //        {
                //            var conditionAsset = CreateCondtionAsset(typeof(BTAndConditionAsset));
                //            bTConditionDecoratorAsset.Condition.AddCondition(conditionAsset);
                //        }
                //        r.x -= 45;
                //        if (GUI.Button(r, new GUIContent("OR"), listButtonStyle))
                //        {
                //            var conditionAsset = CreateCondtionAsset(typeof(BTOrConditionAsset));
                //            bTConditionDecoratorAsset.Condition.AddCondition(conditionAsset);
                //        }
                //    }
                //}
                GUILayout.Space(10);

                Color oldBG = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.25f, 0.25f);
                GUIStyle deleteStyle = new GUIStyle(GUI.skin.button);
                deleteStyle.fontStyle = FontStyle.Bold;
                deleteStyle.fontSize = 12;

                if (selectedNode != rootNode)
                {
                    if (GUILayout.Button("Delete Node", deleteStyle, GUILayout.Height(35)))
                    {
                        DeleteNode(selectedNode);
                    }
                }
                GUI.backgroundColor = oldBG;

                GUILayout.EndVertical();
                GUILayout.Space(15);
                GUILayout.EndHorizontal();

                GUILayout.EndScrollView();
            }

            DrawRectBorder(new Rect(BORDER_SIZE, 0, inspectorRect.width - BORDER_SIZE, inspectorRect.height), new Color(0.25f, 0.65f, 1.0f, 0.8f), BORDER_SIZE);
            GUILayout.EndArea();
        }

        void HandleInspectorResize(Rect splitterRect)
        {
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition))
                    {
                        isResizingInspector = true;
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
                    break;

                case EventType.MouseUp:
                    if (isResizingInspector)
                    {
                        isResizingInspector = false;
                        e.Use();
                    }
                    break;
            }
        }

        // ============ CRITICAL FIX: Record ROOT before destroying sub-assets ============
        bool DeleteNode(BTNodeAsset node, bool showConfirmationWindow = true)
        {
            if (node == null || node == rootNode)
            {
                EditorUtility.DisplayDialog("Cannot Delete", "Cannot delete the root node.", "OK");
                return false;
            }

            if (showConfirmationWindow)
            {
                if (node.Parent != null && node.Parent is BTConditionDecoratorAsset)
                {
                    EditorUtility.DisplayDialog("Delete Not Allowed",
                        "You cannot delete a child of a Decorator node.\n\nTo remove it, delete the Decorator node instead.",
                        "OK");
                    return false;
                }

                var displayDialogue = (node.Children != null && node.Children.Count > 0)
                    ? $"Delete '{node.NodeTitle}' and all of its child nodes?"
                    : $"Delete '{node.NodeTitle}'?";

                var delete = EditorUtility.DisplayDialog("Delete Node", displayDialogue, "Delete", "Cancel");
                if (!delete) return false;
            }

            // CRITICAL: Record the ROOT asset (which contains all sub-assets)
            Undo.RegisterCompleteObjectUndo(rootNode, "Delete Node");

            // Record parent
            BTNodeAsset parent = node.Parent;
            if (parent != null)
            {
                Undo.RegisterCompleteObjectUndo(parent, "Delete Node");
                parent.RemoveChild(node);
                EditorUtility.SetDirty(parent);
            }

            // Collect all nodes to delete
            var nodesToDestroy = new List<BTNodeAsset>();
            CollectSubtreePostOrder(node, nodesToDestroy);

            // Record all nodes before destroying
            foreach (var n in nodesToDestroy)
            {
                if (n != null)
                {
                    Undo.RegisterCompleteObjectUndo(n, "Delete Node");
                }
            }

            // Destroy nodes
            foreach (var n in nodesToDestroy)
            {
                if (n == null) continue;

                // Handle decorator conditions
                if (n is BTConditionDecoratorAsset bTConditionDecoratorAsset) 
                {
                    DeleteConditionAsset(bTConditionDecoratorAsset.Condition);
                }

                n.Parent = null;
                Undo.DestroyObjectImmediate(n);
            }



            EditorUtility.SetDirty(rootNode);
            AssetDatabase.SaveAssets();

            DeselectNode();
            CollectAllNodes();
            Repaint();

            return true;
        }

        void CollectSubtreePostOrder(BTNodeAsset node, List<BTNodeAsset> outList)
        {
            if (node == null) return;
            if (node.Children != null)
            {
                var children = node.Children.ToList();
                foreach (var c in children)
                    CollectSubtreePostOrder(c, outList);
            }
            outList.Add(node);
        }

        void CollectConditionSubtreePostOrder(BlackboardConditionAsset condition, List<BlackboardConditionAsset> outList)
        {
            if (condition == null) return;
            if (condition.Conditions != null)
            {
                var children = condition.Conditions.ToList();
                foreach (var c in children)
                    CollectConditionSubtreePostOrder(c, outList);
            }
            outList.Add(condition);
        }

        #endregion

        #region Connections

        void DrawConnectionsRecursive(BTNodeAsset node)
        {
            if (node == null || node.Children == null) return;

            foreach (var child in node.Children)
            {
                if (child == null) continue;

                Vector2 startScreen, endScreen;

                BTNodeAsset n = node;
                if (node.Parent != null && node.Parent is BTConditionDecoratorAsset cd)
                {
                    n = cd;
                }
                if (autoLayoutDirection == LayoutMode.Vertical)
                {
                    startScreen = WorldToScreen(new Vector2(
                        n.NodeRect.position.x + n.NodeRect.size.x * 0.5f,
                        n.NodeRect.position.y + n.NodeRect.size.y
                    ));

                    endScreen = WorldToScreen(new Vector2(
                        child.NodeRect.position.x + child.NodeRect.size.x * 0.5f,
                        child.NodeRect.position.y
                    ));
                }
                else
                {
                    startScreen = WorldToScreen(new Vector2(
                        n.NodeRect.position.x + n.NodeRect.size.x,
                        n.NodeRect.position.y + n.NodeRect.size.y * 0.5f
                    ));

                    endScreen = WorldToScreen(new Vector2(
                        child.NodeRect.position.x,
                        child.NodeRect.position.y + child.NodeRect.size.y * 0.5f
                    ));
                }

                float thickness = Mathf.Clamp(3.5f * zoom, 1.8f, 10f);

                bool parentRunning = Application.isPlaying && ((node.Node != null && node.Node.IsRunning) || node is BehaviorTree);
                bool childRunning = Application.isPlaying && child.Node != null && child.Node.IsRunning;

                if (node.Node != null && !parentRunning && childRunning)
                    child.Node.IsRunning = false;

                if (parentRunning && childRunning)
                {
                    Color connColor = new Color(1f, 0.85f, 0.25f, 1f);
                    DrawConnectionStraightWithArrows(startScreen, endScreen, connColor, thickness, true);
                }
                //else if (childRunning)
                //{
                //    Color connColor = new Color(0.6f, 1f, 0.6f, 0.95f);
                //    DrawConnectionStraightWithArrows(startScreen, endScreen, connColor, thickness * 0.9f, false);
                //}
                else
                {
                    Handles.BeginGUI();
                    Color old = Handles.color;
                    Handles.color = new Color(0.4f, 0.7f, 0.9f, 0.7f);
                    var arrowSize = Mathf.Clamp(10f * zoom, 6f, 24f);
                    var end = endScreen - ((endScreen - startScreen).normalized * arrowSize);
                    Vector3[] seg = new Vector3[] { startScreen, end };
                    Handles.DrawAAPolyLine(thickness + 2f, seg);
                    Handles.color = new Color(0.4f, 0.7f, 0.9f, 0.9f);
                    Handles.DrawAAPolyLine(thickness, seg);
                    DrawConnectionEndpoints(startScreen, endScreen, new Color(0.4f, 0.85f, 1f));
                    Handles.color = old;
                    Handles.EndGUI();
                }

                DrawConnectionsRecursive(child);
            }
        }

        void DrawConnectionStraightWithArrows(Vector2 startScreen, Vector2 endScreen, Color color, float thickness, bool directional)
        {
            Handles.BeginGUI();
            Color old = Handles.color;

            Handles.color = Color.Lerp(color, Color.black, 0.25f);
            Handles.DrawAAPolyLine(thickness + 2f, startScreen, endScreen);

            Handles.color = color;
            Handles.DrawAAPolyLine(thickness, startScreen, endScreen);

            float totalLen = Vector2.Distance(startScreen, endScreen);
            if (totalLen <= 0.001f)
            {
                Handles.color = old;
                Handles.EndGUI();
                return;
            }

            Vector2 dir = (endScreen - startScreen).normalized;

            float spacing = Mathf.Clamp(CONNECTION_ARROW_SPACING * (1f / zoom), 24f, 120f);
            int arrowCount = Mathf.Max(CONNECTION_ARROW_MIN, Mathf.FloorToInt(totalLen / spacing));

            float offset = (Time.realtimeSinceStartup * CONNECTION_ANIM_SPEED) % spacing;

            for (int i = 0; i < arrowCount; i++)
            {
                float distAlong = i * spacing + offset;
                float t = Mathf.Clamp01(distAlong / totalLen);
                Vector2 pos = Vector2.Lerp(startScreen, endScreen, t);

                float arrowLen = Mathf.Clamp(14f * zoom, 8f, 28f);
                float arrowWidth = Mathf.Clamp(8f * zoom, 4f, 16f);

                Vector2 tip = pos + dir * (arrowLen * 0.5f);
                Vector2 perp = new Vector2(-dir.y, dir.x) * (arrowWidth * 0.5f);

                Vector3[] tri = new Vector3[3];
                tri[0] = tip;
                tri[1] = pos - perp;
                tri[2] = pos + perp;

                Handles.color = color;
                Handles.DrawAAConvexPolygon(tri);

                if (!directional)
                {
                    float tailLen = arrowLen * 0.5f;
                    Vector2 tail = pos - dir * tailLen;
                    Rect r = new Rect(tail.x - arrowWidth * 0.25f, tail.y - arrowWidth * 0.25f, arrowWidth * 0.5f, arrowWidth * 0.5f);
                    EditorGUI.DrawRect(r, color * 0.8f);
                }
            }

            Handles.color = old;
            Handles.EndGUI();
        }

        void DrawConnectionEndpoints(Vector2 p0, Vector2 p1, Color color)
        {
            Vector2 dir = (p1 - p0).normalized;
            if (dir == Vector2.zero) dir = Vector2.up;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float arrowLen = Mathf.Clamp(10f * zoom, 6f, 24f);
            float arrowWidth = Mathf.Clamp(8f * zoom, 4f, 16f);

            Vector2 tip = p1;
            Vector2 baseCenter = p1 - dir * arrowLen;
            Vector2 a1 = baseCenter - perp * (arrowWidth * 0.5f);
            Vector2 a2 = baseCenter + perp * (arrowWidth * 0.5f); 

            Handles.DrawAAConvexPolygon(new[] { (Vector3)tip, (Vector3)a1, (Vector3)a2 });
        }

        void HandleConnectionPort(BTNodeAsset node, Rect screenPortRect)
        {
            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && screenPortRect.Contains(e.mousePosition))
                    {
                        isDraggingConnection = true;
                        connectionFromNode = node;
                        GUIUtility.hotControl = controlId;
                        e.Use();
                    }
                    break;
            }
        }

        void DrawConnectionDrag()
        {
            if (!isDraggingConnection || connectionFromNode == null)
                return;

            Vector2 startScreen;
            BTNodeAsset node = connectionFromNode;
            if (connectionFromNode.Parent != null && connectionFromNode.Parent is BTConditionDecoratorAsset cd)
            {
                node = cd;
            }
            if (autoLayoutDirection == LayoutMode.Vertical)
            {
                startScreen = WorldToScreen(new Vector2(
                    node.NodeRect.position.x + node.NodeRect.size.x * 0.5f,
                    node.NodeRect.position.y + node.NodeRect.size.y
                ));
            }
            else
            {
                startScreen = WorldToScreen(new Vector2(
                    node.NodeRect.position.x + node.NodeRect.size.x,
                    node.NodeRect.position.y + node.NodeRect.size.y * 0.5f
                ));
            }

            Vector2 endScreen = Event.current.mousePosition;

            float thickness = Mathf.Clamp(4f * zoom, 2.5f, 10f);

            Vector3[] seg = new Vector3[] { startScreen, endScreen };
            Handles.DrawAAPolyLine(thickness + 2f, seg);
            Handles.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Handles.DrawAAPolyLine(thickness, seg);
            float sphereRadius = Mathf.Clamp(8 * zoom, 3f, 20);
            var c = Handles.color;
            Handles.color = new Color(0.6f, 1.0f, 0.2f, 1.0f);
            Handles.DrawSolidDisc(startScreen, Vector3.forward, sphereRadius);
            Handles.color = c;
            DrawConnectionEndpoints(startScreen, endScreen, new Color(0.4f, 0.9f, 1f, 0.6f));

            HandleConnectionRelease();

            if (Event.current.type == EventType.Repaint)
                Repaint();
        }

        void HandleConnectionRelease()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseUp && e.button == 0 && isDraggingConnection)
            {
                Vector2 mousePos = e.mousePosition;

                pendingParentForCreation = connectionFromNode;

                isDraggingConnection = false;
                connectionFromNode = null;
                GUIUtility.hotControl = 0;
                newNodeCreatePosition = ScreenToWorld(mousePos);
                ShowCreateNodePopup(mousePos);
                e.Use();
            }
        }

        void ShowCreateNodePopup(Vector2 mousePos)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Selector"), false,
                () => CreateChildNode(typeof(BTSelectorNodeAsset)));

            menu.AddItem(new GUIContent("Sequence"), false,
                () => CreateChildNode(typeof(BTSequenceNodeAsset)));

            menu.AddItem(new GUIContent("Action"), false,
                () => CreateChildNode(typeof(BTStateActionAsset)));

            menu.DropDown(new Rect(mousePos.x, mousePos.y, 1, 1));
        }

        void ShowNodePropertiesPopup(BTNodeAsset node, Vector2 mousePos)
        {
            GenericMenu menu = new GenericMenu();

            if (!(node is BTConditionDecoratorAsset) && (node.Parent != null && !(node.Parent is BTConditionDecoratorAsset)))
            {
                menu.AddItem(new GUIContent("Add Condition/Bool"), false,
                    () => SetNodeAsDecorator(node, typeof(BlackboardBoolConditionAsset)));

                menu.AddItem(new GUIContent("Add Condition/Float"), false,
                   () => SetNodeAsDecorator(node, typeof(BlackboardFloatConditionAsset)));

                menu.AddItem(new GUIContent("Add Condition/AND"), false,
                   () => SetNodeAsDecorator(node, typeof(BlackboardAndConditionAsset)));

                menu.AddItem(new GUIContent("Add Condition/OR"), false,
                   () => SetNodeAsDecorator(node, typeof(BlackboardOrConditionAsset)));
            }
            else
            {
                if (node is BTConditionDecoratorAsset)
                {
                    menu.AddItem(new GUIContent("Remove Condition"), false,
                        () => RemoveDecorator(node));
                }
                if (node.Parent != null)
                {
                    if (node.Parent is BTConditionDecoratorAsset)
                    {
                        menu.AddItem(new GUIContent("Remove Condition"), false,
                           () => RemoveDecorator(node.Parent));
                    }
                }
            }

            if (!(node is BTConditionDecoratorAsset))
            {
                if (!(node is BTSequenceNodeAsset))
                    menu.AddItem(new GUIContent("Replace Node/Sequence"), false,
                      () => ReplaceNode(node, typeof(BTSequenceNodeAsset)));
                if (!(node is BTSelectorNodeAsset))
                    menu.AddItem(new GUIContent("Replace Node/Selector"), false,
                      () => ReplaceNode(node, typeof(BTSelectorNodeAsset)));
                if (!(node is BTStateActionAsset))
                    menu.AddItem(new GUIContent("Replace Node/Action"), false,
                      () => ReplaceNode(node, typeof(BTStateActionAsset)));
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Delete"), false,
                () => DeleteNode(node));

            menu.DropDown(new Rect(mousePos.x, mousePos.y, 1, 1));
        }

        void ReplaceNode(BTNodeAsset node, Type nodeType)
        {
            Undo.RegisterCompleteObjectUndo(rootNode, "Replace Node");
            if (node.Parent != null)
            {
                Undo.RegisterCompleteObjectUndo(node.Parent, "Replace Node");
            }
            Undo.RegisterCompleteObjectUndo(node, "Replace Node");

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    if (child != null)
                    {
                        Undo.RegisterCompleteObjectUndo(child, "Replace Node");
                    }
                }
            }

            var newNode = CreateNode(nodeType);
            Undo.RegisterCreatedObjectUndo(newNode, "Replace Node");

            node.Parent.RemoveChild(node);
            node.Parent.SetChildren(newNode);
            newNode.Parent = node.Parent;

            var childrenToTransfer = node.Children.ToList();
            foreach (var child in childrenToTransfer)
            {
                newNode.SetChildren(child);
                child.Parent = newNode;
            }

            node.Children.Clear();

            EditorUtility.SetDirty(node.Parent);
            node.Parent = null;

            newNode.SetNodePosition(node.NodePosition);

            DeleteNode(node, false);
            EditorUtility.SetDirty(newNode);
            EditorUtility.SetDirty(rootNode);

            SelectNode(newNode);
            CollectAllNodes();
            Repaint();
        }

        void SetNodeAsDecorator(BTNodeAsset node, Type conditionAssetType)
        {
            Undo.RegisterCompleteObjectUndo(rootNode, "Add Condition");
            if (node.Parent != null)
            {
                Undo.RegisterCompleteObjectUndo(node.Parent, "Add Condition");
            }
            Undo.RegisterCompleteObjectUndo(node, "Add Condition");

            newNodeCreatePosition = node.NodeRect.position;
            pendingParentForCreation = node.Parent;

            var cd = CreateChildNode(typeof(BTConditionDecoratorAsset));

            if (cd is BTConditionDecoratorAsset btConditionDecoratorAsset)
            {
                var conditionAsset = CreateCondtionAsset(conditionAssetType, btConditionDecoratorAsset);
                if (conditionAsset != null)
                {
                    btConditionDecoratorAsset.SetCondition(conditionAsset);
                    //Undo.RegisterCreatedObjectUndo(conditionAsset, "Add Condition");
                }
            }

            if (cd != null)
            {
                node.Parent.RemoveChild(node);
                node.Parent = cd;
                cd.SetChildren(node);

                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(cd);
            }

            EditorUtility.SetDirty(rootNode);
            CollectAllNodes();
        }

        public BlackboardConditionAsset CreateCondtionAsset(Type conditionType, ScriptableObject conditionAddTo)
        {
            BlackboardConditionAsset conditionNode = CreateInstance(conditionType) as BlackboardConditionAsset;
            if (conditionNode == null)
            {
                Debug.LogError("Failed to create Condition: " + conditionType);
                return null;
            }
            Undo.RegisterCompleteObjectUndo(rootNode, "Add Condition");
            Undo.RegisterCompleteObjectUndo(conditionAddTo, "Add Condition");
            Undo.RegisterCreatedObjectUndo(conditionNode, "Add Condition");
            conditionNode.name = conditionType.Name;

            if (rootNode != null)
            {
                AssetDatabase.AddObjectToAsset(conditionNode, rootNode);
            }

            if (conditionAddTo is BlackboardConditionAsset conditionAsset)
            {
                conditionNode.ParentCondition = conditionAsset;
            }

            EditorUtility.SetDirty(rootNode);
            EditorUtility.SetDirty(conditionAddTo);
            AssetDatabase.SaveAssets();


          
            return conditionNode;
        }

        public void DeleteConditionAsset(BlackboardConditionAsset bTConditionAsset)
        {
            var conditionNodesToDelete = new List<BlackboardConditionAsset>();
            CollectConditionSubtreePostOrder(bTConditionAsset, conditionNodesToDelete);

            Undo.RegisterCompleteObjectUndo(rootNode, "Remove Condition");
            Undo.RegisterCompleteObjectUndo(bTConditionAsset, "Remove Conditions");
            if(bTConditionAsset.ParentCondition != null)
                Undo.RegisterCompleteObjectUndo(bTConditionAsset.ParentCondition, "Remove Conditions"); 
            foreach (var n in conditionNodesToDelete)
            {
                if (n != null)
                {
                    Undo.RegisterCompleteObjectUndo(n, "Remove Conditions");
                }
            }



            foreach (var item in conditionNodesToDelete) 
            {
                if (item != null)
                {
                    Undo.DestroyObjectImmediate(item);
                }
            }
            if(bTConditionAsset != null)
                EditorUtility.SetDirty(bTConditionAsset);
            EditorUtility.SetDirty(rootNode);
            AssetDatabase.SaveAssets();
        }

        void RemoveDecorator(BTNodeAsset node)
        {
            var delete = EditorUtility.DisplayDialog(
                "Delete Node",
                "Are you sure to remove Condition?",
                "Delete",
                "Cancel");

            if (delete)
            {
                Undo.RegisterCompleteObjectUndo(rootNode, "Remove Condition");

                var parent = node.Parent;
                if (parent != null)
                {
                    Undo.RegisterCompleteObjectUndo(parent, "Remove Condition");
                }

                Undo.RegisterCompleteObjectUndo(node, "Remove Condition");

                var childrenToMove = node.Children.ToList();
                foreach (var child in childrenToMove)
                {
                    if (child != null)
                    {
                        Undo.RegisterCompleteObjectUndo(child, "Remove Condition");
                    }
                }

                node.Parent.RemoveChild(node);

                foreach (var child in childrenToMove)
                {
                    child.Parent = parent;
                    parent.SetChildren(child);
                    node.RemoveChild(child);
                }

                EditorUtility.SetDirty(parent);
                EditorUtility.SetDirty(rootNode);

                DeleteNode(node, false);
            }
        }

        BTNodeAsset CreateNode(System.Type nodeType)
        {
            BTNodeAsset newNode = CreateInstance(nodeType) as BTNodeAsset;
            if (newNode == null)
            {
                Debug.LogError("Failed to create node of type: " + nodeType);
                return null;
            }

            newNode.name = nodeType.Name;
            newNode.SetNodePosition(newNodeCreatePosition);

            if (rootNode != null)
            {
                AssetDatabase.AddObjectToAsset(newNode, rootNode);
            }
            else
            {
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NewBTNode.asset");
                AssetDatabase.CreateAsset(newNode, path);
            }

            return newNode;
        }

        BTNodeAsset CreateChildNode(System.Type nodeType)
        {
            if (pendingParentForCreation == null)
            {
                Debug.LogWarning("No parent stored for node creation");
                return null;
            }

            BTNodeAsset parent = pendingParentForCreation;
            pendingParentForCreation = null;

            Undo.RegisterCompleteObjectUndo(rootNode, "Create Node");
            Undo.RegisterCompleteObjectUndo(parent, "Create Node");

            BTNodeAsset newNode = CreateNode(nodeType);
            Undo.RegisterCreatedObjectUndo(newNode, "Create Node");

            if (parent != null && !parent.HasMultipleChildren && parent.Children != null && !(newNode is BTConditionDecoratorAsset))
            {
                var childrenToDelete = parent.Children.ToList();
                foreach (var n in childrenToDelete)
                {
                    DeleteNode(n, false);
                }
            }

            parent.SetChildren(newNode);
            newNode.Parent = parent;

            EditorUtility.SetDirty(parent);
            EditorUtility.SetDirty(newNode);
            EditorUtility.SetDirty(rootNode);

            CollectAllNodes();
            SelectNode(newNode);

            if (autoLayoutEnabled)
                ApplyLayout(false);

            Repaint();
            return newNode;
        }

        #endregion

        #region Minimap

        void DrawMinimap(Rect canvasRect)
        {
            float minimapSize = 180f;
            minimapRect = new Rect(canvasRect.width - minimapSize - 15, canvasRect.height - minimapSize - 15,
                minimapSize, minimapSize);

            EditorGUI.DrawRect(minimapRect, new Color(0.12f, 0.12f, 0.12f, 0.85f));
            DrawRectBorder(minimapRect, new Color(0.35f, 0.35f, 0.35f), 2f);

            if (allNodes.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var node in allNodes)
            {
                minX = Mathf.Min(minX, node.NodeRect.x);
                minY = Mathf.Min(minY, node.NodeRect.y);
                maxX = Mathf.Max(maxX, node.NodeRect.x + node.NodeRect.width);
                maxY = Mathf.Max(maxY, node.NodeRect.y + node.NodeRect.height);
            }

            float worldWidth = maxX - minX;
            float worldHeight = maxY - minY;

            if (worldWidth < 1) worldWidth = 1;
            if (worldHeight < 1) worldHeight = 1;

            float scale = Mathf.Min((minimapSize - 30) / worldWidth, (minimapSize - 30) / worldHeight);

            foreach (var node in allNodes)
            {
                if (node.Children != null)
                {
                    foreach (var child in node.Children)
                    {
                        if (child == null) continue;

                        Vector2 nodePos = new Vector2(
                            minimapRect.x + 15 + (node.NodeRect.center.x - minX) * scale,
                            minimapRect.y + 15 + (node.NodeRect.center.y - minY) * scale
                        );

                        Vector2 childPos = new Vector2(
                            minimapRect.x + 15 + (child.NodeRect.center.x - minX) * scale,
                            minimapRect.y + 15 + (child.NodeRect.center.y - minY) * scale
                        );

                        Handles.BeginGUI();
                        Handles.color = new Color(0.4f, 0.7f, 0.9f, 0.5f);
                        Handles.DrawLine(nodePos, childPos);
                        Handles.EndGUI();
                    }
                }
            }

            foreach (var node in allNodes)
            {
                Vector2 localPos = new Vector2(
                    (node.NodeRect.x - minX) * scale,
                    (node.NodeRect.y - minY) * scale
                );

                Rect miniNodeRect = new Rect(
                    minimapRect.x + 15 + localPos.x,
                    minimapRect.y + 15 + localPos.y,
                    Mathf.Max(node.NodeRect.width * scale, 3f),
                    Mathf.Max(node.NodeRect.height * scale, 3f)
                );

                Color nodeColor = new Color(0.4f, 0.4f, 0.4f);

                if (node == selectedNode)
                    nodeColor = new Color(0.4f, 0.7f, 1f);

                EditorGUI.DrawRect(miniNodeRect, nodeColor);
            }
        }

        #endregion

        #region Utils

        void HandlePanZoom(Event e, Rect canvasRect)
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

        void DrawGrid(float spacing, Color color, Rect canvasRect)
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

        void DrawShortcutHints(Rect canvasRect)
        {
            float w = 240;
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
            GUILayout.Label("F : Focus selected / root", content);
            GUILayout.Label("Delete : Delete selected node", content);
            GUILayout.Label("Z : Reset Zoom", content);
            GUILayout.Label("V : Align Vertical", content);
            GUILayout.Label("H : Align Horizontal", content);
            GUILayout.Space(6);

            GUILayout.EndArea();
        }

        Rect WorldToScreen(Rect r)
        {
            return new Rect(r.position * zoom + panOffset, r.size * zoom);
        }

        Vector2 WorldToScreen(Vector2 p)
        {
            return p * zoom + panOffset;
        }

        Vector2 ScreenToWorld(Vector2 p)
        {
            return (p - panOffset) / zoom;
        }

        void HandleKeyboardShortcuts(Event e)
        {
            if (e == null) return;
            if (focusedWindow != this) return;

            if (e.type == EventType.KeyDown && e.modifiers == EventModifiers.None)
            {
                if (e.keyCode == KeyCode.Delete)
                {
                    if (selectedNode != null && selectedNode != rootNode)
                    {
                        DeleteNode(selectedNode);
                        e.Use();
                    }
                }
                else if (e.keyCode == KeyCode.F)
                {
                    if (selectedNode != null)
                        FocusNode(selectedNode);
                    else
                        FocusNode(rootNode);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.V)
                {
                    autoLayoutDirection = LayoutMode.Vertical;
                    ApplyLayout();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.H)
                {
                    autoLayoutDirection = LayoutMode.Horizontal;
                    ApplyLayout();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Z)
                {
                    zoom = 1;
                    e.Use();
                }
            }
        }

        int GetFittedFontSize(string text, float maxWidth, GUIStyle style, int maxFontSize, int minFontSize)
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

        string AddSpacesBeforeCapitals(string input)
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
    }

    [InitializeOnLoad]
    public static class BehaviorTreeWindowOpener
    {
        static BehaviorTreeWindowOpener()
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

                if (obj is BTNodeAsset treeRoot)
                {
                    BehaviorTreeEditorWindow.OpenWindow(treeRoot);
                    e.Use();
                }
            }
        }
    }
}
#endif