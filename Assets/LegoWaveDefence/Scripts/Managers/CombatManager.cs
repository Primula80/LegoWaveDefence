using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Tooltip("The final destination for enemies")]
    public Transform enemyDestination;

    [SerializeField] private EnemySpawner enemySpawner;
    private int enemiesRemainingInWave;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void StartWave(WaveDefinition waveDef)
    {
        enemiesRemainingInWave = 0;
        foreach (var group in waveDef.enemyGroups)
        {
            enemiesRemainingInWave += group.count;
        }

        if (enemiesRemainingInWave > 0)
        {
            enemySpawner.StartSpawning(waveDef);
        }
        else
        {
            OnAllEnemiesDefeated();
        }
    }

    public void OnEnemyDefeated()
    {
        enemiesRemainingInWave--;
        if (enemiesRemainingInWave <= 0)
        {
            OnAllEnemiesDefeated();
        }
    }

    public void OnEnemyReachedEnd()
    {
        Debug.LogError("An enemy reached the base! GAME OVER.");
        GameManager.Instance.OnGameOver();
    }

    private void OnAllEnemiesDefeated()
    {
        Debug.Log("Wave Cleared!");
        GameManager.Instance.OnWaveCompleted();
    }
}