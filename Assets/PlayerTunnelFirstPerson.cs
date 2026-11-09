
using UnityEngine;
using FS_Core;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class PlayerTunnelFirstPerson : MonoBehaviour
{
    [Header("Tunnel Detection")]
    [Tooltip("Layer that activates first-person mode.")]
    [Range(0, 31)]
    public int tunnelLayer = 22;

    [Tooltip("Optional layer name. If it exists, it overrides the layer number.")]
    public string tunnelLayerName = "Tunnel";

    [Header("Camera")]
    [Tooltip("Switch back to third person when leaving the tunnel.")]
    public bool returnToThirdPersonOnExit = true;

    [Header("Debug")]
    public bool showDebugLogs = false;

    private PlayerController player;
    private int tunnelOverlapCount;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    private bool IsTunnel(Collider other)
    {
        if (other == null)
            return false;

        int layer = tunnelLayer;

        if (!string.IsNullOrEmpty(tunnelLayerName))
        {
            int namedLayer =
                LayerMask.NameToLayer(tunnelLayerName);

            if (namedLayer >= 0)
                layer = namedLayer;
        }

        return other.gameObject.layer == layer;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsTunnel(other))
            return;

        tunnelOverlapCount++;

        if (showDebugLogs)
            Debug.Log(
                "[Tunnel] Entered tunnel: " + other.name,
                this
            );

        SetFirstPerson(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTunnel(other))
            return;

        tunnelOverlapCount =
            Mathf.Max(0, tunnelOverlapCount - 1);

        if (returnToThirdPersonOnExit &&
            tunnelOverlapCount == 0)
        {
            SetFirstPerson(false);
        }
    }

    private void SetFirstPerson(bool firstPerson)
    {
        if (player == null)
            return;

        FSCameraType targetType = firstPerson
            ? FSCameraType.FirstPerson
            : FSCameraType.ThirdPerson;

        if (player.CameraType == targetType)
            return;

        player.CameraType = targetType;

        // Notify Fantacode's camera listeners.
        player.OnCameraTypeChanged?.Invoke(targetType);

        if (showDebugLogs)
        {
            Debug.Log(
                "[Tunnel] Camera changed to " + targetType,
                this
            );
        }
    }
}
