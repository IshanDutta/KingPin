---
name: fs-stealth-system-2-create-stealth-player
description: "Use this skill whenever the user wants a playable sneaking character with FS Stealth System 2 — e.g. 'build a stealth player with my model', 'add stealth to my FS player', 'guards should see me only when I'm lit', 'drawing my sword should make guards hostile', 'throw a brick to distract guards', 'hide in the locker', 'carry the body'. Covers the FS Systems window (Stealth ticked with other systems), the drop-in prefab, player components (VisualStimulus, SuspicionProfile, WeaponSuspicionFactor, LightVision, carry, cover, throwables, hiding), the managers on the player root (StealthAIManager, StealthSettings, StealthSoundHandler, AwarenessUIManager) and input keys. Do NOT use for guards (see fs-stealth-system-2-create-stealth-ai), level areas (…-design-stealth-areas) or detection tuning (…-tune-detection-and-alerts). When in doubt whether a sneaking-player or player-visibility request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Create a Stealth Player

The stealth player is a Third Person Controller character whose `Player` object is a **VisualStimulus** (type Player, layer `Player`) that guards can see, hear (footsteps via `FootStepEffects`) and touch. Its **SuspicionProfile** collects suspicion factors — `WeaponSuspicionFactor` (a visible equipped weapon makes the player an instant threat), `RestrictedArea` (warn first) and `HostileArea` (instant) — and **Is Suspicious By Default** (true) decides whether guards care outside any area. **LightVision** (off by default) scales detection by how lit the player's vision points are. The player also carries the movement tools of sneaking: crouch (**C**), cover (**G**, Cover System), throwables (**Mouse 0**, `ThrowableController`), body carrying (`CarryHandler`, Carry System) and hiding spots (`EnterExitInteractionHandler`). The **FS Player root** additionally hosts the scene-wide managers: `StealthAIManager` (situations), `StealthSettings` (layers, prefabs), `StealthSoundHandler` (sound → stimulus), `AwarenessUIManager` (detection meters). Build with the FS Systems window (any model, combinable with every other FS system) or drop in the shipped prefab.

## When to use this skill

- "Sneaking player with my model (plus melee / parkour / shooter)."
- "Add stealth to the player I already built."
- "Only detect me when I'm lit."
- "Weapon out = guards attack."
- "Throw things to distract guards."
- "Hide in bins / carry bodies."
- "No detection meters on screen."

Not for:
- Guards, sensors — `fs-stealth-system-2-create-stealth-ai`.
- Areas, lights, hiding bins, dumpsters, distractions placed in the level — `fs-stealth-system-2-design-stealth-areas`.
- Rates, radii, situation timings — `fs-stealth-system-2-tune-detection-and-alerts`.
- Takedowns — FS Melee Combat System 2 (`fs-melee-combat-system-2-add-finishers-and-stealth-attacks`).

## Prerequisites

- Installed: `System.Type.GetType("FS_StealthSystem.SuspicionProfile, Assembly-CSharp") != null` (else import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220).
- A Humanoid model prefab with a valid avatar (FS Window build).
- Project settings imported (layers `Player`, `Enemy`, `Deadbody`, `Cover`, `OcuppantObject`).

## Quick start

Goal: a sneaking player with the user's model.

1. Delete `Main Camera`. `Tools > FS System > FS Window` → **Setup** → character type **Player** → tick **Locomotion** (required), **Stealth** and **Carry** (body carrying and hiding spots); tick every other owned FS system (Interaction, Melee Combat, Shooter, Parkour and Climbing, …) — one character is built with all of them, which is the intended way to combine FS packages.
2. Drop the humanoid model on the preview, click **Setup**.
3. Move `FS Player` to the start position, bake a NavMesh for the guards, Play. **C** crouches; walk into a guard's view to see its meter fill.

Expected observable result: `FS Player` root carries `StealthAIManager`, `StealthSettings`, `StealthSoundHandler`, `AwarenessUIManager` (camera and `Game UI` canvas assigned) plus `AIManager`; `Player` (layer `Player`) has `VisualStimulus` (Player), `SuspicionProfile`, `WeaponSuspicionFactor`, `LightVision`, `CoverController` + `CoverInputManager`, `CarryHandler`, `EnterExitInteractionHandler`, `ThrowableController`, `HotspotController`; `Game UI` contains the `Cover Mobile Controller`; detection meters appear over guards in Play; no Console errors.

## Workflows

### Workflow: Build the player (FS Systems window)

What Setup does (`FSSystemsSetup.StealthSystemSetup`): instantiates the Third Person Controller player, copies the components of `Resources/Stealth Controller.prefab` onto `Player` and the root (managers), merges the stealth/cover/carry animators into the generated controller, adds the Cover mobile UI under `Game UI`, wires `AwarenessUIManager` to the player camera and `FS_GameUI` canvas, and sets up `CoverInputManager`. Other ticked systems add theirs — **Carry** (`FSSystemsSetup.CarrySystemSetup`, donor `Carry System/Resources/Carry Controller.prefab`) contributes `CarryHandler` and `EnterExitInteractionHandler`, and creates the `Carry Socket - …` transforms on the model's right hand and upper arm; a Melee/Shooter weapon equipped later automatically becomes a suspicion factor.

Drop-in alternative: `Game/Prefabs/FS Stealth Player.prefab` (fixed Default Character, everything above included, HP bar and quick switch UI).

**Expected result:** As in Quick start.

### Workflow: Player-side perception components (`Player` object)

| Component | Fields | Effect |
|---|---|---|
| `VisualStimulus` | Stimulus Type Player, Importance 1, Vision Points (local points guards raycast at), Raycast Height Offset, Dialogue Bark Tag | What guards see; keep type Player and the object on layer `Player`. |
| `SuspicionProfile` | **Is Suspicious By Default** (true) | true: guards react everywhere; false: only inside Restricted/Hostile areas or with a weapon out (areas skill). `IsSuspicious`, `IsInstantThreat`, `ShouldWarn` read by guards. |
| `WeaponSuspicionFactor` | — | Active while an `EquippableItem` with **Is Visible When Equipped** is held → instant threat. |
| `LightVision` | **Use Light Vision** (off), Falloff Sharpness, reference Directional 1 / Spot 60 / Point 40 intensities, Use Light Color, debug rays | On: visibility 0–1 from `StealthLight` lights at the vision points multiplies awareness gain (dark = slower detection). |
| `EnterExitInteractionHandler` | (live state) | Marks the player concealed inside an `EnterExitHotspot` (bin, locker): guards' vision reports hidden. Added by the **Carry** toggle. |
| `CarryHandler` | Sockets | Picks up `Carryable` bodies from a `CarryHotspot`, drops/dumps them. Added by the **Carry** toggle, which also fills **Sockets** with the two bone transforms it creates. |
| `CoverController` / `CoverInputManager` | Cover key **G** | Cover System sneaking behind low walls. |
| `ThrowableController` / `ThrowableHandler` | — | Equip a throwable (`Brick` via `EquipHotspot`), aim + **Mouse 0** throws; impact emits a `ThrowImpact` sound guards hear. |

Also relevant: `FootStepEffects` → **Footstep Sound Radius** (15) × speed, **Crouch Volume Multiplier** (0.5) — crouching halves the audible radius.

**Expected result:** `player.GetComponent<FS_StealthSystem.SuspicionProfile>().IsSuspicious` reflects areas/weapon; `LightVision.GetVisibility(stimulus)` returns < 1 in shadow when enabled.

### Workflow: Managers on the FS Player root

| Component | Fields | Notes |
|---|---|---|
| `StealthSettings` | Visual Stimulus Layer (`Player`, `Deadbody`), Hearing Layer (`Enemy`), Vision Volume Prefab, Touch Volume Prefab, Dead Body Prefab, Carry Hotspot | Singleton; guards read masks and instantiate volumes from here. Add layers here if your props/guards use others. |
| `StealthAIManager` | Alert radii, propagation, situation configs, downgrade delay, debug | Scene-wide situation brain (tuning skill). |
| `StealthSoundHandler` | Sound Priority Mappings (Gunshot High, Footstep Low, Throw Impact Low) | Turns `GameSoundEvents` into audio stimuli for `AIHearing` colliders. |
| `AwarenessUIManager` | Detection Meter UI Prefab, Player Camera, Screen Canvas, Hide Delay After Detected (1.5) | One meter per registered guard, positioned toward the guard on screen. |

One player root per scene; duplicating it duplicates the singletons.

**Expected result:** `StealthAIManager.Instance`, `StealthSettings.i`, `AwarenessUIManager.i` non-null in Play.

### Workflow: Input

`LocomotionInputManager`: Crouch **C**, Sprint Shift (louder, faster detection by motion), Quick Switch Caps Lock, Item Throw **Mouse 0**, Throw Cancel **X**. `InteractionInputManager`: Interact **E** (pick up brick/body, enter bins), Throw **R**, Drop **Y** (carried body). `CoverInputManager`: Cover **G**. Mobile equivalents ship as `Cover Mobile Controller` and the Third Person Controller UI. New Input System bindings live in `Third Person Controller/InputAction/FSSystemsInputAction.inputactions`.

**Expected result:** `LocomotionController.IsCrouching` true on C; `CarryHandler.IsCarrying` true after E on a body.

## Verification

- `Player`: `VisualStimulus` (Type Player, layer `Player`), `SuspicionProfile`, `WeaponSuspicionFactor`, `LightVision`, `CarryHandler`, `EnterExitInteractionHandler`, `CoverController`, `ThrowableController` present.
- Root: `StealthAIManager`, `StealthSettings`, `StealthSoundHandler`, `AwarenessUIManager` with camera + canvas assigned.
- Play: guard meters appear; crouching behind a low wall stops awareness growth; equipping a visible weapon flips `SuspicionProfile.IsInstantThreat`.
- No Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/ FS Window` → Setup → **Stealth** | EditorWindow | Builds the player. |
| `FS_StealthSystem.VisualStimulus` | MonoBehaviour | `Type`, `Importance`, `VisionPoints`, `IsConcealed`, `GetStimulus()`. |
| `FS_StealthSystem.SuspicionProfile` | MonoBehaviour | `IsSuspicious`, `IsInstantThreat`, `ShouldWarn`, `AddFactor/RemoveFactor(ISuspicionFactor)`. |
| `FS_StealthSystem.ISuspicionFactor` | interface | `IsActive`, `IsInstant`, `WarnsFirst` — implement for custom factors (disguise, curfew). |
| `FS_StealthSystem.LightVision.GetVisibility(stimulus)` | static | 0–1 lighting visibility. |
| `FS_StealthSystem.StealthSettings.i` / `StealthAIManager.Instance` / `AwarenessUIManager.i` | singletons | Managers. |
| `FS_InteractionSystem.CarryHandler` | MonoBehaviour | `IsCarrying`, `Carry`, `ForceRelease`; blocks finishers while carrying. |
| `FS_Core.GameSoundEvents.Emit(GameSoundEvent)` | static | Emit custom sounds guards can hear. |

## Common issues

- **Symptom:** Guards ignore the player completely. **Cause:** `Is Suspicious By Default` off and no area/weapon factor; or `Player` not on layer `Player`. **Fix:** Tick it or add areas.
- **Symptom:** No meters. **Cause:** `AwarenessUIManager` camera/canvas empty (manual build). **Fix:** Assign `Third Person Camera`'s Camera and the `Game UI` RectTransform.
- **Symptom:** Light vision does nothing. **Cause:** `Use Light Vision` off or lamps lack `StealthLight`. **Fix:** Tick it; areas skill.
- **Symptom:** Two players → errors. **Cause:** Duplicate singletons. **Fix:** One FS Player root.
- **Symptom:** Can't throw. **Cause:** No throwable equipped (pick up `Brick` via its `EquipHotspot`). **Fix:** Add a Brick pickup.
- **Symptom:** Model validation error in the FS Window. **Fix:** Humanoid rig with Animator.

## Boundaries

- One perceivable player; co-op players are not supported.
- No disguise/noise-meter UI beyond the per-guard meters (build custom factors via `ISuspicionFactor`).
- Takedowns and ranged combat come from Melee/Shooter packages.
