#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public class BlackboardBoolConditionAsset : BlackboardConditionAsset
    {
        [SerializeField] BlackboardKey key = new BlackboardKey(BlackboardValueType.Bool);
        [SerializeField] bool expectedValue;

        public override IBlackboardCondition Build()
        {
            return new BlackboardBoolCondition(key.Name, expectedValue);
        }

        
        #region Editor
#if UNITY_EDITOR

        SerializedProperty _key;
        SerializedProperty _expectedValue;
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
            if (_expectedValue == null)
                _expectedValue = serializedObject.FindProperty("expectedValue");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 4f;
            float padding = 6f;

            Rect helpBoxRect = new Rect(rect.x, rect.y, rect.width, helpBoxHeight);
            EditorGUI.HelpBox(helpBoxRect, "", MessageType.None);
            helpBoxRect = new Rect(rect.x, rect.y, rect.width, 30);
            if (FoldOut)
                EditorGUI.HelpBox(helpBoxRect, "", MessageType.None);

            var r = new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2, lineHeight);
            if (FoldOut)
            {
                r.y = rect.y + lineHeight + padding * 3;
                EditorGUI.PropertyField(r, _key);
                r.y += lineHeight + spacing;
                EditorGUI.PropertyField(r, _expectedValue);
            }

            serializedObject.ApplyModifiedProperties();

            var height = r.y - rect.y - padding;
            helpBoxHeight = height + lineHeight + padding * 2;
            return height;
        }

        public override string ConditionTitle => "Bool";
#endif
        #endregion
    }
}
