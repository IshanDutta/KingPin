using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using FS_CombatCore;

// Lives on the soldier root after the impact. Never disable this component or
// destroy the soldier root while its asynchronous fragment operations are running.
[DisallowMultipleComponent]
public class BossSoldierRagdollImpact : MonoBehaviour
{
    public bool HasImpacted { get; private set; }
    private readonly Dictionary<Transform, Rigidbody> bodies = new Dictionary<Transform, Rigidbody>();
    private readonly Dictionary<string, Transform> pending = new Dictionary<string, Transform>();
    private readonly List<RagdollDismembermentVisual> visuals = new List<RagdollDismembermentVisual>();
    private Vector3 impactOrigin;
    private float separationVelocity;
    private float upwardVelocity;
    private float spinVelocity;
    private Vector3 launchCenter;
    private readonly Dictionary<Rigidbody, Vector3> pendingSpin = new Dictionary<Rigidbody, Vector3>();
    private float nextFloorSweep;
    private int corpseLayer;
    private Transform attackingBoss;
    private readonly HashSet<Collider> ragdollColliders = new HashSet<Collider>();
    private readonly HashSet<Collider> disabledCharacterColliders = new HashSet<Collider>();
    private float nextBossCollisionRefresh;
    private static readonly HashSet<Collider> knownCorpseColliders = new HashSet<Collider>();
    private static Collider[] protectedActorColliders = new Collider[0];
    private static float nextActorColliderScan = -1f;
    private static int actorScanFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCorpseCollisionCache()
    {
        knownCorpseColliders.Clear();
        protectedActorColliders = new Collider[0];
        nextActorColliderScan = -1f;
        actorScanFrame = -1;
    }

    public static bool IsCorpseCollider(Collider collider)
    {
        if (collider == null) return false;
        return knownCorpseColliders.Contains(collider) || IsImpacted(collider.transform);
    }

    private static void RefreshProtectedActors(bool force = false)
    {
        if (actorScanFrame == Time.frameCount || (!force && Time.unscaledTime < nextActorColliderScan)) return;
        actorScanFrame = Time.frameCount;
        nextActorColliderScan = Time.unscaledTime + 1f;
        var roots = new HashSet<Transform>();
        foreach (var boss in UnityEngine.Object.FindObjectsOfType<BossEnemyAI>())
        {
            if (boss == null) continue;
            roots.Add(boss.transform);
            if (boss.PlayerCollisionRoot != null) roots.Add(boss.PlayerCollisionRoot);
        }
        // Also support tagged or respawned player characters without relying
        // on the attacking boss's cached player transform.
        foreach (var playerObject in GameObject.FindGameObjectsWithTag("Player"))
            if (playerObject != null) roots.Add(playerObject.transform);
        var colliders = new HashSet<Collider>();
        foreach (var root in roots)
            if (root != null)
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    if (col != null && !IsCorpseCollider(col)) colliders.Add(col);
        protectedActorColliders = colliders.ToArray();
        knownCorpseColliders.RemoveWhere(col => col == null);
    }
    private bool impactRigPrepared;
    private bool queuedEarly;
    private bool preparationAttempted;
    public bool ImpactPreparationFinished => preparationAttempted &&
        (!queuedEarly || visuals.All(v => v == null || v.StagedBatchFinished));
    public bool ImpactBatchReady => queuedEarly && visuals.All(v => v != null && v.StagedBatchFinished && v.StagedReadyCount > 0);
    private HashSet<Transform> preparedBoneSet;
    private SkinnedMeshRenderer[] originalSkins;
    private readonly Dictionary<Rigidbody, BoxCollider> fittedColliders = new Dictionary<Rigidbody, BoxCollider>();
    private readonly Dictionary<Rigidbody, Vector3> pendingSeparation = new Dictionary<Rigidbody, Vector3>();
    private bool holdingForCuts;
    // No artificial post-impact waiting for slow mesh operations.
    private readonly HashSet<Rigidbody> detachedBodies = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, float> lastColliderBottom = new Dictionary<Rigidbody, float>();
    private readonly Dictionary<Rigidbody, float> floorCorrections = new Dictionary<Rigidbody, float>();
    private readonly RaycastHit[] floorSweepHits = new RaycastHit[96];

    [Header("Lightweight Floor Guard")]
    [SerializeField] private bool protectVisibleMesh = true;
    [Header("Ragdoll Joint Stability")]
    [SerializeField] private bool useJointProjection = true;
    [SerializeField, Min(0.001f)] private float jointProjectionDistance = 0.035f;
    [SerializeField, Range(1f, 180f)] private float jointProjectionAngle = 12f;
    [SerializeField] private bool flattenPhysicalBoneHierarchy = true;
    [SerializeField, Min(1f)] private float maximumRagdollAngularSpeed = 9f;
    [Tooltip("Enable on the soldier prefab to prepare asset cuts before the boss arrives.")]
    [SerializeField] private bool prepareOnSpawn = true;
    private static int lastSpawnPreparationFrame = -1;
    private readonly Dictionary<Rigidbody, Rigidbody> physicsGroups = new Dictionary<Rigidbody, Rigidbody>();
    private bool physicsGroupsCaptured;

    private IEnumerator Start()
    {
        if (!prepareOnSpawn || preparationAttempted || HasImpacted) yield break;
        // Let the model and Animator initialize. Prepare at most one prefab
        // per render frame through this spawn path to spread the CPU work.
        yield return null;
        while (!preparationAttempted && !HasImpacted && lastSpawnPreparationFrame == Time.frameCount)
            yield return null;
        if (!preparationAttempted && !HasImpacted)
        {
            lastSpawnPreparationFrame = Time.frameCount;
            PrepareForAttack();
        }
    }

    [Header("Body Knockdown On Impact")]
    [SerializeField] private bool knockDownRemainingBody = true;
    [SerializeField, Min(0f)] private float bodyKnockbackVelocity = 1.6f;
    [SerializeField, Min(0f)] private float bodyDownwardVelocity = 1.25f;
    [SerializeField, Min(0f)] private float bodyTipAngularVelocity = 4.5f;
    private Transform preparedHips;
    private Rigidbody collapseReference;
    private Vector3 collapseLocalUp;
    private Vector3 collapseDirection;
    private float collapseRetryAt = -1f;

    // No render-frame teleports or collider resizing after physics release.
    // Floor CCD and severe-crossing recovery run only on fixed ticks.
    private void IgnoreAllCorpsePairs()
    {
        // Remove secondary authored child shapes when a fitted collider already
        // represents that rigidbody. Prevent multiple contacts for the same part.
        foreach (var col in ragdollColliders.ToArray())
        {
            if (col == null) continue;
            var rb = col.attachedRigidbody;
            if (rb != null && fittedColliders.TryGetValue(rb, out var fitted) && fitted != null && fitted.enabled && col != fitted)
            {
                col.enabled = false;
                ragdollColliders.Remove(col);
            }
        }
        var cols = ragdollColliders.Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy).ToArray();
        for (int i = 0; i < cols.Length; i++)
            for (int j = i + 1; j < cols.Length; j++)
                if (cols[i].attachedRigidbody != cols[j].attachedRigidbody)
                    Physics.IgnoreCollision(cols[i], cols[j], true);
    }

    private bool AnyMovingBody()
    {
        foreach (var body in bodies.Values)
            if (body != null && !body.isKinematic && !body.IsSleeping()) return true;
        return false;
    }

    // Keep environmental contact; ignore specific character collider pairs.
    // All detached fragments are tracked even when the asset reparents them.
    private int RefreshBossCollisionIgnores()
    {
        RefreshProtectedActors();
        int ignored = 0;
        foreach (var corpse in ragdollColliders)
        {
            if (corpse == null) continue;
            knownCorpseColliders.Add(corpse);
            if (!corpse.enabled || !corpse.gameObject.activeInHierarchy) continue;
            foreach (var actorCollider in protectedActorColliders)
            {
                if (actorCollider == null || actorCollider == corpse || !actorCollider.enabled ||
                    !actorCollider.gameObject.activeInHierarchy || knownCorpseColliders.Contains(actorCollider)) continue;
                if (!Physics.GetIgnoreCollision(corpse, actorCollider))
                    Physics.IgnoreCollision(corpse, actorCollider, true);
                ignored++;
            }
        }
        // The attacking boss is always known at impact, even before a cache refresh.
        if (attackingBoss != null)
            foreach (var actorCollider in attackingBoss.GetComponentsInChildren<Collider>(true))
            {
                if (actorCollider == null || !actorCollider.enabled || !actorCollider.gameObject.activeInHierarchy || knownCorpseColliders.Contains(actorCollider)) continue;
                foreach (var corpse in ragdollColliders)
                    if (corpse != null && corpse.enabled && corpse.gameObject.activeInHierarchy && !Physics.GetIgnoreCollision(corpse, actorCollider))
                        Physics.IgnoreCollision(corpse, actorCollider, true);
            }
        return ignored;
    }

    private void FixedUpdate()
    {
        if (!HasImpacted) return;
        // A dead character must never regain its locomotion capsule.
        foreach (var col in disabledCharacterColliders)
            if (col != null && col.enabled) col.enabled = false;
        if (holdingForCuts) return;
        CheckBodyKnockdown();
        if (Time.time >= nextFloorSweep)
        {
            // CCD remains enabled; sweep only awake bodies and at a bounded cadence.
            nextFloorSweep = Time.time + 0.10f;
            if (protectVisibleMesh && AnyMovingBody()) PreventFloorTunnelling();
        }
        if (Time.time < nextBossCollisionRefresh) return;
        nextBossCollisionRefresh = Time.time + 1f;
        // Ignore pairs persist. Refresh at low cadence for newly enabled or
        // respawned character colliders, including a player near a flying limb.
        RefreshBossCollisionIgnores();
    }

    private void ReportFloorCollisionSetup()
    {
        int solidCount = ragdollColliders.Count(c => c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy);
        Debug.Log("[Boss Impact] Floor collision setup | corpse layer=" + LayerMask.LayerToName(corpseLayer) +
            " (" + corpseLayer + ") | solid colliders=" + solidCount, this);
        var floorHits = Physics.RaycastAll(transform.position + Vector3.up * 0.5f, Vector3.down, 5f,
            ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in floorHits.OrderBy(h => h.distance))
        {
            if (hit.collider == null || hit.normal.y < 0.35f) continue;
            var t = hit.collider.transform;
            if (t == transform || t.IsChildOf(transform) || ragdollColliders.Contains(hit.collider)) continue;
            if (attackingBoss != null && (t == attackingBoss || t.IsChildOf(attackingBoss))) continue;
            int floorLayer = hit.collider.gameObject.layer;
            if (Physics.GetIgnoreLayerCollision(corpseLayer, floorLayer))
                Debug.LogError("[Boss Impact] Corpse/floor layer collision is DISABLED: " +
                    LayerMask.LayerToName(corpseLayer) + " vs " + LayerMask.LayerToName(floorLayer) +
                    ". Enable that pair in Project Settings > Physics > Layer Collision Matrix.", this);
            else
                Debug.Log("[Boss Impact] Floor layer collision enabled | surface=" + hit.collider.name +
                    " | floor layer=" + LayerMask.LayerToName(floorLayer), this);
            return;
        }
        Debug.LogWarning("[Boss Impact] No solid upward-facing floor detected within 5m below soldier. Check the floor collider.", this);
    }

    private static readonly HumanBodyBones[] humanoidBones = {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot
    };

    public static bool IsImpacted(Transform target)
    {
        if (target == null) return false;
        var impact = target.GetComponentInParent<BossSoldierRagdollImpact>();
        return impact != null && impact.HasImpacted;
    }

    private bool PrepareImpactRig()
    {
        if (impactRigPrepared) return true;
        var skins = GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(m => m != null && m.sharedMesh != null && m.gameObject.activeInHierarchy).ToArray();
        originalSkins = skins;
        var skinBones = new HashSet<Transform>(skins.SelectMany(m => m.bones).Where(b => b != null));
        Animator rig = GetComponentsInChildren<Animator>(true)
            .Where(a => a.avatar != null && a.avatar.isValid && a.isHuman)
            .OrderByDescending(a => humanoidBones.Count(id => skinBones.Contains(a.GetBoneTransform(id))))
            .FirstOrDefault();
        preparedHips = rig != null ? rig.GetBoneTransform(HumanBodyBones.Hips) : null;
        Debug.Log("[Boss Impact] Setup | active skinned renderers=" + skins.Length +
            " | mesh bones=" + skinBones.Count + " | humanoid animator=" + (rig != null ? rig.name : "NONE"), this);
        visuals.AddRange(GetComponentsInChildren<RagdollDismembermentVisual>(true).Where(v => v != null && v.gameObject.activeInHierarchy));
        if (visuals.Count == 0)
        {
            // No authored component: generate a mesh-cut setup for the Humanoid rig.
            visuals.Add(gameObject.AddComponent<RagdollDismembermentVisual>());
            Debug.Log("[Boss Impact] Added a runtime dismemberment component.", this);
        }
        if (visuals.Count > 1)
        {
            var primary = visuals.OrderByDescending(v => v.Fragments == null ? 0 : v.Fragments.Count).First();
            Debug.LogWarning("[Boss Impact] Multiple visual components found; using " + primary.name + " to avoid cutting the same renderer twice.", this);
            visuals.Clear();
            visuals.Add(primary);
        }
        if (visuals.Count == 0)
            Debug.LogWarning("[Boss Impact] No active RagdollDismembermentVisual or valid Humanoid rig on " + name + ". Configure fragments manually for generic rigs.", this);
        foreach (var visual in visuals)
        {
            visual.enabled = true;

        }
        var boneSet = new HashSet<Transform>();
        if (rig != null)
            foreach (var id in humanoidBones)
            {
                Transform bone = rig.GetBoneTransform(id);
                if (bone != null && bone != transform) boneSet.Add(bone);
            }
        foreach (var visual in visuals)
        {
            if (visual.Fragments == null || visual.Fragments.Count == 0) continue;
            foreach (var fragment in visual.Fragments.Skip(1))
                if (fragment != null && fragment.bone != null && fragment.bone != transform && fragment.bone.IsChildOf(transform))
                    boneSet.Add(fragment.bone);
        }
        // Reuse an authored ragdoll when present, including non-humanoid rigs.
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb.transform == transform) continue;
            foreach (var joint in rb.GetComponents<Joint>())
            {
                boneSet.Add(rb.transform);
                if (joint.connectedBody != null && joint.connectedBody.transform != transform &&
                    joint.connectedBody.transform.IsChildOf(transform)) boneSet.Add(joint.connectedBody.transform);
            }
        }
        // Fill partial setups too. Use the renderer's actual skeleton, not merely
        // a bone name or a different animation rig with an identical hierarchy.
        foreach (var visual in visuals)
        {
            ConfigureImpactFragments(visual, rig, boneSet, skinBones);
            foreach (var fragment in visual.Fragments.Skip(1))
                if (fragment != null && fragment.bone != null && fragment.bone.IsChildOf(transform))
                    boneSet.Add(fragment.bone);
        }
        if (boneSet.Count == 0)
        {
            Debug.LogWarning("[Boss Impact] No humanoid bones, configured fragment bones or authored ragdoll joints found on " + name, this);
            return false;
        }
        preparedBoneSet = boneSet;
        impactRigPrepared = true;
        return true;
    }

    // Called at attack entry. Only private mesh copies are cut; the live mesh,
    // bone transforms, AI and physics stay untouched until the configured impact frame.
    public void PrepareForAttack()
    {
        if (HasImpacted || preparationAttempted) return;
        preparationAttempted = true;
        if (!PrepareImpactRig()) return;
        // Never start a live cut during wind-up if staging could not initialize.
        if (visuals.Count == 0 || visuals.Any(v => v == null || !v.PrepareStagedImpact(transform))) return;
        int count = QueueFragments();
        queuedEarly = count > 0;
        Debug.Log("[Boss Impact] Wind-up preparation | queued=" + count + " | frame=" + Time.frameCount, this);
        foreach (var visual in visuals)
            if (visual != null) visual.StartQueuedOperationsNow();
    }

    public bool Execute(Vector3 source, float detachImpulse, int deadLayer, Transform bossRoot = null,
        float upwardImpulse = 2.2f, float spin = 5f)
    {
        if (HasImpacted) return false;
        if (!preparationAttempted) PrepareForAttack();
        impactOrigin = source;
        separationVelocity = Mathf.Max(0f, detachImpulse);
        upwardVelocity = Mathf.Max(0f, upwardImpulse);
        spinVelocity = Mathf.Max(0f, spin);
        launchCenter = transform.position;
        attackingBoss = bossRoot;
        corpseLayer = Mathf.Clamp(deadLayer, 0, 31);
        // Migrate the previous default, which was Ignore Raycast (layer 2).
        // Custom corpse layers remain configurable in the boss inspector.
        if (corpseLayer == 2)
        {
            corpseLayer = 0;
            Debug.Log("[Boss Impact] Legacy corpse layer 2 migrated to Default (0) for floor collision.", this);
        }
        if (!PrepareImpactRig()) return false;
        foreach (var visual in visuals)
            if (visual != null) visual.CompleteStagedBatchNow();
        bool hasPreparedCuts = ImpactBatchReady;
        RefreshProtectedActors(true);
        SelectFloorCompatibleCorpseLayer();
        var boneSet = preparedBoneSet;
        HasImpacted = true; // Before any disable callbacks or target scans.
        // Death is owned by this custom ragdoll; don't invoke native death-animation code.
        foreach (var fighter in GetComponentsInChildren<FighterCore>(true))
        {
            fighter.IsDead = true;
            if (fighter.Damagable != null) fighter.Damagable.CanTakeHit = false;
        }
        StopCharacterDrivers();

        // Construct all bodies first, then connect them to their nearest ragdoll parent.
        foreach (var bone in boneSet)
        {
            var rb = bone.GetComponent<Rigidbody>();
            bool added = rb == null;
            if (added) rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            if (added) rb.mass = 1f;
            bodies[bone] = rb;
            EnsureBoneCollider(bone, boneSet);
            foreach (var col in bone.GetComponents<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger || col is CharacterController || disabledCharacterColliders.Contains(col)) continue;
                ragdollColliders.Add(col);
            }
        }
        // Avoid baking the original full skin and then baking the cut pieces
        // again in the same impact. The ready batch fits each piece on commit.
        if (!hasPreparedCuts) FitOriginalSkinColliders();
        // Include any authored solid child colliders attached to a ragdoll body.
        foreach (var col in GetComponentsInChildren<Collider>(true))
            if (col != null && col.enabled && !col.isTrigger && !(col is CharacterController) &&
                !disabledCharacterColliders.Contains(col) && col.attachedRigidbody != null && bodies.ContainsValue(col.attachedRigidbody))
                ragdollColliders.Add(col);
        // Apply ignores while every new body is still kinematic, before physics can
        // push the corpse and boss apart at their overlapping attack positions.
        Physics.SyncTransforms();
        int ignoredBossPairs = RefreshBossCollisionIgnores();
        Debug.Log("[Boss Impact] Boss/corpse collision pairs ignored=" + ignoredBossPairs +
            " | boss=" + (attackingBoss != null ? attackingBoss.name : "NOT ASSIGNED"), this);
        foreach (var pair in bodies)
        {
            var bone = pair.Key;
            var rb = pair.Value;
            // Remove authored joints before the asset changes the hierarchy.
            // Rebuild only the connections that survive the atomic cut.
            foreach (var existing in bone.GetComponents<Joint>()) Destroy(existing);
            rb.useGravity = true;
            rb.detectCollisions = true;
            rb.constraints = RigidbodyConstraints.None;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            // Fitted solid colliders and CCD do most of the work; avoid the old
            // expensive 20/10 solver settings on every fragment.
            rb.mass = Mathf.Clamp(rb.mass, 0.5f, 3f);
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 5;
            rb.maxAngularVelocity = Mathf.Max(1f, maximumRagdollAngularSpeed);
            // Prevent fast moving small ragdoll pieces tunneling through thin floors.
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
        Physics.SyncTransforms();
        RefreshBossCollisionIgnores();
        ReportFloorCollisionSetup();
        Debug.Log("[Boss Impact] Phase 1: ragdoll activated | soldier=" + name + " | bodies=" + bodies.Count + " | frame=" + Time.frameCount, this);
        // All rigidbodies are prepared and kinematic. The boss-only asset
        // path is synchronous: complete any remaining work right here, never
        // abandon cuts merely because the old asynchronous deadline elapsed.
        holdingForCuts = true;
        foreach (var visual in visuals)
            if (visual != null) visual.CompleteStagedBatchNow();
        if (ImpactBatchReady)
        {
            foreach (var visual in visuals) visual.CommitStagedImpact();
            Debug.Log("[Boss Impact] ATOMIC RELEASE | detached=" + detachedBodies.Count + " | pending=" + pending.Count + " | Unity frame=" + Time.frameCount, this);
        }
        else
        {
            // A synchronous batch can only fail here if it has no usable
            // cuts (missing mesh, invalid setup, or an actual cut error).
            // Never substitute a timing-based cancellation.
            foreach (var visual in visuals) if (visual != null) visual.AbandonStagedImpact();
            pending.Clear();
            Debug.LogError("[Boss Impact] No committable asset cuts. Check preceding mesh/cut errors; this is not a preparation timeout.", this);
        }
        // Refitting can move collider centers. Finish it while all bodies are
        // still kinematic, not a frame later while the joints are solving.
        if (detachedBodies.Count > 0) RefitRemainingBody();
        else if (hasPreparedCuts) FitOriginalSkinColliders();
        RebuildFinalPhysicsHierarchy();
        IgnoreAllCorpsePairs();
        ReleaseCutHold();
        return true;
    }

    private void RebuildFinalPhysicsHierarchy()
    {
        // Capture the actual post-cut hierarchy before changing it. Each joint
        // is anchored at the current world pose, never to a detached fragment.
        var links = new Dictionary<Rigidbody, Rigidbody>();
        foreach (var pair in bodies)
        {
            if (pair.Key == null || pair.Value == null) continue;
            var rb = pair.Value;
            physicsGroups[rb] = FloorGroup(rb);
            links[rb] = detachedBodies.Contains(rb) ? null : NearestBody(pair.Key.parent);
        }
        physicsGroupsCaptured = true;
        if (flattenPhysicalBoneHierarchy)
        {
            // PhysX controls each rigidbody in world space. Nested dynamic
            // transforms can also inherit motion from their parents; remove
            // that competing transform path without changing bone identities.
            var ordered = links.Keys.OrderBy(rb => Depth(rb.transform)).ToArray();
            foreach (var rb in ordered) rb.transform.SetParent(transform, true);
        }
        Physics.SyncTransforms();
        foreach (var pair in links)
        {
            var rb = pair.Key;
            var parent = pair.Value;
            if (parent == null || physicsGroups[rb] != physicsGroups[parent]) continue;
            var joint = rb.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = parent.transform.InverseTransformPoint(rb.transform.position);
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.enableProjection = useJointProjection;
            joint.projectionDistance = Mathf.Max(0.001f, jointProjectionDistance);
            joint.projectionAngle = Mathf.Clamp(jointProjectionAngle, 1f, 180f);
            joint.lowTwistLimit = new SoftJointLimit { limit = -20f };
            joint.highTwistLimit = new SoftJointLimit { limit = 20f };
            joint.swing1Limit = new SoftJointLimit { limit = 30f };
            joint.swing2Limit = new SoftJointLimit { limit = 30f };
        }
        Debug.Log("[Boss Impact] Stable ragdoll hierarchy rebuilt | projection=" + useJointProjection +
            " | flattened=" + flattenPhysicalBoneHierarchy, this);
    }

    private void ReleaseCutHold()
    {
        if (!holdingForCuts) return;
        holdingForCuts = false;
        foreach (var rb in bodies.Values)
        {
            if (rb == null) continue;
            rb.constraints = RigidbodyConstraints.None;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = Mathf.Max(rb.linearDamping, 0.12f);
            rb.angularDamping = Mathf.Max(rb.angularDamping, 0.8f);
            rb.maxDepenetrationVelocity = Mathf.Min(rb.maxDepenetrationVelocity, 3f);
            rb.interpolation = flattenPhysicalBoneHierarchy ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }
        // Apply coherent motion to the entire surviving connected piece, not
        // a huge impulse on one bone that stretches all its joint neighbours.
        foreach (var rb in bodies.Values)
        {
            if (rb == null) continue;
            var group = FloorGroup(rb);
            if (group == null || !pendingSeparation.TryGetValue(group, out var impulse)) continue;
            var angular = pendingSpin.TryGetValue(group, out var spin) ? spin : Vector3.zero;
            angular = Vector3.ClampMagnitude(angular, maximumRagdollAngularSpeed);
            rb.linearVelocity = impulse + Vector3.ClampMagnitude(Vector3.Cross(angular, rb.worldCenterOfMass - group.worldCenterOfMass), 2f);
            rb.angularVelocity = angular;
        }
        pendingSeparation.Clear();
        pendingSpin.Clear();
        StartBodyKnockdown();
        Physics.SyncTransforms();
        RefreshBossCollisionIgnores();
        CaptureFloorHistory();
        Debug.Log("[Boss Impact] Released ragdoll | remaining cuts=" + pending.Count + " | frame=" + Time.frameCount, this);
    }

    // A gravity-only torso can balance on its fitted colliders. Give the
    // connected body coherent translation and rotation about the pelvis.
    // Detached pieces keep their independent outward/upward launch velocities.
    private void StartBodyKnockdown()
    {
        if (!knockDownRemainingBody) return;
        Rigidbody core = null;
        if (preparedHips != null) bodies.TryGetValue(preparedHips, out core);
        if (core == null || FloorGroup(core) != null)
            core = bodies.Values.Where(rb => rb != null && FloorGroup(rb) == null)
                .OrderBy(rb => Depth(rb.transform)).FirstOrDefault();
        if (core == null) return;
        collapseReference = core;
        // Save the starting world-up in the bone's local axes (rig bone
        // orientation is arbitrary, so transform.up alone is not reliable).
        collapseLocalUp = core.transform.InverseTransformDirection(Vector3.up);
        collapseDirection = Vector3.ProjectOnPlane(launchCenter - impactOrigin, Vector3.up);
        if (collapseDirection.sqrMagnitude < 0.0001f)
            collapseDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (collapseDirection.sqrMagnitude < 0.0001f) collapseDirection = Vector3.forward;
        collapseDirection.Normalize();
        ApplyBodyKnockdown(1f);
        // One bounded check, not a continuous force or teleport. Handles a
        // torso that briefly balances on contact geometry after the first hit.
        collapseRetryAt = Time.time + 0.4f;
        Debug.Log("[Boss Impact] Remaining body knockdown | core=" + core.name + " | frame=" + Time.frameCount, this);
    }

    private void ApplyBodyKnockdown(float strength)
    {
        if (collapseReference == null || FloorGroup(collapseReference) != null) return;
        Vector3 pivot = collapseReference.worldCenterOfMass;
        Vector3 omega = Vector3.Cross(Vector3.up, collapseDirection).normalized *
            (bodyTipAngularVelocity * strength);
        Vector3 translation = (collapseDirection * bodyKnockbackVelocity -
            Vector3.up * bodyDownwardVelocity) * strength;
        foreach (var rb in bodies.Values)
        {
            if (rb == null || FloorGroup(rb) != null) continue;
            rb.constraints = RigidbodyConstraints.None;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = true;
            rb.maxAngularVelocity = Mathf.Max(1f, maximumRagdollAngularSpeed);
            rb.WakeUp();
            // Coherent rigid-body motion limits stretching of connected joints.
            Vector3 tangential = Vector3.Cross(omega, rb.worldCenterOfMass - pivot);
            // Assign the coherent velocity field once rather than adding torque
            // to every connected body on top of its existing angular velocity.
            rb.linearVelocity = translation + Vector3.ClampMagnitude(tangential, 2f);
            rb.angularVelocity = Vector3.ClampMagnitude(omega, maximumRagdollAngularSpeed);
        }
    }

    private void CheckBodyKnockdown()
    {
        if (collapseRetryAt < 0f || Time.time < collapseRetryAt) return;
        collapseRetryAt = -1f;
        if (collapseReference == null || FloorGroup(collapseReference) != null) return;
        Vector3 currentUp = collapseReference.transform.TransformDirection(collapseLocalUp);
        if (Vector3.Dot(currentUp.normalized, Vector3.up) > 0.72f)
        {
            ApplyBodyKnockdown(0.65f);
            Debug.Log("[Boss Impact] Upright torso: applied one knockdown recovery impulse", this);
        }
    }

    private bool IsEnvironmentFloor(RaycastHit hit)
    {
        if (hit.collider == null || hit.normal.y < 0.45f || IsCorpseCollider(hit.collider)) return false;
        if (Array.IndexOf(protectedActorColliders, hit.collider) >= 0) return false;
        Transform t = hit.collider.transform;
        if (t == transform || t.IsChildOf(transform)) return false;
        if (attackingBoss != null && (t == attackingBoss || t.IsChildOf(attackingBoss))) return false;
        // Do not treat another live character's body or a loose rigidbody as a floor.
        if (hit.collider.GetComponentInParent<FighterCore>() != null) return false;
        return hit.rigidbody == null || hit.rigidbody.isKinematic;
    }

    private void SelectFloorCompatibleCorpseLayer()
    {
        var hits = Physics.RaycastAll(transform.position + Vector3.up * 0.5f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits.OrderBy(h => h.distance))
        {
            if (!IsEnvironmentFloor(hit)) continue;
            int floor = hit.collider.gameObject.layer;
            if (!Physics.GetIgnoreLayerCollision(corpseLayer, floor)) return;
            // Prefer Default over putting corpses on a gameplay/target layer.
            if (!Physics.GetIgnoreLayerCollision(0, floor))
            {
                corpseLayer = 0;
                Debug.LogWarning("[Boss Impact] Switched corpse layer to Default to preserve floor collision.", this);
            }
            else Debug.LogError("[Boss Impact] Neither configured corpse layer nor Default collides with floor layer " +
                LayerMask.LayerToName(floor) + ". Fix the Physics Layer Collision Matrix.", this);
            return;
        }
    }

    private bool TryBodyBounds(Rigidbody body, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        if (body == null) return false;
        if (fittedColliders.TryGetValue(body, out var fitted) && fitted != null && fitted.enabled &&
            fitted.gameObject.activeInHierarchy)
        { bounds = fitted.bounds; return true; }
        foreach (var col in ragdollColliders)
        {
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy || col.attachedRigidbody != body) continue;
            if (!found) { bounds = col.bounds; found = true; }
            else bounds.Encapsulate(col.bounds);
        }
        return found;
    }

    private void CaptureFloorHistory()
    {
        lastColliderBottom.Clear();
        foreach (var body in bodies.Values)
            if (body != null && TryBodyBounds(body, out var b)) lastColliderBottom[body] = b.min.y;
    }

    // Sweep the distance a collider's bottom travelled since the last physics
    // step. Only correct crossings of an actual upward-facing solid surface.
    // This is not a fixed world-Y clamp: stairs and ledges remain usable.
    private void PreventFloorTunnelling()
    {
        float clusterLift = 0f;
        floorCorrections.Clear();
        foreach (var body in bodies.Values)
        {
            if (body == null || body.isKinematic || body.IsSleeping() || !TryBodyBounds(body, out var b)) continue;
            float bottom = b.min.y;
            if (lastColliderBottom.TryGetValue(body, out float previous) && previous - bottom > 0.07f)
            {
                Vector3 start = new Vector3(b.center.x, previous + 0.025f, b.center.z);
                int count = Physics.RaycastNonAlloc(start, Vector3.down, floorSweepHits,
                    previous - bottom + 0.035f, ~0, QueryTriggerInteraction.Ignore);
                float lift = 0f;
                for (int i = 0; i < count; i++)
                {
                    var hit = floorSweepHits[i];
                    if (!IsEnvironmentFloor(hit) || Physics.GetIgnoreLayerCollision(body.gameObject.layer, hit.collider.gameObject.layer)) continue;
                    float depth = hit.point.y - bottom;
                    if (depth > 0.12f) lift = Mathf.Max(lift, depth + 0.005f);
                }
                if (lift > 0f)
                {
                    RecordFloorLift(body, lift, ref clusterLift);
                }
            }
            lastColliderBottom[body] = bottom;
        }
        ApplyFloorLifts(clusterLift);
    }

    // A cut root may still have physical child bones (e.g. fingers). Move those
    // together with that fragment, not with the remaining torso.
    private Rigidbody FloorGroup(Rigidbody body)
    {
        if (body == null) return null;
        if (physicsGroupsCaptured) return physicsGroups.TryGetValue(body, out var group) ? group : null;
        for (Transform t = body.transform; t != null; t = t.parent)
            if (bodies.TryGetValue(t, out var candidate) && detachedBodies.Contains(candidate)) return candidate;
        return null;
    }

    private void RecordFloorLift(Rigidbody body, float lift, ref float clusterLift)
    {
        if (lift <= 0f) return;
        var group = FloorGroup(body);
        if (group == null) clusterLift = Mathf.Max(clusterLift, lift);
        else if (!floorCorrections.TryGetValue(group, out float previous) || lift > previous)
            floorCorrections[group] = lift;
    }

    private void ApplyFloorLifts(float clusterLift)
    {
        if (clusterLift <= 0f && floorCorrections.Count == 0) return;
        // Snapshot every body before moving a parent. Restore each child's
        // desired world position so nested transforms cannot double-apply lift.
        var ordered = bodies.Values.Where(rb => rb != null && !rb.isKinematic)
            .Distinct().OrderBy(rb => Depth(rb.transform)).ToArray();
        var positions = ordered.ToDictionary(rb => rb, rb => rb.position);
        foreach (var body in ordered)
        {
            var group = FloorGroup(body);
            float lift = group == null ? clusterLift :
                (floorCorrections.TryGetValue(group, out var amount) ? amount : 0f);
            Vector3 destination = positions[body] + Vector3.up * lift;
            // Setting both world transform and rigidbody pose makes the visual
            // correction immediate, including on rigs with nested bodies.
            if (lift > 0f || (body.transform.position - destination).sqrMagnitude > 0.00000001f)
            {
                if (!flattenPhysicalBoneHierarchy) body.transform.position = destination;
                body.position = destination;
            }
            if (lift <= 0f) continue;
            var velocity = body.linearVelocity;
            if (velocity.y < 0f) { velocity.y = 0f; body.linearVelocity = velocity; }
            body.WakeUp();
        }
        Physics.SyncTransforms();
        CaptureFloorHistory();
    }

    private void RefitRemainingBody()
    {
        var remaining = new HashSet<SkinnedMeshRenderer>();
        foreach (var visual in visuals)
            if (visual != null && visual.Fragments != null && visual.Fragments.Count > 0 &&
                visual.Fragments[0] != null && visual.Fragments[0].SkinnedMeshes != null)
                foreach (var skin in visual.Fragments[0].SkinnedMeshes) if (skin != null && skin.enabled) remaining.Add(skin);
        if (remaining.Count > 0)
        {
            FitSkinColliders(remaining);
            Physics.SyncTransforms();
            RefreshBossCollisionIgnores();
            CaptureFloorHistory();
        }
    }

    // Skin positions are in the renderer's local space. Fit each solid box to
    // vertices assigned to its nearest physical bone, rather than guessing a
    // tiny capsule from the next bone. Use only vertices referenced by triangles.
    private void FitOriginalSkinColliders()
    {
        if (originalSkins != null) FitSkinColliders(originalSkins);
    }

    private void FitSkinColliders(IEnumerable<SkinnedMeshRenderer> skins)
    {
        var bounds = new Dictionary<Rigidbody, Bounds>();
        foreach (var skin in skins)
        {
            if (skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable || !skin.enabled) continue;
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                var vertices = baked.vertices;
                var weights = skin.sharedMesh.boneWeights;
                var bones = skin.bones;
                if (vertices.Length != weights.Length) continue;
                var used = new HashSet<int>(baked.triangles);
                foreach (int i in used)
                {
                    if (i < 0 || i >= vertices.Length) continue;
                    var w = weights[i];
                    int index = w.boneIndex0;
                    float weight = w.weight0;
                    if (w.weight1 > weight) { index = w.boneIndex1; weight = w.weight1; }
                    if (w.weight2 > weight) { index = w.boneIndex2; weight = w.weight2; }
                    if (w.weight3 > weight) { index = w.boneIndex3; weight = w.weight3; }
                    if (index < 0 || index >= bones.Length || bones[index] == null) continue;
                    var body = NearestBody(bones[index]);
                    if (body == null) continue;
                    Vector3 point = body.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
                    Encapsulate(bounds, body, point);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Boss Impact] Collider fitting skipped: " + skin.name + " | " + e.Message, this); }
            finally { Destroy(baked); }
        }
        foreach (var pair in bounds) ApplyFittedCollider(pair.Key, pair.Value);
        Debug.Log("[Boss Impact] Mesh-fitted bone colliders=" + fittedColliders.Count, this);
    }

    private static void Encapsulate(Dictionary<Rigidbody, Bounds> bounds, Rigidbody body, Vector3 point)
    {
        if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
            float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z)) return;
        if (bounds.TryGetValue(body, out var b)) b.Encapsulate(point);
        else b = new Bounds(point, Vector3.zero);
        bounds[body] = b;
    }

    private void ApplyFittedCollider(Rigidbody body, Bounds b)
    {
        if (body == null) return;
        Vector3 scale = body.transform.lossyScale;
        Vector3 padding = new Vector3(0.035f / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
            0.035f / Mathf.Max(0.001f, Mathf.Abs(scale.y)), 0.035f / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        if (!fittedColliders.TryGetValue(body, out var box) || box == null)
        {
            box = body.gameObject.AddComponent<BoxCollider>();
            fittedColliders[body] = box;
        }
        box.center = b.center;
        box.size = b.size + padding * 2f;
        box.isTrigger = false;
        box.contactOffset = Mathf.Max(box.contactOffset, 0.015f);
        box.enabled = true;
        ragdollColliders.Add(box);
        // Keep one fitted shape per bone rather than overlapping primitive
        // colliders, which can push adjacent pieces through the floor.
        foreach (var col in body.GetComponents<Collider>())
            if (col != box && !(col is CharacterController) && !col.isTrigger)
            {
                col.enabled = false;
                ragdollColliders.Remove(col);
            }
        // Fitted boxes can overlap their neighbouring pieces at impact. Ignore
        // internal corpse pairs, but preserve every environment collision.
        foreach (var other in ragdollColliders)
            if (other != null && other != box && other.enabled && other.attachedRigidbody != body &&
                other.gameObject.activeInHierarchy && box.gameObject.activeInHierarchy)
                Physics.IgnoreCollision(box, other, true);
    }

    private void FitDetachedFragment(RagdollDismembermentVisual visual, string fragmentName, Rigidbody body)
    {
        var fragment = visual.Fragments.Find(f => f != null && f.Name == fragmentName);
        if (fragment == null || fragment.SkinnedMeshes == null || body == null) return;
        var bounds = new Dictionary<Rigidbody, Bounds>();
        foreach (var skin in fragment.SkinnedMeshes)
        {
            if (skin == null || skin.sharedMesh == null) continue;
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                var vertices = baked.vertices;
                foreach (int i in new HashSet<int>(baked.triangles))
                    if (i >= 0 && i < vertices.Length)
                        Encapsulate(bounds, body, body.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[i])));
            }
            catch (Exception e) { Debug.LogWarning("[Boss Impact] Fragment collider fit: " + fragmentName + " | " + e.Message, this); }
            finally { Destroy(baked); }
        }
        if (bounds.TryGetValue(body, out var b)) ApplyFittedCollider(body, b);
    }

    private void ConfigureImpactFragments(RagdollDismembermentVisual visual, Animator rig,
        HashSet<Transform> ragdollBones, HashSet<Transform> skinBones)
    {
        var ids = new[] {
            HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };
        var original = visual.Fragments ?? new List<BodyFragment>();
        var root = original.Count > 0 && original[0] != null ? original[0] : new BodyFragment { color = Color.black };
        // The soldier root encompasses both renderer siblings and the full skeleton.
        // Never make a cut on the root or the object running the queue itself.
        root.Name = "ROOT";
        root.bone = transform;
        bool MatchesSkin(Transform bone) => bone != null && bone != transform &&
            bone.IsChildOf(transform) && bone != visual.transform && !visual.transform.IsChildOf(bone) &&
            skinBones.Contains(bone);
        var result = new List<BodyFragment> { root };
        var seen = new HashSet<Transform>();
        var names = new HashSet<string> { "ROOT" };
        void Add(Transform bone, string label, BodyFragment existing = null)
        {
            if (!MatchesSkin(bone) || !seen.Add(bone)) return;
            if (string.IsNullOrEmpty(label)) label = bone.name;
            string unique = label;
            for (int i = 2; !names.Add(unique); i++) unique = label + "_" + i;
            var f = existing ?? new BodyFragment { color = Color.white, Size = Vector3.zero };
            f.Name = unique;
            f.bone = bone;
            result.Add(f);
        }
        foreach (var f in original.Skip(1))
        {
            if (f == null) continue;
            if (!MatchesSkin(f.bone))
                Debug.LogWarning("[Boss Impact] Ignoring fragment not on the render skeleton or owning queue: " + f.Name, this);
            else Add(f.bone, f.Name, f);
        }
        int authored = result.Count - 1;
        int mappedHumanoid = 0;
        if (rig != null)
            foreach (var id in ids)
            {
                var bone = rig.GetBoneTransform(id);
                if (!MatchesSkin(bone)) continue;
                mappedHumanoid++;
                Add(bone, id.ToString());
            }
        // For generic or mismatched humanoid rigs, reuse the actual skin bones
        // that carry ragdoll bodies, leaving the central torso/root intact.
        if (mappedHumanoid == 0)
            foreach (var bone in ragdollBones.OrderBy(Depth))
            {
                if (!MatchesSkin(bone)) continue;
                bool hasBodyAncestor = false;
                for (var p = bone.parent; p != null && p != transform; p = p.parent)
                    if (ragdollBones.Contains(p)) { hasBodyAncestor = true; break; }
                if (!hasBodyAncestor) continue;
                string n = bone.name.ToLowerInvariant();
                if (n.Contains("hips") || n.Contains("pelvis") || n.Contains("spine") || n.Contains("chest") || n.Contains("root")) continue;
                Add(bone, bone.name);
            }
        visual.Fragments = result;
        Debug.Log("[Boss Impact] Fragment setup | component=" + visual.name + " | authored=" + authored +
            " | humanoid matches=" + mappedHumanoid + " | total=" + (result.Count - 1) +
            " | names=" + string.Join(", ", result.Skip(1).Select(f => f.Name).ToArray()), this);
        if (result.Count < 2)
            Debug.LogError("[Boss Impact] No cuttable bones share the renderer skeleton. Check the soldier's SkinnedMeshRenderer bone references and ragdoll rig.", this);
    }

    private void DisableCharacterColliders()
    {
        // Capture the authored gameplay capsule even when FighterCore lives on
        // a child object (and even when the capsule is located on a rig bone).
        foreach (var fighter in GetComponentsInChildren<FighterCore>(true))
        {
            if (fighter.capsuleCollider != null)
                disabledCharacterColliders.Add(fighter.capsuleCollider);
        }
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (col == null) continue;
            // Only colliders associated with a prepared ragdoll bone may stay
            // enabled. A separate locomotion capsule on a child of the root
            // is not a ragdoll collider.
            bool belongsToBone = false;
            for (var t = col.transform; t != null && t != transform; t = t.parent)
                if (preparedBoneSet.Contains(t)) { belongsToBone = true; break; }
            if (col is CharacterController || col.isTrigger || !belongsToBone)
                disabledCharacterColliders.Add(col);
        }
        foreach (var col in disabledCharacterColliders)
        {
            if (col == null) continue;
            col.enabled = false;
            ragdollColliders.Remove(col);
        }
        Debug.Log("[Boss Impact] Character colliders disabled=" + disabledCharacterColliders.Count +
            " | bone colliders retained for floor physics", this);
    }

    private void StopCharacterDrivers()
    {
        // Stop the hold/AI before disabling the Animator. Its OnDisable can restore
        // navigation and animation settings, so the final freeze happens after it.
        var driverNames = new HashSet<string> {
            "CombatAIController", "ShooterAIController", "ShooterFighter", "AIController",
            "StealthAIController", "AwarenessSystem", "LocomotionCore", "LocomotionController",
            "ActiveRagdollSystem", "AnimGraph", "ActionExecutor", "FighterCore", "RigBuilder",
            "ShooterController", "TargetHandler", "CombatTargetSensor", "CharacterController3D"
        };
        var scripts = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var script in scripts)
            if (script != null && script.GetType().Name == "CombatAIController")
            {
                script.StopAllCoroutines();
                script.enabled = false;
            }
        foreach (var script in scripts)
            if (script != null && script.enabled && driverNames.Contains(script.GetType().Name))
            {
                script.StopAllCoroutines();
                script.enabled = false;
            }
        foreach (var agent in GetComponentsInChildren<NavMeshAgent>(true))
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false;
        }
        foreach (var controller in GetComponentsInChildren<CharacterController>(true)) controller.enabled = false;
        foreach (var animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var obstacle in GetComponentsInChildren<NavMeshObstacle>(true)) obstacle.enabled = false;
        // Disable gameplay collision shapes before creating dynamic bodies.
        DisableCharacterColliders();
        var rootBody = GetComponent<Rigidbody>();
        if (rootBody != null)
        {
            rootBody.isKinematic = true;
            rootBody.detectCollisions = false;
        }
        // Detached bones should no longer be acquired as live enemies by layer scans.
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = corpseLayer;
    }

    private Rigidbody NearestBody(Transform t)
    {
        for (; t != null && t != transform; t = t.parent)
            if (bodies.TryGetValue(t, out var rb)) return rb;
        return null;
    }

    private void EnsureBoneCollider(Transform bone, HashSet<Transform> bones)
    {
        foreach (var meshCollider in bone.GetComponents<MeshCollider>())
            if (!meshCollider.convex) meshCollider.enabled = false;
        var solid = bone.GetComponents<Collider>().Where(c => !c.isTrigger && !(c is CharacterController) &&
            !disabledCharacterColliders.Contains(c) &&
            (!(c is MeshCollider mesh) || mesh.convex)).ToArray();
        if (solid.Length > 0)
        {
            foreach (var col in solid) col.enabled = true;
            return;
        }
        Transform child = null;
        float best = float.PositiveInfinity;
        foreach (var candidate in bones)
            if (candidate != bone && candidate.IsChildOf(bone))
            {
                float d = (candidate.position - bone.position).sqrMagnitude;
                if (d > 0.00001f && d < best) { best = d; child = candidate; }
            }
        Vector3 local = child != null ? bone.InverseTransformPoint(child.position) : Vector3.zero;
        float length = local.magnitude;
        float scale = Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(bone.lossyScale.x), Mathf.Max(Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z))));
        if (length < 0.001f)
        {
            var sphere = bone.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.055f / scale;
            return;
        }
        var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
        Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        capsule.direction = abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;
        capsule.center = local * 0.4f;
        capsule.radius = Mathf.Clamp(length * 0.18f, 0.025f / scale, 0.09f / scale);
        capsule.height = Mathf.Max(capsule.radius * 2f, length * 0.85f);
    }

    private static int Depth(Transform t)
    {
        int depth = 0;
        for (; t != null; t = t.parent) depth++;
        return depth;
    }

    private int QueueFragments()
    {
        int total = 0;
        // Most soldiers have one visual component. The asset owns its own asynchronous queue.
        foreach (var visual in visuals)
        {
            if (visual.Fragments == null || visual.Fragments.Count < 2 || visual.Fragments[0] == null) continue;
            var root = visual.Fragments[0].bone != null ? visual.Fragments[0].bone : visual.transform;
            if (visual.OnDismemberCompleted == null)
                visual.OnDismemberCompleted = new RagdollDismembermentVisual.DismemberOperationEvent();
            var valid = visual.Fragments.Skip(1).Where(f => f != null && f.bone != null && f.bone != root &&
                f.bone.IsChildOf(root) && !string.IsNullOrEmpty(f.Name) && f.Name != visual.Fragments[0].Name)
                .OrderByDescending(f => Depth(f.bone)).ToList();
            if (!visual.PrepareForImpact(transform))
            {
                Debug.LogWarning("[Boss Impact] Asset mesh preparation failed on " + visual.name, this);
                continue;
            }
            var captured = visual;
            visual.OnDismemberCompleted.AddListener(fragmentName => OnFragmentDetached(captured, fragmentName));
            if (visual.OnDismemberFailed == null) visual.OnDismemberFailed = new RagdollDismembermentVisual.DismemberOperationEvent();
            visual.OnDismemberFailed.AddListener(fragmentName =>
            {
                pending.Remove(captured.GetInstanceID() + ":" + fragmentName);
                Debug.LogWarning("[Boss Impact] Fragment did not separate: " + fragmentName, this);
            });
            var names = new HashSet<string>();
            foreach (var f in valid)
            {
                if (!names.Add(f.Name)) { Debug.LogWarning("[Boss Impact] Duplicate fragment name: " + f.Name, this); continue; }
                string key = visual.GetInstanceID() + ":" + f.Name;
                if (pending.ContainsKey(key)) continue;
                try
                {
                    if (visual.TryDismember(f.Name))
                    {
                        pending.Add(key, f.bone);
                        total++;
                    }
                    else Debug.LogWarning("[Boss Impact] Queue rejected: " + f.Name, this);
                }
                catch (Exception e) { Debug.LogError("[Boss Impact] Could not queue " + f.Name + ": " + e, this); }
            }
            if (valid.Count == 0) Debug.LogWarning("[Boss Impact] No valid child fragments under ROOT " + root.name, this);
        }
        return total;
    }

    private void LaunchFragment(Rigidbody body, Vector3 impulse, Vector3 angular)
    {
        if (body == null || body.isKinematic) return;
        body.WakeUp();
        // A late cut may already be falling. Don't let its downward momentum
        // cancel the intended lift from the attack.
        Vector3 velocity = body.linearVelocity;
        if (velocity.y < 0f) { velocity.y = 0f; body.linearVelocity = velocity; }
        if (impulse.sqrMagnitude > 0f) body.AddForce(impulse, ForceMode.VelocityChange);
        if (angular.sqrMagnitude > 0f) body.AddTorque(angular, ForceMode.VelocityChange);
    }

    private void OnFragmentDetached(RagdollDismembermentVisual visual, string fragmentName)
    {
        string key = visual.GetInstanceID() + ":" + fragmentName;
        if (!holdingForCuts || !pending.TryGetValue(key, out var bone)) return;
        pending.Remove(key);
        if (bone != null)
        {
            // Existing joints were removed before committing cuts. New joints
            // are constructed once from the final fragment hierarchy.
            foreach (var t in bone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = corpseLayer;
            // The asset reparents each fragment. Keep all detached collider pairs
            // registered independently of the original soldier hierarchy.
            foreach (var col in bone.GetComponentsInChildren<Collider>(true))
                if (col != null && col.enabled && !col.isTrigger && !(col is CharacterController) &&
                    !disabledCharacterColliders.Contains(col))
                    ragdollColliders.Add(col);
            if (bodies.TryGetValue(bone, out var rb) && rb != null)
            {
                FitDetachedFragment(visual, fragmentName, rb);
                detachedBodies.Add(rb);
                // Horizontal radial launch from the victim's center; blend a little
                // of the attack direction so the impact has a coherent outward push.
                Vector3 direction = Vector3.ProjectOnPlane(rb.worldCenterOfMass - launchCenter, Vector3.up);
                Vector3 attackDirection = Vector3.ProjectOnPlane(launchCenter - impactOrigin, Vector3.up);
                if (direction.sqrMagnitude < 0.0025f) direction = UnityEngine.Random.insideUnitSphere;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.right;
                direction = (direction.normalized + attackDirection.normalized * 0.25f).normalized;
                Vector3 impulse = direction * separationVelocity * UnityEngine.Random.Range(0.85f, 1.15f) +
                    Vector3.up * upwardVelocity * UnityEngine.Random.Range(0.8f, 1.2f);
                Vector3 angular = UnityEngine.Random.onUnitSphere * spinVelocity;
                if (holdingForCuts)
                {
                    pendingSeparation[rb] = impulse;
                    pendingSpin[rb] = angular;
                }
                else LaunchFragment(rb, impulse, angular);
            }
            // A prepared batch can finish many fragments on one frame. Sync and
            // refresh once when the batch releases, not once per prepared piece.
            if (!holdingForCuts)
            {
                Physics.SyncTransforms();
                if (bodies.TryGetValue(bone, out var detached) && TryBodyBounds(detached, out var newBounds))
                    lastColliderBottom[detached] = newBounds.min.y;
                RefreshBossCollisionIgnores();
            }
        }
        Debug.Log("[Boss Impact] Fragment detached: " + fragmentName + " | remaining=" + pending.Count, this);
    }
}
