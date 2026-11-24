using FS_Core;
using UnityEngine;

namespace FS_CombatCore 
{ 
public class CombatInputManger : FSInputManagerBase
{
    [SerializeField] CombatUI combatInputUI;
    [SerializeField] FSInput dodgeInput = new FSInput(KeyCode.LeftAlt);
    [SerializeField] FSInput rollInput = new FSInput(KeyCode.Space);
    [SerializeField] FSInput equipInput = new FSInput(KeyCode.Keypad1);
    [SerializeField] FSInput unEquipInput = new FSInput(KeyCode.Keypad1);

    public bool Dodge { get; set; }
    public bool Roll { get; set; }
    public bool Equip { get; set; }
    public bool UnEquip { get; set; }

    public void SetCombatInputUI(CombatUI ui)
    {
        combatInputUI = ui;
        CacheUIInputs();
    }

    protected override void CacheUIInputs()
    {
        if (combatInputUI == null) return;

        combatInputUI.CacheButtons();

        dodgeInput.SetUIButton(combatInputUI.DodgeUIButton);
        rollInput.SetUIButton(combatInputUI.RollUIButton);
        equipInput.SetUIButton(combatInputUI.EquipUIButton);
        unEquipInput.SetUIButton(combatInputUI.UnEquipUIButton);
    }

    private void Update()
    {
        if (Time.deltaTime == 0) return;

        HandleDodge();
        HandleRoll();

        HandleEquip();
        HandleUnEquip();
    }

    void HandleDodge()
    {
#if inputsystem
        Dodge = ReadButtonDown(dodgeInput, () => input.Combat.Dodge.WasPressedThisFrame());
#else
        Dodge = ReadButtonDown(dodgeInput);
#endif
    }

    void HandleRoll()
    {
#if inputsystem
        Roll = ReadButtonDown(rollInput, () => input.Combat.Roll.WasPressedThisFrame());
#else
        Roll = ReadButtonDown(rollInput);
#endif
    }

    void HandleEquip()
    {
#if inputsystem
        Equip = ReadButtonDown(equipInput, () => input.Combat.Equip.WasPressedThisFrame());
#else
        Equip = ReadButtonDown(equipInput);
#endif
    }

    void HandleUnEquip()
    {
#if inputsystem
        UnEquip = ReadButtonDown(unEquipInput, () => input.Combat.UnEquip.WasPressedThisFrame());
#else
        UnEquip = ReadButtonDown(unEquipInput);
#endif
    }
}
}