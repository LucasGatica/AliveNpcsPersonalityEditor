using System.Text.Json;
using System.Text.Json.Serialization;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;

namespace AliveNpcsPersonalityEditor;

/// <summary>Reads and writes per-NPC override JSON files in the overrides/ directory.</summary>
public sealed class PersonalityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string OverridesDirName = "overrides";
    private const string LegacyDataFileName = "custom_personalities.json";

    private readonly string _overridesDir;
    private readonly string _legacyFilePath;
    private readonly IMonitor _monitor;
    private bool _migratedFromLegacy;

    /// <summary>Current custom overrides (NPC name -> structured override). Only contains edited NPCs.</summary>
    public Dictionary<string, NpcOverrideEntry> Overrides { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Absolute path of the overrides directory consumed by AliveNpcs.</summary>
    public string OverridesDirectory => _overridesDir;

    public PersonalityStore(string modDirectoryPath, IMonitor monitor)
    {
        _overridesDir = Path.Combine(modDirectoryPath, OverridesDirName);
        _legacyFilePath = Path.Combine(modDirectoryPath, LegacyDataFileName);
        _monitor = monitor;
    }

    public void Load()
    {
        var dict = new Dictionary<string, NpcOverrideEntry>(StringComparer.OrdinalIgnoreCase);
        _migratedFromLegacy = false;

        if (Directory.Exists(_overridesDir))
        {
            foreach (var file in Directory.EnumerateFiles(_overridesDir, "*.json"))
            {
                var npcName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(npcName))
                    continue;

                try
                {
                    var json = File.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<NpcOverrideEntry>(json, JsonOptions);
                    if (entry != null && entry.HasAnyField)
                    {
                        entry.NpcName = npcName;
                        dict[npcName] = entry;
                    }
                }
                catch (Exception ex)
                {
                    _monitor.Log($"Failed to read override file '{Path.GetFileName(file)}': {ex.Message}", LogLevel.Warn);
                }
            }
        }

        if (dict.Count == 0 && File.Exists(_legacyFilePath))
        {
            _migratedFromLegacy = true;
            try
            {
                var json = File.ReadAllText(_legacyFilePath);
                var legacyData = JsonSerializer.Deserialize<LegacyPersonalityData>(json, JsonOptions);
                if (legacyData?.Personalities != null)
                {
                    foreach (var (npcName, text) in legacyData.Personalities)
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                            dict[npcName] = new NpcOverrideEntry { NpcName = npcName, CanonicalPersonality = text.Trim() };
                    }
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to read legacy custom_personalities.json: {ex.Message}", LogLevel.Warn);
            }
        }

        Overrides = dict;
        _monitor.Log($"Loaded {Overrides.Count} custom personality override(s).", LogLevel.Info);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_overridesDir);

            var existingFiles = Directory.Exists(_overridesDir)
                ? new HashSet<string>(Directory.EnumerateFiles(_overridesDir, "*.json"), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (npcName, entry) in Overrides)
            {
                if (!entry.HasAnyField)
                    continue;

                var filePath = Path.Combine(_overridesDir, $"{npcName}.json");
                entry.NpcName = npcName;
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                existingFiles.Remove(filePath);
            }

            foreach (var staleFile in existingFiles)
            {
                try { File.Delete(staleFile); }
                catch { /* best effort */ }
            }

            if (_migratedFromLegacy && File.Exists(_legacyFilePath))
            {
                try
                {
                    var bakPath = _legacyFilePath + ".bak";
                    File.Move(_legacyFilePath, bakPath, overwrite: true);
                    _monitor.Log("Migrated legacy custom_personalities.json → per-NPC files. Original backed up as .bak.", LogLevel.Info);
                    _migratedFromLegacy = false;
                }
                catch { /* migration cleanup is best-effort */ }
            }

            _monitor.Log($"Saved {Overrides.Count} custom personality override(s).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor.Log($"Failed to save custom personalities: {ex.Message}", LogLevel.Error);
        }
    }

    public void Save(string npcName)
    {
        try
        {
            Directory.CreateDirectory(_overridesDir);

            if (Overrides.TryGetValue(npcName, out var entry) && entry.HasAnyField)
            {
                var filePath = Path.Combine(_overridesDir, $"{npcName}.json");
                entry.NpcName = npcName;
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
            }
            else
            {
                DeleteFile(npcName);
            }
        }
        catch (Exception ex)
        {
            _monitor.Log($"Failed to save override for {npcName}: {ex.Message}", LogLevel.Error);
        }
    }

    public void Delete(string npcName)
    {
        Overrides.Remove(npcName);
        DeleteFile(npcName);
    }

    private void DeleteFile(string npcName)
    {
        var filePath = Path.Combine(_overridesDir, $"{npcName}.json");
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch { /* best effort */ }
    }

    /// <summary>Set or clear a custom override. Pass null to remove.</summary>
    public void Set(string npcName, NpcOverrideEntry? value)
    {
        if (value == null || !value.HasAnyField)
            Overrides.Remove(npcName);
        else
            Overrides[npcName] = value;
    }

    /// <summary>Get the custom override for an NPC, or null if not overridden.</summary>
    public NpcOverrideEntry? Get(string npcName)
    {
        return Overrides.TryGetValue(npcName, out var val) ? val : null;
    }

    public bool HasOverride(string npcName) => Overrides.TryGetValue(npcName, out var entry) && entry.HasAnyField;

    private sealed class LegacyPersonalityData
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("lastModified")]
        public string LastModified { get; set; } = "";

        [JsonPropertyName("personalities")]
        public Dictionary<string, string> Personalities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
