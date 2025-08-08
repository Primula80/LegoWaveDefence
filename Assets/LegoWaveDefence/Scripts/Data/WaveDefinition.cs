using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct EnemyGroup
{
    public GameObject enemyPrefab;
    public int count;
    public float spawnInterval; // Time between each enemy in this group
}

[CreateAssetMenu(fileName = "NewWaveDef", menuName = "Brick Defender/Wave Definition")]
public class WaveDefinition : ScriptableObject
{
    [Header("Wave Configuration")]
    public List<EnemyGroup> enemyGroups;
    public float timeBetweenGroups = 5f;

    [Header("Rewards")]
    public int completionBonus = 100;
    public List<CharacterData> unlockedCharacters;
    public List<BlockData> unlockedBlocks;
}