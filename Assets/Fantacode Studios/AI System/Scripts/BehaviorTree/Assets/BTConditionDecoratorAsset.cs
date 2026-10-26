using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FS_Core
{
    [CreateAssetMenu(menuName = "BT/Decorators/ConditionDecorator")]
    public class BTConditionDecoratorAsset : BTNodeAsset
    {
        [SerializeField] BlackboardConditionAsset condition;
        [SerializeField] BTNodeAsset child;

        public BTNodeAsset Child => child;
        public BlackboardConditionAsset Condition => condition;

        public override IBTNode Build(BTContext context)
        {
            var ibtNode = new BTConditionDecorator(condition.Build(), child.Build(context), context.Blackboard);
            Node = ibtNode;
            return ibtNode;
        }


        #region Editor
#if UNITY_EDITOR
        float helpBoxHeight;
        public override void DrawInspector()
        {
            base.DrawInspector();

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float padding = 6f;
            Rect helpBoxRect = EditorGUILayout.GetControlRect(false, helpBoxHeight);

            Rect contentRect = new Rect(helpBoxRect.x + padding, helpBoxRect.y + padding, helpBoxRect.width - padding * 2, lineHeight + padding * 2);
            helpBoxHeight = condition.DrawInspector(contentRect) + lineHeight + padding * 4;
            EditorGUI.HelpBox(helpBoxRect, "", MessageType.None);
        }
#endif

        public void SetCondition(BlackboardConditionAsset conditionAsset)
        {
            condition = conditionAsset;
        }
        public override string NodeTitle => "Condition Decorator";
        public override List<BTNodeAsset> Children
        {
            get
            {
                if (child == null)
                    return null;

                return new List<BTNodeAsset> { child };
            }
        }
        public override void SetChildren(BTNodeAsset children)
        {
            if (child != null)
            {
                // Remove sub-asset safely
                Object.DestroyImmediate(child, true);
            }

            child = children;
            base.SetChildren(children);

#if UNITY_EDITOR
            AssetDatabase.SaveAssets();
#endif
        }
        public override void RemoveChild(BTNodeAsset children, bool clearList = false)
        {
            child = null;
        }
        public override Color NodeColor => new Color(0.8f, 0.1f, 0.2f);
        public override Vector2 NodeSize { get; } = new Vector2(150, 100);
        public override bool CanCreateChildNodes => true;
        #endregion
    }
}
