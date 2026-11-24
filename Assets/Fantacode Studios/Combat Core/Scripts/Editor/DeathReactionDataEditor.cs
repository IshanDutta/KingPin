using FS_Core;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_CombatCore
{
    [CustomEditor(typeof(DeathReactionsData))]
    public class DeathReactionDataEditor : AnimationPreviewHandler
    {
        DeathReactionsData deathReactionsData;
        public int currentAnimation;

        public List<string> reactionsNameList = new List<string>();

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            reactionsNameList.Clear();
            foreach (var d in deathReactionsData.reactions)
            {
                d.name = $"Type - {d.hitType.ToString()}, Direction - {d.direction.ToString()}";
                if (!string.IsNullOrWhiteSpace(d.tag))
                    d.name += ", Tag - " + d.tag;

                if (!reactionsNameList.Contains(d.name))
                    reactionsNameList.Add(d.name);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            deathReactionsData = target as DeathReactionsData;
            targetData = deathReactionsData;
            OnStart();
        }

        public override void HandleAnimationEnumPopup()
        {
            base.HandleAnimationEnumPopup();
            if (deathReactionsData.reactions.Count > 0)
            {
                currentAnimation = (int)EditorGUILayout.Popup(currentAnimation, reactionsNameList.ToArray(), GUILayout.Width(150));
                if (currentAnimation < reactionsNameList.Count)
                    clip = deathReactionsData.reactions.FirstOrDefault(r => r.name == reactionsNameList[currentAnimation]).reaction.animationClipInfo.clip;
                else
                    currentAnimation = 0;
            }
        }
    }
}
