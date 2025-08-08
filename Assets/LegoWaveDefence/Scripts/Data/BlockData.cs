using UnityEngine;

[CreateAssetMenu(fileName = "NewBlockData", menuName = "Brick Defender/Block Data")]
public class BlockData : ScriptableObject, IPlaceable
{
    [Header("Placement Info")]
    [SerializeField] private string _id = "block_";
    [SerializeField] private int _cost = 10;
    [SerializeField] private GameObject _prefab;

    // Interface Implementation
    public string ID => _id;
    public int Cost => _cost;
    public GameObject Prefab => _prefab;

    [Header("Block Stats")]
    public float health = 200f;
    // Add other block-specific properties here (e.g., isTrap, slowsEnemies)
}