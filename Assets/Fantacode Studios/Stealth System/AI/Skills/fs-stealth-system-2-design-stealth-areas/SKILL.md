---
name: fs-stealth-system-2-design-stealth-areas
description: "Use this skill whenever the user is designing a stealth level with FS Stealth System 2 — e.g. 'guards should warn me before attacking in the courtyard', 'this whole level is hostile', 'tall grass hides me when crouched', 'darkness should hide the player', 'a locker to hide in', 'a dumpster to dump bodies', 'guards should investigate the open door', 'a brick to distract them'. Covers RestrictedArea / HostileArea / HidingArea triggers and Is Suspicious By Default, StealthLight + LightVision concealment, EnterExitHotspot hiding bins, Carryable bodies with CarryHotspot / DumpHotspot, VisualStimulus props (Deadbody layer) and throwable distractions. Do NOT use for building characters (see fs-stealth-system-2-create-stealth-player / …-create-stealth-ai) or detection tuning (…-tune-detection-and-alerts). When in doubt whether a level-design, hiding-spot, restricted-zone or distraction request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Stealth System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.0"
  skill-version: "1.1.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-stealth-system"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-15"
---

# Design Stealth Areas

Level rules are components on trigger volumes, lights and props. **Where guards care:** the player's `SuspicionProfile` is suspicious everywhere when **Is Suspicious By Default** is on; turn it off to make the world public and mark zones — `RestrictedArea` (guards warn, then escalate; a Trespassing situation) and `HostileArea` (instant threat, attack on sight); either can **Cover Entire Scene**. **Where the player is hidden:** `HidingArea` volumes (tall grass — hidden while crouched, or always), `EnterExitHotspot`s (bins, lockers — the player is concealed inside) and, with `LightVision` enabled on the player, shadow: only lights carrying `StealthLight` count, so every lamp that should reveal the player needs one. **What else guards notice:** any `VisualStimulus` on the `Deadbody` layer — dead guards spawn one automatically (Alert Object) plus a `CarryHotspot`, props you mark as Suspicious Object (open door, moved crate) start suspicious investigations — and sounds: thrown `Brick`s (`ThrowableItemObject` → `ThrowImpact`), gunshots, loud footsteps. **Bodies:** carry with **E** (`CarryHandler`), drop with **Y**, dump into a `DumpHotspot` (`Body Dumpster`) so other guards stop finding them.

## When to use this skill

- "Courtyard is off-limits: guards warn first."
- "Inside the base every guard attacks on sight."
- "Grass patches hide a crouching player."
- "Dark corridors hide the player; lamps reveal him."
- "A locker/bin to hide in; a dumpster for bodies."
- "Guards should notice the open safe / a body."
- "Bricks to throw as distractions."

Not for:
- Player/guard components — `fs-stealth-system-2-create-stealth-player` / `fs-stealth-system-2-create-stealth-ai`.
- Awareness rates, hearing math, alert radii, situation timers — `fs-stealth-system-2-tune-detection-and-alerts`.
- Takedown animations — FS Melee Combat System 2.

## Prerequisites

- Installed: `System.Type.GetType("FS_StealthSystem.RestrictedArea, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220).
- A stealth player (with `SuspicionProfile`, `LightVision`, `CarryHandler`, `EnterExitInteractionHandler` — the last two come from the **Carry** toggle in the FS Systems window) and guards in the scene; NavMesh baked.
- Layers `Player`, `Deadbody`, `OcuppantObject` exist (Import Project Settings).

## Quick start

Goal: a public street, a restricted yard where guards warn, and a grass patch to hide in.

1. `FS Player/Player` → `SuspicionProfile` → untick **Is Suspicious By Default** (street is public).
2. Empty `Yard` + `BoxCollider` (**Is Trigger**) covering the yard → Add Component **Restricted Area** (leave **Covers Entire Scene** off).
3. Empty `Grass` + `BoxCollider` (Is Trigger) over the bushes → Add Component **Hiding Area** (**Requires Crouch** ✓); add tall-grass meshes inside.
4. Play: walk the street in front of a guard (ignored), enter the yard (guard warns and follows), crouch into the grass (guard loses you).

Expected observable result: `SuspicionProfile.IsSuspicious` false on the street, true in the yard; entering the yard in view creates a `TrespassingRestrictedArea` situation (`StealthAIManager.Instance.HighestSituationType`), the guard barks `warning_1…` and follows; crouched in the grass `AIVision.CheckVision().IsHidden` is true and awareness decays; no Console errors.

## Workflows

### Workflow: Restricted, hostile and public zones

| Component | Fields | Guard reaction when the player is inside and seen |
|---|---|---|
| `RestrictedArea` | Covers Entire Scene | `ShouldWarn` → Trespassing situation: leader confronts (Warn state: follow or face), barks `warning_N`, escalates to alert/combat on repeated warnings or if the player stays; leaving ends it. `WarnsFirst`. |
| `HostileArea` | Covers Entire Scene | Instant threat: awareness fills → target set, Combat situation. Same effect as Is Suspicious By Default for that volume. |
| (none) + Is Suspicious By Default off | — | Public: guards ignore the player (touch and hostile props aside) unless a visible weapon is equipped (`WeaponSuspicionFactor`, instant). |

Volumes are trigger colliders that register the player's `SuspicionProfile` via `ISuspicionFactor` on enter/exit (`ContainsPosition` helpers for code). Nest as needed; the most severe active factor wins (`IsInstantThreat` beats `ShouldWarn`).

**Expected result:** `profile.ShouldWarn` inside restricted, `profile.IsInstantThreat` inside hostile, `profile.IsSuspicious` false outside both when default is off.

### Workflow: Hiding areas and hiding spots

- **HidingArea** (trigger volume; **Requires Crouch** true): while the player's `VisualStimulus` is inside (and crouching if required) vision reports `IsHidden` — awareness decays even in the volume. Use for grass, fog, under tables. Multiple areas stack.
- **Hiding spot** (`Carry System/Prefabs/Hiding Bin.prefab`, or a prop with `EnterExitHotspot` from Interaction Core): `Interaction Label` "Hide", `Exit Label`, enter/idle/exit animations, optional occupant placement; the player's `EnterExitInteractionHandler.IsInsideSpot` makes `VisualStimulus.IsConcealed` true → invisible to guards. Place on layer `OcuppantObject` like the prefab; interact with **E**.

**Expected result:** `HidingArea.IsHidden(stimulus)` true while crouched inside; `stimulus.IsConcealed` true inside a bin.

### Workflow: Light-based concealment

1. `FS Player/Player` → `LightVision` → **Use Light Vision** ✓ (tune Falloff Sharpness, reference intensities per light type: directional 1 lux, spot 60 cd, point 40 cd; **Use Light Color** weights by perceived brightness).
2. Add **StealthLight** (`Visibility Weight` 1) to every `Light` that should reveal the player — unlisted lights conceal nothing; the sun counts too. Demo 3 moves lights with `StealthLightMotion`.
3. Visibility (0–1) is sampled at the player's `VisualStimulus` **Vision Points** with line-of-sight to each light and multiplies awareness gain; 0 = unseen even in the primary volume.

**Expected result:** `LightVision.GetVisibility(playerStimulus)` ≈ 0 in shadow, → 1 under a weighted lamp; meters fill slowly in the dark.

### Workflow: Bodies, props and distractions

- **Dead guards**: on death `StealthAIController` spawns `StealthSettings.DeadBodyPrefab` (`VisualStimulus` Alert Object, layer `Deadbody`, raycast height offset) and `CarryHotspot` on the hips. Guards that see the body raise an Alert Investigation.
- **Carry & dump**: `Carryable` (already on FS Window guards) + `CarryHandler` on the player: **E** lifts, **Y** drops, walk to a `DumpHotspot` (`Body Dumpster.prefab`: capacity, allowed `CarryableData`, open animation) and **E** to dump; dumped bodies stop being stimuli. `Body Dumpster` also works as a hiding spot.
- **Suspicious props**: add `VisualStimulus` (Type Suspicious Object → suspicious investigation; Alert Object → alert investigation), set Vision Points/Importance, put the collider on the `Deadbody` layer (any layer in `StealthSettings.VisualStimulusLayer`), enable the component when the prop becomes suspicious (door opened); `Resolved` stops re-investigation.
- **Distractions**: `Third Person Controller/Prefabs/Brick.prefab` via an `EquipHotspot` (Item = the brick throwable); throwing (**Mouse 0**) emits a `ThrowImpact` sound (priority Low by default → suspicious investigation at the impact point; a guard that watched the throw barks Saw Throw instead). Custom noises: `GameSoundEvents.Emit(new GameSoundEvent { position, radius, type })`.

**Expected result:** A body in view → guards enter `Investigate` (alerted) and call others; dumped body → no reaction; brick impact → nearest guard walks to it and searches.

## Verification

- Zone volumes: trigger colliders with `RestrictedArea` / `HostileArea` / `HidingArea`; `SuspicionProfile.isSuspiciousByDefault` matches the design.
- Lights: every reveal light has `StealthLight`; `LightVision.useLightVision` true when used.
- Hiding spots on layer `OcuppantObject` with `EnterExitHotspot`; dumpsters with `DumpHotspot`.
- Play: `StealthAIManager.Instance.HighestSituationType` reads Trespassing in restricted, Combat in hostile; hidden checks true in grass/bins; no Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FS_StealthSystem.RestrictedArea` / `HostileArea` | `ISuspicionFactor` MonoBehaviours | `coversEntireScene`, static `ContainsPosition(pos)`. |
| `FS_StealthSystem.HidingArea` | MonoBehaviour | `requiresCrouch`; static `IsHidden(stimulus)`, `IsInsideAnyArea(stimulus)`. |
| `FS_StealthSystem.SuspicionProfile.AddFactor/RemoveFactor` | methods | Custom factors (disguise, alarm state). |
| `FS_StealthSystem.StealthLight` | MonoBehaviour on `Light` | `VisibilityWeight`, static `ActiveLights`. |
| `FS_StealthSystem.LightVision` | MonoBehaviour (player) | `useLightVision`, references; static `GetVisibility`. |
| `FS_StealthSystem.VisualStimulus` | MonoBehaviour | `SetStimulusType`, `Resolved`, `VisionPoints`, `Importance`, `DialogueBarkTag`. |
| `FS_InteractionSystem.EnterExitHotspot` / `CarryHotspot` / `DumpHotspot` / `Carryable` / `CarryableData` | Interaction Core / Carry System | Hiding spots, body pickup and disposal. |
| `FS_Core.GameSoundEvents.Emit` / `GameSoundType` (Gunshot, Footstep, ThrowImpact, Explosion) | static | Audible events. |
| `Carry System/Prefabs/{Hiding Bin, Body Dumpster}.prefab`, `Third Person Controller/Prefabs/Brick.prefab` | prefabs | Ready-made spots and distraction. |

## Common issues

- **Symptom:** Guards attack in the "public" street. **Cause:** Is Suspicious By Default still on, a weapon is drawn, or a Hostile Area covers the scene. **Fix:** Untick / holster / check `HostileArea.coversEntireScene`.
- **Symptom:** Restricted area never warns, goes straight to combat. **Cause:** Hostile area overlapping, or `IdentifiedPlayerAsThreat` already true from an earlier fight. **Fix:** Separate volumes; let situations end.
- **Symptom:** Grass doesn't hide. **Cause:** Standing (Requires Crouch), collider not a trigger, or player stimulus collider not entering it. **Fix:** Crouch; trigger; volume covers the player capsule.
- **Symptom:** Everything is dark → never detected. **Cause:** Light Vision on but no `StealthLight`s. **Fix:** Add `StealthLight` to lamps/sun.
- **Symptom:** Prop stimulus ignored. **Cause:** Collider layer not in `StealthSettings.VisualStimulusLayer`. **Fix:** Use `Deadbody` layer or extend the mask.
- **Symptom:** Body can't be picked up. **Cause:** **Carry** not ticked when the player was built (no `CarryHandler`), its **Sockets** empty, or `CarryHotspot` missing (custom death). **Fix:** Rebuild with **Carry** ticked; keep `StealthSettings` prefabs assigned.

## Boundaries

- Zones are static trigger volumes; no dynamic alarm-state system (toggle components or add factors from your code).
- Light sampling covers Unity `Light` components with `StealthLight` only; emissive materials and baked light are ignored.
- One occupant per hiding spot; dumpsters store `dumpCapacity` bodies.
- Sound design is by radius/type; no occlusion beyond the optional NavMesh-distance hearing.
