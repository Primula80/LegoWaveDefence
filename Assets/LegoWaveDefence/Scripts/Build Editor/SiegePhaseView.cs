using UnityEngine;
using UnityEngine.UI;

public class SiegePhaseView : MonoBehaviour
{
    public static SiegePhaseView Instance { get; private set; }

    public GameObject viewContainer;
    public Text waveText;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void Show() => viewContainer.SetActive(true);
    public void Hide() => viewContainer.SetActive(false);

    public void UpdateWaveInfo(int currentWave, int totalWaves)
    {
        waveText.text = $"Wave: {currentWave} / {totalWaves}";
    }
}