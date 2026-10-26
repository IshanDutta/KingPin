#if UNITY_EDITOR
using System.Linq;
using FS_Core;
using UnityEditor;
using UnityEngine;

namespace FS_StealthSystem
{
    // Adds the Investigate hierarchical state (with its Search sub-machine) and the
    // Warn state (trespassing warning: follow or face the warn target) to the
    // generated AI state machine, mirroring the reference "Stealth AI with Chase SM" asset.
    [AIStateMachinePatch("Stealth System", order: 300)]
    public class StealthStateMachinePatch : IAIStateMachinePatch
    {
        const string InvestigateKey = "Investigate";
        const string InvestigateId = "c32651d4-d631-407a-b23a-8acd3bd31aba";
        const string InvestigatePositionKey = "InvestigatePosition";
        const string InvestigatePositionId = "6ecf6ebc-10bb-4aa0-a088-7c0d3e0dcf9e";
        const string IsAlertedInvestigationKey = "IsAlertedInvestigation";
        const string IsAlertedInvestigationId = "8416c01c-e055-4a83-bd5b-d79a0d77f1ae";
        const string MoveToInvestigatePosKey = "MoveToInvestigatePos";
        const string MoveToInvestigatePosId = "6438eab6-ac3c-4f0c-9c4b-9fcd3a518033";
        const string RestartInvestigationKey = "RestartInvestigation";
        const string RestartInvestigationId = "7bd5631e-07c0-4aa5-a8f1-8b33df836154";
        const string SearchPositionKey = "SearchPosition";
        const string SearchPositionId = "5346be31-7be1-46b4-801e-d8ddf5b568f9";
        const string NormalLookAroundKey = "NormalLookAround";
        const string AlertedLookAroundKey = "AlertedLookAround";
        const string AlertedLookAroundId = "f4006eb7-c1a3-4726-815c-0c41697259b2";
        const string AlertedIdleClipKey = "AlertedIdleClip";
        const string AlertedIdleClipId = "208caab0-db79-4866-9787-a330b6dad770";
        const string AlertedWalkClipKey = "AlertedWalkClip";
        const string AlertedWalkClipId = "85deb4ba-b41f-429b-aa92-e57ac022e060";
        const string AlertedRunClipKey = "AlertedRunClip";
        const string AlertedRunClipId = "7faef49f-247c-4ca2-8883-f713170a36d5";
        const string WarnTargetId = "fe369865-9054-4f8e-90a4-1c670f4d788f";
        const string CanInterruptBehaviorId = "9eca3643-710b-4ef6-91f7-f6cb23316f5a";
        const string FollowWhileWarningId = "f0515b05-24d3-4ce8-860b-9775c0f7a6cb";
        const string FollowTargetKey = "FollowTarget";
        const string FollowTargetId = "e1187f90-6601-40eb-95fa-423fd35a8551";

        // Look-around clips shipped with the stealth package (Act state fallbacks when
        // the blackboard clip keys are unset)
        const string LookInPlaceClipGuid = "3f2fbbc6f8f54614aacb3b2e7ed0930f";
        const long LookInPlaceClipFileId = -8559118331507692685;
        const string LookAround1ClipGuid = "37a9daa1a3eff764e91de3c70c09cd85";
        const long LookAround1ClipFileId = -1880569108107322972;

        public void Apply(PatchContext ctx)
        {
            var defaultSM = ctx.GetDefaultBehaviorChildSM();
            if (defaultSM == null) return;

            var ambient = ctx.FindStateInSM(defaultSM, "Ambient Behavior");
            if (ambient == null) return;

            var searchSM = BuildSearchSM(ctx);
            var investigateSM = BuildInvestigateSM(ctx, searchSM);

            var investigate = ctx.AddSubAsset<HierarchicalStateAsset>("Investigate");
            ctx.SetHierarchicalName(investigate, "Investigate");
            investigate.SetNodePosition(new Vector2(-192.37f, 103.65f));
            investigate.ChildStateMachineAsset = investigateSM;
            ctx.AddStateToSM(defaultSM, investigate);

            ctx.AddTransition(defaultSM, investigate, ambient, transitionOnComplete: false,
                ctx.MakeBoolCondition(InvestigateKey, expected: false, InvestigateId));
            ctx.AddTransition(defaultSM, ambient, investigate, transitionOnComplete: false,
                ctx.MakeBoolCondition(InvestigateKey, expected: true, InvestigateId));
            ctx.AddTransition(defaultSM, investigate, ambient, transitionOnComplete: true);

            AddWarn(ctx, defaultSM, ambient);
            RewireAmbientFollow(ctx);
            AddBehaviorInterrupts(ctx);
        }

        // Stealth stimuli (investigation, trespass warning) pull the AI out of authored/intro
        // behaviors into Default Behavior, gated by the AI Behavior's Interruptible setting
        // (written to CanInterruptBehavior when the behavior starts).
        void AddBehaviorInterrupts(PatchContext ctx)
        {
            var defaultBehavior = ctx.FindRootHierarchical("Default Behavior");
            if (defaultBehavior == null) return;

            foreach (var stateName in new[] { "Authored Behavior", "Intro Behavior" })
            {
                var state = ctx.FindRootState(stateName);
                if (state == null) continue;

                ctx.AddTransition(ctx.RootAsset, state, defaultBehavior, transitionOnComplete: false,
                    ctx.MakeBoolCondition(InvestigateKey, expected: true, InvestigateId),
                    ctx.MakeBoolCondition(AIBlackboardKeys.CanInterruptBehavior, expected: true, CanInterruptBehaviorId));
                ctx.AddTransition(ctx.RootAsset, state, defaultBehavior, transitionOnComplete: false,
                    ctx.MakeIsSetCondition(StealthBlackboardKeys.WarnTarget, expectedSet: true, BlackboardValueType.Transform, WarnTargetId),
                    ctx.MakeBoolCondition(AIBlackboardKeys.CanInterruptBehavior, expected: true, CanInterruptBehaviorId));
            }
        }

        void AddWarn(PatchContext ctx, StateMachineAsset defaultSM, StateAsset ambient)
        {
            var warnSM = BuildWarnSM(ctx);

            var warn = ctx.AddSubAsset<HierarchicalStateAsset>("Warn");
            ctx.SetHierarchicalName(warn, "Warn");
            warn.SetNodePosition(new Vector2(-395.64f, 171.06f));
            warn.ChildStateMachineAsset = warnSM;
            ctx.AddStateToSM(defaultSM, warn);

            ctx.AddTransition(defaultSM, ambient, warn, transitionOnComplete: false,
                ctx.MakeIsSetCondition(StealthBlackboardKeys.WarnTarget, expectedSet: true, BlackboardValueType.Transform, WarnTargetId));
            ctx.AddTransition(defaultSM, warn, ambient, transitionOnComplete: false,
                ctx.MakeIsSetCondition(StealthBlackboardKeys.WarnTarget, expectedSet: false, BlackboardValueType.Transform, WarnTargetId));
        }

        // Warn branches from Empty right away: follow the warn target when
        // FollowWhileWarning is set, otherwise face it in place.
        StateMachineAsset BuildWarnSM(PatchContext ctx)
        {
            var sm = ctx.AddSubAsset<StateMachineAsset>("StateMachine");
            sm.AnyStateAsset = ctx.AddSubAsset<AnyStateAsset>("Any State");

            var empty = ctx.AddSubAsset<EmptyStateAsset>("Empty");
            empty.SetNodePosition(new Vector2(-340.34f, 113.65f));
            ctx.AddStateToSM(sm, empty);

            var follow = ctx.AddSubAsset<ChaseStateAsset>("Chase");
            follow.SetNodePosition(new Vector2(-83.03f, 64.85f));
            SetChaseFields(follow, "Follow", StealthBlackboardKeys.WarnTarget, WarnTargetId);
            ctx.AddStateToSM(sm, follow);

            var faceTarget = ctx.AddSubAsset<FaceTargetStateAsset>("Face Target");
            faceTarget.SetNodePosition(new Vector2(-77.84f, 154.65f));
            SetFaceTargetFields(faceTarget, StealthBlackboardKeys.WarnTarget, WarnTargetId);
            ctx.AddStateToSM(sm, faceTarget);

            sm.InitialStateIndex = sm.States.IndexOf(empty);

            ctx.AddTransition(sm, empty, follow, transitionOnComplete: false,
                ctx.MakeBoolCondition(StealthBlackboardKeys.FollowWhileWarning, expected: true, FollowWhileWarningId));
            ctx.AddTransition(sm, empty, faceTarget, transitionOnComplete: false,
                ctx.MakeBoolCondition(StealthBlackboardKeys.FollowWhileWarning, expected: false, FollowWhileWarningId));

            return sm;
        }

        // Follow is now reachable from any ambient state (not just Idle), since the
        // warning no longer piggybacks on it via Idle.
        void RewireAmbientFollow(PatchContext ctx)
        {
            var ambientSM = ctx.GetAmbientBehaviorChildSM();
            if (ambientSM == null) return;

            var idle = ctx.FindStateInSM(ambientSM, "Idle");
            var follow = ctx.FindStateInSM(ambientSM, "Follow");
            if (follow == null) return;

            var idleToFollow = ambientSM.Transitions.FirstOrDefault(t => t.FromState == idle && t.ToState == follow);
            if (idleToFollow != null)
                ctx.RemoveTransition(ambientSM, idleToFollow);

            ctx.AddAnyStateTransition(ambientSM, follow, transitionOnComplete: false,
                ctx.MakeIsSetCondition(FollowTargetKey, expectedSet: true, BlackboardValueType.Transform, FollowTargetId));
        }

        StateMachineAsset BuildInvestigateSM(PatchContext ctx, StateMachineAsset searchSM)
        {
            var sm = ctx.AddSubAsset<StateMachineAsset>("StateMachine");
            sm.AnyStateAsset = ctx.AddSubAsset<AnyStateAsset>("Any State");

            var move = ctx.AddSubAsset<MoveStateAsset>("Move");
            move.SetNodePosition(new Vector2(-93.47f, -48.15f));
            SetMoveFields(move, "Move", InvestigatePositionKey, InvestigatePositionId,
                stoppingDistance: 1f, moveSpeed: 2f);
            ctx.AddStateToSM(sm, move);

            var actNormal = ctx.AddSubAsset<ActStateAsset>("Act");
            actNormal.SetNodePosition(new Vector2(119.96f, -45.70f));
            SetActFields(actNormal, NormalLookAroundKey, clipKeyId: null,
                LoadClip(LookInPlaceClipGuid, LookInPlaceClipFileId));
            ctx.AddStateToSM(sm, actNormal);

            var search = ctx.AddSubAsset<HierarchicalStateAsset>("Search");
            ctx.SetHierarchicalName(search, "Search");
            search.SetNodePosition(new Vector2(279.24f, 177.85f));
            search.ChildStateMachineAsset = searchSM;
            ctx.AddStateToSM(sm, search);

            var moveFast = ctx.AddSubAsset<MoveStateAsset>("Move");
            moveFast.SetNodePosition(new Vector2(-106.82f, 255.68f));
            SetMoveFields(moveFast, "Move Fast", InvestigatePositionKey, InvestigatePositionId,
                stoppingDistance: 1f, moveSpeed: 4f);
            ctx.AddStateToSM(sm, moveFast);

            var turnToFace = ctx.AddSubAsset<TurnToFaceStateAsset>("Turn To Face");
            turnToFace.SetNodePosition(new Vector2(-451.53f, 128.83f));
            SetTurnToFaceFields(turnToFace);
            ctx.AddStateToSM(sm, turnToFace);

            var actAlerted = ctx.AddSubAsset<ActStateAsset>("Act");
            actAlerted.SetNodePosition(new Vector2(104.57f, 256.07f));
            SetActFields(actAlerted, AlertedLookAroundKey, AlertedLookAroundId,
                LoadClip(LookInPlaceClipGuid, LookInPlaceClipFileId));
            ctx.AddStateToSM(sm, actAlerted);

            var wait = ctx.AddSubAsset<WaitStateAsset>("Wait (5s)");
            wait.SetNodePosition(new Vector2(369.41f, 50.16f));
            SetWaitFields(wait, waitForever: true, duration: 5f);
            ctx.AddStateToSM(sm, wait);

            var overrideLoco = ctx.AddSubAsset<OverrideLocomotionStateAsset>("Override Locomotion");
            overrideLoco.SetNodePosition(new Vector2(-238.45f, 177.63f));
            SetOverrideLocomotionFields(overrideLoco);
            ctx.AddStateToSM(sm, overrideLoco);

            var empty = ctx.AddSubAsset<EmptyStateAsset>("Empty");
            empty.SetNodePosition(new Vector2(-232.84f, 38.82f));
            ctx.AddStateToSM(sm, empty);

            sm.InitialStateIndex = sm.States.IndexOf(turnToFace);

            ctx.AddTransition(sm, move, actNormal, transitionOnComplete: true);
            ctx.AddTransition(sm, turnToFace, empty, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsAlertedInvestigationKey, expected: false, IsAlertedInvestigationId));
            ctx.AddTransition(sm, empty, move, transitionOnComplete: false,
                ctx.MakeBoolCondition(MoveToInvestigatePosKey, expected: true, MoveToInvestigatePosId));
            ctx.AddTransition(sm, empty, wait, transitionOnComplete: false,
                ctx.MakeBoolCondition(MoveToInvestigatePosKey, expected: false, MoveToInvestigatePosId));
            ctx.AddTransition(sm, moveFast, actAlerted, transitionOnComplete: true);
            ctx.AddTransition(sm, actNormal, wait, transitionOnComplete: true);
            ctx.AddTransition(sm, actAlerted, search, transitionOnComplete: true);
            ctx.AddTransition(sm, turnToFace, overrideLoco, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsAlertedInvestigationKey, expected: true, IsAlertedInvestigationId));
            ctx.AddTransition(sm, overrideLoco, search, transitionOnComplete: false,
                ctx.MakeBoolCondition(MoveToInvestigatePosKey, expected: false, MoveToInvestigatePosId));
            ctx.AddTransition(sm, overrideLoco, moveFast, transitionOnComplete: false,
                ctx.MakeBoolCondition(MoveToInvestigatePosKey, expected: true, MoveToInvestigatePosId));

            ctx.AddAnyStateTransition(sm, turnToFace, transitionOnComplete: false,
                ctx.MakeTriggerCondition(RestartInvestigationKey, RestartInvestigationId));

            return sm;
        }

        StateMachineAsset BuildSearchSM(PatchContext ctx)
        {
            var sm = ctx.AddSubAsset<StateMachineAsset>("StateMachine");
            sm.AnyStateAsset = ctx.AddSubAsset<AnyStateAsset>("Any State");

            // FindSearchPointStateAsset defaults (name "FindSearchPoint", searchRadius 10) match the reference
            var findPoint = ctx.AddSubAsset<FindSearchPointStateAsset>("FindSearchPoint");
            findPoint.SetNodePosition(new Vector2(-185.48f, 118.80f));
            ctx.AddStateToSM(sm, findPoint);

            var move = ctx.AddSubAsset<MoveStateAsset>("Move");
            move.SetNodePosition(new Vector2(86.93f, 117.07f));
            SetMoveFields(move, "Move", SearchPositionKey, SearchPositionId,
                stoppingDistance: 1f, moveSpeed: 2f);
            ctx.AddStateToSM(sm, move);

            var act = ctx.AddSubAsset<ActStateAsset>("Act");
            act.SetNodePosition(new Vector2(86.07f, 256.97f));
            SetActFields(act, AlertedLookAroundKey, AlertedLookAroundId,
                LoadClip(LookAround1ClipGuid, LookAround1ClipFileId));
            ctx.AddStateToSM(sm, act);

            var wait = ctx.AddSubAsset<WaitStateAsset>("Wait (5s)");
            wait.SetNodePosition(new Vector2(-182.37f, 257.68f));
            SetWaitFields(wait, waitForever: false, duration: 5f);
            ctx.AddStateToSM(sm, wait);

            sm.InitialStateIndex = sm.States.IndexOf(findPoint);

            ctx.AddTransition(sm, findPoint, move, transitionOnComplete: true);
            ctx.AddTransition(sm, move, act, transitionOnComplete: true);
            ctx.AddTransition(sm, act, wait, transitionOnComplete: true);
            ctx.AddTransition(sm, wait, findPoint, transitionOnComplete: true);

            return sm;
        }

        // ---- SerializedObject field setters ----------------------------------

        static void SetKey(SerializedProperty keyProp, string keyName, string keyId, BlackboardValueType type)
        {
            keyProp.FindPropertyRelative("name").stringValue = keyName;
            keyProp.FindPropertyRelative("type").enumValueIndex = (int)type;
            if (!string.IsNullOrEmpty(keyId))
                keyProp.FindPropertyRelative("id").stringValue = keyId;
        }

        static void SetMoveFields(MoveStateAsset move, string displayName, string keyName, string keyId,
            float stoppingDistance, float moveSpeed)
        {
            var so = new SerializedObject(move);
            so.FindProperty("name").stringValue = displayName;
            so.FindProperty("takeTargetPosFromBlackboard").boolValue = true;
            SetKey(so.FindProperty("blackboardKey"), keyName, keyId, BlackboardValueType.Vector3);
            so.FindProperty("stoppingDistance").floatValue = stoppingDistance;
            so.FindProperty("moveSpeed").floatValue = moveSpeed;
            so.ApplyModifiedProperties();
        }

        static void SetActFields(ActStateAsset act, string clipKeyName, string clipKeyId, AnimationClip fallbackClip)
        {
            var so = new SerializedObject(act);
            so.FindProperty("takeActionClipFromBlackboard").boolValue = true;
            SetKey(so.FindProperty("actionClipBlackboardKey"), clipKeyName, clipKeyId, BlackboardValueType.AnimGraphClipInfo);
            so.FindProperty("waitForDialogue").boolValue = true;
            if (fallbackClip != null)
                so.FindProperty("actionClip").FindPropertyRelative("clip").objectReferenceValue = fallbackClip;
            so.ApplyModifiedProperties();
        }

        static void SetChaseFields(ChaseStateAsset chase, string displayName, string keyName, string keyId)
        {
            var so = new SerializedObject(chase);
            so.FindProperty("_name").stringValue = displayName;
            SetKey(so.FindProperty("targetKey"), keyName, keyId, BlackboardValueType.Transform);
            so.ApplyModifiedProperties();
        }

        static void SetFaceTargetFields(FaceTargetStateAsset faceTarget, string keyName, string keyId)
        {
            var so = new SerializedObject(faceTarget);
            SetKey(so.FindProperty("targetKey"), keyName, keyId, BlackboardValueType.Transform);
            so.ApplyModifiedProperties();
        }

        void SetTurnToFaceFields(TurnToFaceStateAsset turnToFace)
        {
            var so = new SerializedObject(turnToFace);
            so.FindProperty("displayName").stringValue = "Turn To Face";
            SetKey(so.FindProperty("positionKey"), InvestigatePositionKey, InvestigatePositionId, BlackboardValueType.Vector3);
            so.FindProperty("angleTolerance").floatValue = 25f;
            so.FindProperty("triggerThreshold").floatValue = 0f;
            so.ApplyModifiedProperties();
        }

        static void SetWaitFields(WaitStateAsset wait, bool waitForever, float duration)
        {
            var so = new SerializedObject(wait);
            so.FindProperty("waitForever").boolValue = waitForever;
            so.FindProperty("duration").floatValue = duration;
            so.ApplyModifiedProperties();
        }

        void SetOverrideLocomotionFields(OverrideLocomotionStateAsset state)
        {
            var so = new SerializedObject(state);
            so.FindProperty("name").stringValue = "Override Locomotion";
            SetKey(so.FindProperty("idleClipKey"), AlertedIdleClipKey, AlertedIdleClipId, BlackboardValueType.AnimGraphClipInfo);
            SetKey(so.FindProperty("walkClipKey"), AlertedWalkClipKey, AlertedWalkClipId, BlackboardValueType.AnimGraphClipInfo);
            SetKey(so.FindProperty("runClipKey"), AlertedRunClipKey, AlertedRunClipId, BlackboardValueType.AnimGraphClipInfo);
            so.FindProperty("transitionDuration").floatValue = 0.25f;
            so.FindProperty("disableTurningAnimations").boolValue = true;
            so.ApplyModifiedProperties();
        }

        static AnimationClip LoadClip(string guid, long fileId)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;

            AnimationClip fallback = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not AnimationClip clip || clip.name.StartsWith("__preview")) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out _, out long localId) && localId == fileId)
                    return clip;
                fallback ??= clip;
            }
            return fallback;
        }
    }
}
#endif
