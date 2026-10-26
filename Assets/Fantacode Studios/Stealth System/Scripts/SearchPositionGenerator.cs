using FS_Core;
using FS_Util;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FS_StealthSystem
{
    public static class SearchPositionGenerator
    {
        const int maxAttempts = 20;
        const int sampleRadius = 2;

        public static bool TryGetSearchPosition(AIController ai, float seachRadius, 
            out Vector3 position, Vector3 prevDirection)
        {
            var origin = ai.transform.position;

            for (int i = 0; i < maxAttempts; i++)
            {
                var dir = GetSearchDirection(prevDirection, i, ai.transform);
                var candidate = origin + dir * seachRadius;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    continue;

                var finalPos = hit.position;

                // Search position must not be visible for the AI
                if (!PhysicsUtil.SimpleRayCast(finalPos + Vector3.up * 0.5f,
                    origin + Vector3.up * 0.5f, out RaycastHit raycastHit)) 
                    continue;

                finalPos = raycastHit.point + (finalPos - origin).normalized * 2f;

                // Re-sample onto NavMesh after the offset
                if (!NavMesh.SamplePosition(finalPos, out hit, sampleRadius, NavMesh.AllAreas))
                    continue;
                finalPos = hit.position;

                // Reject if the AI can't actually walk there
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(origin, finalPos, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                // Reject if the cell is close to a reserved cell
                if (SearchPositionManager.IsCloseToReservedCell(finalPos))
                {
                    Debug.Log("Rejected since it's close to a reserved cell");
                    continue;
                }

                if (!SearchPositionManager.TryReserve(finalPos))
                    continue;
                
                position = finalPos; 
                return true;
            }


            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Fallback: picks a random reachable NavMesh point within the search radius,
        /// without requiring the point to be hidden from the AI.
        /// </summary>
        public static bool TryGetRandomReachablePosition(AIController ai, float searchRadius,
            out Vector3 position)
        {
            var origin = ai.transform.position;

            for (int i = 0; i < maxAttempts; i++)
            {
                var randomDir = Random.insideUnitCircle;
                var dir = new Vector3(randomDir.x, 0, randomDir.y).normalized;
                var distance = Random.Range(searchRadius * 0.3f, searchRadius);
                var candidate = origin + dir * distance;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                    continue;

                var finalPos = hit.position;

                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(origin, finalPos, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                if (SearchPositionManager.IsCloseToReservedCell(finalPos))
                    continue;

                if (!SearchPositionManager.TryReserve(finalPos))
                    continue;

                position = finalPos;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        static Vector3 GetSearchDirection(Vector3 prevDirection, int index, Transform ai)
        {
            float[] angles = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f, 90f, -90f, 105f, -105f, 120f, -120f, 135f, -135f, 150f, -150f };

            if (prevDirection != Vector3.zero && index < angles.Length)
            {
                // Debug.Log($"trying angle {angles[index]}");
                return Quaternion.Euler(0, angles[index], 0) * ai.forward;
            }
            else
            {
                // choose random direction
                var randomDir = Random.insideUnitCircle;
                return new Vector3(randomDir.x, 0, randomDir.y);
            }
        }
    }

}
