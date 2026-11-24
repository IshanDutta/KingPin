using FS_Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FS_CombatCore
{

    public class CombatAIManager : MonoBehaviour
    {

        FighterCore player;
        public FighterCore Player {
            get => player;
            set { player = value; }
        }

        public static CombatAIManager i { get; private set; }
        private void Awake()
        {
            i = this;

            var playerController = GetComponentInChildren<PlayerController>();
            if (playerController == null)
                playerController = GetComponentInParent<PlayerController>();

            player = playerController.GetComponent<FighterCore>();
            CombatAILookup.Init();
        }

        public List<CombatAIController> MeleeAIList { get; private set; } = new List<CombatAIController>();
        public List<CombatAIController> RangedAIList { get; private set; } = new List<CombatAIController>();

        /// <summary>
        /// Registers the AI into the list for its combat type. Pass <paramref name="registerInMeleeList"/>
        /// to also place a ranged AI in the melee list (e.g. while it fights with a melee weapon), or
        /// <paramref name="registerInRangedList"/> to also place a melee AI in the ranged list.
        /// Safe to call repeatedly — an AI is never added to the same list twice.
        /// </summary>
        public void RegisterCombatAI(CombatAIController combatAI, bool registerInMeleeList = false, bool registerInRangedList = false)
        {
            CombatAILookup.Register(combatAI);

            if (combatAI.AICombatType == AICombatType.Melee || registerInMeleeList)
                AddUnique(MeleeAIList, combatAI);

            if (combatAI.AICombatType == AICombatType.Ranged || registerInRangedList)
                AddUnique(RangedAIList, combatAI);
        }

        public void RemoveCombatAI(CombatAIController combatAI)
        {
            CombatAILookup.Remove(combatAI.Fighter);

            // Remove from every list the AI may belong to — a ranged AI can also be in the melee list
            // when it was registered with registerInMeleeList. Remove is a no-op if it isn't present.
            MeleeAIList.Remove(combatAI);
            RangedAIList.Remove(combatAI);
        }

        static void AddUnique(List<CombatAIController> list, CombatAIController combatAI)
        {
            if (!list.Contains(combatAI))
                list.Add(combatAI);
        }
    }
}
