---
name: fs-shooter-system-2-setup-aiming-and-hand-ik
description: "Use this skill whenever the user wants to fix or tune how a character holds and aims a weapon with FS Shooter System 2 — e.g. 'the left hand floats off the rifle', 'hands don't grip my gun model', 'body doesn't turn toward the crosshair', 'aim too twitchy', 'set up IK for my new character', 'legacy vs new aiming', 'crosshair spread', 'aim assist on controller', 'aim camera too close'. Covers ShooterFighter Aiming Mode (New vs Legacy), Aim Rig Profile, per-weapon Upper Body IK and Support/Hold Hand IK, the Hand IK Setup (New Aiming) and legacy IK Setup windows (Play-mode, per-CharacterID references), aim camera, crosshair and aim assist. Do NOT use for weapon stats or ammo (see fs-shooter-system-2-create-weapons-and-ammo) or for putting the player in the scene (see fs-shooter-system-2-create-shooter-player). When in doubt whether a hand-placement, aiming-pose or crosshair request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Shooter System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.1"
  skill-version: "1.0.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-shooter-system/shooter-weapon"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-13"
---

# Set Up Aiming and Hand IK

Aiming is procedural: while `ShooterFighter.IsAiming`, the upper body rotates toward the aim point and the hands are placed on the weapon by IK. `ShooterFighter` → **Aiming Mode** chooses the implementation: **New** (default; 3-bone upper-body aim from the weapon's `Upper Body IK` settings plus per-hand `Support Hand IK` / `Hold Hand IK` grips, tuned with the **Hand IK Setup (New Aiming)** window) or **Legacy** (spine + arm alignment driven by an **Aim Rig Profile** and `ArmIkData`, tuned with the **IK Setup** window). Grip data lives on the **weapon asset**, with per-character overrides keyed by the character's `CharacterID`, so one weapon fits many rigs. This skill lets an agent fix hand placement for a new model or weapon, tune posture and camera, and configure crosshair and aim assist.

## When to use this skill

- "Support hand isn't on the foregrip / floats in the air."
- "New character model — hands don't match the pistol."
- "Character doesn't twist toward the crosshair / over-rotates."
- "Aiming pose looks stiff / snaps."
- "Crosshair spread and colors."
- "Gamepad aim assist."
- "Aim camera framing/zoom."

Not for:
- Fire rate, magazine, reload clips — `fs-shooter-system-2-create-weapons-and-ammo`.
- Building the player — `fs-shooter-system-2-create-shooter-player`.

## Prerequisites

- Installed: `System.Type.GetType("FS_ShooterSystem.HandIKSetupEditor, Assembly-CSharp-Editor") != null` or simply `FS_ShooterSystem.ShooterFighter` resolves (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- A scene with the shooter player (`ShooterFighter`) and the weapon on its wheel; the character has a `CharacterID` component (the FS Window adds it) — without it, edits write to the weapon's **Default** grip instead of a per-character override.
- The weapon prefab has `supportHandIkTarget` / `holdHandIkTarget` / `aimReference` transforms (`ShooterWeaponObject`).
- Both setup windows work **in Play mode with the weapon equipped**.

## Quick start

Goal: support hand on the foregrip for a new character (New aiming).

1. Press Play, equip the weapon, aim (RMB) so the pose is live.
2. `Tools > FS System > Shooter > Hand IK Setup (New Aiming)` → select the character → **Start Setup**.
3. Use the scene handles via **Hand Position**, **Hand Rotation**, **Elbow Position** (and **Reset All**) for the support hand until it grips; repeat for the hold hand if its IK is enabled.
4. Edits save to the weapon asset immediately (`AssetDatabase.SaveAssets`) as a `HandIKReference` for this `CharacterID` (or the Default when there is none). Stop Play — the data persists because it lives on the asset, not the scene.

Expected observable result: the weapon asset's **Support Hand IK → References** contains an entry for the character; aiming again in Play shows the hand on the grip; `ShooterFighter.ResolveHandIKData(support: true, aiming: true)` returns the saved offsets.

## Workflows

### Workflow: Choose the aiming mode and tune posture (New)

`ShooterFighter` → **Aiming Mode** = `New` (default).
Per weapon (`ShooterWeapon`):
- **Upper Body IK**: `spineWeight` 0.3, `chestWeight` 0.3, `upperChestWeight` 0.4 (fractions of the aim rotation), `yRotationOffset` (bladed stance), `rotationSmoothSpeed` 10.
- **Support Hand IK** / **Hold Hand IK** (`HandIKSettings`): `enableAim` (support on, hold off by default — turn hold on to fine-tune the trigger hand, off for pistols' off hand), `enableIdle` (grip while not aiming), `weight`, `weightBlendSpeed`, default `aimData`/`idleData` (position/rotation/elbow offsets in weapon space), `references` (per-character overrides — authored with the window).
- **Use Hand Ik For Idle**.
- **Aim Profile** (`AimRigProfile`, `Resources/Aim Rig Profile.asset` default): `maxHorizontalAngle`/`maxVerticalAngle` 70, `maxAimAngleFromForward` 75 (aim cone), `bodyRotationSpeed` 10, `ikBlendSpeed` 6, `minIKAimDistance` 3, head look weight 0.3, hand axis mapping (which hand-bone axis points along the weapon — change only for rigs whose hand bones are oriented differently). Create a custom one with **Create > Shooter System > Create Aim Rig Profile** and assign it per weapon.

**Expected result:** Character turns its torso toward the aim point up to the clamp, hands stay on the weapon, weights blend in over `ikBlendSpeed`.

### Workflow: Legacy aiming and the IK Setup window

`ShooterFighter` → Aiming Mode = `Legacy` for projects authored on the previous pipeline (`ArmIkData`: hold/support hand position/rotation + elbow poles; `idleIKData`/`aimIKData` on the weapon and `iKReferences` per character).
1. Play, equip, select the character → `Tools > FS System > Shooter > IK Setup` → **Start Setup**.
2. Toggle **Aim** / **Idle** pose, move the hand helpers, **Save Aim IK** / **Save Idle IK** (tick **Save as Default** to write the weapon default instead of a character reference); **Clear** resets.
3. Only one of the two windows may run at a time (each refuses to start while the other is active).

**Expected result:** Weapon `iKReferences` gains an entry for the character; aim/idle grips persist.

### Workflow: Aim camera, zoom and scope

Weapon: **Zoom While Aim** + **Aim Camera Settings** (`CameraSettings`: distance 1, framing offset (0.4, 1.5, 0), sensitivity 0.2…) applied by `ShooterController` on aim; **Allow Jumping While Aiming**. Scope: `ShooterWeaponObject.hasScope` + `scopeTarget` (camera position) + `scopeFov` (20) or `useCanvasImageForScope` + `canvasObject` (e.g. `Prefabs/8x Scope Canvas.prefab`). Third person: double-tap Aim (RMB) enters scope (`ShooterInputManger.useDoubleClickForScope`); first person: hold.

**Expected result:** `ShooterFighter.IsInScopeView` true with the scope FOV.

### Workflow: Crosshair and aim assist

- `Shooter UI` (under `Game UI`) carries `ShooterCrosshairController`: **Show Crosshair Only When Aiming**, line/dot rects, **Base Spread** 10 → **Max Spread** 30, **Spread Per Shot** 8, **Move Spread** 15, **Recover Speed** 50, hit indicator distance/duration, colors (normal / aiming / aim-on-target red). `Prefabs/Crosshair Controller.prefab` is a standalone variant.
- `ShooterController` → **Enable Aim Assist**, **Aim Assist Strength** (0.2), **Aim Assist Radius** (0.4), **Lock On Max Angle** (45) — pulls the aim point toward `FighterCore` targets inside the radius/angle.
- `ShooterController` → **Equip On Aim** with a weapon: drawing the weapon on RMB.

**Expected result:** Crosshair reacts to firing/moving; on gamepad the reticle drifts toward enemies.

### Workflow: Diagnose hand/aim problems

1. Wrong mode: check `ShooterFighter.AimingMode` matches the window used.
2. No `CharacterID` → edits landed on the weapon Default (other characters change too). Add `CharacterID`, redo.
3. Weapon prefab missing `supportHandIkTarget`/`holdHandIkTarget` → recreate via the setup window's Weapon tab or add the transforms by hand.
4. Hand IK disabled for that hand (`enableAim` false) → enable.
5. Rig with unusual hand-bone axes → custom `AimRigProfile` hand axes.
6. Aim point clamped → `maxAimAngleFromForward`; character faces the camera direction while aiming in third person.

## Verification

- `weapon.supportHandIK.references.Any(r => r.characterReferences.Any(c => /* matches */ true))` after using the window with a `CharacterID` character; or the Default `aimData` changed.
- `ShooterWeaponObject` on the spawned model: `supportHandIkTarget != null && holdHandIkTarget != null && aimReference != null`.
- Play: `ShooterFighter.IsAiming` → the support hand bone position is within ~2 cm of `supportHandIkTarget` (compare `Animator.GetBoneTransform(HumanBodyBones.LeftHand).position`).
- Crosshair: `ShooterCrosshairController` fields as intended; `ShooterController.enableAimAssist` as intended.
- No Console errors from `UpperBodyAimSolver`/`AimPostureSolver`.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/Shooter/Hand IK Setup (New Aiming)` | EditorWindow (`FS_ShooterSystem.HandIKSetupEditor`) | Play-mode grip authoring for New mode; writes `HandIKSettings.references`. |
| `Tools/FS System/Shooter/IK Setup` | EditorWindow (`FS_ShooterSystem.ShooterIKSetupEditor`) | Play-mode Aim/Idle IK authoring for Legacy mode; writes `ArmIkData`/`iKReferences`. |
| `FS_ShooterSystem.ShooterFighter.AimingMode` / `IkSetupMode` / `ResolveHandIKData(...)` / `ResolveIkData(...)` | fields/methods | Mode + resolved grip data. |
| `FS_ShooterSystem.ShooterWeapon.upperBodyIK` / `supportHandIK` / `holdHandIK` / `aimProfile` / `aimCameraSettings` | fields | Per-weapon aim tuning. |
| `Create > Shooter System > Create Aim Rig Profile` | `FS_ShooterSystem.AimRigProfile` | Posture/arm/clamp profile. |
| `FS_ShooterSystem.ShooterWeaponObject` | on the weapon prefab | IK targets, `aimReference`, scope. |
| `FS_ShooterSystem.ShooterCrosshairController` | on `Shooter UI` | Spread/colors/hit indicators. |
| `FS_ShooterSystem.ShooterController` | on `Player` | Aim assist, equip-on-aim. |
| `FS_Core.CharacterID` | on the character | Key for per-character IK references. |

## Common issues

- **Symptom:** Setup button disabled. **Cause:** Not in Play, no weapon equipped, wrong Aiming Mode, or the other window running. **Fix:** Play + equip; set the mode; stop the other window.
- **Symptom:** Edits vanished after Play. **Cause:** Edited scene bones instead of using the window (scene changes are discarded). **Fix:** Use the window; it saves to the asset.
- **Symptom:** Fixing one character broke another. **Cause:** No `CharacterID` → Default overwritten. **Fix:** Add `CharacterID` and re-author as references.
- **Symptom:** Body twists too far / not at all. **Fix:** `Upper Body IK` weights, `AimRigProfile.maxHorizontalAngle`.
- **Symptom:** Point-blank aim jitter. **Fix:** Raise `minIKAimDistance`.

## Boundaries

- IK covers hands/elbows/upper body; fingers are not posed procedurally (author grip clips).
- Per-character references are keyed by `CharacterID`/avatar, not by skeleton similarity.
- Left-handed characters need mirrored weapon prefabs and grip data; there is no automatic mirroring.
- Camera shake/recoil visuals are `CameraController`/`RecoilInfo` (Third Person Controller).
