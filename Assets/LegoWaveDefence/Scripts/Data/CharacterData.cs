using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Brick Defender/Character Data")]
public class CharacterData : ScriptableObject, IPlaceable
{
    [Header("Placement Info")]
    [SerializeField] private string _id = "char_";
    [SerializeField] private int _cost = 50;
    [SerializeField] private GameObject _prefab;

    // Interface Implementation
    public string ID => _id;
    public int Cost => _cost;
    public GameObject Prefab => _prefab;

    [Header("Combat Stats")]
    public float health = 100f;
    public float attackDamage = 15f;
    public float attackRange = 8f;
    public float attackSpeed = 1f; // Attacks per second
}