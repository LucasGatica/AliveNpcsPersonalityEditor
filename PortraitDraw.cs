using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Centralised portrait drawing for gallery/editor cards and modals.
/// Order of preference:
///   1. a real NPC portrait texture (64x64 head shot),
///   2. for the Farmer — who has no portrait — the vanilla Scarecrow sprite as a
///      thematic stand-in, drawn aspect-correct (it's a 16x32 big-craftable, not a
///      square), so it isn't stretched,
///   3. otherwise a subtle gray placeholder box (the previous behaviour).
/// Keeping this in one place means every draw site stays consistent and future
/// placeholders (custom/OC NPCs) only need changing here.
/// </summary>
internal static class PortraitDraw
{
    /// <summary>The NPC key that represents the player character.</summary>
    public const string FarmerKey = "Farmer";

    // Vanilla Scarecrow big-craftable, pulled via ItemRegistry so it survives
    // texture packs / version changes instead of hardcoding spritesheet coords.
    private const string ScarecrowItemId = "(BC)8";
    private static Texture2D? _scarecrowTex;
    private static Rectangle _scarecrowSrc;
    private static bool _scarecrowResolved;

    public static void Draw(SpriteBatch b, Rectangle rect, string? npcName, Texture2D? portrait)
    {
        if (portrait != null)
        {
            b.Draw(portrait, rect, new Rectangle(0, 0, 64, 64), Color.White);
            return;
        }

        if (!string.IsNullOrEmpty(npcName)
            && npcName.Equals(FarmerKey, StringComparison.OrdinalIgnoreCase)
            && TryGetScarecrow(out var tex, out var src))
        {
            // Faint backing so the sprite reads as an intentional placeholder.
            b.Draw(Game1.staminaRect, rect, Color.Black * 0.08f);
            b.Draw(tex, FitCentered(rect, src.Width, src.Height), src, Color.White);
            return;
        }

        b.Draw(Game1.staminaRect, rect, Color.Black * 0.08f);
    }

    /// <summary>Fit a source sprite inside <paramref name="box"/>, preserving aspect ratio and centring it.</summary>
    private static Rectangle FitCentered(Rectangle box, int srcW, int srcH)
    {
        if (srcW <= 0 || srcH <= 0)
            return box;

        // 0.86 leaves a little breathing room around the sprite inside the card.
        var scale = Math.Min(box.Width / (float)srcW, box.Height / (float)srcH) * 0.86f;
        var w = Math.Max(1, (int)(srcW * scale));
        var h = Math.Max(1, (int)(srcH * scale));
        var x = box.X + (box.Width - w) / 2;
        var y = box.Y + (box.Height - h) / 2;
        return new Rectangle(x, y, w, h);
    }

    private static bool TryGetScarecrow(out Texture2D tex, out Rectangle src)
    {
        if (!_scarecrowResolved)
        {
            _scarecrowResolved = true;
            try
            {
                var data = ItemRegistry.GetDataOrErrorItem(ScarecrowItemId);
                _scarecrowTex = data.GetTexture();
                _scarecrowSrc = data.GetSourceRect();
            }
            catch
            {
                _scarecrowTex = null;
            }
        }

        tex = _scarecrowTex!;
        src = _scarecrowSrc;
        return _scarecrowTex != null;
    }
}
