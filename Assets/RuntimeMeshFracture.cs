
using System.Collections.Generic;
using UnityEngine;

public static class RuntimeMeshFracture
{
    private const float EPS = 0.00001f;

    private struct Vertex
    {
        public Vector3 p;
        public Vector3 n;
        public Vector2 uv;

        public Vertex(Vector3 p, Vector3 n, Vector2 uv)
        {
            this.p = p;
            this.n = n;
            this.uv = uv;
        }

        public static Vertex Lerp(Vertex a, Vertex b, float t)
        {
            return new Vertex(
                Vector3.Lerp(a.p, b.p, t),
                Vector3.Lerp(a.n, b.n, t).normalized,
                Vector2.Lerp(a.uv, b.uv, t)
            );
        }
    }

    private class Builder
    {
        public List<Vector3> vertices = new List<Vector3>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector2> uvs = new List<Vector2>();
        public List<int> triangles = new List<int>();

        public void Add(Vertex a, Vertex b, Vertex c)
        {
            int i = vertices.Count;

            vertices.Add(a.p);
            vertices.Add(b.p);
            vertices.Add(c.p);

            normals.Add(a.n);
            normals.Add(b.n);
            normals.Add(c.n);

            uvs.Add(a.uv);
            uvs.Add(b.uv);
            uvs.Add(c.uv);

            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
        }

        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh();

            if (vertices.Count > 65535)
                mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }
    }

    public static List<Mesh> Fracture(
        Mesh source,
        int count)
    {
        List<Mesh> pieces = new List<Mesh>();

        if (source == null || !source.isReadable)
            return pieces;

        pieces.Add(source);

        int attempts = 0;

        while (pieces.Count < count &&
               attempts++ < count * 15)
        {
            int index = Largest(pieces);
            Mesh mesh = pieces[index];

            Bounds b = mesh.bounds;

            Vector3 normal =
                Random.onUnitSphere;

            Vector3 point =
                b.center +
                Vector3.Scale(
                    Random.insideUnitSphere * 0.15f,
                    b.size
                );

            Plane plane = new Plane(normal, point);

            Mesh a;
            Mesh c;

            if (!Cut(mesh, plane, out a, out c))
                continue;

            pieces.RemoveAt(index);
            pieces.Add(a);
            pieces.Add(c);

            // Do not destroy the source mesh,
            // because it belongs to the original model.
            if (mesh != source)
                Object.Destroy(mesh);
        }

        return pieces;
    }

    private static int Largest(List<Mesh> pieces)
    {
        int index = 0;
        float max = -1f;

        for (int i = 0; i < pieces.Count; i++)
        {
            Vector3 s = pieces[i].bounds.size;
            float volume = s.x * s.y * s.z;

            if (volume > max)
            {
                max = volume;
                index = i;
            }
        }

        return index;
    }

    private static bool Cut(
        Mesh mesh,
        Plane plane,
        out Mesh positive,
        out Mesh negative)
    {
        positive = null;
        negative = null;

        Builder front = new Builder();
        Builder back = new Builder();

        List<Vector3> cutPoints =
            new List<Vector3>();

        Vector3[] p = mesh.vertices;
        Vector3[] n = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] tris = mesh.triangles;

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vertex[] triangle = new Vertex[3];

            for (int j = 0; j < 3; j++)
            {
                int id = tris[i + j];

                triangle[j] = new Vertex(
                    p[id],
                    n.Length == p.Length
                        ? n[id] : Vector3.up,
                    uv.Length == p.Length
                        ? uv[id] : Vector2.zero
                );
            }

            ClipAndAdd(triangle, plane, true, front);
            ClipAndAdd(triangle, plane, false, back);

            for (int e = 0; e < 3; e++)
            {
                Vertex v1 = triangle[e];
                Vertex v2 = triangle[(e + 1) % 3];

                float d1 =
                    plane.GetDistanceToPoint(v1.p);

                float d2 =
                    plane.GetDistanceToPoint(v2.p);

                if ((d1 > EPS && d2 < -EPS) ||
                    (d1 < -EPS && d2 > EPS))
                {
                    float t = d1 / (d1 - d2);

                    Vector3 hit =
                        Vector3.Lerp(v1.p, v2.p, t);

                    bool exists = false;

                    foreach (Vector3 existing in cutPoints)
                    {
                        if ((existing - hit).sqrMagnitude <
                            0.000001f)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        cutPoints.Add(hit);
                }
            }
        }

        if (front.triangles.Count == 0 ||
            back.triangles.Count == 0 ||
            cutPoints.Count < 3)
            return false;

        Cap(front, back, cutPoints, plane.normal);

        positive = front.ToMesh();
        negative = back.ToMesh();

        return true;
    }

    private static void ClipAndAdd(
        Vertex[] triangle,
        Plane plane,
        bool positive,
        Builder builder)
    {
        List<Vertex> output =
            new List<Vertex>();

        for (int i = 0; i < 3; i++)
        {
            Vertex a = triangle[i];
            Vertex b = triangle[(i + 1) % 3];

            float da =
                plane.GetDistanceToPoint(a.p);

            float db =
                plane.GetDistanceToPoint(b.p);

            bool ina = positive
                ? da >= -EPS
                : da <= EPS;

            bool inb = positive
                ? db >= -EPS
                : db <= EPS;

            if (ina)
                output.Add(a);

            if (ina != inb)
            {
                float t = da / (da - db);

                output.Add(Vertex.Lerp(a, b, t));
            }
        }

        for (int i = 1; i < output.Count - 1; i++)
        {
            builder.Add(
                output[0],
                output[i],
                output[i + 1]
            );
        }
    }

    private static void Cap(
        Builder front,
        Builder back,
        List<Vector3> points,
        Vector3 normal)
    {
        Vector3 center = Vector3.zero;

        foreach (Vector3 p in points)
            center += p;

        center /= points.Count;

        Vector3 axisX =
            Vector3.Cross(
                normal,
                Mathf.Abs(normal.y) < 0.9f
                    ? Vector3.up
                    : Vector3.right
            ).normalized;

        Vector3 axisY =
            Vector3.Cross(normal, axisX).normalized;

        points.Sort((a, b) =>
        {
            Vector3 da = a - center;
            Vector3 db = b - center;

            float aa = Mathf.Atan2(
                Vector3.Dot(da, axisY),
                Vector3.Dot(da, axisX)
            );

            float ab = Mathf.Atan2(
                Vector3.Dot(db, axisY),
                Vector3.Dot(db, axisX)
            );

            return aa.CompareTo(ab);
        });

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Count];

            Vertex centerFront =
                new Vertex(center, -normal, Vector2.zero);

            Vertex aFront =
                new Vertex(a, -normal, Vector2.zero);

            Vertex bFront =
                new Vertex(b, -normal, Vector2.zero);

            front.Add(
                centerFront,
                bFront,
                aFront
            );

            Vertex centerBack =
                new Vertex(center, normal, Vector2.zero);

            Vertex aBack =
                new Vertex(a, normal, Vector2.zero);

            Vertex bBack =
                new Vertex(b, normal, Vector2.zero);

            back.Add(
                centerBack,
                aBack,
                bBack
            );
        }
    }
}
