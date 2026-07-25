using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using AliveNpcsPersonalityEditor.Models;

namespace AliveNpcsPersonalityEditor;

public sealed class LocalPresetBrowser : IClickableMenu
{
    private readonly Dictionary<string, NpcOverrideEntry> _presets;
    private readonly Texture2D? _defaultPortrait;
    private readonly string _currentNpc;
    private readonly ITranslationHelper _i18n;
    private readonly Action<string, NpcOverrideEntry> _onApply;
    private readonly Action<string> _onDelete;
    private readonly Action _onClose;

    private int _scrollY;
    private int _maxScroll;
    private int _hoveredCard = -1;

    private const int GridCols = 4;
    private const int CardW = 200;
    private const int CardH = 100;
    private const int CardGap = 8;
    private const int HeaderH = 60;

    public LocalPresetBrowser(
        Dictionary<string, NpcOverrideEntry> presets,
        Texture2D? defaultPortrait,
        string currentNpc,
        ITranslationHelper i18n,
        Action<string, NpcOverrideEntry> onApply,
        Action<string> onDelete,
        Action onClose)
        : base(0, 0, 0, 0)
    {
        _presets = presets;
        _defaultPortrait = defaultPortrait;
        _currentNpc = currentNpc;
        _i18n = i18n;
        _onApply = onApply;
        _onDelete = onDelete;
        _onClose = onClose;

        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        var vw = Game1.uiViewport.Width;
        var vh = Game1.uiViewport.Height;
        width = System.Math.Min(900, vw - 80);
        height = System.Math.Min(500, vh - 80);
        xPositionOnScreen = (vw - width) / 2;
        yPositionOnScreen = (vh - height) / 2;
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.75f);
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        var title = _i18n.Get("gallery.button.browse_presets");
        var titleSize = Game1.dialogueFont.MeasureString(title);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
            new Vector2(xPositionOnScreen + (width - titleSize.X) / 2f, yPositionOnScreen + 12), Color.SaddleBrown);

        var contentArea = new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + HeaderH,
            width - 32, height - HeaderH - 16);

        var entries = new List<KeyValuePair<string, NpcOverrideEntry>>(_presets);
        var rows = (entries.Count + GridCols - 1) / GridCols;
        var totalH = rows * (CardH + CardGap);
        var innerW = contentArea.Width - 20;
        var cols = System.Math.Min(GridCols, System.Math.Max(1, innerW / (CardW + CardGap)));
        var contentW = cols * CardW + (cols - 1) * CardGap;
        var startX = contentArea.X + (contentArea.Width - 20 - contentW) / 2;
        _maxScroll = System.Math.Max(0, totalH - contentArea.Height);

        var prevScissor = b.GraphicsDevice.ScissorRectangle;
        var prevRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = contentArea;

        for (int i = 0; i < entries.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var cx = startX + col * (CardW + CardGap);
            var cy = contentArea.Y + row * (CardH + CardGap) - _scrollY;

            if (cy + CardH < contentArea.Y || cy > contentArea.Bottom)
                continue;

            var hovered = _hoveredCard == i;
            var bg = hovered ? new Color(240, 215, 170) : new Color(222, 195, 153);

            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                cx, cy, CardW, CardH, bg);

            var npcName = entries[i].Key;
            var preview = !string.IsNullOrWhiteSpace(entries[i].Value.Appearance)
                ? entries[i].Value.Appearance
                : !string.IsNullOrWhiteSpace(entries[i].Value.SubmissionCredit)
                    ? entries[i].Value.SubmissionCredit
                : (!string.IsNullOrWhiteSpace(entries[i].Value.CanonicalPersonality)
                    ? entries[i].Value.CanonicalPersonality : "(no preview)");

            var nameSize = Game1.smallFont.MeasureString(npcName);
            Utility.drawTextWithShadow(b, npcName, Game1.smallFont,
                new Vector2(cx + (CardW - nameSize.X) / 2f, cy + 4), Color.SaddleBrown);

            var wrapped = Game1.parseText(preview, Game1.tinyFont, CardW - 8);
            var lines = wrapped.Split('\n');
            var maxLines = (CardH - 30) / (int)Game1.tinyFont.MeasureString("A").Y;
            if (lines.Length > maxLines)
                wrapped = string.Join("\n", lines.Take(maxLines - 1)) + "\n...";
            b.DrawString(Game1.tinyFont, wrapped, new Vector2(cx + 4, cy + 24), Color.Black * 0.7f);
        }

        if (entries.Count == 0)
        {
            var emptyText = _i18n.Get("gallery.preset.none");
            var size = Game1.smallFont.MeasureString(emptyText);
            Utility.drawTextWithShadow(b, emptyText, Game1.smallFont,
                new Vector2(contentArea.X + (contentArea.Width - size.X) / 2f, contentArea.Y + 40), Color.Wheat);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, prevRasterizer);
        b.GraphicsDevice.ScissorRectangle = prevScissor;

        drawMouse(b);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        var contentArea = new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + HeaderH,
            width - 32, height - HeaderH - 16);

        var entries = new List<KeyValuePair<string, NpcOverrideEntry>>(_presets);
        var innerW = contentArea.Width - 20;
        var cols = System.Math.Min(GridCols, System.Math.Max(1, innerW / (CardW + CardGap)));
        var contentW = cols * CardW + (cols - 1) * CardGap;
        var startX = contentArea.X + (contentArea.Width - 20 - contentW) / 2;

        for (int i = 0; i < entries.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var cx = startX + col * (CardW + CardGap);
            var cy = contentArea.Y + row * (CardH + CardGap) - _scrollY;

            if (cy + CardH < contentArea.Y || cy > contentArea.Bottom)
                continue;

            var cardRect = new Rectangle(cx, cy, CardW, CardH);
            if (cardRect.Contains(x, y))
            {
                var entry = entries[i];
                _onApply(entry.Key, entry.Value);
                Game1.playSound("smallSelect");
                Close();
                return;
            }
        }

        if (!new Rectangle(xPositionOnScreen, yPositionOnScreen, width, height).Contains(x, y))
        {
            Close();
        }
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        var contentArea = new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + HeaderH,
            width - 32, height - HeaderH - 16);

        var entries = new List<KeyValuePair<string, NpcOverrideEntry>>(_presets);
        var innerW = contentArea.Width - 20;
        var cols = System.Math.Min(GridCols, System.Math.Max(1, innerW / (CardW + CardGap)));
        var contentW = cols * CardW + (cols - 1) * CardGap;
        var startX = contentArea.X + (contentArea.Width - 20 - contentW) / 2;

        for (int i = 0; i < entries.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var cx = startX + col * (CardW + CardGap);
            var cy = contentArea.Y + row * (CardH + CardGap) - _scrollY;

            if (cy + CardH < contentArea.Y || cy > contentArea.Bottom)
                continue;

            var cardRect = new Rectangle(cx, cy, CardW, CardH);
            if (cardRect.Contains(x, y))
            {
                var entry = entries[i];
                _onDelete(entry.Key);
                _presets.Remove(entry.Key);
                Game1.playSound("trashcan");
                return;
            }
        }
    }

    public override void receiveScrollWheelAction(int direction)
    {
        _scrollY -= direction;
        _scrollY = System.Math.Clamp(_scrollY, 0, System.Math.Max(0, _maxScroll));
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape) Close();
    }

    private void Close()
    {
        _onClose();
        if (Game1.activeClickableMenu == this)
            Game1.activeClickableMenu = null;
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        RecalculateLayout();
    }
}
