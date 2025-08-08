using UnityEngine;

public class AssetUnlockManager : MonoBehaviour
{
    public static AssetUnlockManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public void UnlockAssetsFromWave(WaveDefinition waveDef)
    {
        var profile = PersistenceManager.Instance.PlayerProfile;

        foreach (var character in waveDef.unlockedCharacters)
        {
            if (!profile.unlockedCharacterIDs.Contains(character.ID))
            {
                profile.unlockedCharacterIDs.Add(character.ID);
                Debug.Log($"🔓 Unlocked new character: {character.ID}");
            }
        }

        foreach (var block in waveDef.unlockedBlocks)
        {
            if (!profile.unlockedBlockIDs.Contains(block.ID))
            {
                profile.unlockedBlockIDs.Add(block.ID);
                Debug.Log($"🔓 Unlocked new block: {block.ID}");
            }
        }
    }
}