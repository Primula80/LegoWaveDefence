using UnityEngine;
using System.IO;

public class PersistenceManager : MonoBehaviour
{
    public static PersistenceManager Instance { get; private set; }
    public PlayerProfile PlayerProfile { get; private set; }
    private string savePath;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            savePath = Path.Combine(Application.persistentDataPath, "playerProfile.json");
            PlayerProfile = new PlayerProfile();
        }
    }

    public void LoadProfile()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            PlayerProfile = JsonUtility.FromJson<PlayerProfile>(json);
            Debug.Log("Player profile loaded.");
        }
        else
        {
            Debug.Log("No saved profile found. Creating a new one.");
            PlayerProfile = new PlayerProfile();
        }
        CurrencyManager.Instance.Initialize(PlayerProfile.currency);
    }

    public void SaveProfile()
    {
        PlayerProfile.currency = CurrencyManager.Instance.CurrentCurrency;
        string json = JsonUtility.ToJson(PlayerProfile, true);
        File.WriteAllText(savePath, json);
        Debug.Log($"Player profile saved to {savePath}");
    }
}