using StardewModdingAPI;

namespace AliveNpcsPersonalityEditor;

public sealed class EditorConfig
{
    public SButton OpenEditorKey { get; set; } = SButton.F10;
    public SButton OpenFarmerTabKey { get; set; } = SButton.F7;
    public bool OverrideCharacterSheet { get; set; } = true;
    public string GalleryServerUrl { get; set; } = "http://localhost:3000";
    public bool GalleryEnabled { get; set; } = true;

    /// <summary>
    /// Experimental, opt-in: inject an NPC's structured Character Data into the AI prompt.
    /// Off by default because it can cause the model to hallucinate or drift from personality.
    /// </summary>
    public bool IncludeCharacterDataInPrompt { get; set; } = false;
}
