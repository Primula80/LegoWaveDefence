using UnityEngine;

[CreateAssetMenu(fileName = "NewZoneDef", menuName = "Brick Defender/Zone Definition")]
public class ZoneDefinition : ScriptableObject
{
    public string zoneID;
    public Vector3 buildCameraPosition;
    public Quaternion buildCameraRotation;
    // You can define specific grid bounds here if needed
}