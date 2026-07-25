using StardewValley;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Centralised HUD notifications so every popup shows an icon that matches its intent.
///
/// The game's <see cref="HUDMessage"/> icon is chosen by a numeric "whatType" that is
/// easy to mix up: type 3 is the red "error" X. The editor previously passed 3 for
/// almost every message (and 4 for a couple of successes), so confirmations like
/// "Preset saved" showed a red cross. Always route notifications through these helpers
/// — which use the game's named icon constants — instead of calling
/// <c>new HUDMessage(text, number)</c> directly.
/// </summary>
internal static class EditorNotify
{
    /// <summary>
    /// Positive confirmation — saved, imported, applied, uploaded, deleted.
    /// Shows the achievement icon (reads as "done / accomplished").
    /// </summary>
    public static void Success(string message)
        => Game1.addHUDMessage(new HUDMessage(message, HUDMessage.achievement_type));

    /// <summary>A failed action. Shows the red error (X) icon.</summary>
    public static void Error(string message)
        => Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));

    /// <summary>
    /// Neutral status, progress, or hint (e.g. "Uploading…", "No presets").
    /// Shows no icon, avoiding any misleading success/error symbol.
    /// </summary>
    public static void Info(string message)
        => Game1.addHUDMessage(new HUDMessage(message) { noIcon = true });
}
