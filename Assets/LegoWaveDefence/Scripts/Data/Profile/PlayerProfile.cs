using System.Collections.Generic;

[System.Serializable]
public class PlayerProfile
{
    public int currency;
    public List<string> unlockedCharacterIDs;
    public List<string> unlockedBlockIDs;

    public PlayerProfile()
    {
        currency = 150; // Starting currency
        unlockedCharacterIDs = new List<string> { "default_character" }; // Example
        unlockedBlockIDs = new List<string> { "default_block" };       // Example
    }
}