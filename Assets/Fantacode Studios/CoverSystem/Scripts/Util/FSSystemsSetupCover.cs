#if UNITY_EDITOR
using FS_CoverSystem;
using UnityEngine;

namespace FS_Core
{
    public partial class FSSystemsSetup
    {
        // Cover is a shared module — Shooter and Stealth both pull it in. The setup steps live
        // here so every host system wires cover the same way instead of each keeping its own copy.
        public const string CoverMobileControllerPrefab = "Cover Mobile Controller";

        // CoverController only probes the "Cover" layer, so the demo cover props have to be on it.
        static void SetCoverLayerToCoverPrefab()
        {
            var coverPrefab = Resources.Load<GameObject>("Cover Barrier");
            var coverCollider = Resources.Load<GameObject>("Cover Collider");

            if (coverPrefab != null)
            {
                foreach (Transform coverCube in coverPrefab.transform)
                {
                    if (coverCube.gameObject.layer != LayerMask.NameToLayer("Cover"))
                        coverCube.gameObject.layer = LayerMask.NameToLayer("Cover");
                }
            }

            if (coverCollider != null && coverCollider.gameObject.layer != LayerMask.NameToLayer("Cover"))
                coverCollider.gameObject.layer = LayerMask.NameToLayer("Cover");
        }

        // Connects the on-screen cover button of the mobile controller to the created character.
        static void SetupCoverInputManager(GameObject playerObject, GameObject createdPlayerParentObject)
        {
            var mc = createdPlayerParentObject.transform.Find($"Game UI/{CoverMobileControllerPrefab}");
            if (mc == null) return;

            var coverUI = mc.GetComponent<CoverUI>();
            var coverInputManager = playerObject.GetComponent<CoverInputManager>();
            if (coverInputManager != null && coverUI != null)
                coverInputManager.SetCoverUI(coverUI);
        }
    }
}
#endif
