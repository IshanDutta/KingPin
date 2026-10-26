using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public class VisionMeshCreator
    {
        static Dictionary<(float, float, float, float, float, float), Mesh> visionMeshes = new Dictionary<(float, float, float, float, float, float), Mesh>();

        public static Mesh Create(float startWidth, float midWidth, float endWidth, float range, float height, float heightBelow, string name = "Vision Sensor")
        {
            var key = (startWidth, midWidth, endWidth, range, height, heightBelow);

            // mesh != null guards against destroyed meshes lingering in the static cache when domain reload is disabled
            if (visionMeshes.TryGetValue(key, out var cachedMesh) && cachedMesh != null)
                return cachedMesh;

            var visionMesh = new Mesh();
            visionMesh.name = name;

            float h = height;
            float b = -heightBelow;

            Vector3[] vertices =
            {
                    // Start (z = 0)
                    new Vector3(-startWidth * 0.5f, b, 0f),
                    new Vector3(startWidth * 0.5f, b, 0f),
                    new Vector3(-startWidth * 0.5f, h, 0f),
                    new Vector3(startWidth * 0.5f, h, 0f),

                    // Center (z = range / 2)
                    new Vector3(-midWidth * 0.5f, b, range / 2),
                    new Vector3(midWidth * 0.5f, b, range / 2),
                    new Vector3(-midWidth * 0.5f, h, range / 2),
                    new Vector3(midWidth * 0.5f, h, range / 2),

                    // End (z = range)
                    new Vector3(-endWidth * 0.5f, b, range),
                    new Vector3(endWidth * 0.5f, b, range),
                    new Vector3(-endWidth * 0.5f, h, range),
                    new Vector3(endWidth * 0.5f, h, range),
                };

            int[] traingles =
            {
                    // Bottom
                    0, 4, 1,   1, 4, 5,
                    4, 8, 5,   5, 8, 9,

                    // Top
                    2, 3, 6,   3, 7, 6,
                    6, 7,10,   7,11,10,

                    // Left
                    0, 2, 4,   2, 6, 4,
                    4, 6, 8,   6,10, 8,

                    // Right
                    1, 5, 3,   3, 5, 7,
                    5, 9, 7,   7, 9,11,

                    // Front (end cap)
                    8,10, 9,   9,10,11,

                    // Back (start cap)
                    0, 1, 2,   1, 3, 2
                };

            visionMesh.vertices = vertices;
            visionMesh.triangles = traingles;

            visionMesh.RecalculateNormals();
            visionMesh.RecalculateBounds();

            visionMeshes[key] = visionMesh;

            return visionMesh;
        }
    }
}