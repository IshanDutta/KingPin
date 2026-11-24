---
name: fs-shooter-system-2-configure-damage-and-ragdoll
description: "Use this skill whenever the user wants bullets to hurt characters correctly, headshots, ragdoll deaths, hit reactions or explosions with FS Shooter System 2 — e.g. 'enemies don't take damage from my gun', 'headshots do double damage', 'ragdoll when killed', 'my character can't be shot', 'set up bone colliders', 'flinch when hit by a bullet', 'explosion should knock them over'. Covers the RagdollAndCollision tab (Create Ragdoll, Apply Damage Detection Setup), ShooterBoneDamageHandler multipliers, the RagdollBone layer and hit ignore mask, Damagable, ranged reactions (Ranged hit type, blend to ragdoll), explosion settings and damage types. Do NOT use for weapon/ammo authoring (see fs-shooter-system-2-create-weapons-and-ammo) or for enemy behaviour (see fs-shooter-system-2-create-shooter-enemy). When in doubt whether a damage, health, ragdoll or hit-reaction request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Shooter System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.1"
  skill-version: "1.0.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-shooter-system/damage-and-ragdoll"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-13"
---

# Configure Damage and Ragdoll

Bullets are resolved against **bone colliders**, not the character capsule: `ShooterSettings.hitIgnoreMask` skips the `Player`/`Enemy` body layers, and each hit collider is expected to carry a `ShooterBoneDamageHandler` on the `RagdollBone` layer that forwards damage (× its multiplier) to the character's `Damagable`. The **RagdollAndCollision** tab of the Shooter Prefab and Collision Setup window creates those colliders — either through Unity's Ragdoll Wizard (physics death) or as damage-only handlers on head/body/arms/legs. Damage → `FighterCore.TakeHit` → a **Ranged** reaction from the weapon's or fighter's `ReactionsData`, or death (optionally blending into the active ragdoll); explosions use the ammo/throwable radius. This skill sets all of that up on players and enemies.

## When to use this skill

- "My shots don't hurt the enemy / the enemy's shots don't hurt me."
- "Headshots should be lethal / double damage."
- "Ragdoll on death."
- "Enemies should stagger when shot."
- "Explosions should throw bodies."
- "Enemy health 200; player health 100."

Not for:
- Guns, ammo, projectile/explosion *authoring* — `fs-shooter-system-2-create-weapons-and-ammo`.
- AI tuning — `fs-shooter-system-2-create-shooter-enemy`.
- Melee hit reactions — FS Melee Combat System 2's reactions skill (same Combat Core data, `Melee` hit type).

## Prerequisites

- Installed: `System.Type.GetType("FS_ShooterSystem.ShooterBoneDamageHandler, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278).
- Layer `RagdollBone` exists (Import Project Settings); `ShooterSettings` on the player root.
- Characters are humanoid `Animator`s with `Damagable` + `FighterCore` (players/enemies from the FS Window or prefabs). `Prefabs/Shooter Enemy.prefab` and `Prefabs/Shooter Controller.prefab` already have bone handlers; FS-Window-built characters do not.

## Quick start

Goal: an FS-Window-built enemy takes bullet damage with 2× headshots.

1. `Tools > FS System > Shooter > Shooter Prefab and Collision Setup` → **RagdollAndCollision** tab.
2. **Target Animator** = the enemy's `Animator` (must be Humanoid).
3. Optional **Create Ragdoll** (opens Unity's Ragdoll Wizard pre-filled with the humanoid bones; also puts those bones on `RagdollBone`).
4. **Apply Damage Detection** → tick Head/Body (arms/legs as available) → **Apply Damage Detection Setup**: adds `ShooterBoneDamageHandler` to Head, Hips, Chest (and limb bones) and sets them to layer `RagdollBone`. Bones need colliders — the ragdoll step provides them; otherwise add `CapsuleCollider`s to the handled bones.
5. Head bone → `ShooterBoneDamageHandler` → **Damage Multiplier** = 2.
6. Player root → `ShooterSettings` → **Hit Ignore Mask** includes `Player` and `Enemy`.

Expected observable result: shooting the torso reduces `Damagable.CurrentHealth` by weapon+ammo Damage; the head by twice that; `FighterCore.OnGotHit` fires; on death the death reaction or ragdoll plays.

## Workflows

### Workflow: Understand the hit pipeline

1. `ShooterFighter.Shoot` → `ShooterWeaponObject.SpawnBullet` → `ShooterAmmoObject.Initialize` with the aim raycast hit (mask = everything except `ShooterSettings.hitIgnoreMask`; the demo sets it to `Player|Enemy`, and `OcuppantObject` is added automatically).
2. Within 5 m or with ammo `enableDirectHit` the hit applies immediately; otherwise the projectile flies (velocity from **Projectile Speed**, ammo `gravity`) and applies on contact. A muzzle-blocked check refuses shots through a wall the shooter's body touches.
3. `ApplyDirectDamage`: finds `ShooterBoneDamageHandler` on the hit collider → its `ParentDamagable`; damage = `DamageCalculator.CalculateDamage(weapon Damage, ammo Damage, damagable)` × `damageMultiplier`; only if the victim is on the shooter's `FighterCore.targetLayer`; spawns blood (`ammo.bloodEffect` or `fighter.bloodEffectPrefab`) and calls `fighter.TakeHit` with `HitType.Ranged`, `PlayReaction = weapon.playReactionOnTarget`, `ReactionTag = weapon.targetReactionTag`, unblockable.
4. Non-fighter `Damagable`s (barrels, props) take damage directly.

**Expected result:** You can explain any "no damage" case by one of: mask, missing handler/collider, target layer, `Damagable.CanTakeHit`, damage type filter.

### Workflow: Bone colliders and ragdoll with the setup window

RagdollAndCollision tab (`ShooterPrefabAndCollisionEditor`):
- **Target Animator** — Humanoid required.
- **Create Ragdoll** — fills Unity's Ragdoll Wizard (pelvis, hips/knees/feet, chest, head, arms/elbows) and marks bones `RagdollBone`. Finish the wizard (mass/strength) to get `CharacterJoint`s + colliders + rigidbodies; `ActiveRagdollSystem` on the character drives them on death.
- **Apply Damage Detection Setup** — adds `ShooterBoneDamageHandler` and `RagdollBone` layer on the chosen parts. Colliders on those bones are what bullets hit.
- Set `ShooterBoneDamageHandler.damageMultiplier` per bone (Head 2 for headshots, limbs 0.5, …).

`ShooterSettings.Awake` ignores physics collisions between `Player`/`Enemy` capsules and `RagdollBone` so bone colliders never push characters.

**Expected result:** Selecting the character shows capsule/box colliders on bones (layer `RagdollBone`) and handlers on Head/Hips/Chest at least.

### Workflow: Health and damage types

- `Damagable` → **Max Health** (player 100, sample enemy 20), **Filter By Damage Type** + **Allowed Damage Types Mask** (from `DamageTypeDefinitions`, `Create > FS System > Damage > Damage Type Definitions`) — weapons/throwables carry a **Weapon Damage Type** mask; mismatches are rejected by `TakeDamage`. `OnDeadEvent`, `OnHealthUpdated`.
- `ShooterFighter.hasInfiniteAmmo` is ammo, not health. `Damagable.CanTakeHit` false = invulnerable (dodges set it temporarily).
- `HP Bar` (Third Person Controller) reads `Damagable`.

**Expected result:** Health values and filters behave in Play (`Damagable.TakeDamage` returns false when filtered).

### Workflow: Ranged hit reactions and death

Reactions come from Combat Core: on `TakeHit` the list is the weapon's **Reaction Data** (`ShooterWeapon` → `CombatWeapon.reactionData`, e.g. `Demo/Reaction/Shooter Reactions.asset`) if it has entries for `Ranged`/`Any`, else `FighterCore.defaultAnimations.hitReactionData`; filtered by hit type → attacked-from-behind → **Reaction Tag** (weapon `targetReactionTag`) → direction (from the hit point). Death uses `defaultAnimations.deathReactionData`; tick **Blend To Ragdoll** (+ start time 0.8, duration 0.3) on death reactions for physics deaths, or leave the clip empty and let the ragdoll take over. Explosions: `defaultAnimations.useRagdollForExplosion` with **Min Explosion Damage For Ragdoll** (25) ragdolls victims of big blasts.
- Weapon **Play Reaction On Target** off = damage without flinch (machine guns), or use light additive reactions (`isAdditiveAnimation`) so the victim keeps moving; `WaitTimeTillNextReaction` 0.8 s throttles repeated flinches.

**Expected result:** Pistol shots play a tagged `light` flinch; sniper `heavy`; lethal shots play the death clip then ragdoll.

### Workflow: Explosions

- Ammo (`ShooterAmmo`): `isExplosive`, `explosiveRadius`, `minDamage` at the edge (max = weapon+ammo Damage at centre), `explosionForce` on rigidbodies/ragdolls, `explosionTargetLayer`, `explotionPrefab`/`explotionAudio`/`explotionLifeTime`, `explosionSoundRange` (alerts AI).
- Throwables (`ThrowableWeapon`): `explosionRadius/MinDamage/Force/TargetLayer/Effect/Sound`, FuseOnAim delay.
- `ShooterSettings`: **Explosion Affect All Layers** (on — ignores `targetLayer` so explosions hurt everyone incl. the thrower), **Explosion Ignore Mask** (occluders that block the blast).
- `Prefabs/Barrel.prefab` (`Damagable` + `ExplosiveBarrel`, `Demo/Ammo/Explosive Barrel.asset` radius 8) for chain reactions.

**Expected result:** Everything with a `Damagable` in radius takes falling-off damage; ragdolls fly with `explosionForce`.

## Verification

```csharp
var t = GameObject.Find("Enemy"); // the character root
var anim = t.GetComponentInChildren<Animator>();
var head = anim.GetBoneTransform(HumanBodyBones.Head);
var h = head ? head.GetComponent<FS_ShooterSystem.ShooterBoneDamageHandler>() : null;
var settings = Object.FindFirstObjectByType<FS_ShooterSystem.ShooterSettings>();
int ignore = settings ? new SerializedObject(settings).FindProperty("hitIgnoreMask").intValue : 0;
bool ok = h && head.gameObject.layer == LayerMask.NameToLayer("RagdollBone") && head.GetComponent<Collider>()
    && Mathf.Abs(h.damageMultiplier - 2f) < 0.01f
    && (ignore & (1 << LayerMask.NameToLayer("Enemy"))) != 0 && (ignore & (1 << LayerMask.NameToLayer("Player"))) != 0
    && t.GetComponent<FS_Core.Damagable>() != null;
Debug.Log(ok ? "PASS" : "FAIL");
```

Play: one torso shot lowers `Damagable.CurrentHealth` by the expected amount, a head shot by twice; death triggers `FighterCore.OnDeath` and (if set) ragdoll; no Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/Shooter/Shooter Prefab and Collision Setup` → RagdollAndCollision | EditorWindow | **Create Ragdoll**, **Apply Damage Detection Setup**. |
| `FS_ShooterSystem.ShooterBoneDamageHandler` | MonoBehaviour on bones (layer `RagdollBone`) | `damageMultiplier`, `ParentDamagable`. |
| `FS_ShooterSystem.ShooterSettings` (player root) | singleton | `hitIgnoreMask`, `hitBoneMask` (RagdollBone), `explosionIgnoreMask`, `explosionAffectAllLayers`, `hitEffectLifetime`. |
| `FS_Core.Damagable` | MonoBehaviour | `MaxHealth`, `CurrentHealth`, `TakeDamage(dmg, typeMask)`, `TakeImpactDamage`, `CanTakeHit`, `OnDead`, damage-type filter. |
| `FS_CombatCore.FighterCore.TakeHit(CombatHitData, attacker)` / `defaultAnimations` / `bloodEffectPrefab` | methods/fields | Reaction & death handling. |
| `FS_CombatCore.ReactionsData` / `DeathReactionsData` (`Create > Combat > …`) | ScriptableObjects | Reaction lists (`hitType` Ranged). |
| `FS_ShooterSystem.ShooterAmmoObject.TriggerExplosion(pos)` | method | Detonate from code. |
| `FS_ShooterSystem.ExplosiveBarrel` | MonoBehaviour | Explode on `Damagable.OnDead`. |
| `FS_Core.ActiveRagdollSystem` | MonoBehaviour on characters | Ragdoll driver used by death/explosions. |

## Common issues

- **Symptom:** No damage at all. **Cause:** No `ShooterBoneDamageHandler`/colliders on bones (FS-Window build), or `hitIgnoreMask` lacks the body layer so the capsule eats the ray. **Fix:** Apply Damage Detection Setup; set the mask to Player+Enemy.
- **Symptom:** Damage but no reaction. **Cause:** Weapon `playReactionOnTarget` off or no Ranged/Any entries. **Fix:** Enable; assign `Shooter Reactions.asset`.
- **Symptom:** Bullets hit the shooter. **Cause:** Own bone colliders not excluded — `IsOwnCharacter` handles it; if the mask is wrong, the capsule is hit. **Fix:** Mask.
- **Symptom:** Ragdoll launches or jitters. **Cause:** Ragdoll Wizard mass/strength defaults; joint limits. **Fix:** Re-run wizard with higher total mass; check `ActiveRagdollSystem`.
- **Symptom:** Explosion ignores enemies. **Cause:** `explosionTargetLayer` excludes them and `Explosion Affect All Layers` off. **Fix:** Adjust either.
- **Symptom:** Damage rejected. **Cause:** `Damagable.filterByDamageType` mismatch. **Fix:** Align weapon damage type mask.

## Boundaries

- Per-bone damage needs colliders on bones; no automatic hit-zones from the mesh.
- Armor/damage reduction stats come from the Third Person Controller `ArmorHandler`/`DamageCalculator`, not from this package's fields.
- No dismemberment or wound decals beyond hit effects.
- Vehicles/props need their own `Damagable` to be shootable.
