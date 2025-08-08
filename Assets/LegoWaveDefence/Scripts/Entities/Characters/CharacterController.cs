using UnityEngine;

public class CharacterController : MonoBehaviour, IHealth
{
    public CharacterData characterData;
    private float currentHealth;
    private Transform currentTarget;
    private float attackCooldownTimer = 0f;

    void Start()
    {
        currentHealth = characterData.health;
    }

    void Update()
    {
        if (currentTarget == null || !IsTargetInRange())
        {
            FindNewTarget();
        }

        if (currentTarget != null)
        {
            // Optional: Rotate towards target
            transform.LookAt(currentTarget);

            attackCooldownTimer -= Time.deltaTime;
            if (attackCooldownTimer <= 0f)
            {
                Attack();
            }
        }
    }

    void FindNewTarget()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, characterData.attackRange, LayerMask.GetMask("Enemy"));
        float closestDist = float.MaxValue;
        Transform closestEnemy = null;

        foreach (var hitCollider in hitColliders)
        {
            float dist = Vector3.Distance(transform.position, hitCollider.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestEnemy = hitCollider.transform;
            }
        }
        currentTarget = closestEnemy;
    }

    bool IsTargetInRange()
    {
        if (currentTarget == null) return false;
        return Vector3.Distance(transform.position, currentTarget.position) <= characterData.attackRange;
    }

    void Attack()
    {
        attackCooldownTimer = 1f / characterData.attackSpeed;

        // TODO: Animate attack, fire projectile, play sound, etc.
        IHealth enemyHealth = currentTarget.GetComponent<IHealth>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(characterData.attackDamage);
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            // This character has been defeated
            Destroy(gameObject);
        }
    }
}