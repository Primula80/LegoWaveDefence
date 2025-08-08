using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyController : MonoBehaviour, IHealth
{
    public float maxHealth = 50f;
    public int currencyValue = 10;

    private float currentHealth;
    private NavMeshAgent agent;

    void OnEnable() // Use OnEnable for object pooling
    {
        currentHealth = maxHealth;
        agent = GetComponent<NavMeshAgent>();

        if (CombatManager.Instance != null && CombatManager.Instance.enemyDestination != null)
        {
            agent.SetDestination(CombatManager.Instance.enemyDestination.position);
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        CurrencyManager.Instance.AddCurrency(currencyValue);
        CombatManager.Instance.OnEnemyDefeated();

        // TODO: Replace Destroy with gameObject.SetActive(false) for object pooling
        Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        // Check if the enemy reached the destination
        if (other.transform == CombatManager.Instance.enemyDestination)
        {
            CombatManager.Instance.OnEnemyReachedEnd();
            Destroy(gameObject);
        }
    }
}