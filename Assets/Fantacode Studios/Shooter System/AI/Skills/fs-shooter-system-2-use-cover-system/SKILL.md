---
name: fs-shooter-system-2-use-cover-system
description: "Use this skill whenever the user wants characters to take cover, peek and shoot from behind obstacles with FS Shooter System 2 — e.g. 'let the player take cover behind this crate', 'cover button does nothing', 'add cover spots for the enemies', 'AI never uses my walls as cover', 'low wall vs high wall cover', 'exit cover when aiming', 'cover on mobile'. Covers the player CoverController (cover input, detection probes, chase-to-cover, low/high cover, shooting from cover), the Cover layer and Cover Barrier / Cover Collider prefabs that give AI cover points, ShooterAIController cover settings, and the cover input/UI. Do NOT use for enemy shooting behaviour in general (see fs-shooter-system-2-create-shooter-enemy) or for building the player (see fs-shooter-system-2-create-shooter-player). When in doubt whether a cover, crouch-behind-wall or peek request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Use the Cover System

Cover has two independent halves. The **player** side (`CoverController`, `CoverHandler`, `CoverInputManager` from the bundled CoverSystem) probes the world in front of the character when **Cover** (G) is pressed, runs to the nearest vertical surface at least `minCoverHeight` tall, snaps against it, plays low or high cover animation by surface height, lets the player slide along it and pops out to aim/shoot. The **AI** side (`CoverState` + `AICoverPointFinder` in the Shooter package) looks for **trigger colliders on the `Cover` layer** within range and scores them; `CoverSystem/Resources/Cover Barrier.prefab` and `Cover Collider.prefab` are the ready-made markers. This skill sets up both.

## When to use this skill

- "Let the player duck behind this crate."
- "G doesn't put me in cover."
- "Enemies don't use my walls as cover."
- "Mark this wall as cover for AI."
- "Peek and shoot from cover."
- "Cover button on touch."

Not for:
- AI shoot/flank cadence — `fs-shooter-system-2-create-shooter-enemy`.
- Player creation — `fs-shooter-system-2-create-shooter-player`.

## Prerequisites

- Installed: `System.Type.GetType("FS_CoverSystem.CoverController, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- Layer `Cover` exists (Import Project Settings also puts the two cover prefabs on it).
- Player built with Shooter (has `CoverController`, `CoverHandler`, `CoverInputManager`); enemies with `ShooterAIController` + `CoverHandler`.
- Obstacles have solid colliders on a layer in `FSSettings.ObstacleLayer` (default `Default`).

## Quick start

Goal: player takes cover behind a 1.2 m crate.

1. Crate: `BoxCollider`, layer `Default`, ≥ 1 m tall (`minCoverHeight`).
2. Play, stand within **Cover Check Distance** (4 m) facing the crate, press **G**.
3. The character runs to the crate face (`ChaseMode`), snaps against it (`CoverHandler.InCover`), crouches (low cover because the crate is under `playerHeightForCover`); A/D slides along; RMB aims out; G again leaves.

Expected observable result: `PlayerController.CurrentSystemState == SystemState.Cover`; animator `coverMode` true, `CoverType` ≈ 0 for low cover; `CoverController.IsInCover` true.

## Workflows

### Workflow: Tune player cover detection

`Player` → `CoverController`:
- **Enable Cover**; **Cover Check Distance** (4); **Player Height For Cover** (2.6 — surfaces taller than this count as full-height cover, animator `CoverType` blends between low and high).
- **Only Cover In Cover Tags** (off): when on, only obstacles on the `Cover` layer or tagged `Cover` qualify — turn on for hand-picked cover in cluttered levels.
- Probes: **Probe Radius** 0.2, **Probe Back Offset** 0.2, **Probe Start Height** 0.3, **Probe Height Step** 0.15, **Max Probe Height** 2.2, **Min Cover Height** 1 (shorter = a step, not cover), **Max Surface Tilt Angle** 35 (excludes ramps), **Surface Depth Tolerance** 0.4 / **Surface Continuity Angle** 45 (crate in front of a wall = two surfaces), **Cover Stand Gap** 0.05.
- Approach: **Cover Approach Speed** 4, **Cover Arrival Tolerance** 0.25, **Approach Timeout** 4 s, **Approach Stuck Timeout** 0.6 s (cancels an unreachable cover).
- **Draw Cover Probes** for debugging in the Scene view.
`CoverHandler`: **Collider Height In Cover** 1.2 / **Collider Center In Cover** (capsule while crouched).

**Expected result:** G finds the intended surface; probes drawn as spheres when debugging.

### Workflow: Shooting from cover

While in cover the equipped system still updates (`playerController.CurrentEquippedSystem?.HandleUpdate()`), so aiming (RMB) works from cover; systems that need the whole body call `CoverController.ExitCoverForAction()` / `ReturnToCoverAfterAction()` (throwables do this automatically). `LastCoverPosition`/`WasInCover` remember where to return.

**Expected result:** Aim from cover pops the upper body out; releasing returns.

### Workflow: Give AI cover points

`AICoverPointFinder` overlaps a sphere of **Cover Range** (10, capped by the AI's movement range) against `LayerMask.GetMask("Cover")` with trigger interaction on, so cover markers must be colliders on layer `Cover` (triggers are fine and preferred).
1. Drop `CoverSystem/Resources/Cover Barrier.prefab` (several cover cubes, children on `Cover`) or `Cover Collider.prefab` (one trigger box) onto crates/walls; or add a `BoxCollider` (Is Trigger) on layer `Cover` around your own obstacle.
2. Keep the marker roughly the obstacle's footprint; the AI stands behind it relative to the target (`GetBestCoverPoint`), one AI per point (occupancy).
3. Tune on the AI's `ShooterAIController`: **Should Check For Cover**, **Cover Range**, **Min Cover Distance** (2 — markers closer to the target than this are skipped), **Cover Avoid Distance** (2 — no cover when the target is that close), **Min/Max Cover Time** (1–3 s), **Cover Approach Speed** (3.5), **Max Cover Height Multiplier** (3), scoring weights (**Distance Weight** 1, **Distance To Target Weight** 1, **Occupant Weight** 10). Markers farther from the target than `FighterCore.PreferredFightingRange` are ignored.
4. `CoverState.ShouldCover` triggers only when the AI's line of sight to the target is blocked and it is farther than Cover Avoid Distance.

**Expected result:** With markers around and LOS blocked, the AI's state label shows `Ranged Combat > Cover`, it runs to a marker and crouches (`CoverHandler.InCover`), then returns to `Shoot`.

### Workflow: Cover input and touch

`CoverInputManager.coverInput` (G; action `Combat/Cover` — G / Gamepad East with the new Input System). Touch: `Game UI/Cover Mobile Controller` (button `Cover`) or the `Cover` button on `Shooter Mobile Controller`; buttons bind by name (`CoverUI.CacheButtons`).

## Verification

- Player: `pc.GetComponent<FS_CoverSystem.CoverController>().enableCover == true`; in Play after G near a wall `IsInCover == true` and `PlayerController.CurrentSystemState == SystemState.Cover`.
- AI markers: `Physics.OverlapSphere(aiPos, 10f, LayerMask.GetMask("Cover"), QueryTriggerInteraction.Collide).Length > 0`.
- AI: `ShooterAIController.ShouldCheckForCover == true`; in Play `IsStateActive<FS_ShooterSystem.CoverState>()` occurs when LOS is blocked.
- No Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FS_CoverSystem.CoverController` (player) | `SystemBase` (Priority 4, `SystemState.Cover`) | `enableCover`, detection/approach fields, `IsInCover`, `StartCover()/StopCover()`, `TryFindCover(maxDist, out CoverHit)`, `ExitCoverForAction()/ReturnToCoverAfterAction()`. |
| `FS_CoverSystem.CoverHandler` | MonoBehaviour (player and AI) | `InCover`, `GoToCover()/GoOutOfCover()`, capsule size in cover. |
| `FS_CoverSystem.CoverInputManager` | input manager | `coverInput` (G), `CoverPressed`. |
| `CoverSystem/Resources/Cover Barrier.prefab`, `Cover Collider.prefab` | prefabs (layer `Cover`, triggers) | AI cover markers. |
| `FS_ShooterSystem.AICoverPointFinder` / `CoverPoint` | class | Finds/scores cover; `RegisterCoverPoint` occupancy. |
| `FS_ShooterSystem.CoverState` / `CoverStateAsset` | AI state | Cover bout; `ShouldCover(owner)`. |
| `FS_ShooterSystem.ShooterAIController` cover fields | MonoBehaviour | Range, times, weights, `IsCoverAvailable`. |
| Animator params `coverMode`, `CoverType`, `CoverDir` | Animator | Cover blend state (Third Person Controller animator merge). |

## Common issues

- **Symptom:** G does nothing. **Cause:** No qualifying surface within 4 m (too short, tilted, not on `ObstacleLayer`), `Only Cover In Cover Tags` on with an unmarked obstacle, or the player is busy/aiming. **Fix:** Check height ≥ 1 m and layer; enable probe drawing.
- **Symptom:** Character runs then gives up. **Cause:** Approach timeout/stuck (blocked path). **Fix:** Clear the approach path or move the cover.
- **Symptom:** AI ignores cover. **Cause:** No `Cover`-layer triggers in range, markers closer to the target than Min Cover Distance / beyond preferred range, or LOS never blocked. **Fix:** Place markers; adjust ranges.
- **Symptom:** Two AIs on one crate. **Cause:** Marker too large (one point) vs several. **Fix:** Use `Cover Barrier` (multiple cubes) or several colliders.
- **Symptom:** Cover prefabs on wrong layer after import. **Fix:** Import Project Settings (runs the Shooter `OnInstallation` hook).

## Boundaries

- Player cover is surface-based (no authored cover nodes); AI cover is marker-based — the two do not share data.
- No blind fire or corner-lean animations beyond low/high cover and slide.
- Cover-to-cover vaulting is not included (Parkour package handles vaults).
- Destructible cover is not tracked; remove/disable the marker when the obstacle breaks.
