using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public class BlackboardAndConditionAsset : BlackboardConditionAsset
    {
        [SerializeField] List<BlackboardConditionAsset> conditions = new List<BlackboardConditionAsset>();

        public override IBlackboardCondition Build()
        {
            return new BlackboardAndCondition(conditions.Select(c => c.Build()).ToArray());
        }

        #region Editor
#if UNITY_EDITOR
        public override void AddCondition(BlackboardConditionAsset condition)
        {
            base.AddCondition(condition);
            conditions.Add(condition);
        }

        public override void RemoveCondition(BlackboardConditionAsset condition)
        {
            base.RemoveCondition(condition);
            conditions.Remove(condition);
        }

        public override List<BlackboardConditionAsset> Conditions
        {
            get { return conditions; }
        }

        public override string ConditionTitle => "AND";

        SerializedProperty _conditions;
        SerializedObject serializedObject;
        float helpBoxHeight;
        public override float DrawInspector(Rect rect, float xOffset = 0)
        {
            base.DrawInspector(rect);

            if (serializedObject == null)
                serializedObject = new SerializedObject(this);

            serializedObject.Update();

            if (_conditions == null)
                _conditions = serializedObject.FindProperty("conditions");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float padding = 6f;

            var r = new Rect(rect.x + padding, rect.y, rect.width - padding * 2, lineHeight + padding);
            if (FoldOut)
            {
                var headerFoldoutRect = new Rect(rect.x, rect.y, rect.width, helpBoxHeight);
                EditorGUI.HelpBox(headerFoldoutRect, "", MessageType.None);
                GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontStyle = FontStyle.Bold;
                buttonStyle.fontSize = 10;
                buttonStyle.alignment = TextAnchor.MiddleCenter;
                buttonStyle.normal.textColor = Color.green;
                for (int i = 0; i < conditions.Count; i++)
                {
                    var condition = conditions[i];
                    if (condition == null) continue;
                    r.y += lineHeight + padding * 2;
                    Rect contentRect = new Rect(r.x + padding, r.y + padding, r.width - padding * 2, lineHeight + padding * 2);

                    r.y += condition.DrawInspector(contentRect);

                    GUIStyle removeButtonStyle = new GUIStyle(EditorStyles.miniButton);
                    removeButtonStyle.fontStyle = FontStyle.Bold;
                    removeButtonStyle.fontSize = 10;
                    removeButtonStyle.alignment = TextAnchor.MiddleCenter;

                    // IMPORTANT fixes
                    removeButtonStyle.padding = new RectOffset(2, 0, 0, 0);
                    removeButtonStyle.margin = new RectOffset(0, 0, 0, 0);
                    removeButtonStyle.contentOffset = Vector2.zero;

                    removeButtonStyle.normal.textColor = Color.red;
                    removeButtonStyle.hover.textColor = Color.red;
                    removeButtonStyle.active.textColor = Color.red;

                    var removeButtonRect = new Rect(headerFoldoutRect.x + headerFoldoutRect.width - 40, contentRect.y + contentRect.height * .5f - 10, 20, 20);
                    if (GUI.Button(removeButtonRect, "X", removeButtonStyle))
                    {
                        BehaviorTreeEditorWindow.Instance.DeleteConditionAsset(condition);
                        conditions.RemoveAt(i);
                        break;
                    }
                }


                r = new Rect(rect.xMax - 40, r.y + r.height + padding * 2, 40, 25);
                if (GUI.Button(r, new GUIContent("Bool"), buttonStyle))
                {
                    var conditionAsset = BehaviorTreeEditorWindow.Instance.CreateCondtionAsset(typeof(BlackboardBoolConditionAsset), this);
                    AddCondition(conditionAsset);
                }
                r.x -= 45;
                if (GUI.Button(r, new GUIContent("Float"), buttonStyle))
                {
                    var conditionAsset = BehaviorTreeEditorWindow.Instance.CreateCondtionAsset(typeof(BlackboardFloatConditionAsset), this);
                    AddCondition(conditionAsset);
                }
                r.x -= 45;
                if (GUI.Button(r, new GUIContent("AND"), buttonStyle))
                {
                    var conditionAsset = BehaviorTreeEditorWindow.Instance.CreateCondtionAsset(typeof(BlackboardAndConditionAsset), this);
                    AddCondition(conditionAsset);
                }
                r.x -= 45;
                if (GUI.Button(r, new GUIContent("OR"), buttonStyle))
                {
                    var conditionAsset = BehaviorTreeEditorWindow.Instance.CreateCondtionAsset(typeof(BlackboardOrConditionAsset), this);
                    AddCondition(conditionAsset);
                }
            }
            serializedObject.ApplyModifiedProperties();

            var height = r.y - rect.y;
            helpBoxHeight = height + r.height;
            return height;
        }
#endif

        #endregion
    }
}
