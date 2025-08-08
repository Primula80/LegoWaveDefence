using UnityEngine;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance { get; private set; }
    public int CurrentCurrency { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void Initialize(int startingCurrency)
    {
        CurrentCurrency = startingCurrency;
    }

    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        CurrentCurrency += amount;
        BuildPhaseView.Instance.UpdateCurrency(CurrentCurrency);
        Debug.Log($"Added {amount}. New balance: {CurrentCurrency}");
    }

    public bool SpendCurrency(int amount)
    {
        if (amount > 0 && CurrentCurrency >= amount)
        {
            CurrentCurrency -= amount;
            BuildPhaseView.Instance.UpdateCurrency(CurrentCurrency);
            Debug.Log($"Spent {amount}. New balance: {CurrentCurrency}");
            return true;
        }
        Debug.LogWarning("Not enough currency or invalid amount!");
        return false;
    }
}