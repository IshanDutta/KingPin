using FS_CombatCore;
using FS_Core;
using FS_CoverSystem;
using System.Collections.Generic;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class CoverPoint
    {
        public Vector3 Position;
        public Vector3 PositionOffset;
        public Collider CoverCollider;
        public CombatAIController combatAI;
        public CoverPoint(Vector3 p, Collider c, CombatAIController e, Vector3 positionOffset)
        {
            Position = p;
            CoverCollider = c;
            combatAI = e;
            PositionOffset = positionOffset;
        }
    }

    /// <summary>
    /// Finds the best cover point for an AI and tracks which cover points are
    /// currently claimed (shared across all AIs) so they spread out along covers.
    /// </summary>
    public class AICoverPointFinder
    {
        private readonly AIController ai;
        private readonly CombatAIController combatAI;
        private readonly ShooterAIController shooterAI;

        private readonly LayerMask coverMask;

        private static readonly Collider[] _coverOverlapBuffer = new Collider[32]; // Adjust size as needed

        // Cover points currently claimed by AIs, grouped by cover collider
        private static Dictionary<Collider, List<CoverPoint>> CoverPointAlleys = new Dictionary<Collider, List<CoverPoint>>();

        public AICoverPointFinder(AIController ai, CombatAIController combatAI, ShooterAIController shooterAI)
        {
            this.ai = ai;
            this.combatAI = combatAI;
            this.shooterAI = shooterAI;
            coverMask = LayerMask.GetMask("Cover");
        }

        public static void ResetOccupancy()
        {
            CoverPointAlleys = new Dictionary<Collider, List<CoverPoint>>();
        }

        public static void RegisterCoverPoint(CoverPoint coverPoint)
        {
            if (coverPoint == null) return;

            if (!CoverPointAlleys.TryGetValue(coverPoint.CoverCollider, out var list))
            {
                list = new List<CoverPoint>();
                CoverPointAlleys[coverPoint.CoverCollider] = list;
            }
            list.Add(coverPoint);
        }

        public static void UnregisterCoverPoint(CoverPoint coverPoint)
        {
            if (coverPoint == null) return;

            if (CoverPointAlleys.TryGetValue(coverPoint.CoverCollider, out var list))
            {
                list.Remove(coverPoint);
                if (list.Count == 0)
                    CoverPointAlleys.Remove(coverPoint.CoverCollider);
            }
        }

        public CoverPoint FindBestCoverPoint()
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                ai.transform.position,
                combatAI.GetMovementRange() < shooterAI.CoverRange ? combatAI.GetMovementRange() : shooterAI.CoverRange,
                _coverOverlapBuffer,
                coverMask,
                QueryTriggerInteraction.Collide
            );

            List<CoverPoint> coverPoints = new List<CoverPoint>();

            for (int i = 0; i < hitCount; i++)
            {
                var col = _coverOverlapBuffer[i];
                if (col == null) continue;

                if (combatAI.Fighter.Target == null) return null;

                // Only choose covers that don't face the target
                var vecToTarget = ai.transform.position - combatAI.Fighter.Target.transform.position;
                //if (Vector3.Angle(col.transform.forward, vecToTarget) > 90f)
                //    continue;

                // If the target is in cover, then don't choose a close cover point to him
                bool targetInCover = combatAI.Fighter.Target?.animator.GetBool(AnimatorParameters.coverMode) ?? false;
                float distanceToCover = Vector3.Distance(col.transform.position, combatAI.Fighter.Target.transform.position);
                if (distanceToCover < shooterAI.MinCoverDistance)
                    continue;

                // If distance between the cover point and target is greater than max preferred range, skip
                if (distanceToCover >= combatAI.Fighter.PreferredFightingRange)
                    continue;

                Vector3? foundPoint = GetBestCoverPoint(col, ai.transform.position);
                if (!foundPoint.HasValue)
                    continue;
                Vector3 coverPos = foundPoint.Value;
                coverPoints.Add(new CoverPoint(coverPos, col, combatAI, col.transform.forward * 0.5f));
            }

            if (coverPoints.Count == 0)
                return null;

            coverPoints.Sort((a, b) =>
            {
                // 1) Compute how many enemies are already on each collider:
                int countA = 0, countB = 0;

                if (shooterAI.OccupantWeight != 0)
                {
                    if (CoverPointAlleys.TryGetValue(a.CoverCollider, out var listA))
                        countA = listA.Count;
                    if (CoverPointAlleys.TryGetValue(b.CoverCollider, out var listB))
                        countB = listB.Count;
                }

                float distA, distB, disToTargetA, disToTargetB;
                distA = distB = disToTargetA = disToTargetB = 0;

                // 2) Compute squared distance from this AI to each cover‐point:
                if (shooterAI.DistanceWeight != 0)
                {
                    distA = Vector3.SqrMagnitude(ai.transform.position - a.Position);
                    distB = Vector3.SqrMagnitude(ai.transform.position - b.Position);
                }

                if (shooterAI.DistanceToTargetWeight != 0)
                {
                    disToTargetA = Vector3.SqrMagnitude(combatAI.Fighter.Target.transform.position - a.Position);
                    disToTargetB = Vector3.SqrMagnitude(combatAI.Fighter.Target.transform.position - b.Position);
                }

                // 3) Build a “weight” that is (distance + occupant‐penalty):
                float weightA = distA * shooterAI.DistanceWeight + disToTargetA * shooterAI.DistanceToTargetWeight + countA * shooterAI.OccupantWeight;
                float weightB = distB * shooterAI.DistanceWeight + disToTargetB * shooterAI.DistanceToTargetWeight + countB * shooterAI.OccupantWeight;

                return weightA.CompareTo(weightB);
            });

            return coverPoints[0];
        }

        public Vector3? GetBestCoverPoint(Collider collider, Vector3 position)
        {
            // Try right and left independently
            Vector3? rightPoint = getCoverPointFromCollider(collider, position);
            Vector3? leftPoint = getCoverPointFromCollider(collider, position, true);

            if (rightPoint == null && leftPoint == null)
                return null;
            if (rightPoint == null)
                return leftPoint;
            if (leftPoint == null)
                return rightPoint;

            // Both non-null: return whichever is closer to the AI's position
            float distRightSq = Vector3.SqrMagnitude(rightPoint.Value - position);
            float distLeftSq = Vector3.SqrMagnitude(leftPoint.Value - position);

            return (distRightSq <= distLeftSq) ? rightPoint : leftPoint;
        }

        private Vector3? getCoverPointFromCollider(Collider collider, Vector3 position, bool checkToLeft = false)
        {
            Vector3 closestCoverPoint = collider.ClosestPoint(position);
            Vector3 coverPos = closestCoverPoint;
            Vector3 targetPos = combatAI.Fighter.Target.transform.position + Vector3.up * 2f;

            if (CoverPointAlleys.ContainsKey(collider))
            {
                var direction = checkToLeft ? -collider.transform.right : collider.transform.right;
                direction *= 1f;
                List<CoverPoint> alleys = CoverPointAlleys[collider];

                while (true)
                {
                    //if (isCloseToPoint(alleys, closestCoverPoint, 1f))
                    if(Physics.CheckSphere(closestCoverPoint,0.4f, ai.gameObject.layer) )//|| isCloseToPoint(alleys, closestCoverPoint, 0.5f))
                    {
                        var newCoverPoint = collider.ClosestPoint(closestCoverPoint + direction);
                        if (Vector3.Magnitude(closestCoverPoint.SetY(0) - newCoverPoint.SetY(0)) < 0.6f)
                        {
                            return null;
                        }
                        closestCoverPoint = newCoverPoint;
                    }
                    else
                    {
                        if (Physics.Raycast(coverPos, targetPos - coverPos, out RaycastHit hit, 2f, FSSettings.i.ObstacleLayer))
                            return closestCoverPoint;
                        else
                            return null;
                    }
                }
            }
            else
            {
                if (Physics.Raycast(coverPos, targetPos - coverPos, out RaycastHit hit, 2f, FSSettings.i.ObstacleLayer))
                    return closestCoverPoint;
                else
                    return null;
            }
        }

        private bool isCloseToPoint(List<CoverPoint> alleys, Vector3 point, float maxDistance)
        {
            //return !alleys.All(cp => Vector3.Magnitude(cp.Position.SetY(0) - point.SetY(0)) >= maxDistance);
            foreach (var cp in alleys)
            {
                if (Vector3.Magnitude(cp.Position.SetY(0) - point.SetY(0)) < maxDistance )
                    return false;
            }
            return true;
        }
    }
}
