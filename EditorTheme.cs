using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Central chrome helper. Draws every editor surface (panels, cards, buttons,
/// inputs, scrollbars) using the game's own menu textures instead of flat colour
/// rectangles, so the editor honours recolour / UI-reskin mods and matches the
/// player's chosen theme. Mirrors the drawing approach used by the base
/// AliveNpcs menus (drawTextureBox on Game1.menuTexture + a thin highlight).
/// </summary>
internal static class EditorTheme
{
    /// <summary>The standard wooden 9-slice menu frame region inside Game1.menuTexture.</summary>
    private static readonly Rectangle FrameSource = new(0, 256, 60, 60);

    /// <summary>Inner padding to keep content clear of the wooden frame border.</summary>
    public const int FramePad = 16;

    /// <summary>Draw a themed 9-slice frame/panel filling <paramref name="rect"/>.</summary>
    public static void DrawFrame(SpriteBatch b, Rectangle rect, Color tint)
    {
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, FrameSource,
            rect.X, rect.Y, rect.Width, rect.Height, tint, 1f, false);
    }

    /// <summary>Draw a themed button with a centred label.</summary>
    public static void DrawButton(SpriteBatch b, Rectangle rect, string label, Color tint, Color textColor)
    {
        DrawFrame(b, rect, tint);
        var size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            textColor);
    }

    /// <summary>Draw a themed input-field frame, highlighted when focused.</summary>
    public static void DrawInputFrame(SpriteBatch b, Rectangle rect, bool selected)
    {
        DrawFrame(b, rect, selected ? Color.White : Color.White * 0.82f);
        if (selected)
            DrawHighlight(b, rect);
    }

    /// <summary>Draw a thin focus highlight border around <paramref name="rect"/>.</summary>
    public static void DrawHighlight(SpriteBatch b, Rectangle rect, int thickness = 3)
    {
        var color = Color.DarkGoldenrod * 0.7f;
        b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        b.Draw(Game1.fadeToBlackRect, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    private static readonly Color CdAccent = new(214, 148, 46);   // amber accent
    private static readonly Color CdBadgeText = new(74, 45, 20);   // dark brown, high contrast on amber

    /// <summary>
    /// Mark a card whose preset/override writes the game's Data/Characters: a small
    /// amber badge in the top-right corner, aligned with the name band.
    /// </summary>
    public static void DrawCharacterDataBadge(SpriteBatch b, Rectangle card, string label, int thickness = 3)
    {
        var size = Game1.tinyFont.MeasureString(label);
        const float scale = 0.75f;
        var scaledW = size.X * scale;
        var scaledH = size.Y * scale;
        const int padX = 3, padY = 1;
        var badge = new Rectangle(
            card.Right - FramePad - (int)scaledW - padX * 2 - 2,
            card.Y + FramePad + 4,
            (int)scaledW + padX * 2,
            (int)scaledH + padY * 2);
        b.Draw(Game1.staminaRect, badge, CdAccent);
        Utility.drawTextWithShadow(b, label, Game1.tinyFont,
            new Vector2(badge.X + padX, badge.Y + padY), CdBadgeText, scale);
    }

    /// <summary>Draw a themed scrollbar (game runner + thumb sprites) beside an area.</summary>
    public static void DrawScrollbar(SpriteBatch b, Rectangle area, int scroll, int maxScroll)
    {
        const int barW = 24;
        var track = new Rectangle(area.Right + 4, area.Y, barW, area.Height);
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 383, 6, 6),
            track.X, track.Y, track.Width, track.Height, Color.White, 4f, false);

        var denominator = System.Math.Max(1, track.Height + maxScroll);
        var thumbHeight = System.Math.Max(40, track.Height * track.Height / denominator);
        var thumbY = track.Y + (int)((float)scroll / System.Math.Max(1, maxScroll) * (track.Height - thumbHeight));
        b.Draw(Game1.mouseCursors, new Rectangle(track.X, thumbY, barW, thumbHeight),
            new Rectangle(435, 463, 6, 10), Color.White);
    }
}
