using UnityEngine;
using UnityEngine.UI;

namespace FS_ShooterSystem
{
    public class ShooterCrosshairController : MonoBehaviour
    {
        public ShooterController shooterController;
        [Tooltip("If enabled, the crosshair will only be visible while aiming. Otherwise it show when any shooter weapon equipped")]
        public bool showCrosshairOnlyWhenAiming = false;
        public GameObject crosshairUIObject;

        [Header("Crosshair Lines")]
        public RectTransform topLine;
        public RectTransform bottomLine;
        public RectTransform leftLine;
        public RectTransform rightLine;
        public RectTransform centerDot;

        [Header("Diagonal Hit Lines")]
        public RectTransform topLeftHitLine;
        public RectTransform topRightHitLine;
        public RectTransform bottomLeftHitLine;
        public RectTransform bottomRightHitLine;

        [Header("Obstruction Hit Indicator")]
        public RectTransform ObstructionHitIndicator;

        [Tooltip("Distance of hit indicators from center when hit occurs.")]
        public float hitIndicatorDistance = 8;

        [Tooltip("Time in seconds the hit lines stay visible after a hit.")]
        public float hitIndicatorDuration = 0.2f;

        private float hitIndicatorTimer;

        [Header("Crosshair Behavior")]
        [Tooltip("Base spread (distance from center).")]
        public float baseSpread = 10;

        [Tooltip("Maximum spread when firing continuously.")]
        public float maxSpread = 30;

        [Tooltip("Spread increase per shot.")]
        public float spreadPerShot = 8f;

        [Tooltip("Spread increase when moving.")]
        public float moveSpread = 15f;

        [Tooltip("Rate at which spread returns to normal.")]
        public float recoverSpeed = 50;

        [Header("Colors")]
        [Tooltip("Default crosshair color when not aiming.")]
        public Color normalColor = Color.white;

        [Tooltip("Crosshair color when aiming without a target.")]
        public Color aimingColor = Color.white;

        [Tooltip("Crosshair color when aiming directly at a valid target.")]
        public Color aimOnTargetColor = Color.red;

        private float currentSpread;
        private bool isAiming;
        private Image[] crosshairLines;

        private Color currentAimingColor;  // It will dynamically change based on the aim point hit object

        void Start()
        {
            crosshairLines = new[] { topLine.GetComponent<Image>(), bottomLine.GetComponent<Image>(), leftLine.GetComponent<Image>(), rightLine.GetComponent<Image>(), centerDot.GetComponent<Image>() };

            currentSpread = baseSpread;
            SetCrosshairColor(normalColor);
            SetHitIndicatorsActive(false);
            
            if(shooterController == null)
                shooterController = FindAnyObjectByType<ShooterController>();

            shooterController.Shooter.OnStartAim += () => SetAiming(true);
            shooterController.Shooter.OnStopAim += () => SetAiming(false);
            shooterController.Shooter.OnFire += OnFire;
            shooterController.Shooter.OnHitDamagable += ShowHitIndicator;
            shooterController.Shooter.OnTargetLocked += () =>
            {
                currentAimingColor = aimOnTargetColor;
                SetCrosshairColor(currentAimingColor);
            };
            shooterController.Shooter.OnTargetCleared += () =>
            {
                SetCrosshairColor(isAiming ? aimingColor : normalColor);
            };

        }

        void Update()
        {
            float targetSpread = baseSpread;

            if (shooterController.IsFighterMoving())
                targetSpread += moveSpread;

            currentSpread = Mathf.MoveTowards(currentSpread, targetSpread, recoverSpeed * Time.deltaTime);
            UpdateCrosshairPosition();

            if (hitIndicatorTimer > 0)
            {
                hitIndicatorTimer -= Time.deltaTime;
                UpdateHitIndicatorPositions();
                if (hitIndicatorTimer <= 0)
                    SetHitIndicatorsActive(false);
            }
            if(shooterController.Shooter.IsAimPointObstructed)
            {
                SetObstructionHitIndicatorsActive(true);
                UpdateObstructionHitIndicatorPositions();
            }
            else
            {
                SetObstructionHitIndicatorsActive(false);
            }
        }

        public void OnFire()
        {
            currentSpread = Mathf.Min(currentSpread + spreadPerShot, maxSpread);
            UpdateCrosshairPosition();
        }

        public void SetAiming(bool aiming)
        {
            isAiming = aiming;
            SetCrosshairColor(isAiming ? aimingColor : normalColor);
        }

        public void ShowHitIndicator()
        {
            hitIndicatorTimer = hitIndicatorDuration;
            SetHitIndicatorsActive(true);
            UpdateHitIndicatorPositions();
        }

        private void UpdateCrosshairPosition()
        {
            topLine.anchoredPosition = Vector2.up * currentSpread;
            bottomLine.anchoredPosition = Vector2.down * currentSpread;
            leftLine.anchoredPosition = Vector2.left * currentSpread;
            rightLine.anchoredPosition = Vector2.right * currentSpread;
        }

        private void UpdateHitIndicatorPositions()
        {
            float t = hitIndicatorTimer / hitIndicatorDuration;
            float distance = Mathf.Lerp(0, hitIndicatorDistance, 1 - t);

            if (topLeftHitLine) topLeftHitLine.anchoredPosition = new Vector2(-distance, distance);
            if (topRightHitLine) topRightHitLine.anchoredPosition = new Vector2(distance, distance);
            if (bottomLeftHitLine) bottomLeftHitLine.anchoredPosition = new Vector2(-distance, -distance);
            if (bottomRightHitLine) bottomRightHitLine.anchoredPosition = new Vector2(distance, -distance);
        }

        public void SetCrosshairColor(Color color)
        {
            foreach (var img in crosshairLines)
            {
                if (img != null)
                    img.color = color;
            }
        }

        private void SetHitIndicatorsActive(bool active)
        {
            if (topLeftHitLine) topLeftHitLine.gameObject.SetActive(active);
            if (topRightHitLine) topRightHitLine.gameObject.SetActive(active);
            if (bottomLeftHitLine) bottomLeftHitLine.gameObject.SetActive(active);
            if (bottomRightHitLine) bottomRightHitLine.gameObject.SetActive(active);
        }
        private void SetObstructionHitIndicatorsActive(bool active)
        {
            if (ObstructionHitIndicator) ObstructionHitIndicator.gameObject.SetActive(active);
        }

        public void HandleCrosshairVisiblity(bool enableCrosshairLines = true, bool showEntireCrosshair = true)
        {
            topLine.gameObject.SetActive(enableCrosshairLines);
            bottomLine.gameObject.SetActive(enableCrosshairLines);
            leftLine.gameObject.SetActive(enableCrosshairLines);
            rightLine.gameObject.SetActive(enableCrosshairLines);
            //centerDot.gameObject.SetActive(enableCrosshairLines);

            crosshairUIObject.SetActive(showEntireCrosshair);
        }
        private void UpdateObstructionHitIndicatorPositions()
        {
            if (shooterController != null && shooterController.Shooter != null && shooterController.Shooter.IsAimPointObstructed)
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    return;
                }

                // Get the canvas component
                Canvas canvas = crosshairUIObject.GetComponentInParent<Canvas>();
                if (canvas == null)
                {
                    return;
                }

                RectTransform canvasRect = canvas.transform as RectTransform;
                if (canvasRect == null)
                {
                    return;
                }

                Vector3 worldPosition = shooterController.Shooter.BulletHitPoint;
                Vector2 localPoint = Vector2.zero;

                // Handle different canvas render modes
                switch (canvas.renderMode)
                {
                    case RenderMode.ScreenSpaceOverlay:
                        // For overlay mode, use the canvas camera (null) and convert directly
                        Vector3 screenPoint = cam.WorldToScreenPoint(worldPosition);

                        // Check if the point is behind the camera
                        if (screenPoint.z < 0)
                        {
                            // Point is behind camera, hide indicator or handle appropriately
                            if (ObstructionHitIndicator) ObstructionHitIndicator.gameObject.SetActive(false);
                            return;
                        }

                        bool success = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            canvasRect,
                            screenPoint,
                            null, // Use null for overlay canvas
                            out localPoint
                        );

                        if (!success)
                        {
                            return;
                        }
                        break;

                    case RenderMode.ScreenSpaceCamera:
                        // For camera mode, use the canvas camera
                        Camera canvasCamera = canvas.worldCamera ?? cam;
                        Vector3 screenPointCamera = cam.WorldToScreenPoint(worldPosition);

                        // Check if the point is behind the camera
                        if (screenPointCamera.z < 0)
                        {
                            if (ObstructionHitIndicator) ObstructionHitIndicator.gameObject.SetActive(false);
                            return;
                        }

                        bool successCamera = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            canvasRect,
                            screenPointCamera,
                            canvasCamera,
                            out localPoint
                        );

                        if (!successCamera)
                        {
                            return;
                        }
                        break;

                    case RenderMode.WorldSpace:
                        // For world space, we need to handle it differently
                        // Convert world position to canvas local position directly
                        Vector3 localPosition = canvasRect.InverseTransformPoint(worldPosition);
                        localPoint = new Vector2(localPosition.x, localPosition.y);
                        break;
                }

                // Apply the position to the obstruction indicator
                if (ObstructionHitIndicator != null)
                {
                    ObstructionHitIndicator.gameObject.SetActive(true);
                    ObstructionHitIndicator.anchoredPosition = localPoint;
                }
            }
            else
            {
                // Hide the indicator when not obstructed
                if (ObstructionHitIndicator != null)
                {
                    ObstructionHitIndicator.gameObject.SetActive(false);
                }
            }
        }

    }
}
