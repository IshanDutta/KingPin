---
name: fs-stealth-system-2-author-ai-behavior
description: "Use this skill whenever the user wants a guard from FS Stealth System 2 to do something scripted or custom — e.g. 'the guard should walk to the gate and stand there', 'play an intro before patrols start', 'when I open the vault the guards run to it', 'make the guard patrol these points', 'the intro keeps getting interrupted when I'm seen'. Covers AIBehavior nodes (Act, Move, Wait, Set Blackboard Value, looping animations) bound to IntroBehavior / AuthoredBehavior / IdleOverrideBehavior, trigger volumes, patrol and follow, how stealth stimuli interrupt behaviours (Interruptible), blackboard init values, and — only for new AI types — the State Machine Editor. Do NOT use for perception setup (see fs-stealth-system-2-create-stealth-ai) or detection tuning (see fs-stealth-system-2-tune-detection-and-alerts). When in doubt whether a scripted guard moment, patrol or NPC-routine request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Stealth System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.0"
  skill-version: "1.0.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-stealth-system"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-15"
---

# Author AI Behaviors

Every FS AI runs a generated state machine with three hooks for custom content: **Intro Behavior** (played once when the AI starts), **Authored Behavior** (played when a trigger fires, interrupting the routine) and **Idle Override Behavior** (played when the AI settles into idle). Each hook executes an **AIBehavior** component — an ordered node list (act, move, wait, set a value, loop an animation) edited in the Inspector, no graph editing. Ambient routine (idle, patrol, follow) is set with components and blackboard values. Stealth adds one rule: an investigation (`Investigate`) or a trespass warning (`WarnTarget`) pulls the guard out of an Intro/Authored behaviour **only if** that behaviour is **Interruptible** — untick it for must-finish cutscene moments, and the guard will perceive but finish first. This is the recommended way to script guard moments; the State Machine Editor is reserved for a completely different kind of guard and is covered last.

## When to use this skill

- "Guard walks to the gate and stands there."
- "Two guards chat until I open the vault, then run to it."
- "Guard patrols the rooftops."
- "Companion follows me."
- "Idle guards lean on the wall / smoke."
- "Don't let detection cut my intro."
- "I need a non-standard AI."

Not for:
- Sensors, awareness, combat integration — `fs-stealth-system-2-create-stealth-ai`.
- Alert radii, situation timings — `fs-stealth-system-2-tune-detection-and-alerts`.
- Dialogue/cutscenes with UI — FS Dialogue & Cutscene System.

## Prerequisites

- Installed: `System.Type.GetType("FS_Core.AIBehavior, Assembly-CSharp") != null` and `FS_StealthSystem.StealthAIController` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220).
- A guard built with the FS Window (Stealth AI) or `Game/Prefabs/FS Stealth AI.prefab` — its machine derives from `Base AI State Machine`, so the Intro / Authored / Idle Override hooks and the stealth interrupts exist. NavMesh baked.
- Keys `IntroBehavior`, `AuthoredBehavior`, `IdleOverrideBehavior`, `TriggerAuthoredBehavior`, `CanInterruptBehavior`, `PatrolWaypoints`, `PatrolSpeed`, `FollowTarget` registered in `Third Person Controller/Resources/Data/BlackboardKeyContainer.asset` (they ship registered; add more in that asset's Inspector).

## Quick start

Goal: a guard walks to a gate post at scene start and stands there, but still reacts to the player.

1. Create an empty `GatePost` on the NavMesh.
2. Guard root → Add Component → **AI Behavior** (`FS_Core.AIBehavior`) → **Blackboard Key** `IntroBehavior`, **Interruptible** ✓.
3. **Add Node** → `Core/Move` (Target Transform `GatePost`, Move Speed 2, Stopping Distance 1); **Add Node** → `Core/Wait` (Wait Forever).
4. Play.

Expected observable result: Scene-view label reads `Intro Behavior`, the guard walks to `GatePost` and stays; when it hears or sees the player the label switches to `Default Behavior > Investigate …` (or `Warn` / `Combat`) and after the situation ends it returns to `Ambient Behavior > Idle`.

## Workflows

### Workflow: Build a behaviour from nodes

`AIBehavior` (on the AI root; one per key; registers itself in the blackboard at Awake):
- **Blackboard Key** — `IntroBehavior`, `AuthoredBehavior` or `IdleOverrideBehavior`. **Interruptible** (true) — written to `CanInterruptBehavior` when the behaviour starts; false makes investigations, warnings and combat wait until the sequence ends.
- **Nodes** (Add Node menu, ▲▼ reorder, X remove), run in order:

| Node (`Core/…`) | Fields | Ends when |
|---|---|---|
| Act | Action Clip, Use Direct Audio → Direct Audio / Dialogue Bark Set, Wait For Dialogue | clip (and dialogue) done |
| Move | Target Transform, Move Speed (4), Stopping Distance (1) | arrived (NavMesh path) |
| Wait | Wait Forever / Duration (1 s) | time elapsed |
| Set Blackboard Value | Target AI (optional), Key (bool), Value | immediately |
| Start Looping Animation | Clip, Mask, Transition In/Out, Take Clip From Blackboard | immediately (loop continues) |
| Stop Looping Animation | — | immediately |
| Clear Locomotion Override | Transition Duration, Reset Turning Animations | immediately |

Other installed FS packages add node modules to the same menu. While a behaviour runs, perception still works (meters fill); only the state change is gated by Interruptible.

### Workflow: Trigger a behaviour from a volume (Authored Behavior)

1. AI: `AIBehavior` key `AuthoredBehavior` + nodes (e.g. Move to the vault door, Act "look").
2. Empty GameObject + `BoxCollider` (Is Trigger) → Add Component `FS_Core.TriggerAIBehavior`: **Trigger Mode** OnPlayerEnter / OnPlayerExit / OnStart, **AIs** list. Sets `TriggerAuthoredBehavior` on each listed `AIController`; the volume destroys itself after firing. (With Combat Core installed, `TriggerAuthoredAIBehavior` additionally sets a combat target — the Stealth Shooter demo uses it.)
3. The player root must be tagged `Player`.

**Expected result:** Entering the volume switches the AIs' label to `Authored Behavior`; after the nodes they return to ambient (or to Investigate/Combat if a stimulus arrived and the behaviour was interruptible).

### Workflow: Idle override, patrol, follow

- **Idle Override**: `AIBehavior` key `IdleOverrideBehavior` — runs on entering ambient idle (loop "smoke" + Wait Forever); investigations interrupt it and idle resumes afterwards.
- **Patrol**: `FS_Core.PatrolSettings` → **Patrol Waypoints** parent (children in order; the demos add `WayPointsNetwork` on the parent for a route gizmo), **Patrol Speed** 2. Investigations pause the patrol and it resumes when the situation ends.
- **Follow**: `FS_Core.TargetHandler` → **Default Follow Target** → `Follow > Chase` (the Stealth patch keeps this branch; `WarnTarget` follow uses the same Chase state).
- **Init values**: `FS_Core.BlackboardInitValueAssigner` — e.g. preset `IdentifiedPlayerAsThreat` true for a guard that already hates the player, or the alerted clip keys (`AlertedIdleClip`, `AlertedWalkClip`, `AlertedRunClip`, `NormalLookAround`, `AlertedLookAround`) to change look-around animations per guard.

### Workflow: Chain AIs and custom keys

`Set Blackboard Value` with **Target AI** = another `AIController`, Key `TriggerAuthoredBehavior`, Value true fires that AI's Authored Behavior. Stealth keys (`Investigate`, `WarnTarget`, `IsAlertedInvestigation`) are owned by the situation system — read them in conditions, do not set them from nodes. Register new keys in `BlackboardKeyContainer.asset` first.

### Workflow: Build a completely different AI (State Machine Editor) — advanced

Only when nodes cannot express it; never edit a generated machine (rebuilding via the FS Window overwrites it and the Stealth/Melee patches expect the Base layout). Duplicate `Stealth System/Game/AI State Machine/Stealth AI with Chase SM.asset` (already contains Investigate/Search/Warn) or `AI System/Resources/AI State Machines/Base AI State Machine.asset`, assign to `AIController`, double-click to open the **State Machine Editor**: right-click canvas → **Create State** (Idle, Patrol, Wander, Chase, Smooth Chase, Flee, Run From Target, Move, Face Target, Turn To Face, Wait, Act, Execute Behavior, Set Blackboard Value, Override Locomotion, Empty, Paused, Dead, plus Stealth's **FindSearchPoint**, **Alerted Idle**, **Resolve Stimulus**) / **Create Sub-Statemachine**; right-click node → **Make Transition**, **Set As Default State**; select a transition to add conditions (Bool/Float/Is Set/Trigger/And/Or on keys such as `Investigate`, `IsAlertedInvestigation`, `WarnTarget`, `IsTargetSet`) or **Transition On Complete**; **Parameters** panel adds keys; shortcuts F/Delete/Ctrl+D/C/V. Keep `Any State → Dead` on `IsDead`.

## Verification

- `ai.GetComponents<FS_Core.AIBehavior>()` has the intended keys; `NodeCount >= 1`; `Interruptible` as designed.
- Play: `Blackboard.TryGet<FS_Core.AIBehavior>("IntroBehavior", out var b) && b != null`; `IsStateActive<FS_Core.ExecuteBehaviorState>()` while it runs; with Interruptible off the label stays on the behaviour even when `AwarenessSystem.IsAware`.
- Trigger: volume lists the AIs; player tagged `Player`.
- Patrol: waypoint parent has ≥ 2 children; `Blackboard.Has("PatrolWaypoints")` in Play.
- `AIController.StateMachineAsset` unchanged unless the advanced path was intended.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FS_Core.AIBehavior` | MonoBehaviour | Node list per key; `Interruptible`, `Nodes`. |
| `FS_Core.AIBehaviorNode` (+ `[AINodeModule]`) | abstract | Base for custom nodes. |
| `FS_Core.ActNode` / `MoveNode` / `WaitNode` / `SetBlackboardValueNode` / `StartLoopingAnimationNode` / `StopLoopingAnimationNode` / `ClearLocomotionOverrideNode` | nodes | See table. |
| `FS_Core.TriggerAIBehavior` / `FS_CombatCore.TriggerAuthoredAIBehavior` | trigger volumes | Fire Authored Behavior (+ combat target). |
| `FS_Core.PatrolSettings` / `TargetHandler` / `BlackboardInitValueAssigner` | MonoBehaviours | Ambient inputs, per-guard clip keys. |
| `FS_Core.AIBlackboardKeys.CanInterruptBehavior` / `FS_StealthSystem.StealthBlackboardKeys` | consts | Interrupt gate and stealth keys. |
| `FS_Core.AIController.Blackboard` / `PauseStateMachine()` / `ResumeStateMachine()` | runtime | Drive from code. |
| `FS_Core.StateMachineEditorWindow.OpenWindow(asset)` | Editor | Graph editor. |
| `Third Person Controller/Resources/Data/BlackboardKeyContainer.asset` | registry | Keys for pickers. |

## Common issues

- **Symptom:** Intro cut short as soon as the player is visible. **Cause:** Interruptible on. **Fix:** Untick for the intro; keep on for ambient behaviours.
- **Symptom:** Intro never plays. **Cause:** Wrong key / component not on the root / machine not from Base. **Fix:** Root object, correct key, rebuild via FS Window.
- **Symptom:** Guard finishes the behaviour but never reacts afterwards. **Cause:** Situation already ended while it was busy. **Fix:** Expected; new stimuli re-trigger.
- **Symptom:** Trigger fires nothing. **Cause:** Collider not a trigger, player tag, AI not listed. **Fix:** Check all; single-use volume.
- **Symptom:** Custom machine lost after Setup. **Fix:** Keep custom machines as separate assets; assign manually.

## Boundaries

- Sequential nodes only (no branching).
- Behaviours cannot drive the player.
- Behavior Trees are not part of the supported workflow.
- Stealth situation keys are system-owned; scripted "fake alarms" should go through `StealthAIManager.CreateOrJoinSituation` from code rather than setting keys.
