using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_CombatCore
{

    public class CombatAILookup
    {
        static Dictionary<FighterCore, CombatAIController> combatAILookup = new Dictionary<FighterCore, CombatAIController>();

        public static void Init()
        {
            combatAILookup = new Dictionary<FighterCore, CombatAIController>();
        }

        public static void Register(CombatAIController combatAI, FighterCore fighter = null)
        {
            if (fighter == null)
                fighter = combatAI.GetComponent<FighterCore>();

            if (!combatAILookup.ContainsKey(fighter))
            {
                combatAILookup.Add(fighter, combatAI);
            }
        }

        public static CombatAIController Get(FighterCore fighter)
        {
            if (combatAILookup.ContainsKey(fighter))
                return combatAILookup[fighter];
            else
            {
                var combatAI = fighter.GetComponent<CombatAIController>();
                Register(combatAI, fighter);
                return combatAI;
            }
        }

        public static void Remove(FighterCore fighter)
        {
            if (combatAILookup.ContainsKey(fighter))
                combatAILookup.Remove(fighter);
        }
    }
}
