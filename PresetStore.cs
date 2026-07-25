using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AliveNpcsPersonalityEditor.Models;
using StardewModdingAPI;

namespace AliveNpcsPersonalityEditor;

public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string PresetsDirName = "presets";
    private readonly string _presetsDir;
    private readonly IMonitor _monitor;

    public string PresetsDirectory => _presetsDir;

    public PresetStore(string modDirectoryPath, IMonitor monitor)
    {
        _presetsDir = Path.Combine(modDirectoryPath, PresetsDirName);
        _monitor = monitor;
    }

    /// <summary>
    /// Load every saved preset as (fileId, entry). Each preset is its own file, so an
    /// NPC can have many. Legacy files named "{npc}.json" still load (id = npc name).
    /// </summary>
    public List<(string Id, NpcOverrideEntry Entry)> LoadAll()
    {
        var list = new List<(string Id, NpcOverrideEntry Entry)>();

        if (!Directory.Exists(_presetsDir))
            return list;

        foreach (var file in Directory.EnumerateFiles(_presetsDir, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<NpcOverrideEntry>(json, JsonOptions);
                if (entry != null && entry.HasAnyField)
                {
                    if (string.IsNullOrWhiteSpace(entry.NpcName))
                        entry.NpcName = id; // legacy files were named by NPC
                    list.Add((id, entry));
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to read preset file '{Path.GetFileName(file)}': {ex.Message}", LogLevel.Warn);
            }
        }

        return list;
    }

    /// <summary>Save a NEW preset (unique file id) and return that id. Never overwrites.</summary>
    public string Save(NpcOverrideEntry data)
    {
        Directory.CreateDirectory(_presetsDir);
        var id = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_presetsDir, $"{id}.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
        return id;
    }

    /// <summary>Convenience: stamp the NPC name and save a new preset.</summary>
    public string Save(string npcName, NpcOverrideEntry data)
    {
        data.NpcName = npcName;
        return Save(data);
    }

    /// <summary>Delete a single preset by its file id (from LoadAll).</summary>
    public void Delete(string id)
    {
        var filePath = Path.Combine(_presetsDir, $"{id}.json");
        try { if (File.Exists(filePath)) File.Delete(filePath); }
        catch { }
    }
}
