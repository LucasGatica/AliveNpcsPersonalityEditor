namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Public API exposed by AliveNpcs for inter-mod communication.
/// The editor uses this to fetch default personality data.
/// </summary>
public interface IAliveNpcsApi
{
    /// <summary>Get the default (hardcoded) personality for an NPC.</summary>
    string GetDefaultPersonality(string npcName);

    /// <summary>Get all vanilla NPC names that have defined personalities.</summary>
    IEnumerable<string> GetVanillaNpcNames();

    /// <summary>Get all SVE NPC names that have defined personalities.</summary>
    IEnumerable<string> GetSveNpcNames();

    /// <summary>Check if a personality override is active for this NPC.</summary>
    bool HasCustomPersonality(string npcName);

    /// <summary>Reload custom personalities from disk (called after editor saves).</summary>
    void ReloadCustomPersonalities();
}
