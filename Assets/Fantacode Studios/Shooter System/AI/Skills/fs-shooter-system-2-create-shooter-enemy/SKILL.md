---
name: fs-shooter-system-2-create-shooter-enemy
description: "Use this skill whenever the user wants enemies that shoot, take cover, flank or patrol with FS Shooter System 2 — e.g. 'add an enemy with a gun', 'make a sniper that stays back', 'enemies should take cover more', 'they never flank me', 'the enemy shoots too accurately', 'guard that patrols then engages'. Covers building AI with the FS Systems window (Shooter AI, generated Chase / Shoot / Cover / Flank machine), the Shooter Enemy prefab, vision and target layers, tuning ShooterAIController, CombatAIController and ShooterAIManager flanking, and patrols. Do NOT use for the player (see fs-shooter-system-2-create-shooter-player), weapon data (see fs-shooter-system-2-create-weapons-and-ammo), cover geometry (see fs-shooter-system-2-use-cover-system) or scripted moments (see fs-shooter-system-2-author-ai-behavior). When in doubt whether a ranged-enemy or AI-shooter request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Create Shooter Enemies

Ranged AI are `AIController` characters running a generated state machine whose Combat branch is **Ranged Combat**: `Chase` (move to the target position) → `Shoot` when in preferred range or the target is unreachable → `Cover` when the line of sight is blocked and cover is available → back to `Shoot`; `Flank` whenever `ShooterAIManager` sets `FlankRequested`. Shooting itself is the same `ShooterFighter` the player uses, driven by `ShooterAIController` (duration, aim error, fire interval, charged shots, cover scoring) and `CombatAIController` (alert range, preferred range, leash). Enemies see the player through `SimpleVision`, register with `CombatAIManager` on the player root, and `ShooterAIManager` coordinates flanking. This skill creates and tunes them.

## When to use this skill

- "Add an enemy that shoots at me."
- "Sniper on the roof that keeps distance."
- "Enemies should duck behind cover / flank when I hide."
- "Enemies hit me every shot — make them miss more."
- "Guard patrols the corridor until he sees me."
- "Enemy has no gun / never fires."

Not for:
- The player — `fs-shooter-system-2-create-shooter-player`.
- Which gun and ammo (weapon assets) — `fs-shooter-system-2-create-weapons-and-ammo`.
- Placing cover objects — `fs-shooter-system-2-use-cover-system`.
- Intro/triggered behaviours, waypoint routes in depth — `fs-shooter-system-2-author-ai-behavior`.

## Prerequisites

- Installed: `System.Type.GetType("FS_ShooterSystem.ShooterAIController, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- Layers `Enemy`, `Player`, `VisionSensor`, `Cover`, `RagdollBone` (Import Project Settings).
- A shooter player already in the scene (`CombatAIManager`, `ShooterAIManager`, `ShooterSettings` live on its root).
- Baked NavMesh over the enemy's area.
- Custom model: humanoid `Animator` with valid avatar.

## Quick start

1. `Tools > FS System > FS Window` → Setup → character type **AI** → tick **Shooter AI** → drop the humanoid model → **Setup**.
2. The root `FS AI` (layer `Enemy`) gets `ShooterFighter`, `ShooterAIController`, `CombatAIController` (`AICombatType` Ranged), `FighterCore` (target layer `Player`, `PreferredFightingRange` from the donor), a `VisionSensor` child with `SimpleVision`, `NavMeshAgent`, and `Assets/Generated/AI State Machines/FS AI StateMachine.asset` on `AIController`.
3. `FighterCore` → **Default Weapon** = a `ShooterWeapon` (e.g. `Demo/Weapon/Pistol.asset`); `ShooterFighter` → **Has Infinite Ammo** on for AI unless you want them to run dry.
4. Place on the NavMesh, Play, step into view.

Expected observable result: the Scene-view label above the AI shows `Combat > Ranged Combat > Chase`/`Shoot`; the enemy aims and fires at the player, ducks to `Cover` when its line of sight is blocked and cover exists; player health drops (requires player bone colliders — damage skill).

## Workflows

### Workflow: Build a shooter AI (FS Systems window)

What Setup does (`FSSystemsSetupEditorWindow.SetupAI`, `FSSystemsSetupShooter.ShooterSystemAISetup`, `ShooterStateMachinePatch`): instantiates `AI System/Resources/AI Controller.prefab` → `FS AI`, copies components from `Shooter System/Resources/Shooter AI Controller.prefab` (including the `VisionSensor` child and `CoverHandler`), sets `targetLayer = Player`, generates the state machine from `Base AI State Machine` with the **Shooter System** patch (adds `Ranged Combat` = Chase/Shoot/Cover/Flank, Combat Core's Getting Hit/Knocked Down). Ticking other AI systems in the same Setup (Melee Combat AI, Parkour and Climbing AI, …) applies their patches too — a melee+ranged AI switches by `CombatAIController.AICombatType` and backup weapons.

Bone damage colliders are **not** added by Setup — run the ragdoll/damage setup on the enemy so the player's bullets register (damage skill). The drop-in `Prefabs/Shooter Enemy.prefab` already has them (`ShooterBoneDamageHandler` on bones), 20 HP, `Pistol`, `PatrolSettings`, and `Demo/AI StateMachine/Shooter StateMachine.asset`.

**Expected result:** As Quick start; rename freely (generated asset keeps the generation-time name).

### Workflow: Tune shooting behaviour

`ShooterAIController` on the AI root:
- **Use Shoot Duration** (on), **Min/Max Shoot Time** (3–6 s) — how long a `Shoot` bout lasts before considering cover.
- **Aim Error** (0.5 m) — random offset of the aim point; 0 = perfect. Closer targets are hit more regardless.
- **Has Fire Interval** + **Min/Max Fire Interval** (3–6 s) — deliberate shooters (archers) instead of the weapon's fire rate.
- **Use Charged Shots**, **Min/Max Charge Time** (1–1.5 s) — for `isChargedWeapon` weapons (bow).
- **Cover**: **Min/Max Cover Time** (1–3 s), **Cover Approach Speed** (3.5), scoring weights (distance, distance-to-target, occupant 10), **Cover Avoid Distance** (2 — don't cover when the target is this close), **Min Cover Distance** (2), **Cover Range** (10), **Should Check For Cover** (off = never covers; use in open levels).

`CombatAIController`: **Alert Range** (20 m; allies with LOS get the target after 0.5 s), **Preferred Fighting Range** (on `FighterCore`; `Chase` stops and `Shoot` starts inside it; snipers 25–30 m, grunts 10–15 m), **Has Movement Range** + **Movement Range** (leash), **Use Last Seen Position** (Stealth only), **Backup Melee Weapon** (+ preferred range) for a ranged AI that switches to melee up close when Melee Combat is installed.

`AIController`: **Walk/Run Speed**. Generated `Chase` state: Move Speed 4, Stopping Distance 1.

**Expected result:** Play test shows the new cadence and range.

### Workflow: Squad flanking

`ShooterAIManager` on the **player root**: **Should Flank** (on), **Time To Start Flanking** (4 s after the whole squad loses sight → one AI gets `FlankRequested`), **Time To Reposition While Engaged** (4 s an individual AI without LOS while others still see the target; keep above Max Cover Time), **Immediate Flank Distance Threshold** (3 m — losing sight of a target this close flanks at once). The `Flank` state moves to the last known/target position at **Flank Speed** (2) then returns to `Shoot`.

**Expected result:** Hide behind cover for > 4 s: one enemy's label shows `Flank` and it comes around.

### Workflow: Vision, targets and patrol

- **Vision**: `VisionSensor` child (`SimpleVision`: trigger radius, `fov` 180, `eyeHeight` 1.8, `targetLayer` Player+PlayerAlly). Shrink for oblivious guards.
- **Other acquisition**: being shot (`changeTargetWhenTakingHit`), gunfire heard (`ShootSoundEmitter` on the shooter with **Alert Enemies** — weapon `soundRange`), ally alert, `TargetHandler` → Default Target, `TriggerAuthoredAIBehavior` volumes (`Prefabs/Trigger Authored AI Behavior.prefab`).
- **Patrol**: `PatrolSettings` → **Patrol Waypoints** = parent transform whose children are the route (see `Way Points` in demo 1), **Patrol Speed** 2; Ambient `Idle → Patrol` fires automatically.
- **Ally/NPC**: root layer `PlayerAlly`/`NPC`, `targetLayer` `Enemy`; `ShooterAIManager` only coordinates AIs targeting the player.

**Expected result:** `Blackboard["IsTargetSet"]` true on sight; label enters `Combat`.

### Workflow: Give the AI cover to use

AI cover points come from **trigger colliders on the `Cover` layer** within **Cover Range**: drop `CoverSystem/Resources/Cover Barrier.prefab` (a set of cover cubes) or `Cover Collider.prefab` (single trigger) around crates/walls, or put your own trigger collider on layer `Cover`. Points are scored by distance, distance to target and occupancy (one AI per point). See the cover skill for geometry rules.

**Expected result:** With cover in range and LOS blocked, the label shows `Cover` and the AI crouches behind it (`CoverHandler.InCover`).

## Verification

```csharp
var ai = Object.FindFirstObjectByType<FS_ShooterSystem.ShooterAIController>();
var fc = ai ? ai.GetComponent<FS_CombatCore.FighterCore>() : null;
var cai = ai ? ai.GetComponent<FS_CombatCore.CombatAIController>() : null;
bool ok = ai && fc && cai && cai.AICombatType == FS_CombatCore.AICombatType.Ranged
    && fc.defaultWeapon is FS_ShooterSystem.ShooterWeapon
    && ai.GetComponent<FS_Core.AIController>().StateMachineAsset != null
    && ai.gameObject.layer == LayerMask.NameToLayer("Enemy")
    && (fc.targetLayer.value & (1 << LayerMask.NameToLayer("Player"))) != 0
    && ai.GetComponentInChildren<FS_Core.SimpleVision>() != null
    && UnityEngine.AI.NavMesh.SamplePosition(ai.transform.position, out _, 1f, UnityEngine.AI.NavMesh.AllAreas);
Debug.Log(ok ? "PASS" : "FAIL");
```

Play: `ai.GetComponent<FS_ShooterSystem.ShooterFighter>().IsAiming` true in `Shoot`; `IsStateActive<FS_ShooterSystem.ShootState>()`; no Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| FS Window → Setup → AI → **Shooter AI** | EditorWindow | Builds the AI + generates the machine. |
| `Prefabs/Shooter Enemy.prefab` | prefab | Ready enemy (Default Character). |
| `FS_ShooterSystem.ShooterAIController` | MonoBehaviour | Shoot/cover settings; `HasClearShot()`, `IsCoverAvailable`, `ShouldCheckForCover`. |
| `FS_CombatCore.CombatAIController` | MonoBehaviour | `AlertRange`, `AICombatType`, backup weapons, movement range, `IsTargetVisible`, `DistanceToTarget`. |
| `FS_ShooterSystem.ShooterAIManager` (player root) | singleton | Flank timers; `ShooterBlackboardKeys.FlankRequested`. |
| `FS_ShooterSystem.ShootState` / `CoverState` / `FlankState` (+ `…StateAsset`) | AI states (`AIStateModule "Shooter"`) | The Ranged Combat states. |
| `FS_ShooterSystem.AICoverPointFinder` | class | Scores `Cover`-layer trigger colliders. |
| `FS_Core.SimpleVision`, `FS_Core.TargetHandler`, `FS_Core.PatrolSettings` | MonoBehaviours | Vision / default target / patrol. |
| Blackboard keys | strings | `IsTargetSet`, `IsTargetInPreferredRange`, `IsTargetReachable`, `FlankRequested`, `DistanceToTarget`, `TargetPosition`. |

## Common issues

- **Symptom:** Enemy aims but never fires. **Cause:** No ammo (`TotalAmmoCount` 0 and `hasInfiniteAmmo` off), or `Has Fire Interval` long. **Fix:** Infinite ammo on AI; check intervals.
- **Symptom:** Player never takes damage. **Cause:** Player has no `RagdollBone` bone colliders. **Fix:** Damage skill (ragdoll/damage setup on the player).
- **Symptom:** Enemy walks up to point-blank. **Cause:** `PreferredFightingRange` small. **Fix:** Raise it (and `Cover Avoid Distance` accordingly).
- **Symptom:** Never takes cover. **Cause:** No `Cover`-layer triggers in range, `Should Check For Cover` off, or LOS never blocked. **Fix:** Add cover prefabs; check settings.
- **Symptom:** Never flanks. **Cause:** `Should Flank` off or the squad still sees you. **Fix:** Player root `ShooterAIManager`.
- **Symptom:** `CombatAIManager.i` / `ShooterAIManager.Instance` null. **Cause:** No shooter player root in the scene. **Fix:** Add the player first.
- **Symptom:** Hits me every time. **Fix:** Raise **Aim Error**.

## Boundaries

- AI needs a NavMesh.
- Melee fallback needs FS Melee Combat System 2.
- Awareness meters, investigation, last-seen searching belong to FS Stealth System (`Use Last Seen Position` exists for it).
- Vehicles/turrets are outside this package.
