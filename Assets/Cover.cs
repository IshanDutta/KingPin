
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Cover : MonoBehaviour
{
    [Header("Cover Health")]
    [SerializeField, Min(1f)]
    private float maxHealth = 100f;

    [SerializeField]
    private float currentHealth;

    [Header("Runtime Fracture")]
    [SerializeField, Range(2, 10)]
    private int fragmentsPerAxis = 3;

    [SerializeField, Min(0f)]
    private float explosionForce = 5f;

    [SerializeField, Min(0.01f)]
    private float explosionRadius = 3f;

    [SerializeField, Min(0f)]
    private float upwardModifier = 0.5f;

    [SerializeField, Min(0f)]
    private float fragmentLifetime = 10f;

    [SerializeField]
    private bool inheritMaterial = true;

    private bool destroyed;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDestroyed => destroyed;

    private void Awake()
    {
        currentHealth = maxHealth;
        destroyed = false;
    }

    public void TakeDamage(float damage)
    {
        if (destroyed || damage <= 0f)
            return;

        currentHealth = Mathf.Max(
            0f,
            currentHealth - damage
        );

        if (currentHealth <= 0f)
        {
            Fracture();
        }
    }

    private void Fracture()
    {
        if (destroyed)
            return;

        destroyed = true;

        Collider originalCollider =
            GetComponent<Collider>();

        Bounds bounds = originalCollider.bounds;

        Renderer originalRenderer =
            GetComponentInChildren<Renderer>();

        Material material = null;

        if (inheritMaterial && originalRenderer != null)
        {
            material = originalRenderer.sharedMaterial;
        }

        int count = fragmentsPerAxis;

        Vector3 fragmentSize = new Vector3(
            bounds.size.x / count,
            bounds.size.y / count,
            bounds.size.z / count
        );

        Vector3 startPosition =
            bounds.min + fragmentSize * 0.5f;

        Vector3 explosionCenter = bounds.center;

        // Generate fragments entirely in code.
        for (int x = 0; x < count; x++)
        {
            for (int y = 0; y < count; y++)
            {
                for (int z = 0; z < count; z++)
                {
                    Vector3 position =
                        startPosition +
                        new Vector3(
                            x * fragmentSize.x,
                            y * fragmentSize.y,
                            z * fragmentSize.z
                        );

                    CreateFragment(
                        position,
                        fragmentSize,
                        material,
                        explosionCenter
                    );
                }
            }
        }

        Debug.Log(
            name + " fractured into " +
            (count * count * count) +
            " fragments.",
            this
        );

        Destroy(gameObject);
    }

    private void CreateFragment(
        Vector3 position,
        Vector3 size,
        Material material,
        Vector3 explosionCenter
    )
    {
        // Create a new cube fragment.
        GameObject fragment =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        fragment.name = "Cover_Fragment";

        fragment.transform.position = position;
        fragment.transform.localScale = size;

        // Copy original material.
        Renderer fragmentRenderer =
            fragment.GetComponent<Renderer>();

        if (material != null)
        {
            fragmentRenderer.sharedMaterial = material;
        }

        // Add physics.
        Rigidbody rb =
            fragment.AddComponent<Rigidbody>();

        rb.mass = Mathf.Max(
            0.05f,
            size.x * size.y * size.z
        );

        rb.useGravity = true;

        rb.AddExplosionForce(
            explosionForce,
            explosionCenter,
            explosionRadius,
            upwardModifier,
            ForceMode.Impulse
        );

        // Random spin.
        rb.AddTorque(
            Random.insideUnitSphere *
            explosionForce,
            ForceMode.Impulse
        );

        if (fragmentLifetime > 0f)
        {
            Destroy(
                fragment,
                fragmentLifetime
            );
        }
    }
}
