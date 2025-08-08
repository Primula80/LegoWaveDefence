using UnityEngine;
using UnityEngine.UI;

public class BuildPhaseView : MonoBehaviour
{
    public static BuildPhaseView Instance { get; private set; }

    public GameObject viewContainer;
    public Text currencyText;
    public Button startWaveButton;
    // TODO: Add references to the UI panel for selecting blocks/characters

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
        startWaveButton.onClick.AddListener(BuildEditorController.Instance.ExitBuildMode);
    }

    public void Show() => viewContainer.SetActive(true);
    public void Hide() => viewContainer.SetActive(false);

    public void UpdateCurrency(int amount)
    {
        currencyText.text = $"💰 {amount}";
    }
}