// Place in Assets/Editor/BlackboardKeyEditorWindow.cs

using FS_Core;
using FS_Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    public class BlackboardKeyEditorWindow : EditorWindow
    {
        const string RESOURCE_PATH = "Data/BlackboardKeyContainer"; // Resources/Data/BlackboardKeyContainer
        const string CREATE_FOLDER = "Assets/Resources/Data";
        const string CREATE_ASSET_PATH = CREATE_FOLDER + "/BlackboardKeyContainer.asset";

        // Core data
        SerializedObject serializedBoard;
        BlackBoardkeys blackBoardKeys;

        // UI state
        Vector2 listScroll;
        Vector2 detailScroll;
        int selectedIndex = -1; // actual array index in the container
        string searchQuery = "";
        BlackboardValueType filterType = (BlackboardValueType)(-1); // -1 = All

        // Split view
        float splitPosition = 0.35f; // default: LEFT panel is 35%
        bool isDraggingSplit = false;
        readonly float splitWidth = 5f;
        readonly float minLeftWidth = 200f;
        readonly float minRightWidth = 300f;

        // Styles
        GUIStyle headerStyle;
        GUIStyle smallBadge;
        GUIStyle typeBadgeText;
        bool stylesInitialized = false;

        // Serialized property names
        const string KEYS_PROP_NAME = "keys";
        const string KEYS_ID = "id";
        const string KEY_NAME = "name";
        const string KEYS_TYPE = "type";
        const string KEYS_DESCRIPTION = "description";

        [MenuItem("Tools/FS System/Blackboard Key Editor")]
        public static void Open()
        {
            var window = GetWindow<BlackboardKeyEditorWindow>("Blackboard Keys");
            window.minSize = new Vector2(900, 600);
        }

        private void OnEnable()
        {
            LoadContainer();
            InitStyles();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            LoadContainer();
            Repaint();
        }

        void LoadContainer()
        {
            blackBoardKeys = Resources.Load(RESOURCE_PATH) as BlackBoardkeys;
            if (blackBoardKeys != null)
                serializedBoard = new SerializedObject(blackBoardKeys);
            else
                serializedBoard = null;
        }

        void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            smallBadge = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            smallBadge.padding = new RectOffset(6, 6, 4, 4);

            typeBadgeText = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            // No top toolbar: user requested removal of refresh/validate/label
            if (blackBoardKeys == null || serializedBoard == null)
            {
                DrawNoContainerUI();
                return;
            }

            serializedBoard.Update();

            // Full area (no top toolbar reserved)
            float leftWidth = Mathf.Clamp(position.width * splitPosition, minLeftWidth, position.width - minRightWidth);
            Rect leftRect = new Rect(0, 0, leftWidth, position.height);
            Rect splitterRect = new Rect(leftWidth, 0, splitWidth, position.height);
            Rect rightRect = new Rect(leftWidth + splitWidth, 0, position.width - leftWidth - splitWidth, position.height);

            HandleSplitDragging(splitterRect);

            DrawLeftPanel(leftRect);
            DrawSplitter(splitterRect);
            DrawRightPanel(rightRect);

            serializedBoard.ApplyModifiedProperties();
        }

        void DrawNoContainerUI()
        {
            GUILayout.Space(16);
            EditorGUILayout.HelpBox($"Could not load BlackboardKeyContainer at Resources/{RESOURCE_PATH}.", MessageType.Warning);
            GUILayout.Space(12);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create New Container", GUILayout.Height(32)))
                {
                    CreateContainer();
                }

                if (GUILayout.Button("Locate in Project", GUILayout.Height(32)))
                {
                    var found = AssetDatabase.FindAssets("t:BlackBoardkeys");
                    if (found != null && found.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(found[0]);
                        var asset = AssetDatabase.LoadAssetAtPath<BlackBoardkeys>(path);
                        Selection.activeObject = asset;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Not found", "No BlackBoardkeys asset found in project.", "OK");
                    }
                }
            }
        }

        void CreateContainer()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            if (!AssetDatabase.IsValidFolder(CREATE_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Data");
            }

            BlackBoardkeys asset = ScriptableObject.CreateInstance<BlackBoardkeys>();
            AssetDatabase.CreateAsset(asset, CREATE_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LoadContainer();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
        }

        #region Left panel
        void DrawLeftPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            {
                // Determine visible (filtered) count
                SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
                int filteredCount = 0;
                int totalCount = 0;
                if (keysProp != null)
                {
                    totalCount = keysProp.arraySize;
                    for (int i = 0; i < keysProp.arraySize; i++)
                    {
                        var el = keysProp.GetArrayElementAtIndex(i);
                        if (ElementPassesFilter(el)) filteredCount++;
                    }
                }

                // Header (shows filtered count)
                EditorGUILayout.LabelField($"Keys ({filteredCount})", headerStyle);

                // Search + filter row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    searchQuery = EditorGUILayout.TextField(searchQuery, GUILayout.MinWidth(120));
                    if (EditorGUI.EndChangeCheck())
                    {
                        // if search changed, don't change selection automatically
                    }

                    if (GUILayout.Button("Clear", GUILayout.Width(60)))
                    {
                        searchQuery = "";
                        GUI.FocusControl(null);
                    }

                    GUILayout.FlexibleSpace();

                    EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
                    string[] names = System.Enum.GetNames(typeof(BlackboardValueType));
                    string[] options = new string[names.Length + 1];
                    options[0] = "All";
                    for (int i = 0; i < names.Length; i++) options[i + 1] = names[i];

                    int filterIndex = (int)filterType + 1; // -1 -> 0
                    filterIndex = EditorGUILayout.Popup(filterIndex, options, GUILayout.Width(80));
                    int newFilter = filterIndex - 1;
                    filterType = (BlackboardValueType)newFilter;
                }

                GUILayout.Space(6);

                // Add / Duplicate / Remove controls (Duplicate moved to left per request)
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Add", "Add new key"), GUILayout.Height(22)))
                    {
                        ShowAddMenu();
                    }

                    //GUI.enabled = selectedIndex >= 0;
                    //if (GUILayout.Button(new GUIContent("Duplicate", "Duplicate selected key"), GUILayout.Height(22)))
                    //{
                    //    DuplicateSelected();
                    //}

                    if (GUILayout.Button(new GUIContent("Remove", "Remove selected key"), GUILayout.Height(22)))
                    {
                        if (EditorUtility.DisplayDialog("Delete Confirmation", "Delete selected key?", "Delete", "Cancel"))
                            RemoveSelected();
                    }
                    GUI.enabled = true;

                    GUILayout.FlexibleSpace();
                }

                GUILayout.Space(6);

                // list area - expands and scrolls
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
                {
                    listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));

                    if (keysProp == null || keysProp.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox("No keys available. Create a new key using Add.", MessageType.Info);
                    }
                    else
                    {
                        // iterate and show items that pass filters
                        for (int i = 0; i < keysProp.arraySize; i++)
                        {
                            SerializedProperty element = keysProp.GetArrayElementAtIndex(i);
                            if (!ElementPassesFilter(element)) continue;

                            DrawKeyListItem(element, i);
                        }
                    }

                    EditorGUILayout.EndScrollView();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            GUILayout.EndArea();
        }

        void DrawKeyListItem(SerializedProperty element, int arrayIndex)
        {
            bool isSelected = selectedIndex == arrayIndex;

            Rect itemRect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));

            // Row Background
            if (Event.current.type == EventType.Repaint)
            {
                Color bg =
                    isSelected ? new Color(0.18f, 0.45f, 0.9f, 0.35f) :
                    itemRect.Contains(Event.current.mousePosition) ? new Color(0.18f, 0.45f, 0.9f, 0.12f) :
                    new Color(0, 0, 0, 0);

                EditorGUI.DrawRect(itemRect, bg);
            }

            string displayName = GetElementName(element) ?? $"Key {arrayIndex}";
            string typeName = GetElementTypeName(element) ?? "Unknown";

            // Name Rect
            Rect textRect = new Rect(
                itemRect.x + 8,
                itemRect.y + 4,
                itemRect.width - 100,
                itemRect.height - 8
            );

            // Type Badge Rect
            Rect badgeRect = new Rect(
                itemRect.xMax - 90,
                itemRect.y + 4,
                80,
                itemRect.height - 8
            );

            // Draw Name
            GUIStyle nameStyle = isSelected ? EditorStyles.whiteLabel : EditorStyles.label;
            GUI.Label(textRect, displayName, nameStyle);

            // --- TYPE BADGE BACKGROUND (subtle style) ---
            EditorGUI.DrawRect(badgeRect, new Color(0.25f, 0.25f, 0.25f, 0.6f));

            // Type text
            GUIStyle typeStyle = new GUIStyle(EditorStyles.miniLabel);
            typeStyle.alignment = TextAnchor.MiddleCenter;
            typeStyle.normal.textColor = Color.white;

            GUI.Label(badgeRect, typeName, typeStyle);

            // Click handling
            if (Event.current.type == EventType.MouseDown &&
                itemRect.Contains(Event.current.mousePosition))
            {
                selectedIndex = arrayIndex;
                GUI.FocusControl(null);
                Repaint();
                Event.current.Use();
            }
        }

        void ShowAddMenu()
        {
            GenericMenu menu = new GenericMenu();
            var values = System.Enum.GetValues(typeof(BlackboardValueType));
            foreach (BlackboardValueType v in values)
            {
                menu.AddItem(new GUIContent(v.ToString()), false, () => AddKeyWithType(v));
            }
            menu.ShowAsContext();
        }
        #endregion

        #region Right panel (details)
        void DrawRightPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            {
                if (selectedIndex < 0)
                {
                    EditorGUILayout.LabelField("Select a key to view details", headerStyle);
                    EditorGUILayout.HelpBox("Click a key on the left to inspect or edit its fields.", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    GUILayout.EndArea();
                    return;
                }

                SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
                if (keysProp == null || selectedIndex < 0 || selectedIndex >= keysProp.arraySize)
                {
                    EditorGUILayout.LabelField("Selection invalid.", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndVertical();
                    GUILayout.EndArea();
                    return;
                }

                SerializedProperty element = keysProp.GetArrayElementAtIndex(selectedIndex);

                GUILayout.Space(8);

                // -------- NAME ROW --------
                SerializedProperty nameProp = element.FindPropertyRelative(KEY_NAME);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.Width(60));

                if (nameProp != null && nameProp.propertyType == SerializedPropertyType.String)
                {
                    nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue);
                }
                else
                {
                    EditorGUILayout.LabelField("Invalid Name Field");
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                // -------- TYPE ROW (Non-editable) --------
                string typeName = GetElementTypeName(element) ?? "Unknown";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField(typeName, EditorStyles.helpBox);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);


                detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.ExpandHeight(true));

                // Description field as a textarea if present
                SerializedProperty descProp = element.FindPropertyRelative(KEYS_DESCRIPTION);
                if (descProp != null && descProp.propertyType == SerializedPropertyType.String)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Description", EditorStyles.boldLabel, GUILayout.Width(100));

                    string currentText = descProp.stringValue ?? "";

                    // Use text area style
                    GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea);
                    textAreaStyle.wordWrap = true;

                    // Calculate required height based on right panel width (rect)
                    float width = rect.width - 40f; // some padding
                    float requiredHeight = textAreaStyle.CalcHeight(
                        new GUIContent(currentText),
                        Mathf.Max(100f, width)
                    );

                    // Minimum height safeguard
                    requiredHeight = Mathf.Max(EditorGUIUtility.singleLineHeight * 3f, requiredHeight);

                    // Draw text area with dynamic height
                    descProp.stringValue = EditorGUILayout.TextArea(
                        currentText,
                        textAreaStyle,
                        GUILayout.Height(requiredHeight),
                        GUILayout.Width(rect.width - 120)
                    );
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    // no description field present — show concise note
                    EditorGUILayout.HelpBox("No description field found for this key. Add a string field named 'description' to the key element to edit it here.", MessageType.Info);
                }

                GUILayout.Space(8);

                // Draw all other fields (excluding name/type/description fields)
                DrawAllFieldsExcept(element, new string[] { KEYS_TYPE, KEY_NAME, KEYS_DESCRIPTION });

                EditorGUILayout.EndScrollView();

                GUILayout.Space(8);

                // No Save button per request (changes apply through SerializedObject)
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            GUILayout.EndArea();
        }

        void DrawAllFieldsExcept(SerializedProperty element, string[] excludeNames)
        {
            // Iterate child properties and draw them except excluded ones
            SerializedProperty iterator = element.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool entered = iterator.NextVisible(true); // go inside
            while (entered && !SerializedProperty.EqualContents(iterator, end))
            {
                bool skip = false;
                foreach (var ex in excludeNames)
                    if (iterator.name == ex) { skip = true; break; }

                if (!skip)
                    EditorGUILayout.PropertyField(iterator, true);

                entered = iterator.NextVisible(false);
            }
        }
        #endregion

        #region Utilities: add/remove/duplicate/copy
        void AddKeyWithType(BlackboardValueType type)
        {
            if (serializedBoard == null) return;
            SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
            if (keysProp == null)
            {
                Debug.LogError($"Could not find '{KEYS_PROP_NAME}' property on serialized board.");
                return;
            }

            int newIndex = keysProp.arraySize;
            keysProp.arraySize++;
            serializedBoard.ApplyModifiedProperties();

            SerializedProperty newElement = keysProp.GetArrayElementAtIndex(newIndex);

            // set name if possible
            SerializedProperty nameProp = newElement.FindPropertyRelative(KEY_NAME);
            if (nameProp != null)
            {
                string baseName = $"New {type}";
                string finalName = baseName;
                int suffix = 1;

                while (NameExists(finalName))
                {
                    finalName = $"{baseName}_{suffix}";
                    suffix++;
                }

                nameProp.stringValue = finalName;

            }

            // set type if possible
            SerializedProperty typeProp = newElement.FindPropertyRelative(KEYS_TYPE);
            if (typeProp != null)
            {
                if (typeProp.propertyType == SerializedPropertyType.Enum)
                    typeProp.enumValueIndex = (int)type;
                else if (typeProp.propertyType == SerializedPropertyType.Integer)
                    typeProp.intValue = (int)type;
                else if (typeProp.propertyType == SerializedPropertyType.String)
                    typeProp.stringValue = type.ToString();
            }
            SerializedProperty idProp = newElement.FindPropertyRelative(KEYS_ID);
            idProp.stringValue = Guid.NewGuid().ToString();
            serializedBoard.ApplyModifiedProperties();
            EditorUtility.SetDirty(blackBoardKeys);
            AssetDatabase.SaveAssets();

            selectedIndex = newIndex;
        }
        void RemoveSelected()
        {
            if (serializedBoard == null) return;
            SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
            if (keysProp == null || selectedIndex < 0 || selectedIndex >= keysProp.arraySize) return;

            keysProp.DeleteArrayElementAtIndex(selectedIndex);
            serializedBoard.ApplyModifiedProperties();
            EditorUtility.SetDirty(blackBoardKeys);
            AssetDatabase.SaveAssets();

            // adjust selection to nearest valid
            int newSel = selectedIndex;
            if (newSel >= keysProp.arraySize) newSel = keysProp.arraySize - 1;
            selectedIndex = newSel;
        }

        void DuplicateSelected()
        {
            SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
            if (keysProp == null || selectedIndex < 0 || selectedIndex >= keysProp.arraySize) return;

            int copyIndex = keysProp.arraySize;
            keysProp.arraySize++;
            serializedBoard.ApplyModifiedProperties();

            SerializedProperty src = keysProp.GetArrayElementAtIndex(selectedIndex);
            SerializedProperty dst = keysProp.GetArrayElementAtIndex(copyIndex);

            CopySerialized(src, dst);

            // try to make name unique
            SerializedProperty nameProp = dst.FindPropertyRelative(KEY_NAME);
            if (nameProp != null)
            {
                string baseName = nameProp.stringValue;
                int suffix = 1;
                while (NameExists(baseName + "_" + suffix)) suffix++;
                nameProp.stringValue = baseName + "_" + suffix;
            }

            serializedBoard.ApplyModifiedProperties();
            EditorUtility.SetDirty(blackBoardKeys);
            AssetDatabase.SaveAssets();
            selectedIndex = copyIndex;
        }

        void CopySerialized(SerializedProperty src, SerializedProperty dst)
        {
            var iteratorSrc = src.Copy();
            var iteratorEnd = iteratorSrc.GetEndProperty();
            SerializedProperty itDst = dst.Copy();

            iteratorSrc.NextVisible(true);
            itDst.NextVisible(true);

            while (!SerializedProperty.EqualContents(iteratorSrc, iteratorEnd))
            {
                var target = dst.FindPropertyRelative(iteratorSrc.name);
                if (target != null)
                {
                    AssignValue(iteratorSrc, target);
                }

                if (!iteratorSrc.NextVisible(false)) break;
                itDst.NextVisible(false);
            }
        }

        void AssignValue(SerializedProperty src, SerializedProperty dst)
        {
            switch (src.propertyType)
            {
                case SerializedPropertyType.Integer: dst.intValue = src.intValue; break;
                case SerializedPropertyType.Boolean: dst.boolValue = src.boolValue; break;
                case SerializedPropertyType.Float: dst.floatValue = src.floatValue; break;
                case SerializedPropertyType.String: dst.stringValue = src.stringValue; break;
                case SerializedPropertyType.Color: dst.colorValue = src.colorValue; break;
                case SerializedPropertyType.ObjectReference: dst.objectReferenceValue = src.objectReferenceValue; break;
                case SerializedPropertyType.LayerMask: dst.intValue = src.intValue; break;
                case SerializedPropertyType.Enum: dst.enumValueIndex = src.enumValueIndex; break;
                case SerializedPropertyType.Vector2: dst.vector2Value = src.vector2Value; break;
                case SerializedPropertyType.Vector3: dst.vector3Value = src.vector3Value; break;
                case SerializedPropertyType.Vector4: dst.vector4Value = src.vector4Value; break;
                case SerializedPropertyType.Rect: dst.rectValue = src.rectValue; break;
                case SerializedPropertyType.AnimationCurve: dst.animationCurveValue = src.animationCurveValue; break;
                case SerializedPropertyType.Bounds: dst.boundsValue = src.boundsValue; break;
                default:
                    break;
            }
        }

        bool NameExists(string name)
        {
            SerializedProperty keysProp = serializedBoard.FindProperty(KEYS_PROP_NAME);
            if (keysProp == null) return false;

            for (int i = 0; i < keysProp.arraySize; i++)
            {
                var el = keysProp.GetArrayElementAtIndex(i);
                string n = GetElementName(el);
                if (n == name) return true;
            }
            return false;
        }
        #endregion

        #region Helpers: introspection & filters
        bool ElementPassesFilter(SerializedProperty element)
        {
            if (!string.IsNullOrEmpty(searchQuery))
            {
                string name = GetElementName(element) ?? "";
                if (!name.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant()))
                    return false;
            }

            if ((int)filterType >= 0)
            {
                string elementTypeName = GetElementTypeName(element);
                if (string.IsNullOrEmpty(elementTypeName))
                    return false;

                if (!elementTypeName.Equals(filterType.ToString()))
                    return false;
            }

            return true;
        }

        string GetElementName(SerializedProperty element)
        {
            var p = element.FindPropertyRelative(KEY_NAME);
            if (p != null && p.propertyType == SerializedPropertyType.String)
                return p.stringValue;

            var fallback = element.FindPropertyRelative("id") ?? element.FindPropertyRelative("key");
            if (fallback != null && fallback.propertyType == SerializedPropertyType.String)
                return fallback.stringValue;

            return null;
        }

        string GetElementTypeName(SerializedProperty element)
        {
            var p = element.FindPropertyRelative(KEYS_TYPE);
            if (p != null)
            {
                if (p.propertyType == SerializedPropertyType.Enum)
                    return p.enumNames[p.enumValueIndex];
                else if (p.propertyType == SerializedPropertyType.Integer)
                {
                    var vals = System.Enum.GetNames(typeof(BlackboardValueType));
                    int idx = p.intValue;
                    if (idx >= 0 && idx < vals.Length) return vals[idx];
                    return p.intValue.ToString();
                }
                else if (p.propertyType == SerializedPropertyType.String)
                {
                    return p.stringValue;
                }
            }
            return "Unknown";
        }
        #endregion

        #region Splitter handling
        void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.12f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.SplitResizeLeftRight);
        }

        void HandleSplitDragging(Rect splitterRect)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition)) { isDraggingSplit = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (isDraggingSplit)
                    {
                        float mouseX = Mathf.Clamp(e.mousePosition.x, minLeftWidth, position.width - minRightWidth);
                        splitPosition = mouseX / position.width;
                        Repaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (isDraggingSplit) { isDraggingSplit = false; e.Use(); }
                    break;
            }
        }
        #endregion
    }
}
