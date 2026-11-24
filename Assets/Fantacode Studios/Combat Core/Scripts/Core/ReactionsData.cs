using FS_Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_CombatCore
{
    [CustomIcon(FolderPath.DataIcons + "Reaction Icon.png")]
    [Icon(FolderPath.DataIcons + "Reaction Icon.png")]
    [CreateAssetMenu(menuName = "Combat/Create Reactions")]
    public class ReactionsData : ScriptableObject
    {
        [Tooltip("List of reactions for handling reactions for different scenarios.")]
        public List<ReactionContainer> reactions = new List<ReactionContainer>();

        public bool HasReactions(HitType hitType = HitType.Any)
        {
            if (reactions == null || reactions.Count == 0) return false;
            if (hitType == HitType.Any) return true;
            return reactions.Any(r => r.hitType == hitType || r.hitType == HitType.Any);
        }

        private void OnValidate()
        {
            foreach (var item in reactions)
            {
                if (item.reaction.LegacyAnimationClip != null && item.reaction.animationClipInfo.clip == null)
                    item.reaction.animationClipInfo.clip = item.reaction.LegacyAnimationClip;
            }
        }
    }

    public enum HitType { Any, Melee, Ranged }

    public enum ParameterType { Bool, Int, Float }
    public enum ComparisonType { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }

    // 2) A serializable struct for one parameter check
    [Serializable]
    public struct AnimatorParameterCondition
    {
        public string parameterName;   // must match exactly an Animator parameter
        public ParameterType parameterType;
        public ComparisonType comparison;

        // we'll only use the field that matches parameterType:
        public bool boolValue;
        public int intValue;
        public float floatValue;

        // runtime helper: check this condition on a given Animator
        public bool IsMet(Animator anim)
        {
            switch (parameterType)
            {
                case ParameterType.Bool:
                    bool b = anim.GetBool(parameterName);
                    return comparison == ComparisonType.Equal
                        ? b == boolValue
                        : b != boolValue;

                case ParameterType.Int:
                    int i = anim.GetInteger(parameterName);
                    switch (comparison)
                    {
                        case ComparisonType.Equal: return i == intValue;
                        case ComparisonType.NotEqual: return i != intValue;
                        case ComparisonType.Greater: return i > intValue;
                        case ComparisonType.Less: return i < intValue;
                        case ComparisonType.GreaterOrEqual: return i >= intValue;
                        case ComparisonType.LessOrEqual: return i <= intValue;
                    }
                    break;

                case ParameterType.Float:
                    float f = anim.GetFloat(parameterName);
                    switch (comparison)
                    {
                        case ComparisonType.Equal: return Mathf.Approximately(f, floatValue);
                        case ComparisonType.NotEqual: return !Mathf.Approximately(f, floatValue);
                        case ComparisonType.Greater: return f > floatValue;
                        case ComparisonType.Less: return f < floatValue;
                        case ComparisonType.GreaterOrEqual: return f >= floatValue;
                        case ComparisonType.LessOrEqual: return f <= floatValue;
                    }
                    break;
            }
            // should never get here
            return false;
        }
    }

    [System.Serializable]
    public class ReactionContainer
    {
        [HideInInspector]
        public string name;

        [Tooltip("Enable this to use state machine-based conditions for triggering reactions.")]
        public bool isAnimationStateBasedReaction = false;

        [Tooltip("List of state machine conditions that must be met for this reaction to trigger.")]
        public List<StateMachineCondition> StateMachineNames = new List<StateMachineCondition>();

        [Tooltip("The type of hit this reaction applies to (Any, Melee, Ranged).")]
        public HitType hitType;

        [Tooltip("Direction from which the hit was received (e.g., Front, Back, Left, Right).")]
        [HideInInspectorEnum(1)]
        public HitDirections direction;

        [Tooltip("If true, the reaction will only play when the character is hit from behind.")]
        public bool attackedFromBehind = false;

        [Tooltip("A tag to categorize the reaction (e.g., 'Powerful', 'Weak', etc.).")]
        public string tag;

        [Tooltip("The actual reaction to be played when this container matches the hit conditions.")]
        public Reaction reaction;

        [System.Serializable]
        public class StateMachineCondition
        {
            [Tooltip("The name of the Animator state machine to check (must match exactly).")]
            public string StateMachineName = "";

            [Tooltip("A list of Animator parameter conditions that must all be satisfied.")]
            public List<AnimatorParameterCondition> Conditions = new List<AnimatorParameterCondition>();
        }

        public ReactionContainer()
        {
            name = $"Type - {hitType.ToString()}, Direction - {direction.ToString()}";
            if (!string.IsNullOrWhiteSpace(tag))
                name += ", Tag - " + tag;
        }
        public bool MatchesAnyEntry(Animator anim, int layer = 0)
        {
            if (anim.GetNextAnimatorStateInfo(layer).shortNameHash != 0 || anim.isMatchingTarget)
                return false;

            // grab the current stateInfo
            var stateInfo = anim.GetCurrentAnimatorStateInfo(layer);


            // iterate each configured entry
            foreach (var entry in StateMachineNames)
            {
                // 1) state‐name check
                if (!stateInfo.IsName(entry.StateMachineName))
                    continue;

                // 2) all parameter‐conditions must pass
                bool allConditionsMet = true;
                foreach (var cond in entry.Conditions)
                {
                    if (!cond.IsMet(anim))
                    {
                        allConditionsMet = false;
                        break;
                    }
                }

                // 3) if we got here with all = true, we’ve found a match
                if (allConditionsMet)
                    return true;
            }

            // no entry matched
            return false;
        }
    }

    [System.Serializable]
    public class Reaction
    {
        [Tooltip("The animation clip associated with this reaction.")]
        public AnimGraphClipInfo animationClipInfo;

        [Tooltip("Indicates if the character will be knocked down as part of this reaction.")]
        public bool willBeKnockedDown;

        [Tooltip("The direction in which the character will be knocked down.")]
        public KnockDownDirection knockDownDirection;

        [Tooltip("The range of time (in seconds) for how long the character will stay lying down.")]
        public Vector2 lyingDownTimeRange = new Vector2(1, 3);

        [Tooltip("Indicates if the lying down animation should be overridden.")]
        public bool overrideLyingDownAnimation;

        [Tooltip("The animation clip to use for the lying down phase.")]
        public AnimGraphClipInfo lyingDownAnimation;

        [Tooltip("The animation clip to use for the character getting up.")]
        public AnimGraphClipInfo getUpAnimation;

        [Tooltip("Indicates if the character should rotate towards the attacker during the reaction.")]
        public bool rotateToAttacker = true;

        [Tooltip("The angle by which the character should offset from the attacker.")]
        public float rotationOffset = 0f;

        [Tooltip("Animation mask to be applied while playing the reaction.")]
        [HideInInspectorEnum(1, 3, 4, 5, 6)]
        public Mask animationMask;

        [Tooltip("If enabled, this reaction will play additively on top of other animations. Useful for layered effects. If disabled, this prevent all other actions while playing this reaction")]
        public bool isAdditiveAnimation = false;

        [Tooltip("If true, collisions will be ignored during this reaction (e.g., for synced finisher reactions).")]
        public bool ignoreCollision;

        [Tooltip("If true, the character will be prevented from falling off ledges during this reaction's root motion.")]
        public bool requiresLedgeBlocking = true;

        //[Tooltip("If true, ragdoll will be triggered at trigger time. Only applicable for death animations.")]
        //public bool triggerRagdoll;

        //public float ragdollTriggerTime = 0;

        [Tooltip("If true, the reaction blends into active ragdoll at the specified time.")]
        public bool blendToRagdoll;

        [Tooltip("Normalized time (0-1) in the reaction animation to start the ragdoll blend.")]
        public float ragdollBlendStartTime = 0.8f;

        [Tooltip("Duration in seconds to transition from animation to full physics.")]
        public float ragdollBlendDuration = 0.3f;

        [Tooltip("If enabled, prevents the character from falling off ledges during the reaction.")]
        public bool preventFallingFromLedge = true;


        // Moved to animationClipInfo
        [HideInInspector]
        [SerializeField] AnimationClip animationClip;

        public AnimationClip LegacyAnimationClip => animationClip;
    }

    public enum KnockDownDirection { LyingOnBack, LyingOnFront }

    public enum HitDirections
    {
        Any,
        FromCollision,
        Top,
        Bottom,
        Right,
        Left
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class OnlyShowAnimationClipsAttribute : PropertyAttribute
    {
    }


#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ReactionContainer))]
    public class ReactionContainerDrawer : PropertyDrawer
    {
        // Foldout state per property path
        private static Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string propertyKey = property.propertyPath;
            if (!foldoutStates.ContainsKey(propertyKey))
                foldoutStates[propertyKey] = true;

            // Foldout label
            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            foldoutStates[propertyKey] = EditorGUI.Foldout(foldoutRect, foldoutStates[propertyKey], label, true);

            if (!foldoutStates[propertyKey])
                return;

            EditorGUI.BeginProperty(position, label, property);

            // Set indent
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 1;

            float lineHeight = EditorGUIUtility.singleLineHeight + 2f;
            float yOffset = foldoutRect.y + lineHeight;

            Rect rect = new Rect(position.x, yOffset, position.width, lineHeight);

            // Draw fields
            SerializedProperty hitType = property.FindPropertyRelative("hitType");
            SerializedProperty direction = property.FindPropertyRelative("direction");
            SerializedProperty attackedFromBehind = property.FindPropertyRelative("attackedFromBehind");
            SerializedProperty tag = property.FindPropertyRelative("tag");
            SerializedProperty reaction = property.FindPropertyRelative("reaction");
            SerializedProperty isAnimationStateBased = property.FindPropertyRelative("isAnimationStateBasedReaction");
            SerializedProperty stateMachines = property.FindPropertyRelative("StateMachineNames");

            EditorGUI.PropertyField(rect, hitType);
            rect.y += lineHeight;

            EditorGUI.PropertyField(rect, direction);
            rect.y += lineHeight;

            EditorGUI.PropertyField(rect, attackedFromBehind);
            rect.y += lineHeight;

            EditorGUI.PropertyField(rect, tag);
            rect.y += lineHeight;

            EditorGUI.PropertyField(rect, reaction, true);
            rect.y += EditorGUI.GetPropertyHeight(reaction, true);

            EditorGUI.PropertyField(rect, isAnimationStateBased, new GUIContent("Use State Machine"));
            rect.y += lineHeight;

            if (isAnimationStateBased.boolValue)
            {
                EditorGUI.PropertyField(rect, stateMachines, true);
                rect.y += EditorGUI.GetPropertyHeight(stateMachines, true);
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            string propertyKey = property.propertyPath;
            if (!foldoutStates.ContainsKey(propertyKey) || !foldoutStates[propertyKey])
                return EditorGUIUtility.singleLineHeight;

            float height = 0f;
            float lineHeight = EditorGUIUtility.singleLineHeight + 2f;

            height += lineHeight; // foldout
            height += lineHeight; // hitType
            height += lineHeight; // direction
            height += lineHeight; // attackedFromBehind
            height += lineHeight; // tag

            SerializedProperty reaction = property.FindPropertyRelative("reaction");
            height += EditorGUI.GetPropertyHeight(reaction, true); // reaction

            height += lineHeight; // isAnimationStateBasedReaction

            SerializedProperty isAnimationStateBased = property.FindPropertyRelative("isAnimationStateBasedReaction");
            if (isAnimationStateBased.boolValue)
            {
                SerializedProperty stateMachines = property.FindPropertyRelative("StateMachineNames");
                height += EditorGUI.GetPropertyHeight(stateMachines, true); // state machines
            }

            return height;
        }
    }

    [CustomPropertyDrawer(typeof(AnimatorParameterCondition))]
    public class AnimatorParameterConditionDrawer : PropertyDrawer
    {
        // padding between fields  
        const float lineHeight = 18f;
        const float vSpacing = 2f;

        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // grab sub-properties
            var nameProp = prop.FindPropertyRelative("parameterName");
            var typeProp = prop.FindPropertyRelative("parameterType");
            var compProp = prop.FindPropertyRelative("comparison");
            var boolValProp = prop.FindPropertyRelative("boolValue");
            var intValProp = prop.FindPropertyRelative("intValue");
            var floatValProp = prop.FindPropertyRelative("floatValue");

            // layout
            var r = new Rect(pos.x, pos.y, pos.width, lineHeight);
            EditorGUI.PropertyField(r, nameProp);

            r.y += lineHeight + vSpacing;
            EditorGUI.PropertyField(r, typeProp);

            r.y += lineHeight + vSpacing;
            EditorGUI.PropertyField(r, compProp);

            // now only show the matching value
            r.y += lineHeight + vSpacing;
            var chosen = (ParameterType)typeProp.enumValueIndex;
            switch (chosen)
            {
                case ParameterType.Bool:
                    EditorGUI.PropertyField(r, boolValProp, new GUIContent("Value"));
                    break;
                case ParameterType.Int:
                    EditorGUI.PropertyField(r, intValProp, new GUIContent("Value"));
                    break;
                case ParameterType.Float:
                    EditorGUI.PropertyField(r, floatValProp, new GUIContent("Value"));
                    break;
            }

            EditorGUI.indentLevel = indent;
        }

        public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
        {
            // 4 rows: name, type, comparison, value
            return (lineHeight + vSpacing) * 4f;
        }
    }
#endif
}
