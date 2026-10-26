#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public class BlackboardFloatConditionAsset : BlackboardConditionAsset
    {
        [SerializeField] BlackboardKey key = new BlackboardKey(BlackboardValueType.Float);
        [SerializeField] float compareValue;
        [SerializeField] FloatComparison comparison = FloatComparison.Less;
        public override IBlackboardCondition Build()
        {
            var _key = string.IsNullOrEmpty(key.Name) ? GetParameterName(key.Id) : key.Name;
            return new BlackboardFloatCondition(_key, compareValue, comparison);
        }

        #region Editor

#if UNITY_EDITOR
        public override string ConditionTitle => "Float";

        SerializedProperty _key;
        SerializedProperty _compareValue;
        SerializedProperty _comparison;
        SerializedObject serializedObject;
        float helpBoxHeight;

        public override float DrawInspector(Rect rect, float xOffset = 0)
        {
            base.DrawInspector(rect, xOffset);

            if (serializedObject == null)
                serializedObject = new SerializedObject(this);

            serializedObject.Update();

            if (_key == null)
                _key = serializedObject.FindProperty("key");
            if (_compareValue == null)
                _compareValue = serializedObject.FindProperty("compareValue");
            if (_comparison == null)
                _comparison = serializedObject.FindProperty("comparison");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 4f;
            float padding = 6f;

            Rect helpBoxRect = new Rect(rect.x, rect.y, rect.width, 30);
            if (FoldOut)
                EditorGUI.HelpBox(helpBoxRect, "", MessageType.None);
            helpBoxRect = new Rect(rect.x, rect.y, rect.width, helpBoxHeight);
            EditorGUI.HelpBox(helpBoxRect, "", MessageType.None);
            var r = new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2, lineHeight);
            if (FoldOut)
            {
                r.y = rect.y + lineHeight + padding * 3;
                EditorGUI.PropertyField(r, _key);
                r.y += lineHeight + spacing;
                EditorGUI.PropertyField(r, _compareValue);
                r.y += lineHeight + spacing;
                EditorGUI.PropertyField(r, _comparison);
            }

            serializedObject.ApplyModifiedProperties();

            var height = r.y - rect.y - padding;
            helpBoxHeight = height + lineHeight + padding * 2;
            return height;
        }
#endif
        #endregion
    }
}
