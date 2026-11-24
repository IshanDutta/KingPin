#if UNITY_EDITOR
using FS_CombatCore;
using FS_Core;
using UnityEditor;
using UnityEngine;

namespace FS_Shooter
{
    [AIStateMachinePatch("Shooter System", order: 200)]
    public class ShooterStateMachinePatch : IAIStateMachinePatch
    {
        const string IsTargetInPreferredRangeKey = "IsTargetInPreferredRange";
        const string IsTargetInPreferredRangeId = "81920035-d4fb-429f-afd3-86bf86bc2a21";
        const string IsTargetReachableKey = "IsTargetReachable";
        const string IsTargetReachableId = "2120903c-65a9-45a3-a976-277bf2aa1079";
        const string FlankRequestedKey = "FlankRequested";
        const string FlankRequestedId = "2e37ea32-364a-41f9-bca8-0fabf1cc1f4d";
        const string TargetPositionKey = "TargetPosition";

        public void Apply(PatchContext ctx)
        {
            CombatCoreStateMachineHelper.EnsureCombatCoreStates(ctx);
            var combatSM = CombatCoreStateMachineHelper.EnsureCombatSubMachine(ctx);
            if (combatSM == null) return;

            var rangedCombat = ctx.AddSubAsset<HierarchicalStateAsset>("Ranged Combat");
            ctx.SetHierarchicalName(rangedCombat, "Ranged Combat");
            rangedCombat.SetNodePosition(new Vector2(141.29f, 127.37f));

            var rangedSM = ctx.AddSubAsset<StateMachineAsset>("StateMachine");
            rangedSM.AnyStateAsset = ctx.AddSubAsset<AnyStateAsset>("Any State");

            var shoot = ctx.AddSubAsset<ShootStateAsset>("Shoot");
            shoot.SetNodePosition(new Vector2(-218.18f, 0.84f));
            ctx.AddStateToSM(rangedSM, shoot);

            var cover = ctx.AddSubAsset<CoverStateAsset>("Cover");
            cover.SetNodePosition(new Vector2(-2.52f, -1.10f));
            ctx.AddStateToSM(rangedSM, cover);

            var flank = ctx.AddSubAsset<FlankStateAsset>("Flank");
            flank.SetNodePosition(new Vector2(-435.45f, -114.99f));
            SetFlankFields(flank, flankSpeed: 2f);
            ctx.AddStateToSM(rangedSM, flank);

            var chase = ctx.AddSubAsset<MoveStateAsset>("Chase");
            chase.SetNodePosition(new Vector2(-458.65f, -0.28f));
            SetMoveFields(chase, displayName: "Chase", takeTargetPosFromBlackboard: true,
                blackboardKeyName: TargetPositionKey, stoppingDistance: 1f, moveSpeed: 4f);
            ctx.AddStateToSM(rangedSM, chase);

            rangedSM.InitialStateIndex = rangedSM.States.IndexOf(chase);

            // Inner ranged combat transitions.
            ctx.AddTransition(rangedSM, shoot, cover, transitionOnComplete: true);
            ctx.AddTransition(rangedSM, cover, shoot, transitionOnComplete: true);

            ctx.AddTransition(rangedSM, chase, shoot, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsTargetInPreferredRangeKey, expected: true, IsTargetInPreferredRangeId));

            ctx.AddTransition(rangedSM, chase, shoot, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsTargetReachableKey, expected: false, IsTargetReachableId));

            ctx.AddTransition(rangedSM, shoot, chase, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsTargetInPreferredRangeKey, expected: false, IsTargetInPreferredRangeId),
                ctx.MakeBoolCondition(IsTargetReachableKey, expected: true, IsTargetReachableId));

            ctx.AddAnyStateTransition(rangedSM, flank, transitionOnComplete: false,
                ctx.MakeTriggerCondition(FlankRequestedKey, FlankRequestedId));

            // Flank completes (visual regained, or last seen position reached) → back to Shoot.
            ctx.AddTransition(rangedSM, flank, shoot, transitionOnComplete: true);

            rangedCombat.ChildStateMachineAsset = rangedSM;
            ctx.AddStateToSM(combatSM, rangedCombat);

            // Wire Empty entry to route to Ranged Combat when IsCombatTypeMelee==false.
            var entry = ctx.FindStateInSM(combatSM, "Entry");
            if (entry != null)
            {
                ctx.AddTransition(combatSM, entry, rangedCombat, transitionOnComplete: false,
                    ctx.MakeBoolCondition(CombatCoreStateMachineHelper.IsCombatTypeMeleeKey,
                        expected: false, CombatCoreStateMachineHelper.IsCombatTypeMeleeId));
            }

            // If Melee Combat is also present, wire the melee↔ranged cross-transitions.
            // Whichever patch runs second adds these; the helper is idempotent.
            CombatCoreStateMachineHelper.EnsureMeleeRangedCrossTransitions(ctx, combatSM);
        }

        static void SetFlankFields(FlankStateAsset flank, float flankSpeed)
        {
            var so = new SerializedObject(flank);
            so.FindProperty("flankSpeed").floatValue = flankSpeed;
            so.ApplyModifiedProperties();
        }

        static void SetMoveFields(MoveStateAsset move, string displayName, bool takeTargetPosFromBlackboard,
            string blackboardKeyName, float stoppingDistance, float moveSpeed)
        {
            var so = new SerializedObject(move);
            so.FindProperty("name").stringValue = displayName;
            so.FindProperty("takeTargetPosFromBlackboard").boolValue = takeTargetPosFromBlackboard;
            so.FindProperty("blackboardKey").FindPropertyRelative("name").stringValue = blackboardKeyName;
            so.FindProperty("blackboardKey").FindPropertyRelative("type").enumValueIndex = (int)BlackboardValueType.Vector3;
            so.FindProperty("stoppingDistance").floatValue = stoppingDistance;
            so.FindProperty("moveSpeed").floatValue = moveSpeed;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
