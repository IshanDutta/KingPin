using UnityEngine;

// Owns a mesh created at runtime; it must not destroy imported asset meshes.
public class BossGeneratedFragmentMesh : MonoBehaviour
{
    public Mesh RuntimeMesh { get; set; }
    private void OnDestroy()
    {
        if (RuntimeMesh != null) Destroy(RuntimeMesh);
    }
}
