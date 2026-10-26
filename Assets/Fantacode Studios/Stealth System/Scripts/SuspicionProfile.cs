using System.Collections.Generic;
using UnityEngine;

namespace FS_StealthSystem
{
    public class SuspicionProfile : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("If true, AI will detect the player even without any active suspicion factors.")]
        bool isSuspiciousByDefault = true;

        readonly List<ISuspicionFactor> factors = new List<ISuspicionFactor>();

        public bool IsSuspicious
        {
            get
            {
                if (isSuspiciousByDefault) return true;
                for (int i = 0; i < factors.Count; i++)
                    if (factors[i].IsActive) return true;
                return false;
            }
        }

        public bool IsInstantThreat
        {
            get
            {
                for (int i = 0; i < factors.Count; i++)
                    if (factors[i].IsActive && factors[i].IsInstant) return true;
                return false;
            }
        }

        public bool HasActiveWarnFactor
        {
            get
            {
                for (int i = 0; i < factors.Count; i++)
                    if (factors[i].IsActive && factors[i].WarnsFirst) return true;
                return false;
            }
        }

        public bool ShouldWarn => !IsInstantThreat && HasActiveWarnFactor;

        public void AddFactor(ISuspicionFactor factor)
        {
            if (!factors.Contains(factor))
                factors.Add(factor);
        }

        public void RemoveFactor(ISuspicionFactor factor)
        {
            factors.Remove(factor);
        }
    }
}
