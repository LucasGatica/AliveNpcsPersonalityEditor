using System.Text.Json;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;

namespace AliveNpcsPersonalityEditor;

/// <summary>Reads and writes the custom_personalities.json file.</summary>
public sealed class PersonalityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly IMonitor _monitor;

    /// <summary>Current custom overrides (NPC name -> personality text). Only contains edited NPCs.</summary>
    public Dictionary<string, string> Overrides { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public PersonalityStore(string modDirectoryPath, IMonitor monitor)
    {
        _filePath = Path.Combine(modDirectoryPath, "custom_personalities.json");
        _monitor = monitor;
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            Overrides = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<CustomPersonalityData>(json, JsonOptions);
            Overrides = data?.Personalities != null
                ? new Dictionary<string, string>(data.Personalities, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
            _monitor.Log($"Loaded {Overrides.Count} custom personality override(s).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor.Log($"Failed to load custom personalities: {ex.Message}", LogLevel.Warn);
            Overrides = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        try
        {
            var data = new CustomPersonalityData
            {
                SchemaVersion = 1,
                LastModified = DateTime.UtcNow.ToString("o"),
                Personalities = new Dictionary<string, string>(Overrides, StringComparer.OrdinalIgnoreCase)
            };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(_filePath, json);
            _monitor.Log($"Saved {Overrides.Count} custom personality override(s).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            _monitor.Log($"Failed to save custom personalities: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>Set or clear a custom override. Pass null to remove.</summary>
    public void Set(string npcName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Overrides.Remove(npcName);
        else
            Overrides[npcName] = value;
    }

    /// <summary>Get the custom override for an NPC, or null if not overridden.</summary>
    public string? Get(string npcName)
    {
        return Overrides.TryGetValue(npcName, out var val) ? val : null;
    }

    public bool HasOverride(string npcName) => Overrides.ContainsKey(npcName);
}
