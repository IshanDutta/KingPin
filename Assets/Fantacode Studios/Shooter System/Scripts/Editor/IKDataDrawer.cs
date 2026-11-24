using UnityEngine;
using UnityEditor;

namespace FS_ShooterSystem
{
    //[CustomPropertyDrawer(typeof(IKData))]
    public class IKDataDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float Spacing = 2f;
        private const float GroupSpacing = 6f;
        private const float Indent = 15f;
        private const float ResetButtonWidth = 20f;

        private static readonly Color borderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color groupBGColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);
        private static readonly Color sectionBGColor = new Color(0.15f, 0.15f, 0.15f, 0.2f);

        private static GUIStyle resetButtonStyle;
        private static GUIContent resetContent;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            InitializeStyles();
            EditorGUI.BeginProperty(position, label, property);

            // Main foldout
            var foldoutRect = new Rect(position.x, position.y, position.width, LineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, property.displayName);

            if (property.isExpanded)
            {
                float yPos = position.y + LineHeight + Spacing;

                var weaponHoldingByRightHand = property.FindPropertyRelative("weaponHoldingByRightHand");

                // Right Hand
                yPos = DrawIKGroup(position, yPos, "Right Hand", !weaponHoldingByRightHand.boolValue,
                    property.FindPropertyRelative("rightHandIkPosition"),
                    property.FindPropertyRelative("rightHandIkRotation"));

                //// Right Elbow
                //var showRightElbowIkData = property.FindPropertyRelative("showRightElbowIkData");

                //if (showRightElbowIkData.boolValue)
                //{
                //    yPos = DrawIKGroup(position, yPos, "Right Elbow",
                //        property.FindPropertyRelative("rightElbowIkPosition"));
                //}

                // Left Hand

                var weaponHoldingByLeftHand = property.FindPropertyRelative("weaponHoldingByLeftHand");
                yPos = DrawIKGroup(position, yPos, "Left Hand", !weaponHoldingByLeftHand.boolValue,
                    property.FindPropertyRelative("leftHandIkPosition"),
                    property.FindPropertyRelative("leftHandIkRotation"));

                //// Left Elbow
                //var showLeftElbowIkData = property.FindPropertyRelative("showLeftElbowIkData");
                //if (showLeftElbowIkData.boolValue)
                //{
                //    yPos = DrawIKGroup(position, yPos, "Left Elbow",
                //        property.FindPropertyRelative("leftElbowIkPosition"));
                //}
            }

            EditorGUI.EndProperty();
        }

        private void InitializeStyles()
        {
            if (resetButtonStyle == null)
            {
                resetButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 10,
                    fixedWidth = ResetButtonWidth,
                    fixedHeight = LineHeight - 2,
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }

            if (resetContent == null)
            {
                resetContent = new GUIContent("R", "Reset to zero");
            }
        }

        private float DrawIKGroup(Rect position, float yPos, string groupName, bool showRotationProp,
            SerializedProperty positionProp, SerializedProperty rotationProp = null)
        {
            float groupStartY = yPos;

            // Group label
            var groupRect = new Rect(position.x + Indent, yPos, position.width - Indent, LineHeight);
            EditorGUI.LabelField(groupRect, groupName, EditorStyles.boldLabel);
            yPos += LineHeight + Spacing;

            // Position
            if (positionProp != null)
            {
                yPos = DrawVector3Sliders(position, yPos, "Position", positionProp, false);
            }

            // Rotation
            if (showRotationProp && rotationProp != null)
            {
                yPos = DrawVector3Sliders(position, yPos, "Rotation", rotationProp, true);
            }

            // Draw group border and background
            float groupHeight = yPos - groupStartY;
            var groupBorderRect = new Rect(position.x + Indent, groupStartY, position.width - Indent, groupHeight);
            EditorGUI.DrawRect(groupBorderRect, groupBGColor);
            DrawBorder(groupBorderRect);

            return yPos + GroupSpacing;
        }

        private float DrawVector3Sliders(Rect position, float yPos, string label, SerializedProperty vectorProp, bool isRotation)
        {
            float sectionStartY = yPos;

            // Section label
            var labelRect = new Rect(position.x + Indent * 2, yPos, position.width - Indent * 2, LineHeight);
            EditorGUI.LabelField(labelRect, label);
            yPos += LineHeight + Spacing;

            // X, Y, Z sliders
            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                var axisProp = vectorProp.FindPropertyRelative(axes[i].ToLower());

                // Calculate widths for slider and reset button
                float availableWidth = position.width - Indent * 3 - ResetButtonWidth - 4f; // 4f for spacing
                var sliderRect = new Rect(position.x + Indent * 3, yPos, availableWidth, LineHeight);
                var resetRect = new Rect(position.x + Indent * 3 + availableWidth + 2f, yPos + 1, ResetButtonWidth, LineHeight - 2);

                float minValue = isRotation ? 0f : -1f;
                float maxValue = isRotation ? 1f : 1f;
                //float displayValue = axisProp.floatValue;
                float displayMin = isRotation ? 0f : -1f;
                float displayMax = isRotation ? 360f : 1f;

                //EditorGUI.BeginChangeCheck();
                axisProp.floatValue = EditorGUI.Slider(sliderRect, axes[i], axisProp.floatValue, displayMin, displayMax);
                //if (EditorGUI.EndChangeCheck())
                //{
                //     = displayValue;
                //}

                // Reset button
                if (GUI.Button(resetRect, resetContent, resetButtonStyle))
                {
                    axisProp.floatValue = 0f;
                }

                yPos += LineHeight + Spacing;
            }

            // Draw section border and background
            float sectionHeight = yPos - sectionStartY;
            var sectionBorderRect = new Rect(position.x + Indent * 2, sectionStartY, position.width - Indent * 2, sectionHeight);
            EditorGUI.DrawRect(sectionBorderRect, sectionBGColor);
            DrawBorder(sectionBorderRect);

            return yPos;
        }

        private void DrawBorder(Rect rect)
        {
            // Top border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), borderColor);
            // Bottom border
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), borderColor);
            // Left border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), borderColor);
            // Right border
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), borderColor);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return LineHeight;

            float height = LineHeight + Spacing; // Main foldout

            // Right Hand (Position + Rotation)
            height += LineHeight + Spacing; // Group label
            height += LineHeight + Spacing; // Position label
            height += (LineHeight + Spacing) * 3; // X, Y, Z sliders

            var weaponHoldingByRightHand = property.FindPropertyRelative("weaponHoldingByRightHand");
            if (!weaponHoldingByRightHand.boolValue)
            {
                height += LineHeight + Spacing; // Rotation label
                height += (LineHeight + Spacing) * 3; // X, Y, Z sliders
            }
            height += GroupSpacing;


            // Right Elbow (Position only)
            //var showRightElbowIkData = property.FindPropertyRelative("showRightElbowIkData");
            //if (showRightElbowIkData.boolValue)
            //{
            //    height += LineHeight + Spacing; // Group label
            //    height += LineHeight + Spacing; // Position label
            //    height += (LineHeight + Spacing) * 3; // X, Y, Z sliders
            //    height += GroupSpacing;
            //}

            // Left Hand (Position + Rotation)
            height += LineHeight + Spacing; // Group label
            height += LineHeight + Spacing; // Position label
            height += (LineHeight + Spacing) * 3; // X, Y, Z sliders

            var weaponHoldingByLeftHand = property.FindPropertyRelative("weaponHoldingByLeftHand");
            if (!weaponHoldingByLeftHand.boolValue)
            {
                height += LineHeight + Spacing; // Rotation label
                height += (LineHeight + Spacing) * 3; // X, Y, Z sliders
            }
            height += GroupSpacing;

            // Left Elbow (Position only)
            //var showLeftElbowIkData = property.FindPropertyRelative("showLeftElbowIkData");
            //if (showLeftElbowIkData.boolValue)
            //{
            //    height += LineHeight + Spacing; // Group label
            //    height += LineHeight + Spacing; // Position label
            //    height += (LineHeight + Spacing) * 3; // X, Y, Z sliders
            //    height += GroupSpacing;
            //}

            return height;
        }
    }
}