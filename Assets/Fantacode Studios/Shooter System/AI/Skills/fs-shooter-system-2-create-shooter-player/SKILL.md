---
name: fs-shooter-system-2-create-shooter-player
description: "Use this skill whenever the user wants a playable character that aims and shoots with FS Shooter System 2 — e.g. 'add a player with a gun', 'use my soldier model for the shooter', 'start with the rifle equipped', 'make it first person', 'rebind fire/aim', 'add touch buttons for shooting', 'the player can't aim'. Covers the FS Systems window Setup tab (Player, Locomotion + Shooter, custom humanoid model), what gets generated, the drop-in Shooter Controller prefab, starting weapons and the weapon wheel, first-person camera, default controls, rebinding and mobile UI. Do NOT use for enemies (see fs-shooter-system-2-create-shooter-enemy), weapon data (see fs-shooter-system-2-create-weapons-and-ammo) or hand IK (see fs-shooter-system-2-setup-aiming-and-hand-ik). When in doubt whether a 'put a shooter in the scene' or controls request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Create a Shooter Player

This skill puts a player into the scene that equips guns from a weapon wheel, aims (third- or first-person), fires, reloads, scopes, takes cover and dodges. The normal route is the **FS Systems window** (wraps any humanoid model with the Third Person Controller plus Shooter and any other installed FS systems ticked at the same time); the **Shooter Controller** prefab is a drop-in locked to the bundled Default Character.

## When to use this skill

- "Add a player that can shoot."
- "Use my Soldier model as the shooter."
- "Start with the M4 equipped / put the sniper on the wheel."
- "Make the demo first person."
- "Fire on E, aim on Left Ctrl; add mobile fire/aim buttons."
- "The player won't aim / can't shoot."

Not for:
- Enemies — `fs-shooter-system-2-create-shooter-enemy`.
- Designing weapons/ammo — `fs-shooter-system-2-create-weapons-and-ammo`.
- Hands off the weapon / aim pose — `fs-shooter-system-2-setup-aiming-and-hand-ik`.
- Bone damage for the player's own ragdoll — `fs-shooter-system-2-configure-damage-and-ragdoll`.

## Prerequisites

- Installed + settings imported: `System.Type.GetType("FS_ShooterSystem.ShooterFighter, Assembly-CSharp") != null && LayerMask.NameToLayer("Player") != -1 && LayerMask.NameToLayer("Enemy") != -1` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278 / run Import Project Settings).
- Custom model: prefab with `Animator`, Rig Humanoid, valid avatar.
- Delete the scene's own `Main Camera`/`EventSystem` once the player root is added.

## Quick start

1. Delete the default `Main Camera`.
2. `Tools > FS System > FS Window` → **Setup** → character type **Player** → keep **Locomotion**, tick **Shooter** (tick Melee Combat, Parkour, … too if installed and wanted — one character gets all of them).
3. Drag the humanoid model onto the preview area; leave **Animator Name** `Combined Controller`; click **Setup**.
4. `Player` → `FighterCore` → **Default Weapon** is already `Demo/Weapon/M4 16.asset` from the donor; `QuickSwitchHandler` → **Quick Switch Items** holds the sample weapons.
5. Play: Caps Lock opens the wheel, RMB aims, LMB fires.

Expected observable result: root `FS Player` with `Player` (layer `Player`), `Third Person Camera`, `Game UI` (containing `Shooter UI`, `Combat Mobile Controller`, `Cover Mobile Controller`, `Locomotion Mobile Controller`), `EventSystem`; `Player` has `FighterCore` (target layer `Enemy`), `ShooterFighter`, `ShooterController`, `ShooterInputManger`, `CombatInputManger`, `CoverController`, `CoverHandler`, `CoverInputManager`, `ThrowableController`; root has `ShooterSettings`, `ShooterAIManager`, `CombatAIManager`; the ammo HUD shows counts.

## Workflows

### Workflow: Build from a custom model (FS Systems window)

What **Setup** adds for Shooter (`FSSystemsSetupShooter.cs`): copies every component of `Resources/Shooter Controller.prefab`'s `Player` onto the new `Player` and merges its animator; copies root components (`ShooterSettings`, `ShooterAIManager`, `CombatAIManager`, `AIManager`); sets `FighterCore.targetLayer = Enemy`; instantiates `Shooter UI`, `Combat Mobile Controller`, `Cover Mobile Controller` under `Game UI` and wires `ShooterInputManger`, `CombatInputManger`, `CoverInputManager` to them. It does **not** add bone colliders for incoming bullets — run the ragdoll/damage setup on the player if enemies should be able to shoot it (damage skill); it also does not run hand-IK setup for a new rig (aiming skill).

**Expected result:** Component list above; `ShooterSettings.hitIgnoreMask` should include `Player` and `Enemy` (the donor value) — check it.

### Workflow: Drop in the ready-made prefab

Delete the scene `Main Camera`; drag `Assets/Fantacode Studios/Shooter System/Prefabs/Shooter Controller.prefab` in. Root `Shooter Controller` with `Player` (Default Character, bone damage handlers already applied), camera, `Game UI`, `HP Bar`. For another model use the FS Window (IK references and bone colliders are per rig).

### Workflow: Starting weapon and weapon wheel

- `FighterCore` → **Default Weapon**: a `ShooterWeapon` (or a melee weapon if Melee is installed). Guns need ammo: `ShooterWeaponObject` on the spawned model tracks `CurrentAmmoCount`/`TotalAmmoCount`; `ShooterFighter.hasInfiniteAmmo` ignores totals.
- `QuickSwitchHandler` → **Quick Switch Items** (max 8, wheel on **Quick Switch Item** input, Caps Lock / T) — add any `ShooterWeapon`, `ThrowableWeapon`, melee weapon; **Equip First Item On Start** to spawn armed.
- `ShooterController` → **Equip On Aim** + **Weapon To Equip On Aim** + **Unequip On Aim Stop**: aim draws the weapon automatically.
- Code: `GetComponent<FS_Core.ItemEquipper>().EquipItem(weaponAsset)`.

**Expected result:** `ShooterFighter.CurrentWeapon` is the intended asset in Play; HUD image/ammo update (`ShooterUI.UpdateShooterUI`).

### Workflow: First person

`Third Person Camera` → `CameraController` → **Camera Type** = `FirstPerson` (set before Play; `firstPersonCamera` settings follow the generated `Head Follow Target`). In first person the shooter always aims while a gun is equipped, **Scope** is hold instead of double-tap, and locomotion aligns to the camera. Demo `3 - First Person Demo` is the reference.

### Workflow: Default controls and what they do

| Input (legacy default) | Effect |
|---|---|
| RMB (hold) | Aim (`ShooterInputManger.aimInput`); weapon `hipFire` lets LMB fire without aiming |
| LMB | Fire (`fireType` Single = tap, Auto = hold; `isBurst` fires `burstFireBulletCount`); charged weapons (bow) hold to draw, release to shoot |
| Double-tap RMB (third person) / hold RMB (first person) | Scope view if the weapon object `hasScope` (`ShooterInputManger.useDoubleClickForScope`) |
| R | Reload (`preventManualReload` blocks it; `autoReload` reloads on empty) |
| G | Cover (`CoverInputManager`) |
| LMB (hold) with a throwable equipped | Aim throw with trajectory; release to throw; **X** cancels (`LocomotionInputManager.itemThrowInput`/`itemThrowCancelInput`) |
| Left Alt / Space | Dodge / Roll (`ShooterFighter.canDodge/canRoll`, `FighterCore.CanDodge/CanRoll`) |
| Caps Lock / 3 / 4 | Weapon wheel / cycle previous / next |

Aiming while airborne obeys the weapon's `allowJumpingWhileAiming`; walking/running speeds while aiming come from `ShooterSettings.aimWalkSpeed` (2.5) / `aimRunSpeed` (4.5).

### Workflow: Rebind keys, gamepad, touch

- Legacy: `Player` → `ShooterInputManger` (`fireInput` Mouse0, `aimInput` Mouse1, `scopeInput` Mouse1, `reloadInput` R), `CombatInputManger` (`dodgeInput` LeftAlt, `rollInput` Space), `CoverInputManager` (`coverInput` G) → **Key**/**Button**. Code: `new SerializedObject(mgr).FindProperty("fireInput.key").enumValueIndex = …`.
- New Input System: `Tools > FS System > FS Window` → Dependencies → **New Input System** (`inputsystem` define); edit `Third Person Controller/InputAction/FSSystemsInputAction.inputactions` maps **Shooter** (`Fire` LMB/RT, `Aim` RMB/LT, `Scope` RMB/LT, `Reload` R/South, `CancelThrow` X/LB) and **Combat** (`Cover` G/East, `Dodge`, `Roll`, `Equip`, `UnEquip`).
- Touch: `Game UI/Shooter Mobile Controller` (buttons `Fire`, `Aim`, `Scope`, `Reload`, `Cover`; from `Resources/Shooter Mobile Controller.prefab`), `Combat Mobile Controller` (`Dodge`, `Roll`), `Cover Mobile Controller` (`Cover`). Buttons bind by name at Awake (`ShooterUI.CacheButtons`); `InputManagerHub.TryGetInput("fireInput", …)` reads bindings.

**Expected result:** New bindings drive `ShooterInputManger.Fire/Aim/Scope/Reload` flags in Play.

## Verification

```csharp
var pc = Object.FindFirstObjectByType<FS_Core.PlayerController>();
var fc = pc ? pc.GetComponent<FS_CombatCore.FighterCore>() : null;
bool ok = fc && pc.GetComponent<FS_ShooterSystem.ShooterFighter>() && pc.GetComponent<FS_ShooterSystem.ShooterController>()
    && pc.GetComponent<FS_ShooterSystem.ShooterInputManger>() && pc.GetComponent<FS_CoverSystem.CoverController>()
    && pc.gameObject.layer == LayerMask.NameToLayer("Player")
    && (fc.targetLayer.value & (1 << LayerMask.NameToLayer("Enemy"))) != 0 && fc.defaultWeapon
    && pc.transform.root.GetComponentInChildren<FS_ShooterSystem.ShooterUI>(true) != null
    && pc.transform.root.GetComponent<FS_ShooterSystem.ShooterSettings>() != null;
Debug.Log(ok ? "PASS" : "FAIL");
```

Play: RMB → `ShooterFighter.IsAiming == true`; LMB → `ShooterFighter.OnFire` fires and `CurrentShooterWeaponObject.CurrentAmmoCount` drops; `PlayerController.CurrentSystemState == SystemState.Shooter` while aiming.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/ FS Window` → Setup | EditorWindow | Builds Player from selected systems + model. |
| `Prefabs/Shooter Controller.prefab` / `Resources/Shooter Controller.prefab` | prefabs | Drop-in / FS Window donor. |
| `FS_ShooterSystem.ShooterFighter` | MonoBehaviour | `CurrentWeapon`, `IsAiming`, `StartAiming()/StopAiming()`, `Shoot()`, `Reload()`, `hasInfiniteAmmo`, `canDodge/canRoll`, `AimingMode`, events `OnFire`, `OnStartReload`, `OnHitDamagable`. |
| `FS_ShooterSystem.ShooterController` | `EquippableSystemBase` | `enableAimAssist` (+ strength/radius/angle), `equipOnAim`. |
| `FS_ShooterSystem.ShooterInputManger` / `FS_CombatCore.CombatInputManger` / `FS_CoverSystem.CoverInputManager` | input managers | Bindings + runtime flags. |
| `FS_ShooterSystem.ShooterSettings` (root) | singleton | Aim speeds, `hitIgnoreMask`. |
| `FS_Core.QuickSwitchHandler` / `FS_Core.ItemEquipper` | MonoBehaviours | Wheel items / equip at runtime. |
| `FS_Core.CameraController.CameraType` | field | ThirdPerson / FirstPerson. |
| `FS_ShooterSystem.ShooterUI.i` | singleton | Ammo/weapon HUD, crosshair (`ShooterCrosshairController`). |

## Common issues

- **Symptom:** RMB does nothing. **Cause:** No `ShooterWeapon` equipped (`CurrentWeapon == null`), or `canAimWithoutAmmo` off with empty ammo. **Fix:** Equip via wheel/default weapon; check ammo.
- **Symptom:** LMB fires without aiming / only while aiming. **Cause:** Weapon `hipFire`. **Fix:** Toggle it on the weapon asset.
- **Symptom:** Enemies can't hurt the player. **Cause:** No `ShooterBoneDamageHandler` colliders on the player rig (FS Window build). **Fix:** Damage skill → ragdoll & damage setup on the player.
- **Symptom:** Hands float off a new model's gun. **Fix:** Aiming/IK skill (per-character hand IK references).
- **Symptom:** Two cameras. **Fix:** Delete the scene's original camera.
- **Symptom:** Aim assist too strong/weak. **Fix:** `ShooterController` → Enable Aim Assist, Strength (0.2), Radius (0.4), Lock-On Max Angle (45).

## Boundaries

- One player per scene; humanoid only.
- Split-screen/local co-op not supported.
- Vehicle-mounted shooting is outside this package.
- Health bar, armor, inventory UI come from the Third Person Controller / Inventory System.
