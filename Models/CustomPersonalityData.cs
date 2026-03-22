namespace AliveNpcsPersonalityEditor.Models;

/// <summary>JSON model for the custom personalities file.</summary>
public sealed class CustomPersonalityData
{
    public int SchemaVersion { get; set; } = 1;
    public string LastModified { get; set; } = "";
    public Dictionary<string, string> Personalities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
