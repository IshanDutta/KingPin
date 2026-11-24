---
name: fs-shooter-system-2-setup-and-overview
description: "Use this skill whenever the user has, wants to install, or asks anything general about FS Shooter System 2 by Fantacode Studios — e.g. 'is the shooter asset installed', 'set up the fantacode shooter', 'what comes in the shooter package', 'bullets go through enemies', 'enemies don't move', 'where are the shooter demo scenes'. Covers verifying the install, first-time setup (layers, tags, NavMesh, hit ignore mask), package layout across Shooter System, Combat Core, Cover System, AI System and Third Person Controller, the three demo scenes, and an index of the other skills. Do NOT use for building the player (see fs-shooter-system-2-create-shooter-player), enemies (see fs-shooter-system-2-create-shooter-enemy), weapons, aiming/IK, damage, cover or AI behaviours (see the sibling skills). When in doubt whether a gun, shooting, aiming, cover or ranged-enemy request could involve FS Shooter System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
metadata:
  asset: "FS Shooter System 2"
  publisher: "Fantacode Studios"
  asset-version: "2.0.1"
  skill-version: "1.0.0"
  unity: "6000.0+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278"
  documentation-url: "https://fantacode.gitbook.io/fantacode-studios/fantacode-studios/fs-shooter-system"
  support-url: "mailto:studios@fantacode.com"
  last-verified: "2026-09-13"
---

# Set Up FS Shooter System 2

FS Shooter System 2 is a third-/first-person ranged combat framework: data-driven guns, bows and throwables (pistol, rifle, sniper with scope, bazooka, bow, grenade ship as samples), procedural aiming with hand IK, bone-based damage with ragdolls, a cover system for player and AI, and enemy AI that shoots, takes cover and flanks. The package bundles five folders: **Shooter System**, **Combat Core** (shared `FighterCore`, reactions, combat AI base), **CoverSystem**, **AI System** (state machines, blackboard, AI behaviour nodes) and **Third Person Controller** (locomotion, camera, input, equipping, throwables). Configuration is Inspector/ScriptableObject driven; full C# source included. This skill confirms the install, completes one-time setup, maps the package and routes to the right sibling skill.

## When to use this skill

- "Is the Fantacode shooter in this project? Set it up."
- "Bullets pass straight through / hit the player's own collider."
- "Enemies stand still" (NavMesh).
- "Where are the shooter demo scenes / sample guns?"
- "What are Combat Core and CoverSystem for?"
- Any gun, aiming, cover or ranged-AI request in a project with `Assets/Fantacode Studios/Shooter System`.

Not for:
- Player setup — `fs-shooter-system-2-create-shooter-player`.
- Enemies — `fs-shooter-system-2-create-shooter-enemy`.
- Guns, ammo, throwables — `fs-shooter-system-2-create-weapons-and-ammo`.
- Aiming pose, hand IK, crosshair — `fs-shooter-system-2-setup-aiming-and-hand-ik`.
- Ragdoll, headshots, explosions — `fs-shooter-system-2-configure-damage-and-ragdoll`.
- Cover — `fs-shooter-system-2-use-cover-system`.
- Scripted AI moments — `fs-shooter-system-2-author-ai-behavior`.

## Prerequisites

- Unity 6000.0 or newer; render-pipeline agnostic (demo materials are URP).
- **AI Navigation** package for enemies (`NavMeshAgent`), **TextMeshPro** (ammo HUD), Input System package only if using the new Input System.
- Compiles into `Assembly-CSharp`.

**Programmatic install check** (Editor):

```csharp
using UnityEditor;
using UnityEngine;

public static class FSShooterInstallCheck
{
    [MenuItem("Tools/Check FS Shooter Install")]
    public static void Check()
    {
        bool shooter = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/Shooter System");
        bool cover   = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/CoverSystem");
        bool core    = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/Combat Core");
        bool ai      = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/AI System");
        bool tpc     = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/Third Person Controller");
        bool t1 = System.Type.GetType("FS_ShooterSystem.ShooterFighter, Assembly-CSharp") != null;
        bool t2 = System.Type.GetType("FS_CoverSystem.CoverController, Assembly-CSharp") != null;
        bool t3 = System.Type.GetType("FS_CombatCore.FighterCore, Assembly-CSharp") != null;
        bool layers = LayerMask.NameToLayer("Enemy") != -1 && LayerMask.NameToLayer("RagdollBone") != -1 && LayerMask.NameToLayer("Cover") != -1;
        Debug.Log($"FS Shooter install — folders:{shooter}/{cover}/{core}/{ai}/{tpc} ShooterFighter:{t1} CoverController:{t2} FighterCore:{t3} layers:{layers}");
    }
}
```

- All `true` → installed and settings imported. Folders `true`, types `false` → compile errors. Folders `false` → tell the user to import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-shooter-system-2-388278. `layers` `false` → Workflow: Import project settings.

Manual check: `Tools > FS System > Shooter > Shooter Prefab and Collision Setup` exists only with the asset installed.

## Quick start

1. Import the package. The **FS Systems** window opens after import (and on project open unless "Don't Show on Start"), importing project settings on first open; otherwise `Tools > FS System > FS Window` (exact path `Tools/FS System/ FS Window`) → **Welcome** → **Import Project Settings**.
2. Open `Assets/Fantacode Studios/Shooter System/Demo/Scene/1 - Main Shooter Demo Scene.unity`, Play. **RMB** aim, **LMB** fire, **R** reload, double-tap RMB for scope (sniper), **G** cover, Caps Lock weapon wheel, **Left Alt**/**Space** dodge/roll.

Expected observable result: layers `Player`, `Enemy`, `RagdollBone`, `Cover`, `VisionSensor`, `OcuppantObject`, `PlayerAlly` exist; the demo plays with enemies shooting from cover; no Console errors.

## Workflows

### Workflow: Import project settings

`Tools > FS System > FS Window` → Welcome → **Import Project Settings** (or `FS_Core.FSSystemsSetupEditorWindow.ShowWindow(); FS_Core.FSSystemsSetupEditorWindow.setupScript.ImportProjectSettings();`). Adds layers from `FSSystemsSetup.layers` (`Ledge`, `Player`, `Enemy`, `RagdollBone`, `Deadbody`, `NPC`, `Vehicle`, `VisionSensor`, `FootTrigger`, `Cover`, `Hotspot`, `PlayerAlly`, `EnvironmentFinisherProp`, `OcuppantObject`), tags (`Hitbox`, `NarrowBeam`, `SwingableLedge`, `Slide`, `NavmeshLink`, `NavmeshSurface`), ignores `RagdollBone` collisions with characters, sets the `inputsystem` define when the new Input System is the active handler, and runs the Shooter `OnInstallation` hook that puts `CoverSystem/Resources/Cover Barrier.prefab` and `Cover Collider.prefab` on the `Cover` layer.

Why they matter: bullets are raycasts that skip `ShooterSettings.hitIgnoreMask` (the demo sets `Player` + `Enemy`) so they hit **bone colliders on `RagdollBone`** instead of the body capsule; AI searches `Cover`-layer colliders for cover points; `FighterCore.targetLayer` gates who can be hurt.

**Expected result:** Layers present; `Cover Barrier.prefab` children on layer `Cover`.

### Workflow: Bake a NavMesh

Install AI Navigation, add a `NavMeshSurface` (Agent Type Humanoid), **Bake**; re-bake after level changes. Without it enemies never move ("Failed to create agent…" in the Console). Demo scenes keep their surface under `Navmesh Surface` / `NavMeshSurface` roots.

### Workflow: Explore the demo scenes

| Scene (`Shooter System/Demo/Scene/`) | Shows |
|---|---|
| `1 - Main Shooter Demo Scene` | All sample weapons on the wheel, patrolling enemies (`Way Points`), explosive `Barrel`s, cover-based firefight, trigger volumes |
| `2 - Authored Behavior Demo` | Enemies with `AIBehavior` scripted moments started by `TriggerAuthoredAIBehavior` volumes |
| `3 - First Person Demo` | Same systems with `CameraController` in FirstPerson mode |

### Workflow: Locate package content

| Need | Path (under `Assets/Fantacode Studios/`) |
|---|---|
| Drop-in player (fixed model) | `Shooter System/Prefabs/Shooter Controller.prefab` |
| Drop-in enemy | `Shooter System/Prefabs/Shooter Enemy.prefab` |
| FS Window donors (not for scenes) | `Shooter System/Resources/Shooter Controller.prefab`, `Shooter AI Controller.prefab` |
| Sample weapons / ammo | `Shooter System/Demo/Weapon/{M4 16, Pistol, Sniper, Bow, Bazooka, Grenade}.asset`, `Demo/Ammo/*.asset`, `Demo/Item Prefabs/*.prefab` |
| Sample reactions / AI machine | `Demo/Reaction/Shooter Reactions.asset`, `Demo/AI StateMachine/Shooter StateMachine.asset` |
| Cover prefabs (AI cover points) | `CoverSystem/Resources/Cover Barrier.prefab`, `Cover Collider.prefab` |
| Explosive barrel, trigger volume, crosshair | `Shooter System/Prefabs/Barrel.prefab`, `Trigger Authored AI Behavior.prefab`, `Crosshair Controller.prefab` |
| Aim rig defaults | `Shooter System/Resources/Aim Rig Profile.asset` |
| Mobile UI | `Shooter System/Resources/Shooter Mobile Controller.prefab`, `Shooter UI.prefab`, `Combat Core/Resources/Combat Mobile Controller.prefab`, `CoverSystem/Resources/Cover Mobile Controller.prefab` |
| Editor tools | `Tools > FS System > Shooter > Shooter Prefab and Collision Setup`, `IK Setup`, `Hand IK Setup (New Aiming)` |
| Scripts | `Shooter System/Scripts/{Shooter, Data, ShooterAI, UI}`, `CoverSystem/Scripts`, `Combat Core/Scripts`, `AI System/Scripts` |

### Workflow: Pick the right sibling skill

| User wants to… | Skill |
|---|---|
| Playable shooter with their model, controls, first person | `fs-shooter-system-2-create-shooter-player` |
| Enemies that shoot, take cover, flank; patrols; tuning | `fs-shooter-system-2-create-shooter-enemy` |
| New gun / ammo / grenade / bow, fire modes, magazines | `fs-shooter-system-2-create-weapons-and-ammo` |
| Hands not on the weapon, aim pose, crosshair, aim assist | `fs-shooter-system-2-setup-aiming-and-hand-ik` |
| Headshots, ragdoll, explosions, hit reactions | `fs-shooter-system-2-configure-damage-and-ragdoll` |
| Player/AI cover | `fs-shooter-system-2-use-cover-system` |
| Intro/triggered AI behaviours, patrol routes | `fs-shooter-system-2-author-ai-behavior` |

## Verification

- Five folders exist; `FS_ShooterSystem.ShooterFighter`, `FS_CoverSystem.CoverController`, `FS_CombatCore.FighterCore`, `FS_Core.AIController` resolve.
- Layers `Player`, `Enemy`, `RagdollBone`, `Cover`, `VisionSensor`, `OcuppantObject` exist.
- `Unity.AI.Navigation` present; demo scene 1 plays with enemies moving and shooting.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/ FS Window` | EditorWindow (`FS_Core.FSSystemsSetupEditorWindow`) | Shared setup window; Setup tab builds Player/AI characters from any combination of installed FS systems. |
| `FS_Core.FSSystemsSetup.ImportProjectSettings()` | method | Layers, tags, collisions, define, cover prefab layers. |
| `FS_ShooterSystem.ShooterFighter` | MonoBehaviour | Aim/fire/reload logic on any shooter (player or AI). |
| `FS_ShooterSystem.ShooterController` | `EquippableSystemBase` (`SystemState.Shooter`) | Player-side input → aiming, firing, scope, aim assist. |
| `FS_ShooterSystem.ShooterSettings` (player root) | singleton | `hitIgnoreMask`, `explosionIgnoreMask`, `aimWalkSpeed`/`aimRunSpeed`, `hitEffectLifetime`. |
| `FS_ShooterSystem.ShooterAIController` + `FS_CombatCore.CombatAIController` | MonoBehaviours | AI-side shooting, cover, flank settings. |
| `FS_ShooterSystem.ShooterAIManager` (player root) | singleton | Squad flanking logic. |
| `FS_CoverSystem.CoverController` | `SystemBase` (`SystemState.Cover`) | Player cover. |
| `FS_Core.Damagable` / `FS_ShooterSystem.ShooterBoneDamageHandler` | MonoBehaviours | Health / per-bone damage multipliers. |

## Common issues

- **Symptom:** Shots hit nothing / hit the shooter. **Cause:** `ShooterSettings.hitIgnoreMask` missing `Player`/`Enemy`, or no bone colliders. **Fix:** Set the mask (demo uses Player+Enemy); run the ragdoll/damage setup (damage skill).
- **Symptom:** Enemies idle. **Fix:** Bake NavMesh.
- **Symptom:** `ShooterUI.i` null. **Cause:** Player built without the Shooter system (no `Shooter UI` under `Game UI`). **Fix:** Rebuild with Shooter ticked or add `Resources/Shooter UI.prefab` under `Game UI`.
- **Symptom:** Old docs mention `Tools > FS Systems`. **Fix:** It is `Tools > FS System`.
- **Symptom:** `Shooter Weapon Editor`'s `Fire Frequency`/`Magazine Size` missing. **Cause:** Category asset `Resources/Category/Shooter Weapon.asset` not loaded. **Fix:** Keep the Resources folder intact.

## Boundaries

- Humanoid rigs only.
- Melee fighting requires FS Melee Combat System 2 (shares Combat Core; both can be ticked in the FS Window).
- Stealth detection/awareness is FS Stealth System (this package's vision is binary).
- No networking; one player per scene (`ShooterSettings`, `ShooterAIManager`, `CombatAIManager` are singletons).
- Behavior Tree assets under `AI System` are not a supported workflow here.
