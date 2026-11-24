using FS_Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FS_ShooterSystem
{
    public class ShooterUI : FSInputUIBase, IThrowableHUD
    {
        [Header("Ammo UI")]
        public TextMeshProUGUI totalAmmoCount;
        public Image infiniteImage;
        public TextMeshProUGUI loadedAmmoCount;

        [Header("Weapon UI")]
        public Image currentWeaponImage;
        public GameObject weaponUI;
        public ShooterCrosshairController crosshairController;

        [Header("UI Buttons")]
        [SerializeField] UIImageInteraction fireUIButton;
        [SerializeField] UIImageInteraction aimUIButton;
        [SerializeField] UIImageInteraction scopeUIButton;
        [SerializeField] UIImageInteraction reloadUIButton;

        public UIImageInteraction FireUIButton => fireUIButton;
        public UIImageInteraction AimUIButton => aimUIButton;
        public UIImageInteraction ScopeUIButton => scopeUIButton;
        public UIImageInteraction ReloadUIButton => reloadUIButton;

        [Header("Throwable UI")]
        [Tooltip("Optional parent GameObject for the throwable HUD. Activated while a throwable is being aimed and deactivated when aim ends.")]
        [SerializeField] GameObject throwableUI;
        [Tooltip("Optional Image showing the throwable item's icon while a fuse-on-aim throwable is cooking. The Image is enabled while the fuse is active.")]
        [SerializeField] Image fuseItemImage;
        [Tooltip("Optional Image whose fillAmount tracks the fuse countdown (0..1).")]
        [SerializeField] Image fuseFillImage;
        [Tooltip("Optional text showing the remaining fuse time in seconds.")]
        [SerializeField] TextMeshProUGUI fuseTimerText;

        public GameObject ThrowableUI => throwableUI;
        public Image FuseItemImage => fuseItemImage;
        public Image FuseFillImage => fuseFillImage;
        public TextMeshProUGUI FuseTimerText => fuseTimerText;

        public static ShooterUI i;

        protected override void Awake()
        {
            base.Awake();
            i = this;
        }

        public override void CacheButtons()
        {
            fireUIButton ??= FindButtonInContainer("Shooter Mobile Controller", "Fire");
            aimUIButton ??= FindButtonInContainer("Shooter Mobile Controller", "Aim");
            scopeUIButton ??= FindButtonInContainer("Shooter Mobile Controller", "Scope");
            reloadUIButton ??= FindButtonInContainer("Shooter Mobile Controller", "Reload");
        }

        #region Ammo & Weapon UI

        public void UpdateShooterUI(ShooterWeaponObject weapon)
        {
            if (weapon != null)
            {
                loadedAmmoCount.text = weapon.CurrentAmmoCount.ToString();
                if (weapon.ParentShooterFighter.hasInfiniteAmmo)
                {
                    infiniteImage.gameObject.SetActive(true);
                    totalAmmoCount.gameObject.SetActive(false);
                }
                else
                {
                    infiniteImage.gameObject.SetActive(false);
                    totalAmmoCount.gameObject.SetActive(true);
                    totalAmmoCount.text = weapon.TotalAmmoCount.ToString();
                }
            }
        }

        #endregion

        #region Crosshair

        public void SetCrosshairActive(bool showCrosshairLines, bool showEntireCrosshair = true)
        {
            if (crosshairController != null)
            {
                crosshairController.HandleCrosshairVisiblity(showCrosshairLines, showEntireCrosshair);
            }
        }

        #endregion
    }
}
