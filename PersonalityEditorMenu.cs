using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

public class PersonalityEditorMenu : IClickableMenu
{
    // ── Data ──
    private readonly PersonalityStore _store;
    private readonly IAliveNpcsApi _api;
    private readonly IMonitor _monitor;
    private readonly ITranslationHelper _i18n;
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);

    // ── Categories ──
    private static readonly (string Key, string[] Npcs)[] Categories =
    {
        ("page.bachelors",     new[] { "Alex", "Elliott", "Harvey", "Sam", "Sebastian", "Shane" }),
        ("page.bachelorettes", new[] { "Abigail", "Emily", "Haley", "Leah", "Maru", "Penny" }),
        ("page.townspeople",   new[] { "Caroline", "Clint", "Demetrius", "Evelyn", "George", "Gus", "Jodi", "Kent", "Lewis", "Linus", "Marnie", "Pam", "Pierre", "Robin", "Willy" }),
        ("page.special",       new[] { "Dwarf", "Krobus", "Sandy", "Wizard" }),
    };
    private int _activeTab;

    // ── Scrolling ──
    private int _scrollY;
    private int _maxScroll;

    // ── Editing ──
    private string? _editingNpc;
    private TextBox _textBox = null!;
    private bool _textBoxSubscribed;

    // ── Layout constants ──
    private const int CardH = 170;
    private const int CardGap = 10;
    private const int PortraitSrc = 64;
    private const int PortraitDraw = 108;
    private const int TabH = 44;
    private const int EditAreaH = 164;
    private const int Pad = 16;

    // ── Computed rects ──
    private Rectangle _contentArea;
    private Rectangle _editArea;

    // ── Colors ──
    private static readonly Color CardBg = new(222, 195, 153);       // light brown
    private static readonly Color CardSelectedBg = new(242, 220, 180);
    private static readonly Color TabActive = new(170, 120, 60);
    private static readonly Color TabInactive = new(200, 170, 130);
    private static readonly Color EditBg = new(160, 125, 80);
    private static readonly Color BtnSave = new(90, 160, 70);
    private static readonly Color BtnReset = new(190, 90, 70);

    public PersonalityEditorMenu(PersonalityStore store, IAliveNpcsApi api, IMonitor monitor, ITranslationHelper i18n)
        : base(0, 0, 0, 0)
    {
        _store = store;
        _api = api;
        _monitor = monitor;
        _i18n = i18n;

        // Cache all defaults
        foreach (var (_, npcs) in Categories)
            foreach (var npc in npcs)
                _defaults.TryAdd(npc, api.GetDefaultPersonality(npc));

        RecalculateLayout();
        InitTextBox();
    }

    private void RecalculateLayout()
    {
        var vw = Game1.uiViewport.Width;
        var vh = Game1.uiViewport.Height;
        width = Math.Min(1100, vw - 80);
        height = Math.Min(860, vh - 40);
        xPositionOnScreen = (vw - width) / 2;
        yPositionOnScreen = (vh - height) / 2;

        var innerX = xPositionOnScreen + 24;
        var innerW = width - 48;
        var tabsBottom = yPositionOnScreen + 68 + TabH;
        var contentH = height - 68 - TabH - EditAreaH - 32;

        _contentArea = new Rectangle(innerX, tabsBottom + 4, innerW, contentH);
        _editArea = new Rectangle(innerX, _contentArea.Bottom + 4, innerW, EditAreaH);
    }

    private void InitTextBox()
    {
        _textBox = new TextBox(
            Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
            null, Game1.smallFont, Color.Black)
        {
            X = _editArea.X + 12,
            Y = _editArea.Y + 32,
            Width = _editArea.Width - 24,
            Text = ""
        };
    }

    private void SubscribeTextBox()
    {
        if (!_textBoxSubscribed)
        {
            Game1.keyboardDispatcher.Subscriber = _textBox;
            _textBoxSubscribed = true;
        }
    }

    private void UnsubscribeTextBox()
    {
        if (_textBoxSubscribed && Game1.keyboardDispatcher.Subscriber == _textBox)
            Game1.keyboardDispatcher.Subscriber = null;
        _textBoxSubscribed = false;
    }

    // ═══════════════════════════════════════════════════════════
    //  DRAWING
    // ═══════════════════════════════════════════════════════════

    public override void draw(SpriteBatch b)
    {
        // Dim overlay
        b.Draw(Game1.fadeToBlackRect,
            new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
            Color.Black * 0.75f);

        // Menu frame
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        DrawTitle(b);
        DrawTabs(b);
        DrawCards(b);
        DrawEditArea(b);
        drawMouse(b);
    }

    private void DrawTitle(SpriteBatch b)
    {
        var title = _i18n.Get("editor.title");
        var size = Game1.dialogueFont.MeasureString(title);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
            new Vector2(xPositionOnScreen + (width - size.X) / 2f, yPositionOnScreen + 18),
            Color.SaddleBrown);
    }

    private void DrawTabs(SpriteBatch b)
    {
        var tabY = yPositionOnScreen + 64;
        var totalW = width - 48;
        var tabW = totalW / Categories.Length;

        for (int i = 0; i < Categories.Length; i++)
        {
            var rect = new Rectangle(xPositionOnScreen + 24 + i * tabW, tabY, tabW - 4, TabH);
            var active = i == _activeTab;
            var color = active ? TabActive : TabInactive;

            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                rect.X, rect.Y, rect.Width, rect.Height, color);

            var label = _i18n.Get(Categories[i].Key);
            var labelSize = Game1.smallFont.MeasureString(label);
            Utility.drawTextWithShadow(b, label, Game1.smallFont,
                new Vector2(rect.X + (rect.Width - labelSize.X) / 2f, rect.Y + (rect.Height - labelSize.Y) / 2f),
                active ? Color.White : Color.Wheat);
        }
    }

    private void DrawCards(SpriteBatch b)
    {
        var npcs = Categories[_activeTab].Npcs;
        var totalH = npcs.Length * (CardH + CardGap) - CardGap;
        _maxScroll = Math.Max(0, totalH - _contentArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);

        // Scissor clip for the content area
        var prevScissor = b.GraphicsDevice.ScissorRectangle;
        var prevRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = _contentArea;

        for (int i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            var cardY = _contentArea.Y + i * (CardH + CardGap) - _scrollY;

            if (cardY + CardH < _contentArea.Y || cardY > _contentArea.Bottom)
                continue;

            DrawSingleCard(b, npc, _contentArea.X, cardY, _contentArea.Width);
        }

        // Restore
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, prevRasterizer);
        b.GraphicsDevice.ScissorRectangle = prevScissor;

        // Scrollbar
        if (_maxScroll > 0)
            DrawScrollbar(b);
    }

    private void DrawSingleCard(SpriteBatch b, string npc, int cx, int cy, int cw)
    {
        var isSelected = _editingNpc == npc;
        var hasOverride = _store.HasOverride(npc);
        var bg = isSelected ? CardSelectedBg : CardBg;

        // Card background
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            cx, cy, cw, CardH, bg);

        // ── Portrait (left side) ──
        var portraitX = cx + Pad;
        var portraitY = cy + 10;
        try
        {
            var tex = Game1.content.Load<Texture2D>($"Portraits/{npc}");
            b.Draw(tex, new Rectangle(portraitX, portraitY, PortraitDraw, PortraitDraw),
                new Rectangle(0, 0, PortraitSrc, PortraitSrc), Color.White);
        }
        catch
        {
            b.Draw(Game1.staminaRect,
                new Rectangle(portraitX, portraitY, PortraitDraw, PortraitDraw),
                Color.Black * 0.1f);
        }

        // ── Name below portrait ──
        var nameStr = hasOverride ? $"{npc} *" : npc;
        var nameColor = hasOverride ? new Color(70, 140, 50) : Color.SaddleBrown;
        var nameSize = Game1.smallFont.MeasureString(nameStr);
        var nameX = portraitX + (PortraitDraw - nameSize.X) / 2f;
        var nameY = portraitY + PortraitDraw + 4;
        Utility.drawTextWithShadow(b, nameStr, Game1.smallFont, new Vector2(nameX, nameY), nameColor);

        // ── Personality text (right of portrait) ──
        var textX = portraitX + PortraitDraw + Pad;
        var textW = cw - PortraitDraw - Pad * 3 - 12;
        var personality = GetCurrentText(npc);
        var wrapped = Game1.parseText(personality, Game1.smallFont, textW);

        // Clamp lines so text doesn't overflow card
        var lines = wrapped.Split('\n');
        var maxLines = (CardH - 24) / (int)Game1.smallFont.MeasureString("A").Y;
        if (lines.Length > maxLines)
            wrapped = string.Join("\n", lines.Take(maxLines - 1)) + "\n...";

        b.DrawString(Game1.smallFont, wrapped, new Vector2(textX, cy + 14), Color.Black * 0.85f);
    }

    private void DrawScrollbar(SpriteBatch b)
    {
        var barX = _contentArea.Right - 8;
        var barH = _contentArea.Height;
        var thumbH = Math.Max(30, barH * barH / (barH + _maxScroll));
        var thumbY = _contentArea.Y + (int)((float)_scrollY / _maxScroll * (barH - thumbH));

        // Track
        b.Draw(Game1.staminaRect, new Rectangle(barX, _contentArea.Y, 6, barH), Color.Black * 0.15f);
        // Thumb
        b.Draw(Game1.staminaRect, new Rectangle(barX, thumbY, 6, thumbH), Color.SaddleBrown * 0.6f);
    }

    private void DrawEditArea(SpriteBatch b)
    {
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            _editArea.X, _editArea.Y, _editArea.Width, _editArea.Height, EditBg);

        if (_editingNpc != null)
        {
            // Label
            Utility.drawTextWithShadow(b, _i18n.Get("field.editing", new { npcName = _editingNpc }), Game1.smallFont,
                new Vector2(_editArea.X + 12, _editArea.Y + 10), Color.White);

            // Text preview (4 lines, word-wrapped) below the label
            var previewX = _editArea.X + 14;
            var previewY = _editArea.Y + 34;
            var previewW = _editArea.Width - 28;
            var text = _textBox.Text ?? "";
            var wrapped = Game1.parseText(text, Game1.smallFont, previewW);
            var lines = wrapped.Split('\n');
            if (lines.Length > 4)
                wrapped = string.Join("\n", lines.Take(3)) + "\n...";

            // Preview background
            var lineH = (int)Game1.smallFont.MeasureString("A").Y;
            var previewH = lineH * 4 + 8;
            b.Draw(Game1.staminaRect, new Rectangle(previewX - 2, previewY - 2, previewW + 4, previewH), Color.Black * 0.15f);
            b.DrawString(Game1.smallFont, wrapped, new Vector2(previewX, previewY), Color.Wheat);

            // TextBox (input line) below preview
            _textBox.Y = previewY + previewH + 6;
            _textBox.Draw(b);

            // Buttons below textbox
            var btnY = _textBox.Y + 48 + 6;
            DrawButton(b, GetSaveRect(btnY), _i18n.Get("button.save"), BtnSave);
            DrawButton(b, GetResetRect(btnY), _i18n.Get("button.reset"), BtnReset);
            DrawButton(b, GetCloseRect(btnY), _i18n.Get("button.close"), new Color(120, 100, 80));
        }
        else
        {
            Utility.drawTextWithShadow(b, _i18n.Get("editor.select_prompt"),
                Game1.smallFont, new Vector2(_editArea.X + 12, _editArea.Y + 60), Color.Wheat);

            // Close button
            var btnY = _editArea.Bottom - 52;
            DrawButton(b, GetCloseRect(btnY), _i18n.Get("button.close"), new Color(120, 100, 80));
        }
    }

    private static void DrawButton(SpriteBatch b, Rectangle rect, string label, Color bg)
    {
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            rect.X, rect.Y, rect.Width, rect.Height, bg);
        var size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            Color.White);
    }

    // ═══════════════════════════════════════════════════════════
    //  BUTTON RECTS
    // ═══════════════════════════════════════════════════════════

    private int GetButtonY()
    {
        // Match the btnY computed in DrawEditArea
        var previewY = _editArea.Y + 34;
        var lineH = (int)Game1.smallFont.MeasureString("A").Y;
        var previewH = lineH * 4 + 8;
        return previewY + previewH + 6 + 48 + 6;
    }

    private Rectangle GetSaveRect(int btnY) =>
        new(_editArea.Right - 290, btnY, 80, 40);

    private Rectangle GetResetRect(int btnY) =>
        new(_editArea.Right - 195, btnY, 80, 40);

    private Rectangle GetCloseRect(int btnY) =>
        new(_editArea.Right - 100, btnY, 80, 40);

    private Rectangle GetTabRect(int i)
    {
        var tabW = (width - 48) / Categories.Length;
        return new Rectangle(xPositionOnScreen + 24 + i * tabW, yPositionOnScreen + 64, tabW - 4, TabH);
    }

    private Rectangle GetCardRect(int i)
    {
        var cardY = _contentArea.Y + i * (CardH + CardGap) - _scrollY;
        return new Rectangle(_contentArea.X, cardY, _contentArea.Width, CardH);
    }

    // ═══════════════════════════════════════════════════════════
    //  INPUT
    // ═══════════════════════════════════════════════════════════

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        // Tabs
        for (int i = 0; i < Categories.Length; i++)
        {
            if (GetTabRect(i).Contains(x, y))
            {
                CommitCurrentEdit();
                _activeTab = i;
                _scrollY = 0;
                _editingNpc = null;
                UnsubscribeTextBox();
                Game1.playSound("smallSelect");
                return;
            }
        }

        // Cards
        var npcs = Categories[_activeTab].Npcs;
        for (int i = 0; i < npcs.Length; i++)
        {
            var cardRect = GetCardRect(i);
            if (cardRect.Contains(x, y) && _contentArea.Contains(x, y))
            {
                CommitCurrentEdit();
                _editingNpc = npcs[i];
                _textBox.Text = GetCurrentText(npcs[i]);
                SubscribeTextBox();
                Game1.playSound("smallSelect");
                return;
            }
        }

        var btnY = _editingNpc != null ? GetButtonY() : _editArea.Bottom - 52;

        // Save button
        if (_editingNpc != null && GetSaveRect(btnY).Contains(x, y))
        {
            CommitCurrentEdit();
            _store.Save();
            NotifyReload();
            Game1.playSound("coin");
            return;
        }

        // Reset button
        if (_editingNpc != null && GetResetRect(btnY).Contains(x, y))
        {
            _store.Set(_editingNpc, null);
            _textBox.Text = _defaults.GetValueOrDefault(_editingNpc, "");
            _store.Save();
            NotifyReload();
            Game1.playSound("trashcan");
            return;
        }

        // Close button
        if (GetCloseRect(btnY).Contains(x, y))
        {
            CommitCurrentEdit();
            _store.Save();
            NotifyReload();
            exitThisMenu();
            return;
        }

        // Click on textbox area
        _textBox.Update();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        _scrollY -= direction;
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, _maxScroll));
    }

    public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        if (key == Microsoft.Xna.Framework.Input.Keys.Escape)
        {
            CommitCurrentEdit();
            _store.Save();
            NotifyReload();
            exitThisMenu();
        }
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        RecalculateLayout();
        InitTextBox();
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════

    private string GetCurrentText(string npcName)
    {
        return _store.Get(npcName) ?? _defaults.GetValueOrDefault(npcName, "");
    }

    private void CommitCurrentEdit()
    {
        if (_editingNpc == null) return;
        var text = _textBox.Text?.Trim() ?? "";
        var def = _defaults.GetValueOrDefault(_editingNpc, "");

        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(text, def, StringComparison.OrdinalIgnoreCase))
            _store.Set(_editingNpc, null);
        else
            _store.Set(_editingNpc, text);
    }

    private void NotifyReload()
    {
        try { _api.ReloadCustomPersonalities(); }
        catch (Exception ex) { _monitor.Log($"Reload notify failed: {ex.Message}", LogLevel.Warn); }
    }

    protected override void cleanupBeforeExit()
    {
        UnsubscribeTextBox();
        base.cleanupBeforeExit();
    }
}
