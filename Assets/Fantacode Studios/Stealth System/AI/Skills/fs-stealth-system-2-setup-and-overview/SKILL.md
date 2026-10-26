---
name: fs-stealth-system-2-setup-and-overview
description: "Use this skill whenever the user has, wants to install, or asks anything general about FS Stealth System 2 by Fantacode Studios — e.g. 'is the fantacode stealth asset installed', 'set up the stealth system', 'what comes with the stealth package', 'where are the stealth demos', 'how do vision, hearing, awareness and situations fit together'. Covers verifying the install, first-time setup, package layout (Stealth, AI, Carry, Cover, Third Person Controller), the five demo scenes plus the integration package, and an index of the other skills. Do NOT use for building the player (see fs-stealth-system-2-create-stealth-player), guards (…-create-stealth-ai), hiding spots / zones / lights / bodies (…-design-stealth-areas), detection tuning (…-tune-detection-and-alerts) or scripted AI moments (…-author-ai-behavior). When in doubt whether a stealth, guard-detection, sneaking or hiding request could involve FS Stealth System 2, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Set Up FS Stealth System 2

FS Stealth System 2 adds guard perception and stealth gameplay to Third Person Controller characters. Guards (`StealthAIController` on an FS AI) perceive through three sensors — **AIVision** (a primary and a peripheral vision volume with line-of-sight checks), **AIHearing** (footsteps, thrown objects, gunshots broadcast as `GameSoundEvents`) and **AITouch** — and accumulate **awareness** (`AwarenessSystem`, 0–1, shown to the player as a detection meter). The player is a **VisualStimulus** whose **SuspicionProfile** decides whether guards care: by default everywhere is hostile, or you mark **Restricted** (warn first), **Hostile** (attack on sight) and **Hiding** (tall grass while crouched) areas and let a drawn weapon make the player a threat. Full awareness or a touch creates a **situation** on the scene-wide `StealthAIManager` — Suspicious Investigation, Trespassing (warnings), Alert Investigation or Combat — which alerts nearby guards, assigns investigators, plays **dialogue barks** and drives the generated AI state machine (Investigate with search, Warn). Optional **light-based concealment** (`LightVision` + `StealthLight`), **hiding spots** (bins), **body carrying and dumping** (Carry System) and **cover** (Cover System) ship in the package; takedowns, gunplay and parkour come from the Melee, Shooter and Parkour packages and are demonstrated in the bundled integration scenes. Configuration is Inspector-driven; full C# source. This skill confirms the install, completes one-time setup, maps the package and routes to the sibling skills.

## When to use this skill

- "Is the Fantacode stealth asset installed? Set it up."
- "Where are the stealth demos? What does each show?"
- "How do awareness, situations and the AI state machine relate?"
- "Does it work with Melee / Shooter / Parkour?"
- Any sneaking/guard/detection request in a project with `Assets/Fantacode Studios/Stealth System`.

Not for:
- Building the sneaking player (crouch, cover, throwables, carry, HUD) — `fs-stealth-system-2-create-stealth-player`.
- Building guards and their sensors — `fs-stealth-system-2-create-stealth-ai`.
- Level design: hiding areas, restricted/hostile zones, lights, hiding bins, bodies, distractions — `fs-stealth-system-2-design-stealth-areas`.
- Detection speed, hearing, alert spread, situation timings, barks — `fs-stealth-system-2-tune-detection-and-alerts`.
- Intro/triggered/idle behaviours, patrols — `fs-stealth-system-2-author-ai-behavior`.

## Prerequisites

- Unity 6000.0 or newer; render-pipeline agnostic (system asset; demo post-processing volumes are optional).
- **AI Navigation** package (guards path on a baked NavMesh); TextMeshPro for demo HUDs.
- Compiles into `Assembly-CSharp`.

**Programmatic install check** (Editor):

```csharp
using UnityEditor;
using UnityEngine;

public static class FSStealthInstallCheck
{
    [MenuItem("Tools/Check FS Stealth Install")]
    public static void Check()
    {
        bool folder = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/Stealth System");
        bool ai = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/AI System");
        bool carry = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/Carry System");
        bool cover = AssetDatabase.IsValidFolder("Assets/Fantacode Studios/CoverSystem");
        bool t1 = System.Type.GetType("FS_StealthSystem.StealthAIController, Assembly-CSharp") != null;
        bool t2 = System.Type.GetType("FS_StealthSystem.AIVision, Assembly-CSharp") != null;
        bool layers = LayerMask.NameToLayer("Enemy") != -1 && LayerMask.NameToLayer("Deadbody") != -1 && LayerMask.NameToLayer("Player") != -1;
        Debug.Log($"FS Stealth install — folders:{folder}/{ai}/{carry}/{cover} StealthAIController:{t1} AIVision:{t2} Layers:{layers}");
    }
}
```

- All `true` → ready. `folder` `true`, types `false` → compile errors (usually a missing bundled folder). `folder` `false` → tell the user to import from https://assetstore.unity.com/packages/tools/game-toolkits/fs-stealth-system-2-407220. `Layers` `false` → Workflow: Import project settings.

Manual check: the FS Systems window (`Tools > FS System > FS Window`) → Setup lists **Stealth** (Player) and **Stealth AI** (AI).

## Quick start

1. Import the package. The **FS Systems** window opens after import (and on project open unless "Don't Show on Start") and imports project settings on first open (layers `Player`, `Enemy`, `Deadbody`, `OcuppantObject`, `Cover`, tags, ragdoll settings). Otherwise `Tools > FS System > FS Window` (exact path `Tools/FS System/ FS Window`) → Welcome → **Import Project Settings**.
2. Open `Assets/Fantacode Studios/Stealth System/Demo/1 - Basic Stealth Demo/1- Basic Stealth Demo.unity`, press Play. Crouch with **C**, stay out of the guards' vision volumes, pick up the brick (**E**) and throw it (**Mouse 0**) to lure a guard, reach the goal.
3. Watch the detection meters above guards fill while you are in view and drain when you break line of sight.

Expected observable result: meters rise in view and decay out of view; a filled meter triggers "You have been detected" (demo restart); the thrown brick's impact makes the nearest guard walk over and search; `StealthAIManager.Instance.HighestSituationType` changes accordingly; no Console errors.

## Workflows

### Workflow: Import project settings

`Tools > FS System > FS Window` → Welcome → **Import Project Settings** (or `FS_Core.FSSystemsSetupEditorWindow.ShowWindow(); FS_Core.FSSystemsSetupEditorWindow.setupScript.ImportProjectSettings();`). Shared effects: FS layers/tags (`Player`, `Enemy`, `Deadbody`, `NPC`, `Cover`, `OcuppantObject`, `RagdollBone`, …), ragdoll collision ignores, the `inputsystem` define when the new Input System is active. Package-specific: the Stealth entries (`FSSystemsSetup.StealthSystemSetup` / `StealthSystemAISetup`) run the Cover System's install hook (assigns the Cover layer to the cover prefab) and add no layers of their own. Stealth sensing uses `StealthSettings` layer masks: **Visual Stimulus Layer** = `Player` + `Deadbody`, **Hearing Layer** = `Enemy`.

**Expected result:** `LayerMask.NameToLayer("Deadbody") != -1`; Setup lists **Stealth** / **Stealth AI**.

### Workflow: Explore the demo scenes

| Scene (`Stealth System/Demo/`) | Shows |
|---|---|
| `1 - Basic Stealth Demo/1- Basic Stealth Demo.unity` | Four guards, a `HostileArea`, a throwable brick, an authored behaviour trigger, goal/detection restart HUD. |
| `2 - Tresspassing Demo/2 - Tresspassing Demo.unity` | `RestrictedArea` with warning guards, five `HidingArea` grass patches, patrol routes, objective HUD. |
| `3 - Light Detection Demo/3 - Light Detection Demo.unity` | `StealthLight`s on lamps (some moving), player `LightVision` on, light HUD. |
| `4 - Light Types Demo/4 - Light Types Lab.unity` | Lab for directional/spot/point light concealment values. |
| `5 - Body Cary Demo/5 - Body Carry Demo.unity` | Dead body (`Deadbody` stimulus + carry hotspot), `Body Dumpster` (dump + hide), `Hiding Bin`, guarded room. |
| `Integrations (Requires other assets)/Integration Scenes.unitypackage` | Stealth + Melee (takedowns, parkour) and Stealth + Shooter scenes; import only after Melee Combat, Shooter and Parkour & Climbing are installed (see its `_READ ME`). |

Demo-only scripts live in `Demo/_Shared/Scripts` (`DetectionRestartDemo`, `GoalReachedDemo`, HUDs) — not part of the runtime.

### Workflow: Locate package content

| Need | Path (under `Assets/Fantacode Studios/`) |
|---|---|
| Drop-in player (fixed model) | `Stealth System/Game/Prefabs/FS Stealth Player.prefab` |
| Drop-in guard (fixed model, `Stealth AI with Chase SM`) | `Stealth System/Game/Prefabs/FS Stealth AI.prefab` |
| FS Window donors (not for scenes) | `Stealth System/Resources/Stealth Controller.prefab`, `Stealth AI Controller.prefab` |
| Sensor volume prefabs, dead body, carry hotspot, detection meter | `Stealth System/Game/Prefabs/{VisionVolume, TouchVolume, Deadbody, Carry Hotspot, DetectionMeterUI}.prefab` |
| Reference state machines | `Stealth System/Game/AI State Machine/Stealth AI with Chase SM.asset` (+ `Stealth AI SM.asset`) |
| Demo bark sets | `Stealth System/Demo/_Shared/Dialogues/*.asset` |
| Runtime scripts | `Stealth System/Scripts/{Sensors, States, UI}`, `StealthAIController.cs`, `StealthAIManager.cs`, `AwarenessSystem.cs`, `*Area.cs`, `SuspicionProfile.cs`, `VisualStimulus.cs` |
| State-machine patch (FS Window) | `Stealth System/Scripts/Editor/StealthStateMachinePatch.cs` |
| Carry / hiding prefabs | `Carry System/Prefabs/{Body Dumpster, Hiding Bin, Deadbody}.prefab`, `Carry System/Data` |
| Cover | `CoverSystem/` (cover controller, mobile UI) |
| Throwables, barks, interaction core | `Third Person Controller/Prefabs/Brick.prefab`, `Third Person Controller/Scripts/Core/Dialogue`, `Third Person Controller/Interaction Core` |
| Docs | `Stealth System/Stealth System.pdf` (basic setup), gitbook link above |

### Workflow: Pick the right sibling skill

| User wants to… | Skill |
|---|---|
| Sneaking player with own model, crouch/cover/throw/carry/hide, meters HUD | `fs-stealth-system-2-create-stealth-player` |
| Guards with own model, vision/hearing/touch, patrol, combat via Melee/Shooter | `fs-stealth-system-2-create-stealth-ai` |
| Where the player is safe/warned/attacked, grass, lights, bins, bodies, distractions | `fs-stealth-system-2-design-stealth-areas` |
| Detection speed, hearing range, alert spread, situation timings, barks | `fs-stealth-system-2-tune-detection-and-alerts` |
| Intro/authored/idle behaviours and how stealth interrupts them | `fs-stealth-system-2-author-ai-behavior` |

## Verification

- Folders exist; `FS_StealthSystem.StealthAIController`, `AIVision`, `AIHearing`, `AITouch`, `AwarenessSystem`, `StealthAIManager`, `VisualStimulus`, `FS_InteractionSystem.CarryHandler`, `FS_Core.AIController` resolve.
- Layers `Player`, `Enemy`, `Deadbody`, `OcuppantObject` exist.
- Demo 1 plays: meters fill/drain; brick lures a guard; no Console errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools/FS System/ FS Window` → Setup → **Stealth** / **Stealth AI** | EditorWindow | Builds player / guard with any combination of FS systems. |
| `FS_StealthSystem.StealthAIManager.Instance` | MonoBehaviour (on the player root) | Situations: `CreateOrJoinSituation`, `EscalateSituation`, `EndSituation`, `HighestSituationType`, `OnHighestSituationChanged`, `GlobalLastKnownPlayerPos`. |
| `FS_StealthSystem.StealthAIController` | MonoBehaviour (guard) | `PlayerInSight`, `CurrentSituation`, `ActiveStimulus`, `IdentifiedPlayerAsThreat`, bark sets. |
| `FS_StealthSystem.AwarenessSystem` | MonoBehaviour (guard) | `CurrentAwareness`, `IsAware`, rates. |
| `FS_StealthSystem.AIVision` / `AIHearing` / `AITouch` | MonoBehaviours (guard) | Sensors. |
| `FS_StealthSystem.VisualStimulus` / `SuspicionProfile` / `LightVision` | MonoBehaviours (player, props) | What guards perceive and whether it matters. |
| `FS_StealthSystem.HidingArea` / `RestrictedArea` / `HostileArea` / `StealthLight` | MonoBehaviours (level) | Level rules. |
| `FS_StealthSystem.StealthSettings.i` | MonoBehaviour (player root) | Layer masks, volume/body/hotspot prefabs. |
| `FS_StealthSystem.StealthBlackboardKeys` | consts | `Investigate`, `InvestigatePosition`, `IsAlertedInvestigation`, `WarnTarget`, `FollowWhileWarning`, `IdentifiedPlayerAsThreat`, … |

## Common issues

- **Symptom:** Guards never notice the player. **Cause:** No `StealthAIManager`/`StealthSettings` in the scene (they live on the FS Window player root), player not a `VisualStimulus` of type Player on the `Player` layer, or `SuspicionProfile` not suspicious outside marked areas. **Fix:** Build the player with the FS Window; see the areas skill.
- **Symptom:** Guard detects the player twice / instantly. **Cause:** The base AI's `SimpleVision` child left on the guard (the FS Window removes it). **Fix:** Delete the `SimpleVision` object.
- **Symptom:** Nothing happens after detection. **Cause:** No combat package — the guard only chases. **Fix:** Tick Melee Combat AI / Shooter AI when building guards (expected per the PDF).
- **Symptom:** Old docs mention `Tools > FS Systems`. **Fix:** It is `Tools > FS System`.

## Boundaries

- Perception is for AI guards; the player has no stealth "meter" of its own beyond the per-guard detection meters.
- Takedowns, shooting and parkour are separate packages (integration scenes are optional).
- No faction system beyond the `Threat` stimulus type; guards perceive the single tagged Player.
- Behavior Tree assets under `AI System` are not a supported workflow.
