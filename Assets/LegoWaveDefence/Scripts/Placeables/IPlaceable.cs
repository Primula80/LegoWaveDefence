using UnityEngine;

public interface IPlaceable
{
    string ID { get; }
    int Cost { get; }
    GameObject Prefab { get; }
}