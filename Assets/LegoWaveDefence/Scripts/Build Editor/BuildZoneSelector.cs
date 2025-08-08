using UnityEngine;

public class BuildZoneSelector : MonoBehaviour
{
    public ZoneDefinition zoneDefinition;
    public Transform cameraFocusPoint; // The transform the camera will move to

    void OnMouseDown()
    {
        if (GameManager.Instance.CurrentState == GameManager.GameState.BuildPhase &&
            !BuildEditorController.Instance.IsInBuildMode)
        {
            Debug.Log($"Entering build mode for zone: {zoneDefinition.zoneID}");
            BuildEditorController.Instance.EnterBuildMode(this);
        }
    }
}