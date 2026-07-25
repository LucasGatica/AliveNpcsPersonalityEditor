using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>Main three-tab editor shown in the UI mockups.</summary>
public sealed class PersonalityEditorMenu : IClickableMenu
{
    private readonly PersonalityStore _store;
    private readonly PresetStore _presetStore;
    private readonly FarmerStore _farmerStore;
    private readonly IAliveNpcsApi _api;
    private readonly EditorConfig _config;
    private readonly GalleryService? _galleryService;
    private readonly IMonitor _monitor;
    private readonly ITranslationHelper _i18n;
    private readonly Action<string>? _onServerUrlSaved;

    private readonly string[] _npcs;
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D?> _portraits = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledNpcs = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> SveFallbackPersonalities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sophia"] = "A shy and emotionally sensitive vineyard owner from Blue Moon Vineyard. Gentle, anxious, and kind-hearted, she appreciates empathy and calm support.",
        ["Victor"] = "An intelligent and thoughtful young man from a wealthy family. Polite, reserved, and academically inclined, often introspective and idealistic.",
        ["Olivia"] = "A refined and confident businesswoman with sophisticated tastes. Elegant, direct, and protective of her family, with a warm side as trust grows.",
        ["Andy"] = "A hardworking and stubborn farmer who values practical effort and loyalty. Gruff at first, but dependable and sincere underneath.",
        ["Susan"] = "An independent and straightforward woman focused on her own routine and priorities. Practical, no-nonsense, but fair and authentic.",
        ["Claire"] = "A polite, overworked employee trying to stay positive under pressure. Friendly, humble, and appreciative of small acts of kindness.",
        ["Martin"] = "A gentle and enthusiastic younger villager, curious and sincere. Friendly, a little awkward, and eager to connect.",
        ["Lance"] = "A seasoned adventurer tied to the Adventurer's Guild. Brave, composed, and strategic, with understated humor and strong duty.",
        ["Morris"] = "An ambitious and image-conscious executive personality. Calculating and persuasive, though capable of nuance depending on the situation.",
        ["Scarlett"] = "An energetic and social villager with a modern, expressive style. Warm, lively, and quick to engage in conversation.",
        ["Morgan"] = "A magical child with curious and innocent worldview. Imaginative, playful, and prone to wonder.",
        ["Apples"] = "A mysterious Junimo-like being with whimsical and curious mannerisms, expressing emotions in a playful and unusual way.",
    };

    private readonly FarmerFormPanel _farmerPanel;
    private readonly GalleryPane? _galleryPane;
    private int _tab;
    private int _scrollY;
    private int _maxScroll;
    private Rectangle _npcGridArea;
    private string _hoverText = "";

    // NPC category tabs (Bachelors / Bachelorettes / Townspeople / Special / SVE / Other).
    private (string Key, string[] Npcs)[] _categories = Array.Empty<(string, string[])>();
    private int _activeCategory;

    private const int HeaderTitleH = 60;   // in-window title band (per-tab title)
    private const int TabStripH = 48;      // raised tabs sitting on the window's top edge
    private const int CategoryBarH = 44;   // NPC category button row (NPCs tab only)
    private const int CardSize = 194;
    private const int CardRowGap = 32;
    private const int MaxCardGap = 40;   // cap the horizontal gap so wide grids don't spread cards apart
    private const int PortraitSourceSize = 64;

    private static readonly Color TabActive = new(235, 155, 45);
    private static readonly Color TabInactive = new(255, 240, 215);
    private static readonly Color CardBackground = new(222, 195, 153);
    private static readonly Color Border = new(125, 60, 40);

    private static readonly HashSet<string> KnownBachelors = new(StringComparer.OrdinalIgnoreCase)
        { "Alex", "Elliott", "Harvey", "Sam", "Sebastian", "Shane" };
    private static readonly HashSet<string> KnownBachelorettes = new(StringComparer.OrdinalIgnoreCase)
        { "Abigail", "Emily", "Haley", "Leah", "Maru", "Penny" };
    private static readonly HashSet<string> KnownSpecial = new(StringComparer.OrdinalIgnoreCase)
        { "Dwarf", "Krobus", "Sandy", "Wizard", "Leo" };

    public PersonalityEditorMenu(
        PersonalityStore store,
        PresetStore presetStore,
        FarmerStore farmerStore,
        IAliveNpcsApi api,
        EditorConfig config,
        GalleryService? galleryService,
        IMonitor monitor,
        ITranslationHelper i18n,
        Action<string>? onServerUrlSaved = null,
        int initialTab = 0)
        : base(0, 0, 0, 0)
    {
        _store = store;
        _presetStore = presetStore;
        _farmerStore = farmerStore;
        _api = api;
        _config = config;
        _galleryService = galleryService;
        _monitor = monitor;
        _i18n = i18n;
        _onServerUrlSaved = onServerUrlSaved;
        _tab = Math.Clamp(initialTab, 0, 2);

        _npcs = GetEditableNpcNames(api);
        _categories = BuildCategories(api, _npcs);
        LoadNpcPresentationData();
        RecalculateLayout();

        _farmerPanel = new FarmerFormPanel(
            _farmerStore, _api, _presetStore, _galleryService, _monitor, _i18n, GetFarmerContentArea,
            () => exitThisMenu(), NotifyCharacterSheetReload);

        if (_config.GalleryEnabled && _galleryService != null)
        {
            _galleryPane = new GalleryPane(
                _galleryService,
                _store,
                _portraits,
                _i18n,
                _monitor,
                GetCatalogContentArea,
                _config.GalleryServerUrl,
                _presetStore,
                NotifyAliveNpcsReload,
                ApplyFarmerPreset,
                _onServerUrlSaved);
        }
    }

    public int InitialTab => _tab;

    private static string[] GetEditableNpcNames(IAliveNpcsApi api)
    {
        try
        {
            return api.GetEditableNpcNames()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return api.GetVanillaNpcNames()
                .Concat(api.GetSveNpcNames())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>NPCs shown in the grid: the members of the currently-selected category.</summary>
    private string[] CurrentNpcs => _categories.Length == 0
        ? _npcs
        : _categories[Math.Clamp(_activeCategory, 0, _categories.Length - 1)].Npcs;

    // Partition the editable NPCs into display categories, mirroring the base game's social groups.
    private static (string Key, string[] Npcs)[] BuildCategories(IAliveNpcsApi api, string[] editableNpcs)
    {
        var editable = new HashSet<string>(editableNpcs, StringComparer.OrdinalIgnoreCase);
        HashSet<string> vanillaSet, sveSet;
        try { vanillaSet = new HashSet<string>(api.GetVanillaNpcNames(), StringComparer.OrdinalIgnoreCase); }
        catch { vanillaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        try { sveSet = new HashSet<string>(api.GetSveNpcNames(), StringComparer.OrdinalIgnoreCase); }
        catch { sveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

        var vanilla = editable.Where(vanillaSet.Contains).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var sve = editable.Where(n => !vanillaSet.Contains(n) && sveSet.Contains(n)).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var other = editable.Where(n => !vanillaSet.Contains(n) && !sveSet.Contains(n)).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        var bachelors = vanilla.Where(KnownBachelors.Contains).ToArray();
        var bachelorettes = vanilla.Where(KnownBachelorettes.Contains).ToArray();
        var townspeople = vanilla.Where(n => !KnownBachelors.Contains(n) && !KnownBachelorettes.Contains(n) && !KnownSpecial.Contains(n)).ToArray();
        var special = vanilla.Where(KnownSpecial.Contains)
            .Concat(other.Where(KnownSpecial.Contains))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        other = other.Where(n => !KnownSpecial.Contains(n)).ToList();

        var categories = new List<(string, string[])>();
        if (bachelors.Length > 0) categories.Add(("page.bachelors", bachelors));
        if (bachelorettes.Length > 0) categories.Add(("page.bachelorettes", bachelorettes));
        if (townspeople.Length > 0) categories.Add(("page.townspeople", townspeople));
        if (special.Length > 0) categories.Add(("page.special", special));
        if (sve.Length > 0) categories.Add(("page.sve", sve));
        if (other.Count > 0) categories.Add(("page.other", other.ToArray()));

        // Fallback: if categorisation yielded nothing (e.g. API returned no vanilla list), show all.
        return categories.Count > 0 ? categories.ToArray() : new[] { ("page.other", editableNpcs) };
    }

    private void LoadNpcPresentationData()
    {
        foreach (var npcName in _npcs)
        {
            try
            {
                var personality = _api.GetDefaultPersonality(npcName);
                if (string.IsNullOrWhiteSpace(personality) && SveFallbackPersonalities.TryGetValue(npcName, out var sveFallback))
                    personality = sveFallback;
                _defaults[npcName] = personality ?? "";
            }
            catch { _defaults[npcName] = SveFallbackPersonalities.TryGetValue(npcName, out var sveFallback) ? sveFallback : ""; }

            try
            {
                if (_api.IsNpcDisabled(npcName))
                    _disabledNpcs.Add(npcName);
            }
            catch (Exception ex)
            {
                _monitor.Log($"Could not read AliveNpcs disabled state for '{npcName}': {ex.Message}", LogLevel.Trace);
            }

            try
            {
                var npc = Game1.getCharacterFromName(npcName);
                _displayNames[npcName] = npc?.displayName ?? npcName;
                _portraits[npcName] = npc?.Portrait ?? Game1.content.Load<Texture2D>($"Portraits/{npcName}");
            }
            catch
            {
                _displayNames[npcName] = npcName;
                _portraits[npcName] = null;
            }
        }
    }

    private void RecalculateLayout()
    {
        var viewport = Game1.uiViewport;
        width = Math.Min(1104, viewport.Width - 24);
        // Slightly shorter than before, and leave room above for the raised tab strip.
        height = Math.Min(880, viewport.Height - 24 - TabStripH);
        xPositionOnScreen = (viewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(TabStripH + 8, (viewport.Height - height) / 2);

        // Reserve a row for the NPC category buttons above the grid.
        var top = yPositionOnScreen + HeaderTitleH + 20 + CategoryBarH;
        _npcGridArea = new Rectangle(
            xPositionOnScreen + 40,
            top,
            width - 80,
            yPositionOnScreen + height - 44 - top);
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, _maxScroll));
    }

    private Rectangle GetCategoryBarArea()
        => new(xPositionOnScreen + 40, yPositionOnScreen + HeaderTitleH + 14, width - 80, CategoryBarH);

    private Rectangle GetCategoryTabRect(int index)
    {
        var area = GetCategoryBarArea();
        var count = Math.Max(1, _categories.Length);
        const int gap = 6;
        var tabW = (area.Width - (count - 1) * gap) / count;
        return new Rectangle(area.X + index * (tabW + gap), area.Y, tabW, area.Height);
    }

    private void DrawCategoryBar(SpriteBatch b)
    {
        for (var i = 0; i < _categories.Length; i++)
        {
            var rect = GetCategoryTabRect(i);
            var active = i == _activeCategory;
            EditorTheme.DrawFrame(b, rect, active ? TabActive : TabInactive);

            var label = _i18n.Get(_categories[i].Key).ToString();
            var scale = 1f;
            var size = Game1.smallFont.MeasureString(label);
            if (size.X > rect.Width - 16)
                scale = Math.Max(0.6f, (rect.Width - 16) / size.X);
            Utility.drawTextWithShadow(b, label, Game1.smallFont,
                new Vector2(rect.X + (rect.Width - size.X * scale) / 2f, rect.Y + (rect.Height - size.Y * scale) / 2f),
                active ? Color.Black : Color.SaddleBrown, scale);
        }
    }

    private Rectangle GetFarmerContentArea()
    {
        var top = yPositionOnScreen + HeaderTitleH + 12;
        return new Rectangle(xPositionOnScreen + 24, top, width - 48, yPositionOnScreen + height - 16 - top);
    }

    private Rectangle GetCatalogContentArea()
    {
        var top = yPositionOnScreen + HeaderTitleH + 8;
        return new Rectangle(xPositionOnScreen + 40, top, width - 80, yPositionOnScreen + height - 28 - top);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect,
            new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
            Color.Black * 0.72f);
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        DrawTitle(b);
        DrawTopTabs(b);

        switch (_tab)
        {
            case 0:
                _farmerPanel.Draw(b);
                break;
            case 1:
                DrawCategoryBar(b);
                DrawNpcGrid(b);
                break;
            case 2 when _galleryPane != null:
                _galleryPane.Draw(b);
                break;
            case 2:
                DrawGalleryUnavailable(b);
                break;
        }

        if (!string.IsNullOrWhiteSpace(_hoverText))
            drawHoverText(b, _hoverText, Game1.smallFont);

        drawMouse(b);
    }

    private void DrawTitle(SpriteBatch b)
    {
        var key = _tab switch
        {
            0 => "title.farmer",
            2 => "title.gallery",
            _ => "title.personalities"
        };
        var title = _i18n.Get(key).ToString();
        var size = Game1.dialogueFont.MeasureString(title);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
            new Vector2(xPositionOnScreen + (width - size.X) / 2f, yPositionOnScreen + 16),
            Color.Black);
    }

    private static readonly string[] TabKeys = { "tab.farmer", "tab.personalities", "tab.gallery" };

    private void DrawTopTabs(SpriteBatch b)
    {
        // Draw inactive tabs first so the active one overlaps its neighbours cleanly.
        for (var i = 0; i < TabKeys.Length; i++)
            if (i != _tab)
                DrawTab(b, i, active: false);

        DrawTab(b, _tab, active: true);
    }

    private void DrawTab(SpriteBatch b, int index, bool active)
    {
        var rect = GetTopTabRect(index);
        EditorTheme.DrawFrame(b, rect, active ? TabActive : TabInactive);

        // Icon on the left, vertically centred.
        var (texture, source) = GetTabIcon(index);
        const int iconH = 24;
        var scale = iconH / (float)source.Height;
        var iconW = (int)(source.Width * scale);
        var iconX = rect.X + 16;
        var iconY = rect.Y + (rect.Height - iconH) / 2;
        b.Draw(texture, new Rectangle(iconX, iconY, iconW, iconH), source, Color.White);

        // Label centred in the space to the right of the icon.
        var label = _i18n.Get(TabKeys[index]).ToString();
        var textLeft = iconX + iconW + 8;
        var textAreaW = rect.Right - 12 - textLeft;
        var size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(textLeft + (textAreaW - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            active ? Color.Black : Color.SaddleBrown);
    }

    // Themed game icons (respect UI-reskin mods). Source rects are easy to swap.
    private static (Texture2D texture, Rectangle source) GetTabIcon(int index) => index switch
    {
        0 => (Game1.mouseCursors, new Rectangle(211, 428, 7, 6)),   // heart → Farmer backstory
        2 => (Game1.mouseCursors, new Rectangle(229, 410, 14, 14)), // gift  → Gallery (shared presets)
        _ => (Game1.mouseCursors, new Rectangle(346, 392, 8, 8)),   // star  → Personalities
    };

    private Rectangle GetTopTabRect(int index)
    {
        // Raised, browser-style tabs sitting on the window's top edge (they overlap
        // the frame slightly so the strip reads as attached to the panel).
        var tabWidth = Math.Min(232, (width - 48) / 3);
        var y = yPositionOnScreen - TabStripH + 10;
        var startX = xPositionOnScreen + 24;
        return new Rectangle(startX + index * (tabWidth + 8), y, tabWidth, TabStripH);
    }

    private void DrawNpcGrid(SpriteBatch b)
    {
        var npcs = CurrentNpcs;
        var layout = GetNpcGridLayout();
        var rows = (npcs.Length + layout.Columns - 1) / layout.Columns;
        var totalHeight = Math.Max(0, rows * CardSize + Math.Max(0, rows - 1) * CardRowGap);
        _maxScroll = Math.Max(0, totalHeight - _npcGridArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);

        var previousScissor = b.GraphicsDevice.ScissorRectangle;
        var previousRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = _npcGridArea;

        for (var i = 0; i < npcs.Length; i++)
        {
            var rect = GetNpcCardRect(i, layout);
            if (rect.Bottom < _npcGridArea.Top || rect.Top > _npcGridArea.Bottom)
                continue;
            DrawNpcCard(b, npcs[i], rect);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        b.GraphicsDevice.ScissorRectangle = previousScissor;

        if (_maxScroll > 0)
            DrawScrollbar(b, _npcGridArea, _scrollY, _maxScroll);
    }

    private (int Columns, int Gap, int StartX) GetNpcGridLayout()
    {
        var columns = Math.Clamp((_npcGridArea.Width + 40) / (CardSize + 40), 1, 4);
        var gap = columns > 1 ? Math.Min(MaxCardGap, (_npcGridArea.Width - columns * CardSize) / (columns - 1)) : 0;
        var used = columns * CardSize + Math.Max(0, columns - 1) * gap;
        return (columns, gap, _npcGridArea.X + (_npcGridArea.Width - used) / 2);
    }

    private Rectangle GetNpcCardRect(int index, (int Columns, int Gap, int StartX) layout)
    {
        var column = index % layout.Columns;
        var row = index / layout.Columns;
        return new Rectangle(
            layout.StartX + column * (CardSize + layout.Gap),
            _npcGridArea.Y + row * (CardSize + CardRowGap) - _scrollY,
            CardSize,
            CardSize);
    }

    private void DrawNpcCard(SpriteBatch b, string npcName, Rectangle rect)
    {
        EditorTheme.DrawFrame(b, rect, CardBackground);

        DrawNpcDisabledCheckbox(b, npcName, rect);

        var displayName = _displayNames.GetValueOrDefault(npcName, npcName);
        var nameColor = _store.HasOverride(npcName) ? new Color(55, 120, 45) : Color.Black;
        var nameSize = Game1.smallFont.MeasureString(displayName);
        var checkbox = GetNpcDisabledCheckboxRect(rect);
        var nameArea = new Rectangle(checkbox.Right + 4, rect.Y + 6, rect.Right - checkbox.Right - 12, 28);
        var nameScale = nameSize.X > nameArea.Width ? Math.Max(0.65f, nameArea.Width / nameSize.X) : 1f;
        Utility.drawTextWithShadow(b, displayName, Game1.smallFont,
            new Vector2(nameArea.X + (nameArea.Width - nameSize.X * nameScale) / 2f, rect.Y + 8), nameColor, nameScale);

        var portraitSize = Math.Min(148, rect.Width - 24);
        var portraitRect = new Rectangle(rect.X + (rect.Width - portraitSize) / 2, rect.Bottom - portraitSize, portraitSize, portraitSize);
        PortraitDraw.Draw(b, portraitRect, npcName, _portraits.GetValueOrDefault(npcName));

        if (_store.Get(npcName)?.HasCharacterDataOverride == true)
            EditorTheme.DrawCharacterDataBadge(b, rect, _i18n.Get("indicator.character_data").ToString());
    }

    private static Rectangle GetNpcDisabledCheckboxRect(Rectangle cardRect)
        => new(cardRect.X + 8, cardRect.Y + 8, 22, 22);

    private void DrawNpcDisabledCheckbox(SpriteBatch b, string npcName, Rectangle cardRect)
    {
        var checkbox = GetNpcDisabledCheckboxRect(cardRect);
        b.Draw(Game1.staminaRect, checkbox, new Color(255, 248, 230));
        DrawBorder(b, checkbox, Border, 2);

        if (!_disabledNpcs.Contains(npcName))
            return;

        var checkColor = new Color(165, 45, 35);
        DrawLine(b, new Vector2(checkbox.X + 5, checkbox.Y + 12), new Vector2(checkbox.X + 10, checkbox.Y + 17), checkColor, 3);
        DrawLine(b, new Vector2(checkbox.X + 10, checkbox.Y + 17), new Vector2(checkbox.X + 18, checkbox.Y + 6), checkColor, 3);
    }

    private static void DrawLine(SpriteBatch b, Vector2 start, Vector2 end, Color color, int thickness)
    {
        var delta = end - start;
        b.Draw(Game1.staminaRect, start, null, color, (float)Math.Atan2(delta.Y, delta.X), Vector2.Zero,
            new Vector2(delta.Length(), thickness), SpriteEffects.None, 1f);
    }

    private void DrawGalleryUnavailable(SpriteBatch b)
    {
        var area = GetCatalogContentArea();
        var message = _i18n.Get("gallery.browse.empty").ToString();
        var size = Game1.smallFont.MeasureString(message);
        Utility.drawTextWithShadow(b, message, Game1.smallFont,
            new Vector2(area.X + (area.Width - size.X) / 2f, area.Y + 80), Color.SaddleBrown);
    }

    internal static void DrawBorder(SpriteBatch b, Rectangle rect, Color color, int thickness)
    {
        b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        b.Draw(Game1.staminaRect, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    internal static void DrawScrollbar(SpriteBatch b, Rectangle area, int scroll, int maxScroll)
        => EditorTheme.DrawScrollbar(b, area, scroll, maxScroll);

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!GetTopTabRect(i).Contains(x, y))
                continue;
            SetTab(i);
            Game1.playSound("smallSelect");
            return;
        }

        if (_tab == 0)
        {
            _farmerPanel.receiveLeftClick(x, y);
            return;
        }

        if (_tab == 2)
        {
            _galleryPane?.receiveLeftClick(x, y);
            return;
        }

        // Category buttons (above the grid).
        for (var i = 0; i < _categories.Length; i++)
        {
            if (!GetCategoryTabRect(i).Contains(x, y))
                continue;
            if (_activeCategory != i)
            {
                _activeCategory = i;
                _scrollY = 0;
                Game1.playSound("smallSelect");
            }
            return;
        }

        var npcs = CurrentNpcs;
        var layout = GetNpcGridLayout();
        for (var i = 0; i < npcs.Length; i++)
        {
            var rect = GetNpcCardRect(i, layout);
            if (!rect.Contains(x, y) || !_npcGridArea.Contains(x, y))
                continue;

            if (GetNpcDisabledCheckboxRect(rect).Contains(x, y))
            {
                ToggleNpcDisabled(npcs[i]);
                return;
            }

            OpenNpcEditor(npcs[i]);
            Game1.playSound("smallSelect");
            return;
        }
    }

    private void ToggleNpcDisabled(string npcName)
    {
        var disabled = !_disabledNpcs.Contains(npcName);
        try
        {
            if (!_api.SetNpcDisabled(npcName, disabled))
            {
                _monitor.Log($"AliveNpcs rejected the disabled-state update for '{npcName}'.", LogLevel.Warn);
                Game1.playSound("cancel");
                return;
            }

            if (disabled)
                _disabledNpcs.Add(npcName);
            else
                _disabledNpcs.Remove(npcName);

            Game1.playSound("smallSelect");
        }
        catch (Exception ex)
        {
            _monitor.Log($"Could not update AliveNpcs disabled state for '{npcName}': {ex.Message}", LogLevel.Warn);
            Game1.playSound("cancel");
        }
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        _hoverText = "";

        if (_tab != 1 || !_npcGridArea.Contains(x, y))
            return;

        var npcs = CurrentNpcs;
        var layout = GetNpcGridLayout();
        for (var i = 0; i < npcs.Length; i++)
        {
            var rect = GetNpcCardRect(i, layout);
            if (GetNpcDisabledCheckboxRect(rect).Contains(x, y))
            {
                _hoverText = _i18n.Get("npc.disable.tooltip").ToString();
                return;
            }
        }
    }

    private void SetTab(int tab)
    {
        if (_tab == tab)
            return;
        _farmerPanel.Unsubscribe();
        _galleryPane?.Unsubscribe();
        _tab = tab;
        _scrollY = 0;
    }

    private void OpenNpcEditor(string npcName)
    {
        Game1.activeClickableMenu = new PersonalityEditModal(
            npcName,
            _displayNames.GetValueOrDefault(npcName, npcName),
            _defaults.GetValueOrDefault(npcName, ""),
            _portraits.GetValueOrDefault(npcName),
            _store,
            _presetStore,
            _api,
            _config,
            _galleryService,
            _monitor,
            _i18n,
            () => Game1.activeClickableMenu = this);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (_tab == 0)
        {
            _farmerPanel.receiveScrollWheelAction(direction);
            return;
        }
        if (_tab == 2)
        {
            _galleryPane?.receiveScrollWheelAction(direction);
            return;
        }
        _scrollY = Math.Clamp(_scrollY - direction, 0, Math.Max(0, _maxScroll));
    }

    public override void receiveKeyPress(Keys key)
    {
        if (_tab == 0)
        {
            if (key == Keys.Escape)
            {
                _farmerPanel.Unsubscribe();
                exitThisMenu();
                return;
            }
            _farmerPanel.receiveKeyPress(key);
            return;
        }

        if (_tab == 2 && _galleryPane?.receiveKeyPress(key) == true)
            return;

        if (key == Keys.Escape)
            exitThisMenu();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        RecalculateLayout();
    }

    private void NotifyAliveNpcsReload()
    {
        try { _api.ReloadCustomPersonalities(); }
        catch (Exception ex) { _monitor.Log($"Reload notify failed: {ex.Message}", LogLevel.Warn); }
    }

    private void NotifyCharacterSheetReload()
    {
        try { _api.ReloadCharacterSheet(); }
        catch (Exception ex) { _monitor.Log($"Character sheet reload notify failed: {ex.Message}", LogLevel.Warn); }
    }

    // Apply a farmer gallery preset by writing it back to the character sheet through
    // AliveNpcs (maps the preset's generic slots to the backstory fields).
    private void ApplyFarmerPreset(Models.NpcOverrideEntry entry)
    {
        try
        {
            _api.UpdateCharacterSheet(
                entry.CanonicalPersonality ?? "",  // Who am I
                entry.Lore ?? "",                  // Why moved here
                entry.SocialTags ?? "",            // Extra info
                entry.Appearance ?? "");           // At-a-glance
            _farmerPanel.LoadFromStore();          // refresh the Farmer tab UI
        }
        catch (Exception ex)
        {
            _monitor.Log($"Apply farmer preset failed: {ex.Message}", LogLevel.Warn);
        }
    }

    protected override void cleanupBeforeExit()
    {
        _farmerPanel.Unsubscribe();
        _galleryPane?.Unsubscribe();
        base.cleanupBeforeExit();
    }
}
