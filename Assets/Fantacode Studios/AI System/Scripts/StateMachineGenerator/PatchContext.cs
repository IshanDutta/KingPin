#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    // Helper handed to each IAIStateMachinePatch.Apply. Owns asset-mutation primitives
    // and named-anchor lookups so patches don't need to know the YAML internals.
    public class PatchContext
    {
        public StateMachineAsset RootAsset { get; }

        public PatchContext(StateMachineAsset rootAsset)
        {
            RootAsset = rootAsset;
        }

        // Creates a new ScriptableObject of type T, nests it inside RootAsset's .asset file
        // with HideInHierarchy so it shows up as a sub-asset, and marks the root dirty.
        public T AddSubAsset<T>(string name) where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            obj.name = name;
            obj.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(obj, RootAsset);
            EditorUtility.SetDirty(RootAsset);
            return obj;
        }

        // Add a state to the given sub-state-machine. The States property returns the
        // backing list reference so .Add mutates the asset directly.
        public void AddStateToSM(StateMachineAsset sm, StateAsset state)
        {
            sm.States.Add(state);
            EditorUtility.SetDirty(sm);
        }

        public StateTransitionAsset AddTransition(StateMachineAsset sm, StateAsset from, StateAsset to,
            bool transitionOnComplete, params BlackboardConditionAsset[] conditions)
        {
            var t = AddSubAsset<StateTransitionAsset>($"{StateName(from)} To {StateName(to)}");
            t.FromState = from;
            t.ToState = to;
            t.TransitionOnComplete = transitionOnComplete;
            t.Conditions = conditions?.ToList() ?? new List<BlackboardConditionAsset>();
            sm.Transitions.Add(t);
            AddToTransitionCollections(sm, t);
            EditorUtility.SetDirty(sm);
            return t;
        }

        public StateTransitionAsset AddAnyStateTransition(StateMachineAsset sm, StateAsset to,
            bool transitionOnComplete, params BlackboardConditionAsset[] conditions)
        {
            var t = AddSubAsset<StateTransitionAsset>($"Any State To {StateName(to)}");
            t.FromState = sm.AnyStateAsset;
            t.ToState = to;
            t.TransitionOnComplete = transitionOnComplete;
            t.Conditions = conditions?.ToList() ?? new List<BlackboardConditionAsset>();
            sm.AnyStateTransitions.Add(t);
            AddToTransitionCollections(sm, t);
            EditorUtility.SetDirty(sm);
            return t;
        }

        // The editor draws one edge per collection and expects all transitions sharing a
        // (from, to) pair to live in the same collection — match that grouping here.
        static void AddToTransitionCollections(StateMachineAsset sm, StateTransitionAsset t)
        {
            var collection = sm.stateTransitionCollections.FirstOrDefault(c =>
                c.Transitions.Count > 0 &&
                c.Transitions[0].FromState == t.FromState &&
                c.Transitions[0].ToState == t.ToState);

            if (collection != null)
                collection.Transitions.Add(t);
            else
                sm.stateTransitionCollections.Add(new StateTransitionCollection { Transitions = new List<StateTransitionAsset> { t } });
        }

        // Removes a transition from the SM's lists and deletes it (with its
        // conditions) from the asset file.
        public void RemoveTransition(StateMachineAsset sm, StateTransitionAsset t)
        {
            sm.Transitions.Remove(t);
            sm.AnyStateTransitions.Remove(t);

            var collection = sm.stateTransitionCollections.FirstOrDefault(c => c.Transitions.Contains(t));
            if (collection != null)
            {
                collection.Transitions.Remove(t);
                if (collection.Transitions.Count == 0)
                    sm.stateTransitionCollections.Remove(collection);
            }

            if (t.Conditions != null)
                foreach (var c in t.Conditions)
                    if (c != null) Object.DestroyImmediate(c, true);
            Object.DestroyImmediate(t, true);
            EditorUtility.SetDirty(sm);
        }

        // ---- Anchor lookups ---------------------------------------------------

        public StateAsset FindRootState(string name)
        {
            return RootAsset.States.FirstOrDefault(s => s != null && StateName(s) == name);
        }

        public HierarchicalStateAsset FindRootHierarchical(string name)
        {
            return RootAsset.States.OfType<HierarchicalStateAsset>()
                .FirstOrDefault(h => h != null && h.NodeTitle == name);
        }

        public bool HasRootStateOfType<T>() where T : StateAsset
        {
            return RootAsset.States.OfType<T>().Any();
        }

        // Default Behavior > child SM (the one containing Idle/Patrol/Follow/Combat)
        public StateMachineAsset GetDefaultBehaviorChildSM()
        {
            var defaultBehavior = FindRootHierarchical("Default Behavior");
            return defaultBehavior?.ChildStateMachineAsset;
        }

        // Default Behavior > child SM > Ambient Behavior > child SM
        // (the SM containing Idle / Patrol / Idle Override / Follow)
        public StateMachineAsset GetAmbientBehaviorChildSM()
        {
            var defaultSM = GetDefaultBehaviorChildSM();
            if (defaultSM == null) return null;
            var ambient = defaultSM.States.OfType<HierarchicalStateAsset>()
                .FirstOrDefault(h => h.NodeTitle == "Ambient Behavior");
            return ambient?.ChildStateMachineAsset;
        }

        // Default Behavior > child SM > Ambient Behavior > child SM > Follow > child SM
        // (the SM containing Chase). This is the deepest commonly-targeted anchor.
        public StateMachineAsset GetFollowChildSM()
        {
            var ambientSM = GetAmbientBehaviorChildSM();
            if (ambientSM == null) return null;
            var follow = ambientSM.States.OfType<HierarchicalStateAsset>()
                .FirstOrDefault(h => h.NodeTitle == "Follow");
            return follow?.ChildStateMachineAsset;
        }

        public StateAsset FindStateInSM(StateMachineAsset sm, string name)
        {
            return sm?.States.FirstOrDefault(s => s != null && StateName(s) == name);
        }

        // ---- Field setters that have no public API --------------------------

        // HierarchicalStateAsset.name is private [SerializeField] and the only way
        // to read NodeTitle correctly later. Set via SerializedObject.
        public void SetHierarchicalName(HierarchicalStateAsset h, string newName)
        {
            var so = new SerializedObject(h);
            so.FindProperty("name").stringValue = newName;
            so.ApplyModifiedProperties();
        }

        // BlackboardKey is [Serializable] with private fields. Set via reflection
        // on the field instance so we don't depend on SerializedObject paths.
        public void SetBlackboardKeyOnCondition(BlackboardConditionAsset condition,
            string keyName, BlackboardValueType type, string id = null)
        {
            var keyField = condition.GetType().GetField("key", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (keyField == null) return;
            var key = keyField.GetValue(condition) as BlackboardKey;
            if (key == null)
            {
                key = new BlackboardKey(type);
                keyField.SetValue(condition, key);
            }
            key.Name = keyName;
            key.Type = type;
            if (!string.IsNullOrEmpty(id))
            {
                var idField = typeof(BlackboardKey).GetField("id", BindingFlags.Instance | BindingFlags.NonPublic);
                idField?.SetValue(key, id);
            }
            EditorUtility.SetDirty(condition);
        }

        // ---- Condition factories (cuts boilerplate in patches) ---------------

        public BlackboardBoolConditionAsset MakeBoolCondition(string keyName, bool expected, string id = null)
        {
            var c = AddSubAsset<BlackboardBoolConditionAsset>("BlackboardBoolConditionAsset");
            SetBlackboardKeyOnCondition(c, keyName, BlackboardValueType.Bool, id);
            var so = new SerializedObject(c);
            so.FindProperty("expectedValue").boolValue = expected;
            so.ApplyModifiedProperties();
            return c;
        }

        public BlackboardIsSetConditionAsset MakeIsSetCondition(string keyName, bool expectedSet, BlackboardValueType keyType, string id = null)
        {
            var c = AddSubAsset<BlackboardIsSetConditionAsset>("BlackboardIsSetConditionAsset");
            SetBlackboardKeyOnCondition(c, keyName, keyType, id);
            var so = new SerializedObject(c);
            so.FindProperty("expectedIsSet").boolValue = expectedSet;
            so.ApplyModifiedProperties();
            return c;
        }

        public BlackboardTriggerConditionAsset MakeTriggerCondition(string keyName, string id = null)
        {
            var c = AddSubAsset<BlackboardTriggerConditionAsset>("BlackboardTriggerConditionAsset");
            SetBlackboardKeyOnCondition(c, keyName, BlackboardValueType.Bool, id);
            return c;
        }

        // ---- Internals -------------------------------------------------------

        static string StateName(StateAsset s)
        {
            if (s == null) return "null";
            // For HierarchicalStateAsset, NodeTitle returns the private 'name' field.
            // For other state assets, NodeTitle reflects their _name field (or display name).
            // Fall back to asset.name for things that don't override NodeTitle (e.g. AnyStateAsset).
            var t = s.NodeTitle;
            return string.IsNullOrEmpty(t) || t == "State" ? s.name : t;
        }
    }
}
#endif
