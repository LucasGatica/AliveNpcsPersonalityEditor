using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Persists the player's character sheet (backstory) into the same file the
/// base AliveNpcs mod's CharacterSheetManager reads: <modDir>/Data/<saveId>/character_sheet.json.
/// When the base mod directory can't be resolved, writes to the editor mod's
/// own directory instead so data is never lost.
/// </summary>
public sealed class FarmerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string FileName = "character_sheet.json";

    private readonly string _modDirectoryPath;
    private readonly IMonitor _monitor;
    private CharacterSheetData _sheet = new();

    public CharacterSheetData Sheet => _sheet;

    public FarmerStore(string modDirectoryPath, IMonitor monitor)
    {
        _modDirectoryPath = modDirectoryPath ?? "";
        _monitor = monitor;
    }

    public void Load()
    {
        try
        {
            var path = ResolvePath();
            if (!File.Exists(path))
            {
                _sheet = new CharacterSheetData();
                _monitor.Log("FarmerStore: no existing character sheet loaded.", LogLevel.Debug);
                return;
            }

            var json = File.ReadAllText(path);
            _sheet = Deserialize(json) ?? new CharacterSheetData();
            _monitor.Log($"FarmerStore: loaded sheet (filled: {_sheet.HasBeenFilled}).", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            _monitor.Log($"FarmerStore load failed: {ex.Message}", LogLevel.Warn);
            _sheet = new CharacterSheetData();
        }
    }

    public void Save(CharacterSheetData sheet)
    {
        sheet.HasBeenFilled = true;
        try
        {
            sheet.LastEdited = $"{Game1.currentSeason} {Game1.dayOfMonth}, Year {Game1.year}";
        }
        catch { /* best-effort timestamp */ }

        _sheet = sheet;
        try
        {
            var path = ResolvePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var envelope = new SaveEnvelope<CharacterSheetData>
            {
                SchemaVersion = 1,
                Data = sheet
            };
            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            _monitor.Log("FarmerStore: character sheet saved.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            _monitor.Log($"FarmerStore save failed: {ex.Message}", LogLevel.Error);
        }
    }

    private string ResolvePath()
    {
        var saveId = GetSaveId();
        return Path.Combine(_modDirectoryPath, "Data", saveId, FileName);
    }

    private static string GetSaveId()
    {
        try
        {
            var uniqueId = StardewValley.Game1.uniqueIDForThisGame;
            var farmerName = StardewValley.Game1.player?.Name ?? "Unknown";
            var safeName = string.Concat(farmerName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            return $"{safeName}_{uniqueId}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static CharacterSheetData? Deserialize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("schema_version", out _)
                && doc.RootElement.TryGetProperty("data", out var data))
            {
                return data.Deserialize<CharacterSheetData>(JsonOptions);
            }
            return JsonSerializer.Deserialize<CharacterSheetData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
