using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class BuildEditorController : MonoBehaviour
{
    public static BuildEditorController Instance { get; private set; }

    public Camera mainCamera;
    public LayerMask placementLayerMask;
    public float gridSize = 1.0f;
    public bool IsInBuildMode { get; private set; }

    private BuildZoneSelector activeZone;
    private IPlaceable selectedBuildable;
    private GameObject placementGhost;

    private Vector3 originalCameraPos;
    private Quaternion originalCameraRot;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    void Update()
    {
        if (!IsInBuildMode || selectedBuildable == null) return;

        HandlePlacementGhost();

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            PlaceItem();
        }
    }

    public void EnterBuildMode(BuildZoneSelector zone)
    {
        IsInBuildMode = true;
        activeZone = zone;

        // Save original camera state and move to build view
        originalCameraPos = mainCamera.transform.position;
        originalCameraRot = mainCamera.transform.rotation;
        StartCoroutine(MoveCameraToTarget(zone.cameraFocusPoint.position, zone.cameraFocusPoint.rotation));

        // TODO: Show build-specific UI
    }

    public void ExitBuildMode()
    {
        IsInBuildMode = false;
        if (placementGhost != null) Destroy(placementGhost);

        StartCoroutine(MoveCameraToTarget(originalCameraPos, originalCameraRot));
        selectedBuildable = null;
        activeZone = null;

        // Transition to the siege phase
        GameManager.Instance.StartSiegePhase();
    }

    public void SelectBuildable(IPlaceable buildable)
    {
        selectedBuildable = buildable;
        Debug.Log($"Selected {buildable.ID} for placement.");

        if (placementGhost != null) Destroy(placementGhost);
        placementGhost = Instantiate(buildable.Prefab);
        // TODO: Apply a transparent/ghost material to the placementGhost
    }

    private void HandlePlacementGhost()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, placementLayerMask))
        {
            placementGhost.SetActive(true);
            placementGhost.transform.position = SnapToGrid(hit.point);
            placementGhost.transform.rotation = Quaternion.identity;
        }
        else
        {
            placementGhost.SetActive(false);
        }
    }

    private void PlaceItem()
    {
        if (placementGhost.activeSelf && CurrencyManager.Instance.SpendCurrency(selectedBuildable.Cost))
        {
            Instantiate(selectedBuildable.Prefab, placementGhost.transform.position, placementGhost.transform.rotation, activeZone.transform);
            Debug.Log($"Placed {selectedBuildable.ID}");
        }
    }

    private Vector3 SnapToGrid(Vector3 position)
    {
        return new Vector3(
            Mathf.Round(position.x / gridSize) * gridSize,
            position.y,
            Mathf.Round(position.z / gridSize) * gridSize
        );
    }

    private IEnumerator MoveCameraToTarget(Vector3 position, Quaternion rotation)
    {
        float duration = 0.5f;
        float time = 0;
        Vector3 startPos = mainCamera.transform.position;
        Quaternion startRot = mainCamera.transform.rotation;

        while (time < duration)
        {
            float t = time / duration;
            t = t * t * (3f - 2f * t); // Smoothstep interpolation
            mainCamera.transform.position = Vector3.Lerp(startPos, position, t);
            mainCamera.transform.rotation = Quaternion.Slerp(startRot, rotation, t);
            time += Time.deltaTime;
            yield return null;
        }
        mainCamera.transform.position = position;
        mainCamera.transform.rotation = rotation;
    }
}