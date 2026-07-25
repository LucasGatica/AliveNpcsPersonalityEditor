using AliveNpcsPersonalityEditor.Models;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// FORCED visual layer: while a dialogue box is open, show the NPC's overridden display name under the
/// portrait (e.g. "Sam" → "Samantha") with a green tint, and an "Originally Sam" hover tooltip.
/// The swap is momentary (only during the portrait draw) and restored immediately, so the game's
/// internal data is never changed — this is purely presentational and gated by the Character Data
/// feature toggle.
/// </summary>
public static class PortraitNamePatches
{
    private static IMonitor? _monitor;
    private static ITranslationHelper? _i18n;
    private static Func<Dictionary<string, NpcOverrideEntry>>? _getOverrides;
    private static Func<bool>? _isEnabled;

    // Momentary swap state, restored in the postfix of the same draw call.
    private static NPC? _swappedNpc;
    private static string? _savedName;
    // True only while the swapped portrait name is being drawn, so we can tint just that text.
    private static bool _drawingOverrideName;

    public static void Initialise(IMonitor monitor, ITranslationHelper i18n, Func<Dictionary<string, NpcOverrideEntry>> getOverrides, Func<bool> isEnabled)
    {
        _monitor = monitor;
        _i18n = i18n;
        _getOverrides = getOverrides;
        _isEnabled = isEnabled;
    }

    public static void Apply(Harmony harmony)
    {
        try
        {
            var drawPortrait = AccessTools.Method(typeof(DialogueBox), nameof(DialogueBox.drawPortrait));
            if (drawPortrait != null)
                harmony.Patch(drawPortrait,
                    prefix: new HarmonyMethod(typeof(PortraitNamePatches), nameof(DrawPortrait_Prefix)),
                    postfix: new HarmonyMethod(typeof(PortraitNamePatches), nameof(DrawPortrait_Postfix)));

            var draw = AccessTools.Method(typeof(DialogueBox), nameof(DialogueBox.draw), new[] { typeof(SpriteBatch) });
            if (draw != null)
                harmony.Patch(draw, postfix: new HarmonyMethod(typeof(PortraitNamePatches), nameof(Draw_Postfix)));

            var spriteText = AccessTools.Method(typeof(SpriteText), nameof(SpriteText.drawString));
            if (spriteText != null)
                harmony.Patch(spriteText, prefix: new HarmonyMethod(typeof(PortraitNamePatches), nameof(SpriteText_DrawString_Prefix)));
        }
        catch (Exception ex)
        {
            _monitor?.Log($"PortraitNamePatches failed to apply: {ex.Message}", LogLevel.Warn);
        }
    }

    private static NPC? Speaker(DialogueBox box) => box?.characterDialogue?.speaker;

    /// <summary>
    /// Returns the display name to show (user override or detected external change), or null
    /// if nothing applies.
    /// </summary>
    private static string? GetFeedName(string npcName)
    {
        if (_isEnabled == null || !_isEnabled() || string.IsNullOrWhiteSpace(npcName))
            return null;

        var overrides = _getOverrides?.Invoke();
        var user = overrides != null && overrides.TryGetValue(npcName, out var e) ? e.CharacterData?.DisplayName : null;

        CharacterDataService.GetDetectedExternalChanges().TryGetValue(npcName, out var detected);
        var detectedName = detected != null && detected.TryGetValue("DisplayName", out var dn) ? dn : null;

        var feed = !string.IsNullOrWhiteSpace(user) ? user
            : !string.IsNullOrWhiteSpace(detectedName) ? detectedName
            : null;

        return feed;
    }

    // Swap the speaker's display name to the fed name just for the portrait draw.
    private static void DrawPortrait_Prefix(DialogueBox __instance)
    {
        _swappedNpc = null;
        _savedName = null;

        var speaker = Speaker(__instance);
        if (speaker == null)
            return;

        var feed = GetFeedName(speaker.Name);
        if (string.IsNullOrWhiteSpace(feed) || string.Equals(feed, speaker.displayName, StringComparison.Ordinal))
            return;

        _swappedNpc = speaker;
        _savedName = speaker.displayName;
        speaker.displayName = feed;
        _drawingOverrideName = true;
    }

    private static void DrawPortrait_Postfix()
    {
        _drawingOverrideName = false;
        if (_swappedNpc == null)
            return;
        _swappedNpc.displayName = _savedName;
        _swappedNpc = null;
        _savedName = null;
    }

    // Tint just the swapped portrait name green as a "this was replaced" indicator.
    // SpriteText color is a palette index; 6 = LimeGreen in the game's getColorFromIndex.
    private const int SpriteTextGreenIndex = 6;

    private static void SpriteText_DrawString_Prefix(ref int color)
    {
        if (_drawingOverrideName && color < 0)
            color = SpriteTextGreenIndex;
    }

    // After the whole box draws, show the "Originally X" tooltip when hovering the portrait area.
    // The original name is the NPC's internal name (speaker.Name), which is always the vanilla
    // name — it can't be overridden by token edits or Data/Characters changes.
    private static void Draw_Postfix(DialogueBox __instance, SpriteBatch b)
    {
        var speaker = Speaker(__instance);
        if (speaker == null)
            return;

        var feed = GetFeedName(speaker.Name);
        var original = speaker.Name; // NPC internal name = vanilla display name
        if (string.IsNullOrWhiteSpace(feed) || string.IsNullOrWhiteSpace(original) || string.Equals(feed, original, StringComparison.OrdinalIgnoreCase))
            return;

        // Approximate portrait/name region on the right side of the dialogue box.
        var portraitArea = new Rectangle(__instance.x + __instance.width - 448, __instance.y, 448, __instance.height);
        var mouse = Game1.getMousePosition();
        if (portraitArea.Contains(mouse.X, mouse.Y))
            IClickableMenu.drawHoverText(b, _i18n!.Get("patch.portraitOriginalName", new { name = original }), Game1.smallFont);
    }
}
