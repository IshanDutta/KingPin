using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    public abstract class BlackboardConditionAsset : ScriptableObject
    {
        public abstract IBlackboardCondition Build();

        static BlackBoardkeys parameterContainer;
        public static BlackBoardkeys ParameterContainer
        {
            get
            {
                if (parameterContainer == null)
                {
                    parameterContainer = Resources.Load("Data/BlackboardKeyContainer") as BlackBoardkeys;
                }
                return parameterContainer;
            }
        }

        public string GetParameterName(string id)
        {
            if (ParameterContainer == null)
                return "No Parameter Available";
            var param = ParameterContainer.GetParameterByID(id);
            return param != null ? param.Name : "Parameter Not Found";
        }


        #region Editor

        public virtual float DrawInspector(Rect rect, float xOffset = 0)
        {
#if UNITY_EDITOR
            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout);
            foldoutStyle.alignment = TextAnchor.MiddleLeft;
            foldoutStyle.fontStyle = FontStyle.Bold;
            foldoutStyle.fontSize = 11;
            foldoutStyle.normal.textColor = Color.white;
            foldoutStyle.onNormal.textColor = Color.white;
            foldoutStyle.hover.textColor = Color.white;
            foldoutStyle.onHover.textColor = Color.white;
            foldoutStyle.focused.textColor = Color.white;
            foldoutStyle.onFocused.textColor = Color.white;


            float lineHeight = EditorGUIUtility.singleLineHeight;
            float padding = 6f;
            var r = new Rect(rect.x + padding, rect.y, rect.width - padding * 2, lineHeight + padding);
            var foldoutRect = new Rect(r.x, r.y, r.width - 30, lineHeight + padding * 2);

            //EditorGUI.HelpBox(rect, "", MessageType.None);
            foldoutRect.x += xOffset;
            FoldOut = EditorGUI.Foldout(foldoutRect, FoldOut, ConditionTitle, true, foldoutStyle);
            return foldoutRect.height;
#else
            return 0f;
#endif
        }
        public bool FoldOut { get; set; }
        public virtual string ConditionTitle => "Condition";
        [SerializeField] BlackboardConditionAsset parentCondition;
        public BlackboardConditionAsset ParentCondition
        {
            get => parentCondition;
            set => parentCondition = value;
        }
        public virtual List<BlackboardConditionAsset> Conditions { get; }
        public virtual void AddCondition(BlackboardConditionAsset condition) { }
        public virtual void RemoveCondition(BlackboardConditionAsset condition) { }
        #endregion
    }
}
