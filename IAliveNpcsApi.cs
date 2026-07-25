namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Public API exposed by AliveNpcs for inter-mod communication.
/// The editor uses this to fetch default personality data and register overrides.
/// </summary>
public interface IAliveNpcsApi
{
    /// <summary>Get the default personality for an NPC, without a saved editor override.</summary>
    string GetDefaultPersonality(string npcName);

    /// <summary>Get all vanilla NPC names that have defined personalities.</summary>
    IEnumerable<string> GetVanillaNpcNames();

    /// <summary>Get all SVE NPC names that have defined personalities.</summary>
    IEnumerable<string> GetSveNpcNames();

    /// <summary>
    /// Get NPCs that the current AliveNpcs save allows the editor to customize.
    /// This respects compatibility settings and community AI opt-out rules.
    /// </summary>
    IEnumerable<string> GetEditableNpcNames();

    /// <summary>Check whether an NPC is explicitly disabled from all AliveNpcs interactions.</summary>
    bool IsNpcDisabled(string npcName);

    /// <summary>Persistently enable or disable an NPC across all AliveNpcs AI-driven systems.</summary>
    bool SetNpcDisabled(string npcName, bool disabled);

    /// <summary>Check if a personality override is active for this NPC.</summary>
    bool HasCustomPersonality(string npcName);

    /// <summary>Register this editor instance's overrides directory with AliveNpcs.</summary>
    bool RegisterCustomPersonalityDirectory(string dirPath);

    /// <summary>Reload custom personalities from disk (called after editor saves).</summary>
    void ReloadCustomPersonalities();

    /// <summary>Reload the player character sheet from disk (called after the Farmer tab saves).</summary>
    void ReloadCharacterSheet();

    /// <summary>Persist the character sheet through AliveNpcs itself (mirrors the in-game F7 menu, path-independent).</summary>
    void UpdateCharacterSheet(string whoAmI, string whyMovedHere, string extraInfo, string atAGlanceDetails);

    /// <summary>Get the current sheet fields [whoAmI, whyMovedHere, extraInfo, atAGlanceDetails], or null.</summary>
    string[]? GetCharacterSheet();

    /// <summary>Get effective default CharacterData [gender, age, manner, socialAnxiety, optimism, canSocialize, canBeRomanced], or null.</summary>
    int[]? GetBaseCharacterData(string npcName);

    /// <summary>Get the effective default DisplayName (a mod's detected change, else original), or null.</summary>
    string? GetBaseDisplayName(string npcName);

    /// <summary>Enable/disable injecting structured CharacterData into the AI prompt (experimental, off by default).</summary>
    void SetCharacterDataPromptEnabled(bool enabled);
}
