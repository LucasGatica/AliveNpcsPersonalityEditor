using System.Text.Json.Serialization;

namespace AliveNpcsPersonalityEditor.Models;

/// <summary>
/// Mirror of AliveNpcs.Models.CharacterSheet, used to read/write the
/// character_sheet.json produced by the base mod's CharacterSheetManager.
/// Kept in this assembly to avoid a hard reference to the base mod.
/// </summary>
public sealed class CharacterSheetData
{
    [JsonPropertyName("whoAmI")]
    public string WhoAmI { get; set; } = "";

    [JsonPropertyName("whyMovedHere")]
    public string WhyMovedHere { get; set; } = "";

    [JsonPropertyName("extraInfo")]
    public string ExtraInfo { get; set; } = "";

    [JsonPropertyName("atAGlanceDetails")]
    public string AtAGlanceDetails { get; set; } = "";

    [JsonPropertyName("hasBeenFilled")]
    public bool HasBeenFilled { get; set; }

    [JsonPropertyName("lastEdited")]
    public string LastEdited { get; set; } = "";

    [JsonIgnore]
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(WhoAmI) ||
        !string.IsNullOrWhiteSpace(WhyMovedHere) ||
        !string.IsNullOrWhiteSpace(ExtraInfo) ||
        !string.IsNullOrWhiteSpace(AtAGlanceDetails);
}

/// <summary>
/// Envelope matching SaveDataStore.SaveEnvelope<T> shape so that files written
/// by the base mod (schema_version + data) round-trip cleanly here.
/// </summary>
internal sealed class SaveEnvelope<T>
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}
