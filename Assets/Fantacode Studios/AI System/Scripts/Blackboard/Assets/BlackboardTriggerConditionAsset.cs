#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public class BlackboardTriggerConditionAsset : BlackboardConditionAsset
    {
        [SerializeField] BlackboardKey key = new BlackboardKey(BlackboardValueType.Bool);

        public override IBlackboardCondition Build()
        {
            return new BlackboardTriggerCondition(key.Name);
        }


        #region Editor
#if UNITY_EDITOR

        SerializedProperty _key;
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

            float lineHeight = EditorGUIUtility.singleLineHeight;
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
            }

            serializedObject.ApplyModifiedProperties();

            var height = r.y - rect.y - padding;
            helpBoxHeight = height + lineHeight + padding * 2;
            return height;
        }

        public override string ConditionTitle => "Trigger";
#endif
        #endregion
    }
}
