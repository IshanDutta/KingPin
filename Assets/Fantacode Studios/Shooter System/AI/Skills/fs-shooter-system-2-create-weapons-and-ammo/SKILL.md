---
name: fs-shooter-system-2-create-weapons-and-ammo
description: "Use this skill whenever the user wants to add or change guns, ammunition, projectiles, bows or throwables with FS Shooter System 2 — e.g. 'add a shotgun', 'make the rifle full auto', 'bigger magazine', 'a rocket launcher that explodes', 'a bow that charges', 'a grenade', 'my gun model as a weapon', 'add a scope', 'weapon has no ammo'. Covers the Shooter Prefab and Collision Setup window (Weapon and Ammo tabs), Create > Shooter System > Create Weapon / Create Ammo / Throwable Weapon, ShooterWeapon fields and category attributes, ShooterWeaponObject references, ShooterAmmo (projectile, gravity, explosions), throwables and explosive barrels. Do NOT use for hand IK / aim pose (see fs-shooter-system-2-setup-aiming-and-hand-ik) or for damage reception/ragdoll (see fs-shooter-system-2-configure-damage-and-ragdoll). When in doubt whether a gun, ammo, projectile or grenade request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Create Weapons and Ammo

A gun is three things: a **ShooterWeapon** asset (an `EquippableItem` with fire mode, magazine, reload, recoil, aim/reload/shoot clips, IK data), a **weapon prefab** whose root has `ShooterWeaponObject` (aim reference, ammo spawn point, muzzle flash, scope, hand IK targets), and a **ShooterAmmo** asset with its own **ammo prefab** (`ShooterAmmoObject`: projectile, trail, physics, explosion). Numbers such as Fire Frequency, Magazine Size, Projectile Speed and Damage are **category attributes** shown in the weapon's Inspector (from `Resources/Category/Shooter Weapon.asset`). The **Shooter Prefab and Collision Setup** window builds the two prefabs from plain models. Throwables (grenades) are `ThrowableWeapon` assets thrown by the Third Person Controller's throwable system. This skill lets an agent author all of it.

## When to use this skill

- "Add a shotgun / SMG / revolver from my model."
- "Make the M4 semi-auto; 3-round burst."
- "Magazine 40, reload 1.5 s, more recoil, less spread."
- "Rocket launcher with splash damage."
- "Bow with draw strength."
- "Frag grenade / sticky bomb."
- "Sniper scope zoom."
- "Explosive barrel."

Not for:
- Hands not gripping the gun, aim posture — `fs-shooter-system-2-setup-aiming-and-hand-ik`.
- Headshot multipliers, ragdoll on death, hit reactions — `fs-shooter-system-2-configure-damage-and-ragdoll`.
- Giving an enemy a gun — `fs-shooter-system-2-create-shooter-enemy` (just `FighterCore.defaultWeapon`).

## Prerequisites

- Installed: `System.Type.GetType("FS_ShooterSystem.ShooterWeapon, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- A shooter player to test with; a weapon model (mesh prefab) and optionally a bullet/arrow model; clips for aim/reload/shoot if not reusing the shipped `Animations/Item Actions/{Pistol, Rifle, Sniper, Bazuka, Arrow, Grenade}` clips.
- Reference assets: `Demo/Weapon/*.asset`, `Demo/Ammo/*.asset`, `Demo/Item Prefabs/*.prefab`, `Demo/Weapon Models/*.prefab`.

## Quick start

Goal: a new rifle from the user's model, reusing the M4's ammo and clips.

1. `Tools > FS System > Shooter > Shooter Prefab and Collision Setup` → **Weapon** tab → **Weapon Model** = the mesh prefab, **Weapon Name** = `AK` → **Create Weapon Prefab**. This creates a prefab with `ShooterWeaponObject` plus helper transforms.
2. Open the new prefab: position **Aim Reference** (points along the barrel from the grip), **Ammo Spawn Point** (muzzle), assign **Flash** (a `Behaviour` such as a Light/ParticleSystem toggled on fire) and **Vfx** if any; for a scope tick **Has Scope** and set **Scope Target**.
3. Duplicate `Demo/Weapon/M4 16.asset` → `AK.asset`; set the model prefab reference in the `EquippableItem` section to the new prefab; keep **Ammo Data** = `5.56 Ammo`.
4. Attributes: **Fire Frequency** (seconds between shots; M4 0.1), **Magazine Size** (30), **Projectile Speed**, **Damage**.
5. Add `AK` to the player's `QuickSwitchHandler` → Quick Switch Items (or `FighterCore.defaultWeapon`). Play, equip, aim, fire.

Expected observable result: the model spawns in the right hand on equip, muzzle flash on fire, ammo HUD counts down from the magazine size, hits register on `RagdollBone` colliders.

## Workflows

### Workflow: Create weapon and ammo prefabs with the setup window

- **Weapon tab**: Weapon Model + Weapon Name → **Create Weapon Prefab**. Output prefab: `ShooterWeaponObject` with `aimReference`, `ammoSpawnPoint`, `supportHandIkTarget`, `holdHandIkTarget` transforms to place by hand.
- **Ammo tab**: Ammo Model + Ammo Name, **Has Trail Effect** + trail prefab, **Add Rigidbody** (physics projectiles such as arrows/rockets) → **Create Ammo Prefab**. Output: `ShooterAmmoObject` (+ `Rigidbody`/`Collider` when chosen). Assign it to a `ShooterAmmo` asset's **Ammo**.
- **RagdollAndCollision tab**: characters, not weapons (damage skill).

**Expected result:** Prefabs saved at the chosen path; `ShooterWeaponObject.aimReference != null`.

### Workflow: Author a ShooterWeapon

**Create > Shooter System > Create Weapon** (or duplicate a demo weapon). Sections:
- `EquippableItem` (Third Person Controller): name/icon, **Spawn Model** + prefab, holder bone + hand offsets, sheath attachment, **Override Controller**, equip/unequip clips, **Is Dual Item**, **Un Equip During Actions**.
- `CombatWeapon`: **Reaction Data** (ranged reactions; `Demo/Reaction/Shooter Reactions.asset`), **Weapon Damage Type** mask, dodge/roll overrides.
- Ammo & fire: **Ammo Data**, **Range** (100), **Fire Type** Single/Auto, **Is Burst** + **Burst Fire Bullet Count** (3) + **Burst Spreading Angle Range**, **Bullet Spread Radius** (0.1), **Enable Recoil** + **Weapon Recoil Info** (camera recoil amount/duration), **Hip Fire** (fire without aiming), **Can Aim Without Ammo**, **Allow Jumping While Aiming**, **Zoom While Aim** + **Aim Camera Settings**.
- Reload: **Use Reload Animation Duration** / **Reload Time** (2), **Auto Reload**, **Prevent Manual Reload**, **Play Load Animation Per Shot** (bolt-action; `loadAmmoClip`), **Ammo Enable Time** (arrow appears during reload), audio clips (fire, trigger-empty, reload, load).
- Clips & masks: **Aim Clip**, **Reload Clip** (+ `reloadAnimationMask` Arm), **Shoot Clip** (+ mask), **Aimed Locomotion Clip Set 2D** (strafe set while aiming).
- Charged: **Is Charged Weapon** + **Charge Clip** + mask + **Charge Force** — hold Fire to draw, release to shoot (bow); `ShooterAIController.useChargedShots` on AI.
- Reactions: **Play Reaction On Target**, **Target Reaction Tag**.
- Sound: **Sound Range** (50 m — enemies hear it via `ShootSoundEmitter`).
- IK/aim (aiming skill): Aim Profile, Upper Body IK, Support/Hold Hand IK, ArmIK data.
- **Attributes** (category): Fire Frequency, Magazine Size, Projectile Speed, Damage.

Shipped presets: `M4 16` (Auto, hip fire, 0.1 s, 30 rounds), `Pistol` (Single, 0.5 s, 10), `Sniper` (Single, load per shot, scope, 5), `Bow` (charged, 1 arrow), `Bazooka` (explosive ammo, 1 rocket).

**Expected result:** Equip → `ShooterFighter.CurrentWeapon` is the asset; behaviour matches the fields.

### Workflow: Author ammo, projectiles and explosions

**Create > Shooter System > Create Ammo**: **Ammo** prefab (`ShooterAmmoObject`), **Hit Effects** (per-layer VFX/audio list — `HitEffectData.layer`), **Gravity** (9.8; bullet drop), **Max Lifetime** (5 s), **Override Hit Ignore Mask** + mask, **Blood Effect**, **Destroy Immediate After Hit** (off for arrows that stick: `ShooterAmmoObject.hitTransform` attaches to the hit), **Enable Direct Hit** (spawn at hit point, no travel), and explosives: **Is Explosive**, **Explosive Radius** (3), **Min Damage** at the edge, **Explosion Force** (500), **Explosion Target Layer**, **Explotion Prefab/Audio/Life Time**, **Explosion Sound Range**. Attribute **Damage** on the ammo adds to the weapon's Damage (`DamageCalculator`).

Rules from `ShooterAmmoObject`: hits are resolved by the aim raycast when the hit is within 5 m or `enableDirectHit`; otherwise the projectile travels with `velocity` and gravity, and a muzzle-blocked check prevents shooting through a wall the body is against. `ShooterSettings.explosionAffectAllLayers` (on) lets explosions hurt everyone; `explosionIgnoreMask` excludes layers.

**Expected result:** Rockets explode with `TriggerExplosion`, damaging `Damagable`s in radius and pushing ragdolls.

### Workflow: Throwables (grenades) and explosive barrels

- **Create > Shooter System > Throwable Weapon** (`ThrowableWeapon : ThrowableItem`): model + physics from the item; **Damage Type** HitImpact (stone) / ExplosionImpact (grenade), **Damage** (20), **Explosion Trigger** FuseOnAim (fuse starts when aiming; **Explosion Delay** 3 s) / OnCollision, **Explosion Radius/Force/Target Layer/Effect/Sound**, **Max Throw Distance**, **Is Recoverable**, aim camera. Equip it (wheel), hold **Throw** (LMB) to show the trajectory, release to throw, **X** to cancel (`ThrowableController` on the player; HUD fuse timer in `Shooter UI`). Reference: `Demo/Weapon/Grenade.asset`, `Demo/Item Prefabs/Grenade Object.prefab`.
- **Explosive barrel**: `Prefabs/Barrel.prefab` = `Damagable` + `ExplosiveBarrel` (**Explosive Ammo** = `Demo/Ammo/Explosive Barrel.asset`, radius 8; **Destroy After Explode**, **On Explode** event). Any object with a `Damagable` + `ExplosiveBarrel` explodes on death.

**Expected result:** Grenade arcs along the drawn trajectory and detonates after the fuse; barrels chain-react.

## Verification

- Weapon: `weapon.ammoData != null`, category attributes present (`weapon.GetAttributeValue<int>("Magazine Size") > 0`, `GetAttributeValue<float>("Fire Frequency") > 0`), model prefab has `FS_ShooterSystem.ShooterWeaponObject` with `aimReference` and `ammoSpawnPoint` set.
- Ammo: `ammo.ammo` prefab has `ShooterAmmoObject`; explosive fields consistent.
- Play: equip → aim → fire: `ShooterWeaponObject.CurrentAmmoCount` decrements; target `Damagable.CurrentHealth` drops by weapon+ammo Damage (× bone multiplier); `ShooterFighter.OnFire` fires; no Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/Shooter/Shooter Prefab and Collision Setup` | EditorWindow (`FS_ShooterSystem.ShooterPrefabAndCollisionEditor`) | Weapon / Ammo / RagdollAndCollision tabs. |
| `Create > Shooter System > Create Weapon` | `FS_ShooterSystem.ShooterWeapon : FS_CombatCore.CombatWeapon` | Gun/bow asset. |
| `Create > Shooter System > Create Ammo` | `FS_ShooterSystem.ShooterAmmo : FS_Core.Item` | Projectile asset. |
| `Create > Shooter System > Throwable Weapon` | `FS_ShooterSystem.ThrowableWeapon : FS_Core.ThrowableItem` | Grenade/stone. |
| `FS_ShooterSystem.ShooterWeaponObject` | on the weapon prefab | `aimReference`, `ammoSpawnPoint`, `flash`, `vfx`, `hasScope`/`scopeTarget`/`scopeFov`, `CurrentAmmoCount`, `TotalAmmoCount`, `OnFire`. |
| `FS_ShooterSystem.ShooterAmmoObject` | on the ammo prefab | `Initialize(...)`, `TriggerExplosion(pos)`, `trailObject`, `hitTransform`. |
| `FS_ShooterSystem.ExplosiveBarrel` | MonoBehaviour | Explode on `Damagable.OnDead`. |
| `FS_ShooterSystem.ShooterFighter.Shoot(forceMultiplier)` / `Reload()` | methods | Fire/reload from code. |
| `Resources/Category/Shooter Weapon.asset`, `Ammo.asset` | `ItemCategory` | Attribute definitions. |

## Common issues

- **Symptom:** Weapon equips but Fire does nothing. **Cause:** `CurrentAmmoCount` 0 and no total ammo; or not aiming and `hipFire` off. **Fix:** Set ammo counts on the prefab's `ShooterWeaponObject` / `hasInfiniteAmmo`; aim first.
- **Symptom:** Bullets come out sideways. **Cause:** `aimReference` misaligned. **Fix:** Rotate it so its forward runs along the barrel.
- **Symptom:** No muzzle flash. **Fix:** Assign **Flash** (Light/particle) on the weapon prefab.
- **Symptom:** Scope never opens. **Cause:** `hasScope` off or third-person double-tap not registered. **Fix:** Tick it; assign `scopeTarget`; or set `useDoubleClickForScope` off on `ShooterInputManger`.
- **Symptom:** Rocket doesn't explode. **Cause:** Ammo `isExplosive` off / prefab without `Rigidbody` collider path. **Fix:** Tick it; use the Ammo tab with Add Rigidbody.
- **Symptom:** Arrows vanish on hit. **Cause:** `destroyImmediateAfterHit`. **Fix:** Untick; set `hitTransform`.

## Boundaries

- Hitscan vs projectile is decided per shot by distance/`enableDirectHit`; there is no separate "pure hitscan" flag.
- No weapon attachments/mod system (scope is per weapon object).
- Ammo pickups/inventory counts are Inventory System territory; here totals live on the weapon object.
- Dual-wield uses `isDualItem` with left/right objects; per-hand separate ammo types are not supported.
