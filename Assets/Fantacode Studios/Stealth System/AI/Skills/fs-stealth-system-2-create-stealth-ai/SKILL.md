---
name: fs-stealth-system-2-create-stealth-ai
description: "Use this skill whenever the user wants guards or NPCs that perceive the player with FS Stealth System 2 — e.g. 'add a guard with my model', 'make this AI patrol and look for me', 'guards should hear my footsteps behind walls', 'widen the guard's field of view', 'guard should attack after spotting me', 'show the vision cone'. Covers the FS Systems window (Stealth AI ticked with Melee Combat AI / Shooter AI), the drop-in guard prefab, the generated state machine (Investigate, Search, Warn), AIVision / AIHearing / AITouch setup, AwarenessSystem and StealthAIController fields, bark sets and patrol. Do NOT use for the player (see fs-stealth-system-2-create-stealth-player), level areas (…-design-stealth-areas), alert tuning (…-tune-detection-and-alerts) or scripted moments (…-author-ai-behavior). When in doubt whether a guard, sentry, enemy-vision or NPC-perception request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Stealth System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.0"
  skill-version: "1.0.1"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-stealth-system"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-15"
---

# Create Stealth AI

A guard is an FS `AIController` with `StealthAIController` plus three sensors and an awareness meter: **AIVision** builds two trapezoid vision volumes (primary: fast detection; peripheral: slow) from `VisionVolume` prefabs and confirms hits with eye-height line-of-sight raycasts to the stimulus's vision points; **AIHearing** receives sound stimuli that `StealthSoundHandler` broadcasts to hearing colliders on the `Enemy` layer; **AITouch** is a body-hugging trigger — contact means instant detection. **AwarenessSystem** integrates vision into 0–1 awareness (faster up close, when the target moves fast, when lit, or while the guard is already alerted) and decays it. When awareness fills, the guard either **warns** (trespassing) or **identifies the player as a threat**, sets its target and reports a **Combat** situation; sounds and suspicious objects create **investigations**. The FS Systems window generates the guard's state machine from the Base machine with the Stealth patch (Investigate → Move / Search sub-machine, Warn → Follow / Face Target), and combat states come from Melee Combat AI / Shooter AI if ticked — without them a spotted player is only chased.

## When to use this skill

- "Guard with my model that patrols and spots me."
- "Guard hears footsteps around corners / through walls."
- "Wider / longer / lower vision; see from a balcony."
- "Guard attacks once it sees me (Melee/Shooter)."
- "Show vision cones in the Scene view."
- "Custom warning / investigation lines."

Not for:
- Player — `fs-stealth-system-2-create-stealth-player`.
- Areas, lights, bodies, bins — `fs-stealth-system-2-design-stealth-areas`.
- Awareness rates, alert radii, situation timings in depth — `fs-stealth-system-2-tune-detection-and-alerts` (key fields summarised here).
- Intro/triggered behaviours, patrol details — `fs-stealth-system-2-author-ai-behavior`.

## Prerequisites

- Installed: `System.Type.GetType("FS_StealthSystem.StealthAIController, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220).
- A stealth player in the scene (its root carries `StealthAIManager` / `StealthSettings` the guards need).
- NavMesh baked (`NavMeshSurface` on the environment → Bake); AI Navigation package.
- Humanoid model prefab for the FS Window build.

## Quick start

Goal: a patrolling guard with the user's model that fights with Melee when it spots the player.

1. `Tools > FS System > FS Window` → **Setup** → character type **AI** → tick **Stealth AI** and **Melee Combat AI** (or Shooter AI; tick any other owned AI systems — one guard is built with all of them). Drop the model, **Animator Name** e.g. `Guard`, click **Setup**.
2. Select `FS AI` → Add Component `FS_Core.PatrolSettings` → **Patrol Waypoints** = a parent with waypoint children.
3. `AIVision` → tick **Debug Draw Vision Volume** to see the two volumes; adjust ranges to the corridor.
4. Place `FS AI` on the NavMesh, Play.

Expected observable result: `FS AI` (layer `Enemy`) has `StealthAIController`, `AIVision`, `AIHearing`, `AITouch`, `AwarenessSystem`, `AIController` with `Assets/Generated/AI State Machines/Guard StateMachine.asset` containing `Investigate` (with `Search`) and `Warn`, `NavMeshAgent`, `TargetHandler`, `Carryable`; child `VisionVolume` objects appear at Play (no `SimpleVision` child); a meter fills when the player stands in the primary volume; on full awareness the label switches to `Combat` and the guard attacks; no Console errors.

## Workflows

### Workflow: Build the guard (FS Systems window)

What Setup does (`FSSystemsSetup.StealthSystemAISetup`, donor `Resources/Stealth AI Controller.prefab`): instantiates `AI System/Resources/AI Controller.prefab` → `FS AI` (layer `Enemy`), copies `StealthAIController`, `AIVision`, `AIHearing`, `AITouch`, `AwarenessSystem`, `Carryable`, `RagdollExecutor`, …, removes the base AI's `SimpleVision` child (it would double-report), merges the alerted/look-around animations, generates the state machine and applies `StealthStateMachinePatch` (order 300): inside `Default Behavior` adds **Investigate** (entered when `Investigate` is true: Turn To Face → normal: Move → Act look-around → Wait 5 s; alerted: Override Locomotion (alert clips) → Move fast → Act → **Search** sub-machine: FindSearchPoint (radius 10) → Move → Act → Wait, looping until the situation ends; `RestartInvestigation` re-runs it) and **Warn** (entered when `WarnTarget` is set: Follow when `FollowWhileWarning`, else Face Target), lets stealth stimuli interrupt Intro/Authored behaviours when `CanInterruptBehavior`, and rewires ambient Follow. Melee/Shooter patches add `Combat` (entered on `IsTargetSet`).

Drop-in alternative: `Game/Prefabs/FS Stealth AI.prefab` (fixed model, `Stealth AI with Chase SM.asset` whose Combat is a chase-only hierarchical state; layer must be set to `Enemy`).

**Expected result:** As in Quick start; `StealthAIManager.Instance` registers the guard at enable.

### Workflow: Configure the sensors

`AIVision` (volumes rebuilt at Play from `StealthSettings.VisionVolumePrefab`):

| Field | Default | Notes |
|---|---|---|
| Primary Start / Mid / End Width, Range, Height, Height Below | 4 / 6 / 3 m, 12 m, 2 m, 0 | Fast-detection trapezoid in front of the guard; Height Below > 0 for guards on balconies. |
| Peripheral Start / Mid / End Width, Range, Height, Height Below | 5 / 10 / 5 m, 20 m, 3 m, 0 | Slow-detection volume that should wrap the primary one. |
| Eye Height | 1.8 | Origin of line-of-sight raycasts to the stimulus vision points (blocked by `FSSettings` Vision Obstruction Layer). |
| Debug Draw Vision Volume + colors | off | Scene-view gizmos independent of the Gizmos toggle. |

`AIHearing`: **Use NavMesh Distance** (off; on = sounds behind walls must travel the path), **Use Distance Attenuation** (off; on = loudness falls with distance and **Min Hearing Threshold** 0.2 drops quiet sounds). The hearing collider is the guard's own collider on layer `Enemy` (`StealthSettings.HearingLayer`).

`AITouch`: **Radius** 0.45, **Height** 1.8 — keep it hugging the body so Melee takedowns start from outside it. Touch = awareness 1 immediately (ignored for a non-suspicious player in a public area).

**Expected result:** With debug on, two volumes draw; standing in the primary volume fills the meter at 0.8/s base, in the peripheral at 0.2/s.

### Workflow: Awareness and reactions (StealthAIController / AwarenessSystem)

`AwarenessSystem`: Decay Rate 0.15/s, Primary 0.8 / Peripheral 0.2 per second, Instant Detection Distance 3 m (10×), Distance Multiply Threshold 6 m / Distance Multiplier 2, Motion Ramp 2.5–6 m/s → Max Motion Multiplier 4, Sound Awareness Bump 0.3 (heard + seen). `StealthAIController`: **Alerted Investigation Multiplier** 4 (spots faster while investigating on alert), **Combat Sense Radius** 25 (omnidirectional LOS tracking and leash once in combat; 0 disables), **Hit Sense Range** 4 (being hit within it reveals the attacker), bark sets: **Suspicious Investigation**, **Alert Investigation**, **Trespassing**, **Combat**, **Saw Throw** (`DialogueBarkSet` assets; demo sets in `Demo/_Shared/Dialogues`, create via `Assets > Create > Dialogue > Bark Set`, tag lines `initial`/`ongoing`/`ended`/`arrived`, `warning_1…`).

Outcomes at full awareness: `SuspicionProfile.ShouldWarn` (restricted area, no instant factor) → Trespassing situation (guard warns, follows if `FollowWhileWarning`); otherwise → target set, `IdentifiedPlayerAsThreat` true, Combat situation reported to all guards in range. Awareness decays to 0 out of sight; a dead guard becomes a `Deadbody` stimulus + carry hotspot (`StealthSettings` prefabs) that other guards investigate on alert.

**Expected result:** Meter behaviour matches the rates; `StealthAIController.IdentifiedPlayerAsThreat` true after detection in a hostile area.

### Workflow: Patrol, follow and combat integration

- Patrol/follow/intro like any FS AI (`PatrolSettings`, `TargetHandler`, `AIBehavior` — behaviour skill).
- Combat: tick **Melee Combat AI** / **Shooter AI** in the same build; Melee's stealth takedowns need the `StealthFinisherProvider` bridge from the integration package (`System Integrations/Stealth Integrations` after importing `Integration Scenes.unitypackage`).
- Multiple guards share situations automatically through `StealthAIManager` (alert radii, propagation) — no per-guard wiring.

**Expected result:** `Combat` state active after detection with a combat package; guards within the alert radius join.

## Verification

- Guard has `FS_StealthSystem.StealthAIController`, `AIVision`, `AIHearing`, `AITouch`, `AwarenessSystem`, `FS_Core.AIController` (state machine under `Assets/Generated/AI State Machines/`, or the drop-in `Stealth AI with Chase SM`, whose `StateAsset.name` values include `Investigate`, `Search`, `Warn` — check the `name` field, not the sub-asset's `m_Name`: in the drop-in machine `Warn` is a hierarchical state whose sub-asset is labelled `Sub-State`), `NavMeshAgent`; layer `Enemy`; no child with `FS_Core.SimpleVision`.
- Play: `GetComponentsInChildren<FS_StealthSystem.VisionVolume>().Length == 2`; `AwarenessSystem.CurrentAwareness` rises in view, decays out of view; `StealthAIManager.Instance.HighestSituationType` becomes `Combat` on detection.
- No Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/ FS Window` → Setup (AI) → **Stealth AI** | EditorWindow | Builds the guard (patch order 300). |
| `FS_StealthSystem.StealthAIController` | MonoBehaviour | `PlayerInSight`, `ActiveStimulus`, `CurrentSituation`, `IdentifiedPlayerAsThreat`, `EyePosition`; `HandleVision/HandleAudio/HandleTouch` called by sensors. |
| `FS_StealthSystem.AIVision.CheckVision(requireLOS)` → `VisionInfo` (`Zone`, `Stimulus`, `IsHidden`) | MonoBehaviour | Per-frame sight test. |
| `FS_StealthSystem.AIHearing.Hear(stimulus, radius, loudness)` / `AITouch.Touch(stimulus)` | MonoBehaviours | Sensor entry points. |
| `FS_StealthSystem.AwarenessSystem` | MonoBehaviour | `CurrentAwareness`, `IsAware`, `SetAwareness`, `PauseDecay`. |
| `FS_StealthSystem.StealthStateMachinePatch` | `IAIStateMachinePatch` | Investigate/Search/Warn generation. |
| `FS_StealthSystem.FindSearchPointStateAsset` / `AlertedIdleStateAsset` / `ResolveStimulusStateAsset` | `StateAsset`s | Stealth states for custom machines. |
| `FS_Core.DialogueBarkSet` (`Assets/Create/Dialogue/Bark Set`) | ScriptableObject | Tagged bark lines. |
| `Stealth System/Game/AI State Machine/Stealth AI with Chase SM.asset` | reference machine | Hand-authored equivalent of the generated graph. |

## Common issues

- **Symptom:** Meter fills instantly anywhere. **Cause:** `SimpleVision` child still present, or player inside the touch volume. **Fix:** Delete `SimpleVision`; enlarge nothing — move the player.
- **Symptom:** Guard doesn't hear anything. **Cause:** Guard collider not on `Enemy` (Hearing Layer), or attenuation threshold too high. **Fix:** Layer `Enemy`; lower threshold.
- **Symptom:** Guard sees through walls. **Cause:** Walls not in `FSSettings` Vision Obstruction Layer. **Fix:** Add the wall layer.
- **Symptom:** Guard on a balcony never sees below. **Cause:** Height Below 0. **Fix:** Raise Primary/Peripheral Height Below.
- **Symptom:** After detection the guard just runs at me. **Cause:** No combat AI system ticked. **Fix:** Rebuild with Melee/Shooter AI.
- **Symptom:** Custom state machine lost. **Cause:** Rebuilt via FS Window. **Fix:** Keep custom machines as separate assets.

## Boundaries

- Guards perceive the single Player stimulus and `Deadbody`/suspicious props; no guard-vs-guard factions beyond the `Threat` type.
- Vision volumes are trapezoids in front of the guard; no cone-angle parameter (shape by widths/ranges).
- Combat behaviour requires a combat package; the stealth package supplies detection and chase only.
- Behavior Tree assets are not a supported workflow.
