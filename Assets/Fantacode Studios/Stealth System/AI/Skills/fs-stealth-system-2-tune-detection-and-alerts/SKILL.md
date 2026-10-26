---
name: fs-stealth-system-2-tune-detection-and-alerts
description: "Use this skill whenever the user wants to change how fast, how far or how widely guards in FS Stealth System 2 detect, react and coordinate — e.g. 'guards spot me too fast', 'sprinting should be riskier', 'footsteps shouldn't carry through walls', 'one guard alerts the whole base', 'the search lasts too long', 'guards forget me too quickly', 'what should a gunshot do'. Covers AwarenessSystem rates, AIVision sizes, AIHearing attenuation, footstep radius, StealthSoundHandler priorities, StealthAIManager alert radii, propagation, situation configs, combat downgrade, escalation, the Investigate/Search flow and blackboard keys. Do NOT use for placing areas or lights (see fs-stealth-system-2-design-stealth-areas) or building characters (…-create-stealth-player / …-create-stealth-ai). When in doubt whether a detection-speed, hearing, alert-spread or investigation-timing request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Tune Detection and Alerts

Detection has three layers you can tune independently. **Per guard:** `AIVision` volumes decide *where* the player can be seen, `AwarenessSystem` decides *how fast* awareness fills (base rate by zone × distance boost × motion boost × light visibility × alerted multiplier) and drains, `AIHearing` decides *which sounds* arrive. **Per sound:** `FootStepEffects` radius (× speed, halved when crouched), throw impacts, gunshots — mapped to stimulus priorities by `StealthSoundHandler` (Low → suspicious investigation, High/Critical → alert investigation). **Scene-wide:** `StealthAIManager` turns reports into **situations** (`SuspiciousInvestigation` < `TrespassingRestrictedArea` < `AlertInvestigation` < `Combat`), alerts guards in the inner radius unconditionally and in the outer radius when they see the reporter, spreads alerts by proximity, assigns up to `MaxInvestigators`, times situations out, plays barks, escalates and downgrades. The generated state machine reacts to blackboard keys (`Investigate`, `IsAlertedInvestigation`, `InvestigatePosition`, `WarnTarget`, `IsTargetSet`) — so custom reactions are extra transitions on those keys.

## When to use this skill

- "Too easy/hard to sneak past."
- "Running past a guard should be dangerous."
- "Guards hear me through walls."
- "Whole base alerts from one shout / only nearby guards should react."
- "Search takes forever / they give up instantly."
- "Warnings escalate too fast/slow."
- "Gunshots should be treated like combat."
- "Debug what the AI thinks."

Not for:
- Placing zones/lights/bins — `fs-stealth-system-2-design-stealth-areas`.
- Building characters — `fs-stealth-system-2-create-stealth-player` / `fs-stealth-system-2-create-stealth-ai`.
- Custom behaviours — `fs-stealth-system-2-author-ai-behavior`.

## Prerequisites

- Installed: `System.Type.GetType("FS_StealthSystem.StealthAIManager, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220).
- A stealth player (root carries `StealthAIManager`, `StealthSoundHandler`) and at least one guard.
- For repeatable tests: `StealthAIManager` → **Show Situation Debug Info** ✓ (Editor overlay of active situations) and `AIVision` → Debug Draw.

## Quick start

Goal: slower detection at range, sprinting clearly punished, alerts stay local.

1. Guard → `AwarenessSystem`: Primary Detection Speed 0.8 → 0.5, Peripheral 0.2 → 0.1, keep Instant Detection Distance 3, raise Max Motion Multiplier 4 → 6.
2. Guard → `AIVision`: Primary Range 12 → 10, Peripheral Range 20 → 16 (keep peripheral wrapping primary).
3. `FS Player` → `StealthAIManager`: Inner Alert Radius 15 → 8, Outer Alert Radius 40 → 20, Propagation Radius 10 → 6.
4. Play with the debug overlay on.

Expected observable result: standing at 8 m in the primary volume takes about twice as long to fill the meter; sprinting across the volume fills it faster than walking; when one guard spots the player only guards within 8 m (or 20 m with sight of the reporter) join the `Combat` situation shown in the overlay; no Console errors.

## Workflows

### Workflow: Awareness (per guard, `AwarenessSystem`)

| Field | Default | Effect |
|---|---|---|
| Decay Rate | 0.15 /s | Drain when nothing is seen. |
| Primary / Peripheral Detection Speed | 0.8 / 0.2 /s | Base fill rate by vision zone (full at 1). |
| Instant Detection Distance | 3 m | Inside: 10× rate. |
| Distance Multiply Threshold / Distance Multiplier | 6 m / 2 | Boost just outside instant range fading to 1× at the threshold. |
| Motion Ramp Start / End Speed, Max Motion Multiplier | 2.5 / 6 m/s, 4 | Faster targets are seen faster (walk ≈ 2, sprint ≈ 6 m/s). |
| Sound Awareness Bump | 0.3 | Flat gain per heard sound from a source already in view. |

Multipliers on `StealthAIController`: **Alerted Investigation Multiplier** 4 (while searching on alert). Light: `LightVision` visibility (areas skill). Hidden (`HidingArea`, bins): no gain.

**Expected result:** Meter timings match the formula `rate × distance × motion × light × alerted`; awareness 1 = detected.

### Workflow: Vision and hearing reach

- `AIVision`: Primary widths 4/6/3 m, range 12, height 2; Peripheral 5/10/5 m, range 20, height 3; Height Below for elevated posts; Eye Height 1.8 (LOS origin; blocked by `FSSettings` Vision Obstruction Layer). Narrow corridors → smaller widths; long sightlines → longer range.
- `AIHearing`: **Use NavMesh Distance** (sound must travel the walkable path — walls muffle), **Use Distance Attenuation** + **Min Hearing Threshold** 0.2 (quiet/distant sounds dropped: strength = loudness × (1 − (d/r)²)). Off (default): anything inside the sound radius is heard fully.
- Sound sources: `FootStepEffects` **Footstep Sound Radius** 15 × normalized speed, **Crouch Volume Multiplier** 0.5; `ThrowableItemObject` impacts; Shooter gunshots. `StealthSoundHandler` **Sound Priority Mappings**: Gunshot High, Footstep Low, Throw Impact Low (add Explosion etc.); Low → Suspicious investigation, High/Critical → Alert investigation. Footsteps from an already-visible player only bump awareness.

**Expected result:** Behind a wall with NavMesh distance on, a 15 m footstep is heard only if the path is < 15 m; gunshots trigger an alert investigation at the shot position.

### Workflow: Situations and alert spread (`StealthAIManager`)

| Field | Default | Effect |
|---|---|---|
| Inner / Outer Alert Radius | 15 / 40 m | Unconditional / line-of-sight-to-reporter alert on `ReportPlayerSpotted` / hits. |
| Investigator Swap Distance | 5 m | A closer guard takes over only if its path is this much shorter. |
| Propagation Radius / Interval | 10 m / 0.75 s | Periodic spread from each participant to nearby guards. |
| Suspicious Investigation Config | 15 s, cooldown 10, 1 investigator | Duration after the stimulus is reached, bark spacing, max movers. |
| Trespassing Config | 16 s, cooldown 6, 2 investigators, Leader Speaks Only | Warnings come from the leader; `WarningCount` tags barks `warning_1/2/3…`; repeated warnings escalate. |
| Alert Investigation Config | 60 s, cooldown 20, 3 investigators | Long search with alerted clips/speed. |
| Combat Downgrade Delay | 3 s | Minimum LOS loss before Combat → Alert Investigation at the last known position. |
| Show Situation Debug Info | off | On-screen list (Editor). |

Escalation: a report of a higher type upgrades the existing situation (`EscalateSituation`); Combat that loses the player for longer than the delay downgrades to Alert Investigation at `GlobalLastKnownPlayerPos`; expired situations end (`OnSituationEnded` clears the guards' keys) and guards return to Ambient. `OnHighestSituationChanged` drives music/HUD.

**Expected result:** Overlay shows one situation per stimulus with participants/investigators; timings follow the configs.

### Workflow: What the guard does (state machine keys)

Generated machine (Stealth patch): `Investigate` (bool) enters **Investigate**: Turn To Face → non-alerted: Move to `InvestigatePosition` (when `MoveToInvestigatePos`) → Act (look-around clip from `NormalLookAround` key) → Wait 5 s; alerted (`IsAlertedInvestigation`): Override Locomotion (alerted idle/walk/run clips keys) → Move fast → Act (`AlertedLookAround`) → **Search**: FindSearchPoint (radius 10, cells reserved per guard) → Move → Act → Wait, looping; `RestartInvestigation` trigger restarts on a new stimulus; `Investigate` false → Ambient. `WarnTarget` set → **Warn** (Follow when `FollowWhileWarning`, else Face Target). `IsTargetSet` → Combat (Melee/Shooter patch). Add your own transitions on these keys (e.g. an alarm-pull state when `IsAlertedInvestigation`) in a duplicated machine; the `FindSearchPointStateAsset` **Search Radius** controls how wide searches roam.

**Expected result:** Scene-view label reads `Default Behavior > Investigate > Search > Move` during an alert search.

## Verification

- Guard fields match the intended numbers (`AwarenessSystem`, `AIVision`, `AIHearing` serialized values).
- `StealthAIManager` radii/configs as intended; `StealthSoundHandler` mappings cover the sound types used.
- Play with debug overlay: situations appear/escalate/end on schedule; `AwarenessSystem.CurrentAwareness` rises at the expected rate (log it).
- No Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FS_StealthSystem.AwarenessSystem` | MonoBehaviour | Rates above; `CurrentAwareness`, `SetAwareness`, `PauseDecay`. |
| `FS_StealthSystem.AIVision` / `AIHearing` | MonoBehaviours | Volume sizes; attenuation. |
| `FS_StealthSystem.StealthSoundHandler` / `SoundPriorityMapping` | MonoBehaviour (player root) | `GameSoundType` → `StimulusPriority`. |
| `FS_StealthSystem.StealthAIManager` | MonoBehaviour (player root) | Radii, configs; `CreateOrJoinSituation`, `EscalateSituation`, `EndSituation`, `ReportPlayerSpotted`, `ReportHitByPlayer`, `ReportPlayerLost`, `HighestSituationType`, `OnHighestSituationChanged`. |
| `FS_StealthSystem.SituationType` / `SituationConfig` / `Situation` | types | Priority order, timings, participants. |
| `FS_StealthSystem.Stimulus` / `StimulusPriority` / `StimulusModality` | types | Visual / Audio / Damage / Touch stimuli. |
| `FS_StealthSystem.StealthBlackboardKeys` | consts | Keys for custom transitions. |
| `FS_StealthSystem.SearchPositionGenerator.TryGetSearchPosition` | static | Search-point sampling used by `FindSearchPointState`. |
| `FS_ThirdPerson.FootStepEffects` (footstep radius, crouch multiplier) | MonoBehaviour (player) | Player noise. |

## Common issues

- **Symptom:** Detected the instant I peek. **Cause:** Inside Instant Detection Distance or touch volume; alerted multiplier active. **Fix:** Raise distance thresholds / lower speeds; end the situation first.
- **Symptom:** Whole map converges. **Cause:** Radii/propagation too large. **Fix:** Lower Inner/Outer Alert and Propagation radii.
- **Symptom:** Guards never give up. **Cause:** Long Alert Investigation duration, or the player keeps re-triggering (footsteps). **Fix:** Shorten duration; crouch-walk away.
- **Symptom:** Guards forget instantly after LOS loss. **Cause:** Combat Downgrade Delay tiny / Combat Sense Radius small. **Fix:** Raise both.
- **Symptom:** Gunshot only causes a casual look. **Cause:** Mapping set to Low. **Fix:** Gunshot → High.
- **Symptom:** No overlay. **Cause:** Debug only in the Editor. **Fix:** Expected.

## Boundaries

- Awareness is a single scalar per guard; no separate suspicion/alert meters.
- Sound has no occlusion model beyond NavMesh-path distance.
- Situation configs are global per scene (on `StealthAIManager`), not per guard.
- Escalation rules are code-defined; only their parameters are exposed.
