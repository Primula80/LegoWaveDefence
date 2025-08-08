// BlockController.cs
using UnityEngine;

public class BlockController : MonoBehaviour, IHealth
{
    public BlockData blockData;
    private float currentHealth;

    void Start()
    {
        if (blockData != null)
        {
            currentHealth = blockData.health;
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            Destroy(gameObject); // Block is destroyed
        }
    }
}