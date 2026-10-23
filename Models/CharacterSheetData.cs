using System.Text.Json;
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

    /// <summary>
    /// The farmer's birthday season, added by AliveNpcs 1.7.3. The editor doesn't offer a field for
    /// it, but it must round-trip: <see cref="FarmerStore"/> writes this whole object over
    /// character_sheet.json on the API fallback path, so a missing property would silently erase a
    /// birthday the player set through the in-game sheet.
    /// </summary>
    [JsonPropertyName("birthdaySeason")]
    public string BirthdaySeason { get; set; } = "";

    /// <summary>The farmer's birthday day (1-28), or 0 when unset. Preserved, not edited.</summary>
    [JsonPropertyName("birthdayDay")]
    public int BirthdayDay { get; set; }

    [JsonPropertyName("hasBeenFilled")]
    public bool HasBeenFilled { get; set; }

    [JsonPropertyName("lastEdited")]
    public string LastEdited { get; set; } = "";

    /// <summary>
    /// Any property AliveNpcs writes that this mirror doesn't model yet. Captured on read and
    /// written back on save, so the editor can never silently drop a field the base mod adds
    /// between releases — this file belongs to AliveNpcs, the editor only edits four of its values.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }

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
