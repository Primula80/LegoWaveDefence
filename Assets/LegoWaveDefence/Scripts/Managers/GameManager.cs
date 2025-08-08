using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { MainMenu, BuildPhase, SiegePhase, Paused, GameOver }
    public GameState CurrentState { get; private set; }

    public List<WaveDefinition> waves;
    private int currentWaveIndex = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    void Start()
    {
        // For now, we go straight into the game.
        // You would typically start with a MainMenu state.
        PersistenceManager.Instance.LoadProfile();
        StartBuildPhase();
    }

    public void StartBuildPhase()
    {
        CurrentState = GameState.BuildPhase;
        Debug.Log("--- BUILD PHASE STARTED ---");
        // Activate UI and controls for building
        BuildPhaseView.Instance.Show();
        SiegePhaseView.Instance.Hide();
    }

    public void StartSiegePhase()
    {
        CurrentState = GameState.SiegePhase;
        Debug.Log("--- SIEGE PHASE STARTED ---");
        // Deactivate build controls and start the enemy wave
        BuildPhaseView.Instance.Hide();
        SiegePhaseView.Instance.Show();

        if (currentWaveIndex < waves.Count)
        {
            CombatManager.Instance.StartWave(waves[currentWaveIndex]);
            SiegePhaseView.Instance.UpdateWaveInfo(currentWaveIndex + 1, waves.Count);
        }
        else
        {
            Debug.Log("🎉 ALL WAVES COMPLETED! YOU WIN! 🎉");
            // Handle game win logic
        }
    }

    public void OnWaveCompleted()
    {
        Debug.Log($"Wave {currentWaveIndex + 1} completed!");
        CurrencyManager.Instance.AddCurrency(waves[currentWaveIndex].completionBonus);
        AssetUnlockManager.Instance.UnlockAssetsFromWave(waves[currentWaveIndex]);
        currentWaveIndex++;
        PersistenceManager.Instance.SaveProfile(); // Save progress after each wave
        StartBuildPhase();
    }

    public void OnGameOver()
    {
        CurrentState = GameState.GameOver;
        Debug.Log("--- GAME OVER ---");
        // Show game over screen, etc.
    }
}