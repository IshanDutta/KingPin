using FS_Core;
using UnityEngine;

namespace FS_CombatCore
{
    public class CombatUI : FSInputUIBase
    {
        [Header("UI Buttons")]
        [SerializeField] UIImageInteraction dodgeUIButton;
        [SerializeField] UIImageInteraction rollUIButton;
        [SerializeField] UIImageInteraction equipUIButton;
        [SerializeField] UIImageInteraction unEquipUIButton;

        public UIImageInteraction DodgeUIButton => dodgeUIButton;
        public UIImageInteraction RollUIButton => rollUIButton;
        public UIImageInteraction EquipUIButton => equipUIButton;
        public UIImageInteraction UnEquipUIButton => unEquipUIButton;

        public override void CacheButtons()
        {
            dodgeUIButton ??= FindButtonInContainer("Combat Mobile Controller", "Dodge");
            rollUIButton ??= FindButtonInContainer("Combat Mobile Controller", "Roll");
            equipUIButton ??= FindButtonInContainer("Combat Mobile Controller", "Equip");
            unEquipUIButton ??= FindButtonInContainer("Combat Mobile Controller", "Unequip", "UnEquip", "Un Equip");
        }
    }
}