using FS_Core;
using System;
using UnityEngine;

namespace FS_ShooterSystem
{
    public class ShooterInputManger : FSInputManagerBase
    {
        [SerializeField] ShooterUI shooterInputUI;
        [SerializeField] FSInput fireInput = new FSInput(KeyCode.Mouse0);
        [SerializeField] FSInput aimInput = new FSInput(KeyCode.Mouse1);
        [SerializeField] FSInput scopeInput = new FSInput(KeyCode.Mouse1);
        [SerializeField] FSInput reloadInput = new FSInput(KeyCode.R);

        [Space(10)]
        [SerializeField] bool useDoubleClickForScope = true;

        public bool Fire { get; set; }
        public bool FireDown { get; set; }
        public bool FireUp { get; set; }
        public bool Aim { get; set; }
        public bool AimDown { get; set; }
        public bool AimUp { get; set; }
        public bool Scope { get; set; }
        public bool Reload { get; set; }

        PlayerController playerController;

        public void SetShooterInputUI(ShooterUI ui)
        {
            shooterInputUI = ui;
            CacheUIInputs();
        }

        protected override void CacheUIInputs()
        {
            if (shooterInputUI == null) return;

            shooterInputUI.CacheButtons();

            fireInput.SetUIButton(shooterInputUI.FireUIButton);
            aimInput.SetUIButton(shooterInputUI.AimUIButton);
            scopeInput.SetUIButton(shooterInputUI.ScopeUIButton);
            reloadInput.SetUIButton(shooterInputUI.ReloadUIButton);
        }

        private void Start()
        {
            playerController = GetComponent<PlayerController>();
        }

        private void Update()
        {
            HandleAim();
            HandleFire();
            HandleReload();
        }

        void HandleFire()
        {
#if inputsystem
            Fire = ReadButton(fireInput, () => input.Shooter.Fire.inProgress);
            FireDown = ReadButtonDown(fireInput, () => input.Shooter.Fire.WasPerformedThisFrame());
            FireUp = ReadButtonUp(fireInput, () => input.Shooter.Fire.WasReleasedThisFrame());

            if (playerController.CameraType == FSCameraType.FirstPerson)
            {
                Scope = ReadButton(scopeInput, () => input.Shooter.Scope.inProgress);
            }
            else if (playerController.CameraType == FSCameraType.ThirdPerson)
            {
                if (CheckForMultiTap(input.Shooter.Scope.WasPressedThisFrame()) || (!useDoubleClickForScope && input.Shooter.Scope.WasPressedThisFrame()))
                    Scope = true;
                else if (input.Shooter.Scope.WasReleasedThisFrame())
                    Scope = false;
            }
#else
            Fire = ReadButton(fireInput);
            FireDown = ReadButtonDown(fireInput);
            FireUp = ReadButtonUp(fireInput);

            if (playerController.CameraType == FSCameraType.FirstPerson)
            {
                Scope = ReadButton(scopeInput);
            }
            else if (playerController.CameraType == FSCameraType.ThirdPerson)
            {
                var scopePressed = ReadButtonDown(scopeInput);
                var scopeReleased = ReadButtonUp(scopeInput);
                if (CheckForMultiTap(scopePressed))
                    Scope = true;
                else if (scopeReleased)
                    Scope = false;
            }
#endif
        }

        public event Action OnAimPressed;
        public event Action OnAimReleased;

        void HandleAim()
        {
#if inputsystem
            bool prevAim = Aim;
            Aim = ReadButton(aimInput, () => input.Shooter.Aim.inProgress);
            AimDown = ReadButtonDown(aimInput, () => input.Shooter.Aim.WasPerformedThisFrame());
            AimUp = ReadButtonUp(aimInput, () => input.Shooter.Aim.WasReleasedThisFrame());
#else
            bool prevAim = Aim;
            Aim = ReadButton(aimInput);
            AimDown = ReadButtonDown(aimInput);
            AimUp = ReadButtonUp(aimInput);
#endif

            if (!prevAim && Aim)
                OnAimPressed?.Invoke();
            if (prevAim && !Aim)
                OnAimReleased?.Invoke();
        }

        void HandleReload()
        {
#if inputsystem
            Reload = ReadButtonDown(reloadInput, () => input.Shooter.Reload.WasPerformedThisFrame());
#else
            Reload = ReadButtonDown(reloadInput);
#endif
        }

        private int requiredTapCount = 2;
        private float maxTimeBetweenTaps = 0.5f;
        private int currentTapCount = 0;
        private float lastTapTime = 0f;

        public bool CheckForMultiTap(bool inputPressed)
        {
            bool isMultiTapped = false;
            float currentTime = Time.time;

            if (inputPressed)
            {
                if (currentTime - lastTapTime <= maxTimeBetweenTaps)
                    currentTapCount++;
                else
                    currentTapCount = 1;

                lastTapTime = currentTime;

                if (currentTapCount == requiredTapCount)
                {
                    isMultiTapped = true;
                    currentTapCount = 0;
                }
            }

            return isMultiTapped;
        }
    }
}