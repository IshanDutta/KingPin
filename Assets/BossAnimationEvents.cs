    
using UnityEngine;

public class BossAnimationEvents : MonoBehaviour
{
    private BossEnemyAI boss;

    private void Awake()
    {
        boss = GetComponentInParent<BossEnemyAI>();

        if (boss == null)
        {
            Debug.LogError(
                "BossAnimationEvents could not find BossEnemyAI.",
                this
            );
        }
    }

    // Called by the attack animation event.
    public void DamageCover()
    {
        if (boss != null)
        {
            boss.DamageCover();
        }
    }
}
