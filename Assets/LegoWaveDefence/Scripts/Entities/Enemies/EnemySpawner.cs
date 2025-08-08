using UnityEngine;
using System.Collections;

public class EnemySpawner : MonoBehaviour
{
    public Transform spawnPoint;

    public void StartSpawning(WaveDefinition waveDef)
    {
        StartCoroutine(SpawnWaveCoroutine(waveDef));
    }

    IEnumerator SpawnWaveCoroutine(WaveDefinition wave)
    {
        foreach (var group in wave.enemyGroups)
        {
            for (int i = 0; i < group.count; i++)
            {
                // TODO: Replace Instantiate with an object pool for performance
                Instantiate(group.enemyPrefab, spawnPoint.position, spawnPoint.rotation);
                yield return new WaitForSeconds(group.spawnInterval);
            }
            yield return new WaitForSeconds(wave.timeBetweenGroups);
        }
    }
}