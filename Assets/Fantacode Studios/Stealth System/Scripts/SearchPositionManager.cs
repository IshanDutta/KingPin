using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public static class SearchPositionManager
    {
        const int cellSize = 2;

        static readonly Dictionary<Vector2Int, Vector3> reservedCells = new();

        public static Vector2Int GetCell(Vector3 position)
        {
            int x = Mathf.FloorToInt(position.x / cellSize);
            int z = Mathf.FloorToInt(position.z / cellSize);
            return new Vector2Int(x, z);
        }

        public static bool TryReserve(Vector3 position)
        {
            Vector2Int cell = GetCell(position);
            if (reservedCells.ContainsKey(cell))
                return false;

            reservedCells[cell] = position;
            return true;
        }

        public static void Release(Vector3 position)
        {
            reservedCells.Remove(GetCell(position));
        }

        public static bool IsCloseToReservedCell(Vector3 candidatePos)
        {
            var candidate = GetCell(candidatePos);

            foreach (var reserved in reservedCells.Keys)
            {
                int distance = Mathf.Abs(reserved.x - candidate.x) + Mathf.Abs(reserved.y - candidate.y);

                if (distance <= 2)
                    return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnRuntimeInit()
        {
            reservedCells.Clear();
        }
    }
}
