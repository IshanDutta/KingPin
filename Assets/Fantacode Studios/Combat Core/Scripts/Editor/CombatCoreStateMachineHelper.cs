#if UNITY_EDITOR
using FS_Core;
using UnityEditor;
using UnityEngine;

namespace FS_CombatCore
{
    // Shared helpers used by Melee and Shooter patches. Idempotent so the two patches
    // can run in either order and produce the same merged state machine.
    public static class CombatCoreStateMachineHelper
    {
        // Blackboard key GUIDs taken verbatim from the reference assets so the editor's
        // key registry resolves them to the same entries other systems write.
        public const string IsTargetSetKey = "IsTargetSet";
        public const string IsCombatTypeMeleeKey = "IsCombatTypeMelee";
        public const string IsCombatTypeMeleeId = "d19997e9-8c37-47ea-9094-945242b0cc0c";
        public const string IsKnockedDownKey = "IsKnockedDown";
        public const string IsKnockedDownId = "330e987b-57a8-4990-ad57-b6abe56bceb4";
        public const string IsDeadKey = "IsDead";
        public const string IsDeadId = "b3fef841-3f3f-4ea8-bc27-476bd1ee0356";
        public const string IsGettingHitKey = "IsGettingHit";
        public const string IsGettingHitId = "fed94909-3391-42b1-a0b9-91a8e79fba42";
        public const string IsTargetUnreachableByGroundKey = "IsTargetUnreachableByGround";
        public const string IsTargetUnreachableByGroundId = "09c9b736-bd43-436d-b863-d4b7068744c1";

        // Adds (or returns) the Combat hierarchical state inside Default Behavior's child SM.
        // The Combat state's child SM contains:
        //   - Entry (empty), the dispatch node; combat-type-specific patches (Melee, Shooter)
        //     wire Entry to their own combat hierarchical.
        //   - Getting Hit and Knocked Down as interrupt sub-states with AnyState transitions
        //     so the Combat hierarchical never exits during a hit/knockdown — combat mode
        //     is preserved across those interrupts.
        public static StateMachineAsset EnsureCombatSubMachine(PatchContext ctx)
        {
            var defaultSM = ctx.GetDefaultBehaviorChildSM();
            if (defaultSM == null)
            {
                Debug.LogError("CombatCoreStateMachineHelper: Default Behavior child SM not found on root asset.");
                return null;
            }

            // Idempotent — return existing if already present.
            var existing = ctx.FindStateInSM(defaultSM, "Combat") as HierarchicalStateAsset;
            if (existing != null && existing.ChildStateMachineAsset != null)
                return existing.ChildStateMachineAsset;

            var combat = ctx.AddSubAsset<HierarchicalStateAsset>("Combat");
            ctx.SetHierarchicalName(combat, "Combat");
            combat.SetNodePosition(new Vector2(251.4841f, 70.63534f));

            var combatSM = ctx.AddSubAsset<StateMachineAsset>("StateMachine");
            var anyState = ctx.AddSubAsset<AnyStateAsset>("Any State");
            combatSM.AnyStateAsset = anyState;

            var entry = ctx.AddSubAsset<EmptyStateAsset>("Empty");
            entry.SetNodePosition(new Vector2(-90.95f, 121.58f));
            // EmptyStateAsset.NodeTitle returns its private 'name' field — set it to "Entry"
            // so combat-type patches (Melee, Shooter) can locate it by that title.
            var entrySO = new SerializedObject(entry);
            entrySO.FindProperty("name").stringValue = "Entry";
            entrySO.ApplyModifiedProperties();
            ctx.AddStateToSM(combatSM, entry);
            combatSM.InitialStateIndex = combatSM.States.IndexOf(entry);

            // Interrupt sub-states: nesting Getting Hit and Knocked Down inside Combat means
            // the Combat hierarchical isn't exited during these reactions, so CombatModeBehavior
            // (attached to Combat by the Melee patch) doesn't clear combat mode mid-fight.
            var gettingHit = ctx.AddSubAsset<GettingHitStateAsset>("Getting Hit");
            gettingHit.SetNodePosition(new Vector2(-170.69923f, -27.322252f));
            ctx.AddStateToSM(combatSM, gettingHit);

            var knockedDown = ctx.AddSubAsset<KnockedDownStateAsset>("Knocked Down");
            knockedDown.SetNodePosition(new Vector2(106.82913f, -30.338865f));
            ctx.AddStateToSM(combatSM, knockedDown);

            // AnyState → Knocked Down fires before AnyState → Getting Hit since the Getting Hit
            // condition also requires !IsKnockedDown (a knockdown supersedes a regular hit).
            ctx.AddAnyStateTransition(combatSM, knockedDown, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsKnockedDownKey, expected: true, IsKnockedDownId));

            ctx.AddAnyStateTransition(combatSM, gettingHit, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsGettingHitKey, expected: true, IsGettingHitId),
                ctx.MakeBoolCondition(IsKnockedDownKey, expected: false, IsKnockedDownId));

            // Back to Entry — interrupts complete and dispatch resumes Melee or Ranged combat.
            ctx.AddTransition(combatSM, gettingHit, entry, transitionOnComplete: true);
            ctx.AddTransition(combatSM, knockedDown, entry, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsKnockedDownKey, expected: false, IsKnockedDownId));

            combat.ChildStateMachineAsset = combatSM;

            ctx.AddStateToSM(defaultSM, combat);

            // Wire Combat into Default Behavior child SM:
            //   Any State → Combat (IsTargetSet == true) — fires from any sibling state
            //   Combat → Ambient Behavior (on complete) — return to ambient when combat ends
            ctx.AddAnyStateTransition(defaultSM, combat, transitionOnComplete: false,
                ctx.MakeBoolCondition(IsTargetSetKey, expected: true));

            var ambient = ctx.FindStateInSM(defaultSM, "Ambient Behavior");
            if (ambient != null)
                ctx.AddTransition(defaultSM, combat, ambient, transitionOnComplete: false,
                    ctx.MakeBoolCondition(IsTargetSetKey, expected: false));

            return combatSM;
        }

        // Adds Dead at the root with its Any→Dead transition. Getting Hit and Knocked Down
        // are no longer added at root — they live inside the Combat hierarchical now
        // (see EnsureCombatSubMachine) so combat mode isn't lost during those interrupts.
        public static void EnsureCombatCoreStates(PatchContext ctx)
        {
            var root = ctx.RootAsset;

            if (!ctx.HasRootStateOfType<DeadStateAsset>())
            {
                var dead = ctx.AddSubAsset<DeadStateAsset>("Dead");
                dead.SetNodePosition(new Vector2(291.85593f, -23.532486f));
                ctx.AddStateToSM(root, dead);
                ctx.AddAnyStateTransition(root, dead, transitionOnComplete: false,
                    ctx.MakeBoolCondition(IsDeadKey, expected: true, IsDeadId));
            }
        }

        // Wires Melee↔Ranged Combat cross-transitions when both hierarchicals are present.
        // Called by both the Melee and Shooter patches; whichever runs second sees both
        // states and adds the transitions. Idempotent — re-runs are no-ops.
        public static void EnsureMeleeRangedCrossTransitions(PatchContext ctx, StateMachineAsset combatSM)
        {
            if (combatSM == null) return;

            var meleeCombat = ctx.FindStateInSM(combatSM, "Melee Combat") as HierarchicalStateAsset;
            var rangedCombat = ctx.FindStateInSM(combatSM, "Ranged Combat") as HierarchicalStateAsset;
            if (meleeCombat == null || rangedCombat == null) return;

            // Melee → Ranged when the target becomes unreachable by ground.
            if (!TransitionExists(combatSM, meleeCombat, rangedCombat))
            {
                ctx.AddTransition(combatSM, meleeCombat, rangedCombat, transitionOnComplete: false,
                    ctx.MakeBoolCondition(IsTargetUnreachableByGroundKey, expected: true,
                        IsTargetUnreachableByGroundId));
            }

            // Ranged → Melee when AI is a melee fighter and target is reachable again.
            if (!TransitionExists(combatSM, rangedCombat, meleeCombat))
            {
                ctx.AddTransition(combatSM, rangedCombat, meleeCombat, transitionOnComplete: false,
                    ctx.MakeBoolCondition(IsCombatTypeMeleeKey, expected: true, IsCombatTypeMeleeId),
                    ctx.MakeBoolCondition(IsTargetUnreachableByGroundKey, expected: false,
                        IsTargetUnreachableByGroundId));
            }
        }

        static bool TransitionExists(StateMachineAsset sm, StateAsset from, StateAsset to)
        {
            foreach (var t in sm.Transitions)
            {
                if (t != null && t.FromState == from && t.ToState == to)
                    return true;
            }
            return false;
        }
    }
}
#endif
