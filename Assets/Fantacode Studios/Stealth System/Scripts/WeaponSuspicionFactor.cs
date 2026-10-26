using FS_Core;
using UnityEngine;

namespace FS_StealthSystem
{
    public class WeaponSuspicionFactor : MonoBehaviour, ISuspicionFactor
    {
        SuspicionProfile profile;
        ItemEquipper itemEquipper;
        bool visibleWeaponEquipped;

        public bool IsActive => visibleWeaponEquipped;
        public bool IsInstant => true;
        public bool WarnsFirst => false;

        void Awake()
        {
            profile = GetComponent<SuspicionProfile>();
            itemEquipper = GetComponent<ItemEquipper>();
        }

        void OnEnable()
        {
            if (itemEquipper != null)
            {
                itemEquipper.OnEquip += OnEquip;
                itemEquipper.OnUnEquipComplete += OnUnEquipComplete;
            }

            if (profile != null)
                profile.AddFactor(this);

            UpdateState();
        }

        void OnDisable()
        {
            if (itemEquipper != null)
            {
                itemEquipper.OnEquip -= OnEquip;
                itemEquipper.OnUnEquipComplete -= OnUnEquipComplete;
            }

            if (profile != null)
                profile.RemoveFactor(this);
        }

        void OnEquip(EquippableItem item)
        {
            UpdateState();
        }

        void OnUnEquipComplete()
        {
            visibleWeaponEquipped = false;
        }

        void UpdateState()
        {
            visibleWeaponEquipped = itemEquipper != null
                && itemEquipper.EquippedItem != null
                && itemEquipper.EquippedItem.IsVisbleWhenEquipped();
        }
    }
}
