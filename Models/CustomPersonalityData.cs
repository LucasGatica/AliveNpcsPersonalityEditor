using System.Text.Json.Serialization;

namespace AliveNpcsPersonalityEditor.Models;

/// <summary>Structured override data for a single NPC, persisted as one JSON file in overrides/.</summary>
public sealed class NpcOverrideEntry
{
    public string NpcName { get; set; } = "";

    public string CanonicalPersonality { get; set; } = "";

    public string Appearance { get; set; } = "";

    public string Lore { get; set; } = "";

    public string SocialTags { get; set; } = "";

    public string SubmissionCredit { get; set; } = "";

    /// <summary>Preset kind: "" / "npc" for NPC personalities, "farmer" for the player backstory sheet.</summary>
    public string PresetType { get; set; } = "";

    public CharacterDataOverride? CharacterData { get; set; }

    /// <summary>True when this preset represents the farmer/player character sheet rather than an NPC.</summary>
    [JsonIgnore]
    public bool IsFarmer =>
        string.Equals(PresetType, "farmer", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(NpcName, "Farmer", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool HasAnyField =>
        !string.IsNullOrWhiteSpace(CanonicalPersonality)
        || !string.IsNullOrWhiteSpace(Appearance)
        || !string.IsNullOrWhiteSpace(Lore)
        || !string.IsNullOrWhiteSpace(SocialTags)
        || !string.IsNullOrWhiteSpace(SubmissionCredit)
        || CharacterData?.HasAnyField == true;

    [JsonIgnore]
    public bool HasOnlySupplementaryFields =>
        string.IsNullOrWhiteSpace(CanonicalPersonality)
        && (!string.IsNullOrWhiteSpace(Appearance)
            || !string.IsNullOrWhiteSpace(Lore)
            || !string.IsNullOrWhiteSpace(SocialTags)
            || !string.IsNullOrWhiteSpace(SubmissionCredit));

    [JsonIgnore]
    public bool HasCharacterDataOverride => CharacterData?.HasAnyField == true;
}

/// <summary>Overrides for Stardew Valley base game CharacterData fields.</summary>
public sealed class CharacterDataOverride
{
    public string? DisplayName { get; set; }
    public int? Gender { get; set; }
    public int? Age { get; set; }
    public int? Manner { get; set; }
    public int? SocialAnxiety { get; set; }
    public int? Optimism { get; set; }
    public string? BirthSeason { get; set; }
    public int? BirthDay { get; set; }
    public bool? CanSocialize { get; set; }
    public bool? CanReceiveGifts { get; set; }
    public bool? CanBeRomanced { get; set; }

    [JsonIgnore]
    public bool HasAnyField =>
        !string.IsNullOrWhiteSpace(DisplayName)
        || Gender.HasValue || Age.HasValue || Manner.HasValue
        || SocialAnxiety.HasValue || Optimism.HasValue
        || !string.IsNullOrWhiteSpace(BirthSeason) || BirthDay.HasValue
        || CanSocialize.HasValue || CanReceiveGifts.HasValue || CanBeRomanced.HasValue;
}
