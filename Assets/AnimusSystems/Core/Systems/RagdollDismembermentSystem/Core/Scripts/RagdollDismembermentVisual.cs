using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Events;
using Unity.Collections;
using UnityEngine.Rendering;

public class RagdollDismembermentVisual : MonoBehaviour
{
    [Header("Events")]
    public DismemberOperationEvent OnDismemberCompleted;
    public List<BodyFragment> Fragments;
    private bool isInitialized = false;
    private List<BodyFragment> operations = new List<BodyFragment>();
    private readonly HashSet<BodyFragment> queuedOrCompleted = new HashSet<BodyFragment>();
    private LODGroup sourceLODGroup;
    private readonly Dictionary<SkinnedMeshRenderer, int[]> rendererLods = new Dictionary<SkinnedMeshRenderer, int[]>();
    public bool DebugDismemberment = true;
    public int PendingOperations => operations.Count;
    public DismemberOperationEvent OnDismemberFailed = new DismemberOperationEvent();

    private void Reset()
    {
        Fragments = new List<BodyFragment>()
        {
            new BodyFragment()
            {
                color = Color.black,
                Name = "ROOT",
                bone = transform
            }
        };
    }

    // Start the asset's original async mesh-cutting workflow only when work
    // exists, rather than spinning an async Task.Yield loop from Awake.
    // Pre-cut private mesh copies during the boss wind-up. The intact soldier
    // remains visible and its skeleton is not detached until impact.
    private bool stagingImpact;
    private bool stagingCommitted;
    private bool stagingAbandoned;
    public bool StagedBatchFinished => stagingImpact && !stagingAbandoned && isInitialized && !processingOperations && operations.Count == 0;
    public int StagedReadyCount => stagedCompleted.Count;

    // Never expose a partially prepared cut. A timed-out attack can keep its
    // original intact skin while the in-flight asset task finishes privately.
    public void AbandonStagedImpact()
    {
        if (!stagingImpact || stagingCommitted) return;
        stagingAbandoned = true;
        if (!processingOperations) DiscardStagedMeshes();
    }

    private void DiscardStagedMeshes()
    {
        operations.Clear();
        var copies = new HashSet<SkinnedMeshRenderer>(stagedSources.Values);
        if (Fragments != null)
            foreach (var f in Fragments)
                if (f != null && f.SkinnedMeshes != null)
                    foreach (var mesh in f.SkinnedMeshes) if (mesh != null) copies.Add(mesh);
        foreach (var copy in copies)
            if (copy != null && !stagedSources.ContainsKey(copy))
            {
                copy.enabled = false;
                if (copy.sharedMesh != null && ownedStagedMeshes.Remove(copy.sharedMesh)) Destroy(copy.sharedMesh);
                Destroy(copy);
            }
        stagedCompleted.Clear();
    }
    private readonly List<BodyFragment> stagedCompleted = new List<BodyFragment>();
    private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> stagedSources = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
    private readonly Dictionary<SkinnedMeshRenderer, bool> stagedVisibility = new Dictionary<SkinnedMeshRenderer, bool>();
    private readonly HashSet<Mesh> ownedStagedMeshes = new HashSet<Mesh>();

    private void OnDestroy()
    {
        foreach (var mesh in ownedStagedMeshes) if (mesh != null) Destroy(mesh);
        ownedStagedMeshes.Clear();
    }

    public bool PrepareStagedImpact(Transform characterRoot)
    {
        if (isInitialized) return stagingImpact;
        stagingImpact = true;
        if (PrepareForImpact(characterRoot)) return true;
        stagingImpact = false;
        return false;
    }

    public void StartQueuedOperationsNow()
    {
        if (processingOperations || operations.Count == 0 || stagingAbandoned) return;
        if (stagingImpact) ProcessStagedBatchNow();
        else DismemberManager();
    }

    public void CommitStagedImpact()
    {
        if (!stagingImpact || stagingCommitted || stagingAbandoned || !StagedBatchFinished || stagedCompleted.Count == 0) return;
        stagingCommitted = true;
        // Keep the source LOD group on the remaining torso meshes, not the
        // original intact renderers. Detached pieces get their own LOD groups.
        if (sourceLODGroup != null)
        {
            var lods = sourceLODGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
                lods[i].renderers = lods[i].renderers.Select(r =>
                {
                    var sm = r as SkinnedMeshRenderer;
                    if (sm != null && stagedSources.TryGetValue(sm, out var replacement))
                        return Fragments[0].SkinnedMeshes.Contains(replacement) ? (Renderer)replacement : null;
                    return r;
                }).Where(r => r != null).ToArray();
            sourceLODGroup.SetLODs(lods);
        }
        foreach (var pair in stagedSources)
        {
            if (pair.Key != null) pair.Key.enabled = false;
            if (pair.Value != null) pair.Value.enabled = Fragments[0].SkinnedMeshes.Contains(pair.Value) &&
                stagedVisibility.TryGetValue(pair.Value, out var visible) && visible;
        }
        int ready = stagedCompleted.Count;
        foreach (var f in stagedCompleted.ToArray())
        {
            try { FinishDetachment(f); NotifyResult(f, true); }
            catch (System.Exception e) { Debug.LogException(e, this); NotifyResult(f, false); }
        }
        stagedCompleted.Clear();
        Debug.Log("[Dismember] IMPACT COMMIT | prepared=" + ready + " | still processing=" + operations.Count + " | frame=" + Time.frameCount, this);
        StartQueuedOperationsNow();
    }

    private bool processingOperations;
    private float operationStartedAt;
    private bool reportedSlowOperation;

    private void Update()
    {
        if (!processingOperations && operations.Count > 0)
            StartQueuedOperationsNow();
        else if (processingOperations && operations.Count > 0 && !reportedSlowOperation &&
                 Time.realtimeSinceStartup - operationStartedAt > 5f)
        {
            reportedSlowOperation = true;
            Debug.LogWarning("[Dismember] Still awaiting asset cut: " + operations[0].Name +
                " | pending=" + operations.Count + ". Check the SimpleSplit implementation or other Console exceptions.", this);
        }
    }

    // Called with the soldier root by the boss; ordinary Dismember callers can still use this component.
    public bool PrepareForImpact(Transform characterRoot = null)
    {
        if (isInitialized) return true;
        if (Fragments == null || Fragments.Count < 2 || Fragments[0] == null)
        {
            Debug.LogWarning("[Dismember] Configure ROOT and at least one body fragment on " + name, this);
            return false;
        }
        if (Fragments[0].bone == null) Fragments[0].bone = transform;
        Transform rootBone = Fragments[0].bone;
        Transform scope = characterRoot != null ? characterRoot : transform;
        // A mesh can be a sibling of the skeleton instead of a descendant of ROOT.
        var candidates = new HashSet<SkinnedMeshRenderer>(scope.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        candidates.UnionWith(rootBone.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        candidates.UnionWith(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        var configuredBones = new HashSet<Transform>(Fragments.Skip(1).Where(f => f != null && f.bone != null).Select(f => f.bone));
        foreach (var f in Fragments)
            if (f != null) f.SkinnedMeshes = new List<SkinnedMeshRenderer>();
        sourceLODGroup = scope.GetComponentInChildren<LODGroup>(true);
        Debug.Log("[Dismember] Mesh scan | candidates=" + candidates.Count + " | cut bones=" + configuredBones.Count, this);
        foreach (var sm in candidates)
        {
            if (sm == null) continue;
            var mesh = sm.sharedMesh;
            int matches = sm.bones.Count(b => b != null && configuredBones.Any(f => b == f || b.IsChildOf(f)));
            Debug.Log("[Dismember] Mesh candidate | renderer=" + sm.name + " | mesh=" + (mesh != null ? mesh.name : "NONE") +
                " | active=" + sm.gameObject.activeInHierarchy + " | readable=" + (mesh != null && mesh.isReadable) +
                " | bones=" + sm.bones.Length + " | matching bones=" + matches, this);
            if (mesh == null || !sm.gameObject.activeInHierarchy) continue;
            if (!sm.sharedMesh.isReadable)
            {
                Debug.LogWarning("[Dismember] Read/Write disabled: " + sm.sharedMesh.name + " on " + sm.name, this);
                continue;
            }
            if (matches == 0) continue;
            try
            {
                SkinnedMeshRenderer working = sm;
                if (stagingImpact)
                {
                    // A separate holder avoids modifying the live renderer's mesh.
                    var holder = new GameObject(sm.name + " [Impact Mesh]");
                    holder.layer = sm.gameObject.layer;
                    holder.transform.SetParent(sm.transform, false);
                    try
                    {
                        // Explicit copy: do not depend on the asset's fragment
                        // renderer helper to preserve the source object's transform.
                        working = holder.AddComponent<SkinnedMeshRenderer>();
                        working.enabled = false;
                        working.sharedMesh = Instantiate(sm.sharedMesh);
                        ownedStagedMeshes.Add(working.sharedMesh);
                        working.bones = sm.bones;
                        working.rootBone = sm.rootBone;
                        working.sharedMaterials = sm.sharedMaterials;
                        working.localBounds = sm.localBounds;
                        working.quality = sm.quality;
                        working.shadowCastingMode = sm.shadowCastingMode;
                        working.receiveShadows = sm.receiveShadows;
                        working.lightProbeUsage = sm.lightProbeUsage;
                        working.reflectionProbeUsage = sm.reflectionProbeUsage;
                        working.probeAnchor = sm.probeAnchor;
                        working.updateWhenOffscreen = true;
                        for (int i = 0; i < working.sharedMesh.blendShapeCount; i++)
                            working.SetBlendShapeWeight(i, sm.GetBlendShapeWeight(i));
                        stagedSources[sm] = working;
                        stagedVisibility[working] = sm.enabled;
                    }
                    catch { Destroy(holder); throw; }
                }
                else sm.sharedMesh = sm.sharedMesh.Copy();
                Fragments[0].SkinnedMeshes.Add(working);
                if (sourceLODGroup != null)
                {
                    var lods = sourceLODGroup.GetLODs();
                    rendererLods[working] = Enumerable.Range(0, lods.Length).Where(i => lods[i].renderers.Contains(sm)).ToArray();
                }
            }
            catch (System.Exception e) { Debug.LogError("[Dismember] Mesh copy failed: " + sm.name + " | " + e, this); }
        }
        isInitialized = Fragments[0].SkinnedMeshes.Count > 0;
        if (DebugDismemberment || !isInitialized)
            Debug.Log("[Dismember] ROOT=" + rootBone.name + " | scope=" + scope.name + " | readable matching meshes=" + Fragments[0].SkinnedMeshes.Count + " | configured fragments=" + configuredBones.Count, this);
        return isInitialized;
    }

    public void Dismember(string FragmentName) { TryDismember(FragmentName); }

    public bool TryDismember(string fragmentName)
    {
        if (Fragments == null || Fragments.Count == 0 || Fragments[0] == null) return false;
        var fragment = Fragments.Find(f => f != null && f.Name == fragmentName);
        if (fragment == null || fragment == Fragments[0] || fragment.bone == null || queuedOrCompleted.Contains(fragment)) return false;
        if (!isInitialized && !PrepareForImpact()) return false;
        queuedOrCompleted.Add(fragment);
        operations.Add(fragment);
        if (DebugDismemberment) Debug.Log("[Dismember] Queued: " + fragmentName, this);
        return true;
    }

    // Boss batch path: no await, no Task.Yield, no frame-dependent worker.
    // Ordinary single-fragment Dismember callers still use the asset's original
    // asynchronous SimpleSplit path below. Only private staged meshes use this.
    public bool CompleteStagedBatchNow()
    {
        if (!stagingImpact || stagingAbandoned || !isInitialized) return false;
        if (!processingOperations && operations.Count > 0) ProcessStagedBatchNow();
        return StagedBatchFinished && StagedReadyCount > 0;
    }

    private void ProcessStagedBatchNow()
    {
        if (processingOperations || stagingAbandoned || !stagingImpact || stagingCommitted) return;
        processingOperations = true;
        float started = Time.realtimeSinceStartup;
        int attempted = operations.Count;
        try
        {
            while (operations.Count > 0)
            {
                var fragment = operations[0];
                operations.RemoveAt(0);
                bool success = false;
                try
                {
                    var parents = Fragments.FindAll(f => f != null && f != fragment && f.bone != null &&
                        fragment.bone != null && fragment.bone.IsChildOf(f.bone) && f.SkinnedMeshes != null &&
                        f.SkinnedMeshes.Any(m => m != null));
                    var parent = fragment.GetNearestParent(parents.ToArray());
                    if (parent == null) throw new System.InvalidOperationException("No mesh-bearing parent for " + fragment.Name);
                    int before = fragment.SkinnedMeshes.Count;
                    for (int i = 0; i < parent.SkinnedMeshes.Count;)
                    {
                        var skin = parent.SkinnedMeshes[i];
                        if (skin == null) { parent.SkinnedMeshes.RemoveAt(i); continue; }
                        bool consumed = false;
                        try { consumed = CutStagedMeshImmediately(skin, fragment); }
                        catch (System.Exception e)
                        {
                            Debug.LogError("[Dismember] Immediate cut failed: " + fragment.Name + " / " + skin.name + " | " + e, this);
                        }
                        if (consumed) parent.SkinnedMeshes.RemoveAt(i);
                        else i++;
                    }
                    success = fragment.SkinnedMeshes.Count > before;
                    if (success) stagedCompleted.Add(fragment);
                    else Debug.LogWarning("[Dismember] No triangles for " + fragment.Name + "; check cut bounds and skin weights.", this);
                }
                catch (System.Exception e) { Debug.LogError("[Dismember] Immediate fragment failed: " + fragment.Name + " | " + e, this); }
                if (!success)
                {
                    queuedOrCompleted.Remove(fragment);
                    NotifyResult(fragment, false);
                }
            }
        }
        finally { processingOperations = false; }
        Debug.Log("[Dismember] SYNC BATCH | prepared=" + stagedCompleted.Count + "/" + attempted +
            " | pending=" + operations.Count + " | ms=" + ((Time.realtimeSinceStartup - started) * 1000f).ToString("F1"), this);
    }

    private bool CutStagedMeshImmediately(SkinnedMeshRenderer skin, BodyFragment fragment)
    {
        Mesh source = skin.sharedMesh;
        if (source == null || !source.isReadable) return false;
        // Preserve authored asset cut volumes. Auto-generated fragments have no
        // volume and use the existing bone-weight fallback instead.
        var childFragments = Fragments.FindAll(f => f != null && f.bone != null &&
            (f.bone == fragment.bone || f.bone.IsChildOf(fragment.bone)) && !stagedCompleted.Contains(f));
        SelectionMask = new bool[source.vertexCount];
        SelectionMaskLength = source.vertexCount;
        for (int i = 0; i < childFragments.Count; ++i)
            GetSelection(skin, childFragments[i], i == 0 ? SelectionMode.Replace : SelectionMode.Add);
        if (GetSelectionStatus() == SelectionStatus.Empty && !SelectBySkinWeights(skin, fragment.bone)) return false;

        // Partition the selected triangles without starting the asset's async
        // SimpleSplit task. Each triangle belongs to exactly one output.
        Mesh piece, remainder;
        if (!SplitSelectedTrianglesNow(source, SelectionMask, out piece, out remainder)) return false;
        SkinnedMeshRenderer clone = null;
        bool committed = false;
        try
        {
            // Same fragment renderer arrangement as the asset, but avoid its
            // Copy helper making another full-size copy of the parent mesh.
            var holder = new GameObject(skin.name + " [" + fragment.Name + "]");
            holder.layer = fragment.bone.gameObject.layer;
            holder.transform.SetParent(fragment.bone, false);
            clone = holder.AddComponent<SkinnedMeshRenderer>();
            clone.enabled = false;
            clone.rootBone = fragment.bone;
            clone.bones = skin.bones;
            clone.sharedMaterials = skin.sharedMaterials;
            clone.localBounds = skin.localBounds;
            clone.quality = skin.quality;
            clone.shadowCastingMode = skin.shadowCastingMode;
            clone.receiveShadows = skin.receiveShadows;
            clone.lightProbeUsage = skin.lightProbeUsage;
            clone.reflectionProbeUsage = skin.reflectionProbeUsage;
            clone.probeAnchor = skin.probeAnchor;
            clone.updateWhenOffscreen = false;
            clone.sharedMesh = piece;
            for (int i = 0; i < piece.blendShapeCount; i++)
                clone.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
            stagedVisibility[clone] = stagedVisibility.TryGetValue(skin, out var visible) && visible;
            if (rendererLods.TryGetValue(skin, out var indices)) rendererLods[clone] = indices;
            // Same bindpose-remapping logic as the original asset. Do it on
            // private meshes so no live character geometry is modified.
            ReplaceBindposes(clone, fragment.bone, BindposeReplacementMode.ReplaceParents);
            if (remainder != null)
            {
                skin.sharedMesh = remainder;
                for (int i = 0; i < remainder.blendShapeCount; i++)
                    skin.SetBlendShapeWeight(i, clone.GetBlendShapeWeight(i));
                try { ReplaceBindposes(skin, fragment.bone, BindposeReplacementMode.ReplaceChildren); }
                catch { skin.sharedMesh = source; throw; }
            }
            fragment.SkinnedMeshes.Add(clone);
            ownedStagedMeshes.Add(piece);
            if (remainder != null) ownedStagedMeshes.Add(remainder);
            committed = true;
            if (remainder == null)
            {
                skin.enabled = false;
                Destroy(skin);
            }
            // Every staged source is a private runtime mesh, not an imported asset.
            ownedStagedMeshes.Remove(source);
            Destroy(source);
            return remainder == null;
        }
        finally
        {
            if (!committed)
            {
                if (clone != null) { stagedVisibility.Remove(clone); rendererLods.Remove(clone); Destroy(clone.gameObject); }
                if (piece != null) Destroy(piece);
                if (remainder != null) Destroy(remainder);
            }
        }
    }

    private static bool SplitSelectedTrianglesNow(Mesh source, bool[] selection, out Mesh piece, out Mesh remainder)
    {
        piece = null; remainder = null;
        int count = source.subMeshCount;
        var selected = new List<int>[count];
        var retained = new List<int>[count];
        int selectedCount = 0, retainedCount = 0;
        for (int sub = 0; sub < count; sub++)
        {
            if (source.GetTopology(sub) != MeshTopology.Triangles)
                throw new System.NotSupportedException("Dismemberment requires triangle topology: " + source.name);
            int[] triangles = source.GetIndices(sub);
            var a = selected[sub] = new List<int>();
            var b = retained[sub] = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int v0 = triangles[i], v1 = triangles[i + 1], v2 = triangles[i + 2];
                int votes = (selection[v0] ? 1 : 0) + (selection[v1] ? 1 : 0) + (selection[v2] ? 1 : 0);
                var destination = votes >= 2 ? a : b;
                destination.Add(v0); destination.Add(v1); destination.Add(v2);
            }
            selectedCount += a.Count; retainedCount += b.Count;
        }
        if (selectedCount == 0) return false;
        try
        {
            piece = BuildCompactCutMesh(source, selected, " [Cut]");
            if (retainedCount > 0) remainder = BuildCompactCutMesh(source, retained, " [Remaining]");
            return true;
        }
        catch
        {
            if (piece != null) Destroy(piece);
            if (remainder != null) Destroy(remainder);
            piece = null; remainder = null;
            throw;
        }
    }

    private static T[] RemapChannel<T>(T[] source, List<int> indices)
    {
        var result = new T[indices.Count];
        for (int i = 0; i < indices.Count; i++) result[i] = source[indices[i]];
        return result;
    }

    private static Mesh BuildCompactCutMesh(Mesh source, List<int>[] submeshes, string suffix)
    {
        // Compact each piece: do not keep a complete copy of the original
        // vertex buffer on every limb. Preserve submeshes, UVs and skin weights.
        var remap = new int[source.vertexCount];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var oldIndices = new List<int>();
        var localTriangles = new int[submeshes.Length][];
        for (int sub = 0; sub < submeshes.Length; sub++)
        {
            var input = submeshes[sub];
            var output = localTriangles[sub] = new int[input.Count];
            for (int i = 0; i < input.Count; i++)
            {
                int old = input[i];
                if (remap[old] < 0) { remap[old] = oldIndices.Count; oldIndices.Add(old); }
                output[i] = remap[old];
            }
        }
        var mesh = new Mesh { name = source.name + suffix, indexFormat = oldIndices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        try
        {
            mesh.vertices = RemapChannel(source.vertices, oldIndices);
            var normals = source.normals;
            if (normals.Length == source.vertexCount) mesh.normals = RemapChannel(normals, oldIndices);
            var tangents = source.tangents;
            if (tangents.Length == source.vertexCount) mesh.tangents = RemapChannel(tangents, oldIndices);
            var colors = source.colors;
            if (colors.Length == source.vertexCount) mesh.colors = RemapChannel(colors, oldIndices);
            for (int channel = 0; channel < 8; channel++)
            {
                var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attr)) continue;
                var uv = new List<Vector4>(); source.GetUVs(channel, uv);
                if (uv.Count != source.vertexCount) continue;
                int dim = source.GetVertexAttributeDimension(attr);
                if (dim <= 2) mesh.SetUVs(channel, oldIndices.Select(i => new Vector2(uv[i].x, uv[i].y)).ToList());
                else if (dim == 3) mesh.SetUVs(channel, oldIndices.Select(i => new Vector3(uv[i].x, uv[i].y, uv[i].z)).ToList());
                else mesh.SetUVs(channel, oldIndices.Select(i => uv[i]).ToList());
            }
            mesh.bindposes = source.bindposes;
            // Keep modern variable-count bone weights (not only the legacy four).
            var counts = source.GetBonesPerVertex();
            var weights = source.GetAllBoneWeights();
            if (counts.Length == source.vertexCount && weights.Length > 0)
            {
                var offsets = new int[counts.Length + 1];
                for (int i = 0; i < counts.Length; i++) offsets[i + 1] = offsets[i] + counts[i];
                int weightCount = 0;
                foreach (int old in oldIndices) weightCount += counts[old];
                // NativeArray is a struct: using variables cannot be mutated (CS1654).
                // Explicit disposal keeps both arrays writable and exception-safe.
                var outCounts = default(NativeArray<byte>);
                var outWeights = default(NativeArray<BoneWeight1>);
                try
                {
                    outCounts = new NativeArray<byte>(oldIndices.Count, Allocator.Temp);
                    outWeights = new NativeArray<BoneWeight1>(weightCount, Allocator.Temp);
                    int cursor = 0;
                    for (int i = 0; i < oldIndices.Count; i++)
                    {
                        int old = oldIndices[i]; outCounts[i] = counts[old];
                        for (int w = offsets[old]; w < offsets[old + 1]; w++) outWeights[cursor++] = weights[w];
                    }
                    mesh.SetBoneWeights(outCounts, outWeights);
                }
                finally
                {
                    if (outWeights.IsCreated) outWeights.Dispose();
                    if (outCounts.IsCreated) outCounts.Dispose();
                }
            }
            else
            {
                var legacy = source.boneWeights;
                if (legacy.Length == source.vertexCount) mesh.boneWeights = RemapChannel(legacy, oldIndices);
            }
            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < localTriangles.Length; i++) mesh.SetTriangles(localTriangles[i], i, false);
            // Preserve blend shapes when present, including their frame weights.
            if (source.blendShapeCount > 0)
            {
                var dv = new Vector3[source.vertexCount]; var dn = new Vector3[source.vertexCount]; var dt = new Vector3[source.vertexCount];
                for (int shape = 0; shape < source.blendShapeCount; shape++)
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                    {
                        source.GetBlendShapeFrameVertices(shape, frame, dv, dn, dt);
                        mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                            RemapChannel(dv, oldIndices), RemapChannel(dn, oldIndices), RemapChannel(dt, oldIndices));
                    }
            }
            if (normals.Length != source.vertexCount) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        catch { Destroy(mesh); throw; }
    }

    async void DismemberManager()
    {
        if (processingOperations || stagingAbandoned) return;
        processingOperations = true;
        try
        {
            while (this != null && operations.Count > 0 && !stagingAbandoned)
            {
                var fragment = operations[0];
                operationStartedAt = Time.realtimeSinceStartup;
                reportedSlowOperation = false;
                if (DebugDismemberment) Debug.Log("[Dismember] Processing: " + fragment.Name + " | pending=" + operations.Count, this);
                bool success = false;
                try
                {
                    var parents = Fragments.FindAll(f => f != null && f != fragment && f.bone != null && fragment.bone != null && fragment.bone.IsChildOf(f.bone) && f.SkinnedMeshes != null && f.SkinnedMeshes.Any(m => m != null));
                    var parent = fragment.GetNearestParent(parents.ToArray());
                    if (parent == null) throw new System.InvalidOperationException("No mesh-bearing parent for " + fragment.Name + "; check ROOT hierarchy.");
                    int before = fragment.SkinnedMeshes.Count;
                    for (int i = 0; i < parent.SkinnedMeshes.Count;)
                    {
                        var mesh = parent.SkinnedMeshes[i];
                        if (mesh == null) { parent.SkinnedMeshes.RemoveAt(i); continue; }
                        if (await CutSkinnedMesh(mesh, fragment)) parent.SkinnedMeshes.RemoveAt(i);
                        else i++;
                        if (this == null) return;
                    }
                    if (fragment.SkinnedMeshes.Count == before)
                        throw new System.InvalidOperationException("No triangles selected; check fragment bounds, bone assignment and mesh weights.");
                    if (stagingImpact && !stagingCommitted)
                    {
                        if (!stagingAbandoned) stagedCompleted.Add(fragment);
                        if (DebugDismemberment) Debug.Log("[Dismember] Prepared (hidden): " + fragment.Name, this);
                    }
                    else FinishDetachment(fragment);
                    success = true;
                }
                catch (System.Exception e) { Debug.LogError("[Dismember] Failed: " + fragment.Name + " | " + e, this); }
                if (this == null) return;
                operations.RemoveAt(0);
                if (!success) queuedOrCompleted.Remove(fragment);
                if (!stagingAbandoned && (!success || !stagingImpact || stagingCommitted))
                    NotifyResult(fragment, success);
            }
        }
        finally
        {
            processingOperations = false;
            if (this != null && stagingAbandoned) DiscardStagedMeshes();
        }
    }

    private void NotifyResult(BodyFragment fragment, bool success)
    {
        try
        {
            if (success) OnDismemberCompleted?.Invoke(fragment.Name);
            else OnDismemberFailed?.Invoke(fragment.Name);
        }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }

    private void FinishDetachment(BodyFragment fragment)
    {
        if (stagingImpact)
            foreach (var mesh in fragment.SkinnedMeshes)
                if (mesh != null)
                {
                    mesh.updateWhenOffscreen = true;
                    mesh.enabled = stagedVisibility.TryGetValue(mesh, out var visible) && visible;
                }
        // Optional effects and LOD metadata must not prevent actual detachment.
        try
        {
            if (fragment.BoneEffect)
            {
                var effect = Instantiate(fragment.BoneEffect, fragment.bone).transform;
                effect.localPosition = fragment.BoneEffectPosition;
                effect.localRotation = Quaternion.Euler(fragment.BoneEffectRotation);
                effect.localScale = fragment.BoneEffectSize;
            }
            if (fragment.BoneParentEffect)
            {
                var effect = Instantiate(fragment.BoneParentEffect, fragment.bone.parent).transform;
                effect.localPosition = fragment.BoneParentEffectPosition;
                effect.localRotation = Quaternion.Euler(fragment.BoneParentEffectRotation);
                effect.localScale = fragment.BoneParentEffectSize;
            }
            if (sourceLODGroup != null) CloneLODGroupForFragment(fragment, sourceLODGroup);
        }
        catch (System.Exception e) { Debug.LogWarning("[Dismember] Optional effect/LOD setup: " + e, this); }
        fragment.bone.SetParent(Fragments[0].bone.parent, true);
        if (DebugDismemberment) Debug.Log("[Dismember] Detached: " + fragment.Name + " | meshes=" + fragment.SkinnedMeshes.Count, this);
    }

    void CloneLODGroupForFragment(BodyFragment fragment, LODGroup original)
    {
        var lods = original.GetLODs();
        bool hasMappedLod = false;
        for (int i = 0; i < lods.Length; i++)
        {
            int index = i;
            lods[i].renderers = fragment.SkinnedMeshes.Where(m => m != null && rendererLods.TryGetValue(m, out var indices) && indices.Contains(index)).Cast<Renderer>().ToArray();
            hasMappedLod |= lods[i].renderers.Length > 0;
        }
        if (!hasMappedLod) return;
        var group = fragment.bone.GetComponent<LODGroup>();
        if (group == null) group = fragment.bone.gameObject.AddComponent<LODGroup>();
        group.SetLODs(lods);
        group.RecalculateBounds();
    }
    public bool[] SelectionMask;
    public int SelectionMaskLength;
    public void GetSelection(SkinnedMeshRenderer skinnedMesh, BodyFragment fragment, SelectionMode mode = SelectionMode.Replace)
    {
        var Mesh = skinnedMesh.sharedMesh;
        var Vertices = Mesh.vertices;
        if (SelectionMask == null || Vertices.Length != SelectionMask.Length)
        {
            SelectionMask = new bool[Vertices.Length];
            SelectionMaskLength = Vertices.Length;
        }
        else
        {
            SelectionMaskLength = Vertices.Length;
        }

        if (mode == SelectionMode.Replace) for (int i = 0; i < Vertices.Length; i++) SelectionMask[i] = false;
        if (fragment.bone == null || fragment.Size.x == 0 || fragment.Size.y == 0 || fragment.Size.z == 0) return;

        var bindposeIndex = System.Array.FindIndex(skinnedMesh.bones, b => b == fragment.bone);
        if (bindposeIndex < 0 || bindposeIndex >= Mesh.bindposes.Length) return;

        var Tris = Mesh.triangles;

        var M = Matrix4x4.Inverse(Matrix4x4.TRS(fragment.Position, Quaternion.Euler(fragment.Rotation), fragment.Size)) * Mesh.bindposes[bindposeIndex];
        Vector3 point;

        for (int i = 0; i < Tris.Length; i += 3)
        {
            var p = (Vertices[Tris[i]] + Vertices[Tris[i + 1]] + Vertices[Tris[i + 2]]) / 3.0f;

            point.x = M.m00 * p.x + M.m01 * p.y + M.m02 * p.z + M.m03;
            point.y = M.m10 * p.x + M.m11 * p.y + M.m12 * p.z + M.m13;
            point.z = M.m20 * p.x + M.m21 * p.y + M.m22 * p.z + M.m23;

            if (point.x > -0.5 && point.x < 0.5
                && point.y > -0.5 && point.y < 0.5 &&
                point.z > -0.5 && point.z < 0.5)
            {

                SelectionMask[Tris[i]] = true;
                SelectionMask[Tris[i + 1]] = true;
                SelectionMask[Tris[i + 2]] = true;
            }
        }
    }
    public SelectionStatus GetSelectionStatus()
    {
        if (SelectionMask == null || SelectionMaskLength == 0) return SelectionStatus.Empty;
        bool anySelected = false, anyUnselected = false;
        for (int i = 0; i < SelectionMaskLength; i++)
        {
            if (SelectionMask[i]) anySelected = true;
            else anyUnselected = true;
            if (anySelected && anyUnselected) return SelectionStatus.Mixed;
        }
        return anySelected ? SelectionStatus.Full : SelectionStatus.Empty;
    }

    // Fall back to the dominant skin weights if a configured cut box selects nothing.
    bool SelectBySkinWeights(SkinnedMeshRenderer sm, Transform bone)
    {
        var mesh = sm.sharedMesh;
        var weights = mesh.boneWeights;
        if (weights == null || weights.Length != mesh.vertexCount) return false;
        var bones = sm.bones;
        bool[] belongs = bones.Select(b => b != null && (b == bone || b.IsChildOf(bone))).ToArray();
        bool InFragment(int index) => index >= 0 && index < belongs.Length && belongs[index];
        SelectionMask = new bool[mesh.vertexCount];
        SelectionMaskLength = mesh.vertexCount;
        var triangles = mesh.triangles;
        bool any = false;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            float sum = 0f;
            for (int j = 0; j < 3; j++)
            {
                var w = weights[triangles[i + j]];
                if (InFragment(w.boneIndex0)) sum += w.weight0;
                if (InFragment(w.boneIndex1)) sum += w.weight1;
                if (InFragment(w.boneIndex2)) sum += w.weight2;
                if (InFragment(w.boneIndex3)) sum += w.weight3;
            }
            if (sum < 1.5f) continue;
            any = true;
            for (int j = 0; j < 3; j++) SelectionMask[triangles[i + j]] = true;
        }
        return any;
    }

    async Task<bool> CutSkinnedMesh(SkinnedMeshRenderer skinnedMesh, BodyFragment fragment)
    {
        if (skinnedMesh == null || skinnedMesh.sharedMesh == null) return false;
        var childFragments = Fragments.FindAll(f => f != null && f.bone != null &&
            (f.bone == fragment.bone || f.bone.IsChildOf(fragment.bone)) && !stagedCompleted.Contains(f));
        SelectionMask = new bool[skinnedMesh.sharedMesh.vertexCount];
        SelectionMaskLength = SelectionMask.Length;
        for (int i = 0; i < childFragments.Count; i++)
            GetSelection(skinnedMesh, childFragments[i], i == 0 ? SelectionMode.Replace : SelectionMode.Add);
        var status = GetSelectionStatus();
        if (status == SelectionStatus.Empty && SelectBySkinWeights(skinnedMesh, fragment.bone))
        {
            status = GetSelectionStatus();
            if (DebugDismemberment) Debug.Log("[Dismember] Skin-weight fallback: " + fragment.Name + " / " + skinnedMesh.name, this);
        }
        if (status == SelectionStatus.Empty) return false;
        var clone = skinnedMesh.Copy(fragment.bone.gameObject);
        try
        {
            clone.rootBone = fragment.bone;
            if (stagingImpact)
            {
                stagedVisibility[clone] = stagedVisibility.TryGetValue(skinnedMesh, out var visible) && visible;
                clone.enabled = false;
            }
            else clone.enabled = skinnedMesh.enabled;
            clone.updateWhenOffscreen = true;
            if (status == SelectionStatus.Full)
            {
                clone.sharedMesh = skinnedMesh.sharedMesh.Copy();
                ReplaceBindposes(clone, fragment.bone, BindposeReplacementMode.ReplaceParents);
            }
            else
            {
                if (DebugDismemberment) Debug.Log("[Dismember] SimpleSplit BEGIN: " + fragment.Name + " / " + skinnedMesh.name, this);
                var mesh = await skinnedMesh.sharedMesh.SimpleSplit(SelectionMask);
                if (this == null) return false;
                if (DebugDismemberment) Debug.Log("[Dismember] SimpleSplit END: " + fragment.Name + " / " + skinnedMesh.name + " | vertices=" + (mesh != null ? mesh.vertexCount : 0), this);
                if (mesh == null || mesh.vertexCount == 0) throw new System.InvalidOperationException("SimpleSplit returned an empty fragment mesh.");
                clone.sharedMesh = mesh;
                ReplaceBindposes(skinnedMesh, fragment.bone, BindposeReplacementMode.ReplaceChildren);
                ReplaceBindposes(clone, fragment.bone, BindposeReplacementMode.ReplaceParents);
            }
            fragment.SkinnedMeshes.Add(clone);
            if (rendererLods.TryGetValue(skinnedMesh, out var indices)) rendererLods[clone] = indices;
            if (status == SelectionStatus.Full)
            {
                skinnedMesh.enabled = false;
                Destroy(skinnedMesh);
                return true;
            }
            return false;
        }
        catch
        {
            if (clone != null) Destroy(clone);
            throw;
        }
    }

    void ReplaceBindposes(SkinnedMeshRenderer skinnedMesh, Transform CrackedBone, BindposeReplacementMode mode)
    {
        var bones = skinnedMesh.bones;
        var bindposes = skinnedMesh.sharedMesh.bindposes;
        Transform replacement = mode == BindposeReplacementMode.ReplaceParents ? CrackedBone : CrackedBone.parent;
        var index = System.Array.FindIndex(bones, b => b == replacement);
        if (mode == BindposeReplacementMode.ReplaceChildren)
        {
            while (replacement != null && (index < 0 || index >= bindposes.Length))
            {
                replacement = replacement.parent;
                index = replacement == null ? -1 : System.Array.FindIndex(bones, b => b == replacement);
            }
        }
        if (index < 0 || index >= bindposes.Length || replacement == null) return;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;

            switch (mode)
            {
                case BindposeReplacementMode.ReplaceChildren:
                    if (bones[i].IsChildOf(CrackedBone))
                    {
                        bones[i] = replacement;
                        bindposes[i] = bindposes[index];
                    }
                    break;
                case BindposeReplacementMode.ReplaceParents:
                    if (!bones[i].IsChildOf(CrackedBone))
                    {
                        bones[i] = CrackedBone;
                        bindposes[i] = bindposes[index];
                    }
                    break;
            }
        }
        skinnedMesh.bones = bones;
        skinnedMesh.sharedMesh.bindposes = bindposes;
    }
    enum BindposeReplacementMode { ReplaceParents, ReplaceChildren }
    public enum SelectionMode { Add, Replace }
    public enum SelectionStatus { Empty, Mixed, Full }
    [System.Serializable]
    public class DismemberOperationEvent : UnityEvent<string> { };
}
[System.Serializable]
public class BodyFragment
{
    public bool ShowWireframe = true;
    public bool ShowProperties;
    public string Name;
    public Transform bone;

    public bool BoundsDetails;
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Size;

    public GameObject BoneEffect;
    public bool BoneEffectDetails;
    public Vector3 BoneEffectPosition;
    public Vector3 BoneEffectRotation;
    public Vector3 BoneEffectSize;

    public GameObject BoneParentEffect;
    public bool BoneParentEffectDetails;
    public Vector3 BoneParentEffectPosition;
    public Vector3 BoneParentEffectRotation;
    public Vector3 BoneParentEffectSize;

    public Color color;
    public List<SkinnedMeshRenderer> SkinnedMeshes;

    public BodyFragment GetNearestParent(params BodyFragment[] Parents)
    {
        var Parent = bone.parent;
        while (Parent != null)
        {
            for (int i = 0; i < Parents.Length; i++) if (Parent == Parents[i].bone) return Parents[i];
            Parent = Parent.parent;
        }
        return null;
    }
}
