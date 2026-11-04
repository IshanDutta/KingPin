using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Destructible cover that breaks into irregular 3D Voronoi fragments.
/// Debris has NO colliders and cannot block movement, vaulting or raycasts.
/// The public API stays compatible with BossEnemyAI.TakeDamage calls.
///
/// Best results: one closed, convex MeshFilter (crate, pillar, barricade, etc.).
/// For non-convex/unreadable meshes, uses the mesh's local bounds as a fallback.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Cover : MonoBehaviour
{
    [Header("Cover Health")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("Irregular Fracture")]
    [Tooltip("3 = approximately 27 jagged pieces. Higher values cost more CPU.")]
    [SerializeField, Range(2, 5)] private int fragmentsPerAxis = 3;
    [SerializeField, Range(0f, 0.48f)] private float fractureIrregularity = 0.4f;
    [SerializeField] private bool inheritMaterial = true;

    [Header("Debris Motion (No Collision)")]
    [SerializeField, Min(0f)] private float explosionForce = 4f;
    [SerializeField, Min(0.01f)] private float explosionRadius = 3f;
    [SerializeField, Min(0f)] private float upwardModifier = 0.5f;
    [SerializeField, Min(0f)] private float spinImpulse = 2f;
    [SerializeField, Min(0f)] private float fragmentLifetime = 8f;
    [SerializeField, Min(0.01f)] private float fragmentMassPerCubicUnit = 0.15f;

    private bool destroyed;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDestroyed => destroyed;

    private void Awake()
    {
        currentHealth = maxHealth;
        destroyed = false;
    }

    public void TakeDamage(float damage)
    {
        if (destroyed || damage <= 0f) return;
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        if (currentHealth <= 0f) Fracture();
    }

    private void Fracture()
    {
        if (destroyed) return;
        destroyed = true;

        MeshFilter sourceFilter = GetComponentInChildren<MeshFilter>();
        Renderer sourceRenderer = sourceFilter != null
            ? sourceFilter.GetComponent<Renderer>()
            : GetComponentInChildren<Renderer>();

        Transform sourceTransform = sourceFilter != null
            ? sourceFilter.transform : null;

        Bounds localBounds;
        Mesh sourceMesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
        bool hasVolumetricMesh = sourceMesh != null &&
            Mathf.Min(sourceMesh.bounds.size.x,
                Mathf.Min(sourceMesh.bounds.size.y, sourceMesh.bounds.size.z)) > 0.01f;
        if (hasVolumetricMesh)
        {
            localBounds = sourceMesh.bounds;
        }
        else
        {
            // Thin plane meshes are common for walls. Use their BoxCollider
            // volume so they still fracture into actual 3D pieces.
            BoxCollider box = GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                localBounds = new Bounds(box.center, box.size);
                sourceTransform = box.transform;
            }
            else
            {
                // Collider.bounds are WORLD-space. Do not transform twice.
                localBounds = GetComponent<Collider>().bounds;
                sourceTransform = null;
            }
            sourceMesh = null;
        }

        // Gather source faces where possible, so convex mesh objects fracture
        // along their true outline instead of an artificial rectangular box.
        List<List<Vector3>> sourceFaces = null;
        bool usesOriginalShape = sourceMesh != null &&
            TryGetConvexMeshFaces(sourceMesh, out sourceFaces);
        if (!usesOriginalShape)
            sourceFaces = CreateBoxFaces(localBounds);

        Vector3[] seeds = CreateJitteredSeeds(localBounds);
        Material material = inheritMaterial && sourceRenderer != null
            ? sourceRenderer.sharedMaterial : null;

        // Prevent the intact object from blocking AI, vision or vault checks.
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        foreach (NavMeshObstacle obstacle in GetComponentsInChildren<NavMeshObstacle>(true))
            obstacle.enabled = false;
        foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
            rend.enabled = false;

        Vector3 explosionCenter = sourceTransform != null
            ? sourceTransform.TransformPoint(localBounds.center)
            : localBounds.center;

        int piecesCreated = 0;
        for (int i = 0; i < seeds.Length; ++i)
        {
            List<List<Vector3>> cell = CloneFaces(sourceFaces);
            for (int j = 0; j < seeds.Length && cell.Count > 0; ++j)
            {
                if (i == j) continue;
                Vector3 between = seeds[j] - seeds[i];
                if (between.sqrMagnitude < 0.000001f) continue;
                Vector3 normal = between.normalized;
                float planeDistance = Vector3.Dot((seeds[i] + seeds[j]) * 0.5f, normal);
                cell = ClipConvexSolid(cell, normal, planeDistance);
            }

            if (cell.Count == 0) continue;
            if (CreateFragment(cell, sourceTransform, material, explosionCenter, i))
                piecesCreated++;
        }

        Debug.Log($"{name} shattered into {piecesCreated} irregular, non-colliding fragments." +
                  (usesOriginalShape ? "" : " (Fractured from its bounds.)"), this);

        // These fragments are independent root objects, so they survive this.
        Destroy(gameObject);
    }

    private Vector3[] CreateJitteredSeeds(Bounds bounds)
    {
        int n = Mathf.Clamp(fragmentsPerAxis, 2, 5);
        Vector3[] result = new Vector3[n * n * n];
        Vector3 step = new Vector3(bounds.size.x / n, bounds.size.y / n, bounds.size.z / n);
        int index = 0;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    Vector3 basePoint = bounds.min + new Vector3(
                        (x + 0.5f) * step.x,
                        (y + 0.5f) * step.y,
                        (z + 0.5f) * step.z);
                    Vector3 jitter = new Vector3(
                        Random.Range(-fractureIrregularity, fractureIrregularity) * step.x,
                        Random.Range(-fractureIrregularity, fractureIrregularity) * step.y,
                        Random.Range(-fractureIrregularity, fractureIrregularity) * step.z);
                    result[index++] = basePoint + jitter;
                }
        return result;
    }

    private static List<List<Vector3>> CreateBoxFaces(Bounds b)
    {
        Vector3 a = new Vector3(b.min.x, b.min.y, b.min.z);
        Vector3 c0 = new Vector3(b.max.x, b.min.y, b.min.z);
        Vector3 c1 = new Vector3(b.max.x, b.max.y, b.min.z);
        Vector3 d = new Vector3(b.min.x, b.max.y, b.min.z);
        Vector3 e = new Vector3(b.min.x, b.min.y, b.max.z);
        Vector3 f = new Vector3(b.max.x, b.min.y, b.max.z);
        Vector3 g = new Vector3(b.max.x, b.max.y, b.max.z);
        Vector3 h = new Vector3(b.min.x, b.max.y, b.max.z);
        return new List<List<Vector3>>
        {
            new List<Vector3>{ a, d, c1, c0 }, // -Z
            new List<Vector3>{ e, f, g, h },   // +Z
            new List<Vector3>{ a, e, h, d },   // -X
            new List<Vector3>{ c0, c1, g, f },// +X
            new List<Vector3>{ a, c0, f, e }, // -Y
            new List<Vector3>{ d, h, g, c1 }  // +Y
        };
    }

    private static bool TryGetConvexMeshFaces(Mesh mesh, out List<List<Vector3>> faces)
    {
        faces = null;
        if (mesh == null || !mesh.isReadable) return false;
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        // Limit expensive convexity testing for highly detailed meshes.
        if (indices.Length < 12 || indices.Length > 1500 || vertices.Length > 400)
            return false;

        Vector3 middle = mesh.bounds.center;
        float tolerance = Mathf.Max(0.002f, mesh.bounds.size.magnitude * 0.001f);
        List<List<Vector3>> candidate = new List<List<Vector3>>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]];
            Vector3 b = vertices[indices[i + 1]];
            Vector3 c = vertices[indices[i + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 0.0000001f) continue;
            if (Vector3.Dot(normal, middle - a) > 0f)
            {
                Vector3 tmp = b;
                b = c;
                c = tmp;
                normal = -normal;
            }
            normal.Normalize();
            // A convex closed mesh must be entirely behind each outward face.
            for (int j = 0; j < vertices.Length; j++)
            {
                if (Vector3.Dot(normal, vertices[j] - a) > tolerance)
                    return false;
            }
            candidate.Add(new List<Vector3> { a, b, c });
        }
        if (candidate.Count < 4) return false;
        faces = candidate;
        return true;
    }

    private static List<List<Vector3>> CloneFaces(List<List<Vector3>> faces)
    {
        List<List<Vector3>> result = new List<List<Vector3>>(faces.Count);
        foreach (List<Vector3> face in faces)
            result.Add(new List<Vector3>(face));
        return result;
    }

    /// <summary>Clip a convex polyhedron to dot(point, n) <= d.</summary>
    private static List<List<Vector3>> ClipConvexSolid(
        List<List<Vector3>> faces, Vector3 n, float d)
    {
        const float epsilon = 0.0001f;
        List<List<Vector3>> result = new List<List<Vector3>>(faces.Count + 1);
        List<Vector3> intersections = new List<Vector3>();

        foreach (List<Vector3> face in faces)
        {
            List<Vector3> clipped = new List<Vector3>();
            if (face.Count < 3) continue;
            for (int i = 0; i < face.Count; ++i)
            {
                Vector3 a = face[i];
                Vector3 b = face[(i + 1) % face.Count];
                float da = Vector3.Dot(a, n) - d;
                float db = Vector3.Dot(b, n) - d;
                bool insideA = da <= epsilon;
                bool insideB = db <= epsilon;
                if (insideA) clipped.Add(a);
                if (insideA == insideB) continue;
                float t = Mathf.Clamp01(da / (da - db));
                Vector3 intersection = Vector3.LerpUnclamped(a, b, t);
                clipped.Add(intersection);
                AddUnique(intersections, intersection, 0.0002f);
            }
            RemoveConsecutiveDuplicates(clipped);
            if (clipped.Count >= 3) result.Add(clipped);
        }

        if (intersections.Count >= 3)
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 v in intersections) center += v;
            center /= intersections.Count;
            Vector3 tangent = Vector3.Cross(
                Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right, n).normalized;
            Vector3 bitangent = Vector3.Cross(n, tangent).normalized;
            intersections.Sort((p, q) =>
            {
                float angleP = Mathf.Atan2(Vector3.Dot(p - center, bitangent),
                                            Vector3.Dot(p - center, tangent));
                float angleQ = Mathf.Atan2(Vector3.Dot(q - center, bitangent),
                                            Vector3.Dot(q - center, tangent));
                return angleP.CompareTo(angleQ);
            });
            result.Add(intersections);
        }
        return result;
    }

    private static void AddUnique(List<Vector3> points, Vector3 p, float epsilon)
    {
        float sq = epsilon * epsilon;
        foreach (Vector3 v in points)
            if ((v - p).sqrMagnitude <= sq) return;
        points.Add(p);
    }

    private static void RemoveConsecutiveDuplicates(List<Vector3> polygon)
    {
        const float eps2 = 0.00000001f;
        for (int i = polygon.Count - 1; i > 0; --i)
            if ((polygon[i] - polygon[i - 1]).sqrMagnitude < eps2)
                polygon.RemoveAt(i);
        if (polygon.Count > 1 &&
            (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude < eps2)
            polygon.RemoveAt(polygon.Count - 1);
    }

    private bool CreateFragment(List<List<Vector3>> faces, Transform sourceTransform,
        Material material, Vector3 explosionCenter, int index)
    {
        // Calculate a shared center for this shard, then put all its vertices in
        // world-relative local coordinates. This preserves rotated/scaled covers.
        Vector3 worldCenter = Vector3.zero;
        int vertexCount = 0;
        foreach (List<Vector3> face in faces)
            foreach (Vector3 point in face)
            {
                worldCenter += sourceTransform != null
                    ? sourceTransform.TransformPoint(point) : point;
                vertexCount++;
            }
        if (vertexCount < 4) return false;
        worldCenter /= vertexCount;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvs = new List<Vector2>();
        foreach (List<Vector3> face in faces)
        {
            if (face.Count < 3) continue;
            Vector3 p0 = sourceTransform != null
                ? sourceTransform.TransformPoint(face[0]) : face[0];
            Vector3 p1 = sourceTransform != null
                ? sourceTransform.TransformPoint(face[1]) : face[1];
            Vector3 p2 = sourceTransform != null
                ? sourceTransform.TransformPoint(face[2]) : face[2];
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            if (normal.sqrMagnitude < 0.5f) continue;
            int start = verts.Count;
            foreach (Vector3 point in face)
            {
                Vector3 world = sourceTransform != null
                    ? sourceTransform.TransformPoint(point) : point;
                verts.Add(world - worldCenter);
                Vector2 uv = Mathf.Abs(normal.y) > 0.65f
                    ? new Vector2(world.x, world.z)
                    : Mathf.Abs(normal.x) > 0.65f
                        ? new Vector2(world.z, world.y)
                        : new Vector2(world.x, world.y);
                uvs.Add(uv);
            }
            for (int t = 1; t < face.Count - 1; ++t)
            {
                tris.Add(start);
                tris.Add(start + t);
                tris.Add(start + t + 1);
            }
        }
        if (tris.Count == 0) return false;

        Mesh shardMesh = new Mesh();
        shardMesh.name = "FracturedCoverMesh";
        // More than 65K indices can occur only with excessive source geometry.
        if (verts.Count > 65535)
            shardMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        shardMesh.SetVertices(verts);
        shardMesh.SetTriangles(tris, 0);
        shardMesh.SetUVs(0, uvs);
        shardMesh.RecalculateNormals();
        shardMesh.RecalculateBounds();

        GameObject fragment = new GameObject("Cover_Fragment_" + index);
        fragment.transform.position = worldCenter;
        MeshFilter mf = fragment.AddComponent<MeshFilter>();
        mf.sharedMesh = shardMesh;
        MeshRenderer mr = fragment.AddComponent<MeshRenderer>();
        if (material != null) mr.sharedMaterial = material;

        // Intentionally NO Collider. Unlike cubes made with CreatePrimitive,
        // this GameObject cannot collide, block navigation or intercept raycasts.
        Rigidbody rb = fragment.AddComponent<Rigidbody>();
        rb.detectCollisions = false;
        rb.useGravity = true;
        Vector3 extents = shardMesh.bounds.size;
        float estimatedVolume = Mathf.Max(0.001f,
            extents.x * extents.y * extents.z);
        rb.mass = Mathf.Clamp(estimatedVolume * fragmentMassPerCubicUnit, 0.05f, 5f);
        Vector3 outward = (worldCenter - explosionCenter).normalized;
        if (outward.sqrMagnitude < 0.01f)
            outward = Random.onUnitSphere;
        outward.y += upwardModifier;
        rb.AddForce(outward.normalized * explosionForce, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * spinImpulse, ForceMode.Impulse);

        // Lifetime zero leaves debris around permanently (it will fall through
        // collidable floors because collisions are disabled).
        if (fragmentLifetime > 0f)
        {
            Destroy(fragment, fragmentLifetime);
            Destroy(shardMesh, fragmentLifetime);
        }
        return true;
    }
}
