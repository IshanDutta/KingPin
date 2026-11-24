---
name: fs-shooter-system-2-author-ai-behavior
description: "Use this skill whenever the user wants an AI character from FS Shooter System 2 to do something scripted or custom outside of shooting — e.g. 'the guards should run to the alarm when I enter', 'play an intro before the firefight', 'sniper walks to the window then holds position', 'make the enemy patrol these points', 'follow the player', 'a totally different kind of AI'. Covers AIBehavior nodes (Act, Move, Wait, Set Blackboard Value, looping animations) bound to IntroBehavior / AuthoredBehavior / IdleOverrideBehavior, trigger volumes, patrol and follow setup, blackboard init values, and — only for genuinely new AI types — the State Machine Editor. Do NOT use for shoot/cover/flank tuning (see fs-shooter-system-2-create-shooter-enemy) or weapon data (see fs-shooter-system-2-create-weapons-and-ammo). When in doubt whether a scripted AI moment, patrol or NPC-routine request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Shooter System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.1"
  skill-version: "1.0.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-shooter-system/quick-start"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-13"
---

# Author AI Behaviors

Every FS AI runs a generated state machine with three hooks for custom content: **Intro Behavior** (played once when the AI starts), **Authored Behavior** (played when a trigger fires, interrupting the routine) and **Idle Override Behavior** (played when the AI settles into idle). Each hook executes an **AIBehavior** component — an ordered node list (act, move, wait, set a value, loop an animation) edited in the Inspector, no graph editing. Ambient routine (idle, patrol, follow) is set with components and blackboard values. This is the recommended way to script AI moments; the State Machine Editor is reserved for a completely different kind of AI and is covered last. Demo `2 - Authored Behavior Demo` is built entirely this way.

## When to use this skill

- "Before the fight the sniper walks to the window and kneels."
- "When I open the gate both guards sprint to the courtyard and open fire."
- "Guard patrols WP1→WP3 until he sees me."
- "This squadmate follows me."
- "Idle guards should lean on the wall / smoke."
- "Set a blackboard flag from a behaviour."
- "I want a non-combat AI (medic, scientist)."

Not for:
- Aim error, cover, flank, ranges — `fs-shooter-system-2-create-shooter-enemy`.
- Guns — `fs-shooter-system-2-create-weapons-and-ammo`.
- Dialogue/cutscene camera — FS Dialogue & Cutscene System.

## Prerequisites

- Installed: `System.Type.GetType("FS_Core.AIBehavior, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- AI built with the FS Window or `Prefabs/Shooter Enemy.prefab` (machine generated from `Base AI State Machine`, so the Intro / Authored / Idle Override hooks exist). NavMesh baked.
- Keys `IntroBehavior`, `AuthoredBehavior`, `IdleOverrideBehavior`, `TriggerAuthoredBehavior`, `PatrolWaypoints`, `PatrolSpeed`, `FollowTarget` are registered in `Third Person Controller/Resources/Data/BlackboardKeyContainer.asset` (they ship registered; add more in that asset's Inspector).

## Quick start

Goal: a guard walks to a post and kneels, then behaves normally.

1. Create an empty `Post` on the NavMesh.
2. Enemy root → Add Component → **AI Behavior** (`FS_Core.AIBehavior`) → **Blackboard Key** `IntroBehavior`.
3. **Add Node** → `Core/Move` (Target Transform `Post`, Move Speed 2, Stopping Distance 1); **Add Node** → `Core/Act` (Action Clip = kneel clip) or `Core/Start Looping Animation` (kneel loop) + `Core/Wait` (Wait Forever) for a held pose.
4. Play.

Expected observable result: Scene-view label reads `Intro Behavior`, the guard walks to `Post` and kneels; with Wait Forever it holds until a target appears (combat interrupts since **Interruptible** is on) — otherwise it returns to `Default Behavior > Ambient Behavior > Idle`.

## Workflows

### Workflow: Build a behaviour from nodes

`AIBehavior` (on the AI root; one per key; registers itself in the blackboard at Awake):
- **Blackboard Key** — `IntroBehavior`, `AuthoredBehavior` or `IdleOverrideBehavior`. **Interruptible** (true) — false makes combat/investigation wait until the sequence ends (`CanInterruptBehavior`).
- **Nodes** (Add Node menu, ▲▼ reorder, X remove), run in order:

| Node (`Core/…`) | Fields | Ends when |
|---|---|---|
| Act | Action Clip, Use Direct Audio → Direct Audio / Dialogue Bark Set, Wait For Dialogue | clip (and dialogue) done |
| Move | Target Transform, Move Speed (4), Stopping Distance (1) | arrived (NavMesh) |
| Wait | Wait Forever / Duration (1 s) | time elapsed (forever = only interrupts end it) |
| Set Blackboard Value | Target AI (optional), Key (bool), Value | immediately |
| Start Looping Animation | Clip, Mask, Transition In/Out, Take Clip From Blackboard | immediately (loop continues) |
| Stop Looping Animation | — | immediately |
| Clear Locomotion Override | Transition Duration, Reset Turning Animations | immediately |

Other installed FS packages add node modules to the same menu.

### Workflow: Trigger a behaviour from a volume (Authored Behavior)

1. AI: `AIBehavior` with key `AuthoredBehavior` + nodes (e.g. Move to the courtyard, Act "alert" bark).
2. Drop `Shooter System/Prefabs/Trigger Authored AI Behavior.prefab` (a trigger box with `FS_CombatCore.TriggerAuthoredAIBehavior`) at the gate: **Trigger Type** OnPlayerEnter / OnPlayerExit / OnStart, **Enemies** = the `CombatAIController`s, **Set Target** (make the entering player their target so they fight after the sequence), **Target**. It activates disabled enemies first and destroys itself after firing. `FS_Core.TriggerAIBehavior` is the targeting-free variant.
3. The player root must be tagged `Player`.

**Expected result:** Entering the volume switches the AIs' label to `Authored Behavior`; after the nodes they go to combat (target set) or back to ambient.

### Workflow: Idle override, patrol, follow

- **Idle Override**: `AIBehavior` key `IdleOverrideBehavior` — runs on entering ambient idle (loop + Wait Forever for a held pose); combat/triggers interrupt it.
- **Patrol**: `FS_Core.PatrolSettings` → **Patrol Waypoints** = parent whose children are the route (demo 1 `Way Points`), **Patrol Speed** 2; Ambient `Idle → Patrol` fires when the key is set; loops.
- **Follow**: `FS_Core.TargetHandler` → **Default Follow Target** → Ambient `Follow > Chase` (keeps 3 m, runs beyond 6 m).
- **Init values**: `FS_Core.BlackboardInitValueAssigner` (key + typed value at Start).

### Workflow: Chain AIs and custom keys

`Set Blackboard Value` with **Target AI** = another `AIController`, Key `TriggerAuthoredBehavior`, Value true fires that AI's Authored Behavior from this one. New keys: add them to `BlackboardKeyContainer.asset` first — pickers only list registered keys.

### Workflow: Build a completely different AI (State Machine Editor) — advanced

Only when nodes cannot express it. Do not edit the generated machine of a normal enemy (regeneration via the FS Window overwrites it, and the Shooter patch expects the Base layout). Duplicate `AI System/Resources/AI State Machines/Base AI State Machine.asset` (or an `Examples/` machine), assign to `AIController`, double-click to open the **State Machine Editor**: right-click canvas → **Create State** (Idle, Patrol, Wander, Chase, Smooth Chase, Flee, Run From Target, Move, Face Target, Turn To Face, Wait, Act, Execute Behavior, Set Blackboard Value, Override Locomotion, Empty, Paused, Dead, plus Shooter `Shoot`/`Cover`/`Flank` and Combat Core `Getting Hit`/`Knocked Down`) or **Create Sub-Statemachine**; right-click node → **Make Transition**, **Set As Default State**; select a transition to add conditions (Bool/Float/Is Set/Trigger/And/Or) or **Transition On Complete**; **Parameters** panel adds keys; shortcuts F/Delete/Ctrl+D/C/V. Keep `Any State → Dead` on `IsDead`.

## Verification

- `ai.GetComponents<FS_Core.AIBehavior>()` has the intended keys; `NodeCount >= 1`.
- Play: `Blackboard.TryGet<FS_Core.AIBehavior>("IntroBehavior", out var b) && b != null`; `IsStateActive<FS_Core.ExecuteBehaviorState>()` while the intro runs.
- Trigger: prefab instance lists the enemies; player tagged `Player`; after entering, label shows `Authored Behavior`.
- Patrol: `PatrolSettings` parent has ≥ 2 children; `Blackboard.Has("PatrolWaypoints")` in Play.
- `AIController.StateMachineAsset` unchanged unless the advanced path was intended.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FS_Core.AIBehavior` | MonoBehaviour | Node list per key; `Interruptible`, `Nodes`. |
| `FS_Core.AIBehaviorNode` (+ `[AINodeModule]`) | abstract | Base for custom nodes. |
| `FS_Core.ActNode` / `MoveNode` / `WaitNode` / `SetBlackboardValueNode` / `StartLoopingAnimationNode` / `StopLoopingAnimationNode` / `ClearLocomotionOverrideNode` | nodes | See table. |
| `Shooter System/Prefabs/Trigger Authored AI Behavior.prefab` (`FS_CombatCore.TriggerAuthoredAIBehavior`) / `FS_Core.TriggerAIBehavior` | trigger volumes | Fire Authored Behavior (+ target). |
| `FS_Core.PatrolSettings` / `TargetHandler` / `BlackboardInitValueAssigner` | MonoBehaviours | Ambient inputs. |
| `FS_Core.AIController.Blackboard` / `PauseStateMachine()` / `ResumeStateMachine()` | runtime | Drive from code. |
| `FS_Core.StateMachineEditorWindow.OpenWindow(asset)` | Editor | Graph editor. |
| `Third Person Controller/Resources/Data/BlackboardKeyContainer.asset` | registry | Keys for pickers. |

## Common issues

- **Symptom:** Intro never plays. **Cause:** Wrong key / component not on the root / machine not generated from Base. **Fix:** Root object, correct key, rebuild via FS Window.
- **Symptom:** Move node stalls. **Cause:** Target off the NavMesh. **Fix:** Place on NavMesh; Stopping Distance ≥ 0.5.
- **Symptom:** Sequence cut short. **Cause:** Player visible → combat interrupt. **Fix:** Untick Interruptible.
- **Symptom:** Trigger fires nothing. **Cause:** Collider not a trigger, player tag, AI not listed. **Fix:** Check all; the volume is single-use.
- **Symptom:** Custom machine lost after Setup. **Fix:** Keep custom machines as separate assets; assign manually.

## Boundaries

- Sequential nodes only (no branching); use two behaviours or the state machine.
- Behaviours cannot drive the player.
- Behavior Trees are not part of the supported workflow.
- Dialogue UI/choices belong to the Dialogue & Cutscene System; Act nodes only play clips and barks.
