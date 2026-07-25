using AliveNpcsPersonalityEditor.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>Community catalog rendered inside the Catalog tab.</summary>
public sealed class GalleryPane
{
    private readonly GalleryService _service;
    private readonly PersonalityStore _store;
    private readonly Dictionary<string, Texture2D?> _portraits;
    private readonly ITranslationHelper _i18n;
    private readonly IMonitor _monitor;
    private readonly Func<Rectangle> _getContentArea;
    private readonly Action _onImported;
    private readonly Action<NpcOverrideEntry>? _applyFarmerSheet;
    private readonly PresetStore? _presetStore;
    private readonly List<PresetMetadata> _presets = new();
    private readonly SearchSubscriber _search = new();

    private int _currentPage;
    private bool _loading;
    private bool _hasMore = true;
    private string? _searchQuery;
    private int _scrollY;
    private int _maxScroll;
    private int _mode; // 0 = Discover (server), 1 = Local (saved presets)
    private Rectangle _discoverButton;
    private Rectangle _localButton;
    private Rectangle _npcFilterButton;
    private Rectangle _searchBox;
    private Rectangle _searchButton;
    private GalleryPreviewModal? _preview;
    private CharacterDataWarning? _warning;

    // NPC filter dropdown (replaces the old "All" button).
    private readonly List<string> _npcFilterNames = new();
    private int _npcFilterIndex;   // 0 = All NPCs
    private bool _npcFilterOpen;
    private int _npcDropScroll;
    private const int DropItemH = 34;

    // In-UI server URL editor (avoids a GMCM trip).
    private readonly Action<string>? _onServerUrlSaved;
    private Rectangle _serverButton;
    private bool _editingServer;
    private readonly SearchSubscriber _urlSub = new() { MaxLength = 300 };
    private Rectangle _urlBox;
    private Rectangle _urlSaveButton;
    private Rectangle _urlCancelButton;

    // Local-preset (Manage) mode state.
    private readonly List<(string Id, string Npc, NpcOverrideEntry Entry)> _localPresets = new();

    private const int DiscoverH = 44;
    private const int SearchH = 40;
    private const int CardSize = 194;
    private const int CardRowGap = 32;
    private const int MaxCardGap = 40;   // cap the horizontal gap so wide grids don't spread cards apart
    private static readonly Color Active = new(235, 155, 45);
    private static readonly Color Paper = new(255, 248, 234);
    private static readonly Color Border = new(125, 60, 40);
    private static readonly Color CardBg = new(222, 195, 153); // original card frame tint

    public GalleryPane(
        GalleryService service,
        PersonalityStore store,
        Dictionary<string, Texture2D?> portraits,
        ITranslationHelper i18n,
        IMonitor monitor,
        Func<Rectangle> getContentArea,
        string serverUrl,
        PresetStore? presetStore = null,
        Action? onImported = null,
        Action<NpcOverrideEntry>? applyFarmerSheet = null,
        Action<string>? onServerUrlSaved = null)
    {
        _service = service;
        _store = store;
        _portraits = portraits;
        _i18n = i18n;
        _monitor = monitor;
        _getContentArea = getContentArea;
        _onImported = onImported ?? (() => { });
        _applyFarmerSheet = applyFarmerSheet;
        _onServerUrlSaved = onServerUrlSaved;
        _presetStore = presetStore;
        if (!string.IsNullOrWhiteSpace(serverUrl))
            _service.SetBaseUrl(serverUrl);

        _npcFilterNames.Add(_i18n.Get("gallery.filter.all_npcs").ToString());
        _npcFilterNames.AddRange(portraits.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

        _ = FetchPageAsync();
    }

    private string? NpcFilter =>
        _npcFilterIndex > 0 && _npcFilterIndex < _npcFilterNames.Count ? _npcFilterNames[_npcFilterIndex] : null;

    private async Task FetchPageAsync()
    {
        if (_loading || !_hasMore)
            return;
        _loading = true;
        try
        {
            var response = await _service.SearchPresetsAsync(_searchQuery, _currentPage + 1, npcFilter: NpcFilter);
            if (response == null)
            {
                if (_currentPage == 0)
                    _hasMore = false;
                return;
            }

            _presets.AddRange(response.Presets);
            _currentPage = response.Page;
            _hasMore = response.Presets.Count >= response.Limit;
            UpdateMaxScroll();
        }
        catch (Exception ex)
        {
            _monitor.Log($"Gallery fetch error: {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SearchAsync()
    {
        _loading = true;
        _presets.Clear();
        _currentPage = 0;
        _hasMore = true;
        _scrollY = 0;
        _searchQuery = string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim();
        try
        {
            var response = await _service.SearchPresetsAsync(_searchQuery, 1, npcFilter: NpcFilter);
            if (response != null)
            {
                _presets.AddRange(response.Presets);
                _currentPage = response.Page;
                _hasMore = response.Presets.Count >= response.Limit;
            }
            else
            {
                _hasMore = false;
            }
            UpdateMaxScroll();
        }
        catch (Exception ex)
        {
            _monitor.Log($"Gallery search error: {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            _loading = false;
        }
    }

    private (Rectangle GridArea, int Columns, int Gap, int StartX) GetGridLayout()
    {
        var area = _getContentArea();
        var gridTop = area.Y + DiscoverH + 92;
        var gridArea = new Rectangle(area.X, gridTop, area.Width, area.Bottom - gridTop);
        var columns = Math.Clamp((gridArea.Width + 40) / (CardSize + 40), 1, 4);
        var gap = columns > 1 ? Math.Min(MaxCardGap, (gridArea.Width - columns * CardSize) / (columns - 1)) : 0;
        var used = columns * CardSize + Math.Max(0, columns - 1) * gap;
        return (gridArea, columns, gap, gridArea.X + (gridArea.Width - used) / 2);
    }

    private Rectangle GetCardRect(int index, (Rectangle GridArea, int Columns, int Gap, int StartX) layout)
    {
        var column = index % layout.Columns;
        var row = index / layout.Columns;
        return new Rectangle(
            layout.StartX + column * (CardSize + layout.Gap),
            layout.GridArea.Y + row * (CardSize + CardRowGap) - _scrollY,
            CardSize,
            CardSize);
    }

    public void Draw(SpriteBatch b)
    {
        var area = _getContentArea();

        // Mode toggle: Discover (community server) | Local (saved presets).
        const int modeW = 170;
        _discoverButton = new Rectangle(area.X, area.Y, modeW, DiscoverH);
        _localButton = new Rectangle(area.X + modeW + 12, area.Y, modeW, DiscoverH);
        DrawButton(b, _discoverButton, _i18n.Get("tab.discover"), _mode == 0 ? Active : Paper);
        DrawButton(b, _localButton, _i18n.Get("tab.local"), _mode == 1 ? Active : Paper);

        _serverButton = new Rectangle(area.Right - 150, area.Y, 150, DiscoverH);
        DrawButton(b, _serverButton, _i18n.Get("gallery.server.button"), Paper);

        var controlsY = area.Y + DiscoverH + 26;
        _npcFilterButton = new Rectangle(area.X, controlsY, 200, SearchH);
        DrawFilterButton(b);

        _searchButton = new Rectangle(area.Right - 120, controlsY, 120, SearchH);
        _searchBox = new Rectangle(_npcFilterButton.Right + 16, controlsY,
            _searchButton.X - 16 - (_npcFilterButton.Right + 16), SearchH);
        EditorTheme.DrawInputFrame(b, _searchBox, _search.Selected);

        var textX = _searchBox.X + EditorTheme.FramePad;
        var searchText = _search.Text;
        if (string.IsNullOrEmpty(searchText) && !_search.Selected)
        {
            searchText = _i18n.Get("gallery.browse.search_placeholder");
            b.DrawString(Game1.smallFont, searchText,
                new Vector2(textX, _searchBox.Y + 8), Color.Gray);
        }
        else
        {
            b.DrawString(Game1.smallFont, searchText,
                new Vector2(textX, _searchBox.Y + 8), Color.Black);
        }

        if (_search.Selected && DateTime.UtcNow.Millisecond < 500)
        {
            var cursorX = textX + (int)Game1.smallFont.MeasureString(_search.Text).X;
            b.Draw(Game1.staminaRect, new Rectangle(cursorX, _searchBox.Y + 7, 2, SearchH - 14), Color.Black);
        }
        DrawButton(b, _searchButton, _i18n.Get("gallery.browse.search"), Paper);

        if (_mode == 1)
            DrawLocalCards(b);
        else
            DrawCards(b);

        DrawNpcDropdown(b);
        _preview?.Draw(b);
        _warning?.Draw(b);
        DrawServerEditor(b);
    }

    private void DrawServerEditor(SpriteBatch b)
    {
        if (!_editingServer)
            return;

        var vp = Game1.uiViewport;
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        var w = Math.Min(600, vp.Width - 80);
        const int h = 210;
        var box = new Rectangle((vp.Width - w) / 2, (vp.Height - h) / 2, w, h);
        EditorTheme.DrawFrame(b, box, Color.White);

        Utility.drawTextWithShadow(b, _i18n.Get("gallery.config.server_url").ToString(), Game1.smallFont,
            new Vector2(box.X + 24, box.Y + 20), Color.Black);

        _urlBox = new Rectangle(box.X + 24, box.Y + 58, box.Width - 48, 46);
        EditorTheme.DrawInputFrame(b, _urlBox, _urlSub.Selected);
        b.DrawString(Game1.smallFont, _urlSub.Text,
            new Vector2(_urlBox.X + EditorTheme.FramePad, _urlBox.Y + 11), Color.Black);
        if (_urlSub.Selected && DateTime.UtcNow.Millisecond < 500)
        {
            var cx = _urlBox.X + EditorTheme.FramePad + (int)Game1.smallFont.MeasureString(_urlSub.Text).X;
            b.Draw(Game1.staminaRect, new Rectangle(cx, _urlBox.Y + 10, 2, 26), Color.Black);
        }

        _urlCancelButton = new Rectangle(box.X + 24, box.Bottom - 24 - 48, 170, 48);
        _urlSaveButton = new Rectangle(box.Right - 24 - 170, box.Bottom - 24 - 48, 170, 48);
        DrawButton(b, _urlCancelButton, _i18n.Get("button.cancel"), new Color(225, 125, 85));
        DrawButton(b, _urlSaveButton, _i18n.Get("button.save"), new Color(120, 190, 100));
    }

    private void OpenServerEditor()
    {
        Unsubscribe();
        _npcFilterOpen = false;
        _editingServer = true;
        _urlSub.Text = _service.BaseUrl;
        _urlSub.Selected = true;
        Game1.keyboardDispatcher.Subscriber = _urlSub;
    }

    private void SaveServerUrl()
    {
        var url = _urlSub.Text.Trim();
        if (!string.IsNullOrWhiteSpace(url))
        {
            _service.SetBaseUrl(url);
            _onServerUrlSaved?.Invoke(url);         // persist to config
            _presets.Clear();
            _currentPage = 0;
            _hasMore = true;
            _scrollY = 0;
            _ = FetchPageAsync();                   // reload from the new server
            Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.server.saved"), 3));
        }
        CloseServerEditor();
    }

    private void CloseServerEditor()
    {
        _editingServer = false;
        _urlSub.Selected = false;
        if (Game1.keyboardDispatcher.Subscriber == _urlSub)
            Game1.keyboardDispatcher.Subscriber = null;
    }

    private void DrawFilterButton(SpriteBatch b)
    {
        EditorTheme.DrawFrame(b, _npcFilterButton, _npcFilterOpen ? Active : Paper);
        var label = _npcFilterIndex > 0 ? _npcFilterNames[_npcFilterIndex] : _i18n.Get("gallery.filter.all_npcs").ToString();
        b.DrawString(Game1.smallFont, label,
            new Vector2(_npcFilterButton.X + EditorTheme.FramePad, _npcFilterButton.Y + 9), Color.Black * 0.9f);
        b.DrawString(Game1.smallFont, _npcFilterOpen ? "^" : "v",
            new Vector2(_npcFilterButton.Right - 22, _npcFilterButton.Y + 9), Color.Black * 0.6f);
    }

    private Rectangle GetDropdownRect()
    {
        var area = _getContentArea();
        var fullH = _npcFilterNames.Count * DropItemH;
        var availH = Math.Max(DropItemH * 3, area.Bottom - (_npcFilterButton.Bottom + 4));
        var h = Math.Min(fullH, availH);
        return new Rectangle(_npcFilterButton.X, _npcFilterButton.Bottom + 4, Math.Max(_npcFilterButton.Width, 220), h);
    }

    private void DrawNpcDropdown(SpriteBatch b)
    {
        if (!_npcFilterOpen)
            return;

        var dd = GetDropdownRect();
        EditorTheme.DrawFrame(b, dd, new Color(250, 240, 220));
        var inner = new Rectangle(dd.X + 8, dd.Y + 6, dd.Width - 16, dd.Height - 12);

        var fullH = _npcFilterNames.Count * DropItemH;
        var maxScroll = Math.Max(0, fullH - inner.Height);
        _npcDropScroll = Math.Clamp(_npcDropScroll, 0, maxScroll);

        var prevScissor = b.GraphicsDevice.ScissorRectangle;
        var prevRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = inner;

        for (var i = 0; i < _npcFilterNames.Count; i++)
        {
            var itemY = inner.Y + i * DropItemH - _npcDropScroll;
            if (itemY + DropItemH < inner.Y || itemY > inner.Bottom)
                continue;
            if (i == _npcFilterIndex)
                b.Draw(Game1.staminaRect, new Rectangle(inner.X, itemY, inner.Width, DropItemH), Active * 0.35f);
            b.DrawString(Game1.smallFont, _npcFilterNames[i],
                new Vector2(inner.X + 6, itemY + 6), i == _npcFilterIndex ? Color.Black : Color.Black * 0.75f);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, prevRasterizer);
        b.GraphicsDevice.ScissorRectangle = prevScissor;

        if (maxScroll > 0)
            PersonalityEditorMenu.DrawScrollbar(b, inner, _npcDropScroll, maxScroll);
    }

    private void ApplyNpcFilter()
    {
        _scrollY = 0;
        if (_mode == 0)
            _ = SearchAsync();  // re-query the server with the new NPC filter
        // Local mode filters live in GetFilteredLocal().
    }

    private void DrawCards(SpriteBatch b)
    {
        var layout = GetGridLayout();
        if (_presets.Count == 0)
        {
            var key = _loading ? "gallery.browse.loading" : "gallery.browse.empty";
            var message = _i18n.Get(key).ToString();
            var size = Game1.smallFont.MeasureString(message);
            Utility.drawTextWithShadow(b, message, Game1.smallFont,
                new Vector2(layout.GridArea.X + (layout.GridArea.Width - size.X) / 2f, layout.GridArea.Y + 40),
                Color.SaddleBrown);
            return;
        }

        var previousScissor = b.GraphicsDevice.ScissorRectangle;
        var previousRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = layout.GridArea;

        for (var i = 0; i < _presets.Count; i++)
        {
            var rect = GetCardRect(i, layout);
            if (rect.Bottom < layout.GridArea.Top || rect.Top > layout.GridArea.Bottom)
                continue;
            DrawCard(b, _presets[i], rect);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        b.GraphicsDevice.ScissorRectangle = previousScissor;

        if (_maxScroll > 0)
            PersonalityEditorMenu.DrawScrollbar(b, layout.GridArea, _scrollY, _maxScroll);
    }

    private void DrawCard(SpriteBatch b, PresetMetadata preset, Rectangle rect)
    {
        EditorTheme.DrawFrame(b, rect, CardBg);

        var nameSize = Game1.smallFont.MeasureString(preset.NpcName);
        Utility.drawTextWithShadow(b, preset.NpcName, Game1.smallFont,
            new Vector2(rect.X + (rect.Width - nameSize.X) / 2f, rect.Y + 8), Color.Black);

        const int portraitSize = 64;
        var portraitRect = new Rectangle(rect.X + (rect.Width - portraitSize) / 2, rect.Y + 34, portraitSize, portraitSize);
        PortraitDraw.Draw(b, portraitRect, preset.NpcName, _portraits.GetValueOrDefault(preset.NpcName));

        // Description preview (two lines), same as the Local cards.
        var preview = string.IsNullOrWhiteSpace(preset.Preview) ? "" : preset.Preview;
        if (!string.IsNullOrWhiteSpace(preview))
        {
            var wrapped = Game1.parseText(preview, Game1.smallFont, rect.Width - 20);
            var lines = wrapped.Split('\n');
            if (lines.Length > 2)
                wrapped = string.Join("\n", lines.Take(2)).TrimEnd() + "…";
            b.DrawString(Game1.smallFont, wrapped, new Vector2(rect.X + 10, rect.Y + 106), Color.Black * 0.7f);
        }

        // Author centered across the bottom frame, yellow so it reads on the brown border.
        var author = _i18n.Get("gallery.card.by", new { author = preset.Author }).ToString();
        var aSize = Game1.tinyFont.MeasureString(author);
        var aPos = new Vector2(rect.X + (rect.Width - aSize.X) / 2f, rect.Bottom - aSize.Y - 8);
        Utility.drawTextWithShadow(b, author, Game1.tinyFont, aPos, Color.Yellow);

        if (preset.HasCharacterData)
            EditorTheme.DrawCharacterDataBadge(b, rect, _i18n.Get("indicator.character_data").ToString());
    }

    private static void DrawButton(SpriteBatch b, Rectangle rect, string label, Color background)
        => EditorTheme.DrawButton(b, rect, label, background, Color.Black);

    // ── Local presets (Manage) mode ─────────────────────────────────────────

    private void ReloadLocal()
    {
        _localPresets.Clear();
        if (_presetStore == null)
            return;
        foreach (var (id, entry) in _presetStore.LoadAll()
                     .OrderBy(p => p.Entry.NpcName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            _localPresets.Add((id, entry.NpcName, entry));
    }

    private List<(string Id, string Npc, NpcOverrideEntry Entry)> GetFilteredLocal()
    {
        IEnumerable<(string Id, string Npc, NpcOverrideEntry Entry)> list = _localPresets;

        var npc = NpcFilter;
        if (npc != null)
            list = list.Where(p => string.Equals(p.Npc, npc, StringComparison.OrdinalIgnoreCase));

        var query = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.ToLowerInvariant();
            list = list.Where(p =>
                p.Npc.ToLowerInvariant().Contains(q)
                || (p.Entry.CanonicalPersonality?.ToLowerInvariant().Contains(q) ?? false)
                || (p.Entry.Lore?.ToLowerInvariant().Contains(q) ?? false)
                || (p.Entry.SocialTags?.ToLowerInvariant().Contains(q) ?? false)
                || (p.Entry.SubmissionCredit?.ToLowerInvariant().Contains(q) ?? false));
        }
        return list.ToList();
    }

    private void DrawLocalCards(SpriteBatch b)
    {
        var layout = GetGridLayout();
        var list = GetFilteredLocal();

        if (list.Count == 0)
        {
            var message = _i18n.Get(_localPresets.Count == 0 ? "gallery.preset.none" : "gallery.browse.empty").ToString();
            var size = Game1.smallFont.MeasureString(message);
            Utility.drawTextWithShadow(b, message, Game1.smallFont,
                new Vector2(layout.GridArea.X + (layout.GridArea.Width - size.X) / 2f, layout.GridArea.Y + 40),
                Color.SaddleBrown);
            return;
        }

        var rows = (list.Count + layout.Columns - 1) / layout.Columns;
        var contentHeight = rows * CardSize + Math.Max(0, rows - 1) * CardRowGap;
        _maxScroll = Math.Max(0, contentHeight - layout.GridArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);

        var previousScissor = b.GraphicsDevice.ScissorRectangle;
        var previousRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = layout.GridArea;

        for (var i = 0; i < list.Count; i++)
        {
            var rect = GetCardRect(i, layout);
            if (rect.Bottom < layout.GridArea.Top || rect.Top > layout.GridArea.Bottom)
                continue;
            DrawLocalCard(b, list[i], rect);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        b.GraphicsDevice.ScissorRectangle = previousScissor;

        if (_maxScroll > 0)
            PersonalityEditorMenu.DrawScrollbar(b, layout.GridArea, _scrollY, _maxScroll);
    }

    private void DrawLocalCard(SpriteBatch b, (string Id, string Npc, NpcOverrideEntry Entry) preset, Rectangle rect)
    {
        EditorTheme.DrawFrame(b, rect, CardBg);

        var nameSize = Game1.smallFont.MeasureString(preset.Npc);
        Utility.drawTextWithShadow(b, preset.Npc, Game1.smallFont,
            new Vector2(rect.X + (rect.Width - nameSize.X) / 2f, rect.Y + 8), Color.Black);

        const int portraitSize = 64;
        var portraitRect = new Rectangle(rect.X + (rect.Width - portraitSize) / 2, rect.Y + 34, portraitSize, portraitSize);
        PortraitDraw.Draw(b, portraitRect, preset.Npc, _portraits.GetValueOrDefault(preset.Npc));

        var preview = !string.IsNullOrWhiteSpace(preset.Entry.SubmissionCredit)
            ? preset.Entry.SubmissionCredit
            : (!string.IsNullOrWhiteSpace(preset.Entry.CanonicalPersonality)
                ? preset.Entry.CanonicalPersonality
                : "");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            var wrapped = Game1.parseText(preview, Game1.smallFont, rect.Width - 20);
            var lines = wrapped.Split('\n');
            if (lines.Length > 2)
                wrapped = string.Join("\n", lines.Take(2)).TrimEnd() + "…";
            b.DrawString(Game1.smallFont, wrapped, new Vector2(rect.X + 10, rect.Y + 108), Color.Black * 0.7f);
        }

        if (preset.Entry.HasCharacterDataOverride)
            EditorTheme.DrawCharacterDataBadge(b, rect, _i18n.Get("indicator.character_data").ToString());
    }

    private async void UploadLocal(string npc, NpcOverrideEntry entry)
    {
        Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.upload.progress"), 3));
        try
        {
            var author = Game1.player?.Name ?? "Anonymous";
            var ok = await _service.UploadPresetAsync(npc, entry, author);
            Game1.addHUDMessage(new HUDMessage(_i18n.Get(ok ? "gallery.upload.success" : "gallery.upload.failed"), ok ? 4 : 3));
        }
        catch (Exception ex)
        {
            _monitor.Log($"Local preset upload failed: {ex.Message}", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.upload.failed"), 3));
        }
    }

    private void SwitchMode(int mode)
    {
        if (_mode == mode)
            return;
        Unsubscribe();
        _mode = mode;
        _search.Text = "";
        _scrollY = 0;
        if (mode == 1)
            ReloadLocal();
        Game1.playSound("smallSelect");
    }

    public void receiveLeftClick(int x, int y)
    {
        if (_warning != null)
        {
            _warning.receiveLeftClick(x, y);
            return;
        }
        if (_preview != null)
        {
            _preview.receiveLeftClick(x, y);
            return;
        }

        // Server URL editor takes priority while open.
        if (_editingServer)
        {
            if (_urlBox.Contains(x, y))
            {
                _urlSub.Selected = true;
                Game1.keyboardDispatcher.Subscriber = _urlSub;
                return;
            }
            if (_urlSaveButton.Contains(x, y)) { SaveServerUrl(); return; }
            if (_urlCancelButton.Contains(x, y)) { CloseServerEditor(); return; }
            _urlSub.Selected = false;
            if (Game1.keyboardDispatcher.Subscriber == _urlSub)
                Game1.keyboardDispatcher.Subscriber = null;
            return;
        }

        // NPC filter dropdown takes priority while open.
        if (_npcFilterOpen)
        {
            var dd = GetDropdownRect();
            var inner = new Rectangle(dd.X + 8, dd.Y + 6, dd.Width - 16, dd.Height - 12);
            if (inner.Contains(x, y))
            {
                var idx = (y - inner.Y + _npcDropScroll) / DropItemH;
                if (idx >= 0 && idx < _npcFilterNames.Count && idx != _npcFilterIndex)
                {
                    _npcFilterIndex = idx;
                    ApplyNpcFilter();
                }
                _npcFilterOpen = false;
                Game1.playSound("smallSelect");
                return;
            }
            _npcFilterOpen = false; // click outside closes it
            return;
        }

        if (_serverButton.Contains(x, y)) { OpenServerEditor(); Game1.playSound("smallSelect"); return; }
        if (_discoverButton.Contains(x, y)) { SwitchMode(0); return; }
        if (_localButton.Contains(x, y)) { SwitchMode(1); return; }

        if (_npcFilterButton.Contains(x, y))
        {
            _npcFilterOpen = true;
            _npcDropScroll = 0;
            Unsubscribe();
            Game1.playSound("smallSelect");
            return;
        }

        if (_searchBox.Contains(x, y))
        {
            _search.Selected = true;
            Game1.keyboardDispatcher.Subscriber = _search;
            return;
        }
        if (_searchButton.Contains(x, y))
        {
            Unsubscribe();
            if (_mode == 0) _ = SearchAsync();
            Game1.playSound("smallSelect");
            return;
        }

        Unsubscribe();

        if (_mode == 1)
        {
            var localLayout = GetGridLayout();
            var list = GetFilteredLocal();
            for (var i = 0; i < list.Count; i++)
            {
                var rect = GetCardRect(i, localLayout);
                if (!localLayout.GridArea.Contains(x, y) || !rect.Contains(x, y))
                    continue;
                OpenLocalPreview(list[i]);
                Game1.playSound("smallSelect");
                return;
            }
            return;
        }

        var layout = GetGridLayout();
        for (var i = 0; i < _presets.Count; i++)
        {
            var rect = GetCardRect(i, layout);
            if (!layout.GridArea.Contains(x, y) || !rect.Contains(x, y))
                continue;
            _ = OpenPreviewAsync(_presets[i]);
            Game1.playSound("smallSelect");
            return;
        }
    }

    private async Task OpenPreviewAsync(PresetMetadata preset)
    {
        var data = await _service.DownloadPresetAsync(preset.Id);
        if (data == null)
        {
            Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.upload.failed"), 3));
            return;
        }

        var npc = preset.NpcName;
        var entry = data.Data;
        var actions = new List<GalleryPreviewModal.PreviewAction>
        {
            // Import: copy into the LOCAL gallery only (does not touch the live NPC).
            new(_i18n.Get("gallery.button.import").ToString(), new Color(120, 190, 100), () =>
                GuardCharacterData(entry, () =>
                {
                    _presetStore?.Save(npc, entry);
                    _ = _service.ReportDownloadAsync(preset.Id);
                    Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.preset.saved", new { npcName = npc }), 3));
                    Game1.playSound("coin");
                    _preview = null;
                })),
            // Import & Apply: save locally AND apply live (NPC personality or farmer sheet).
            new(_i18n.Get("gallery.button.import_apply").ToString(), new Color(90, 160, 80), () =>
                GuardCharacterData(entry, () =>
                {
                    _presetStore?.Save(npc, entry);
                    _ = _service.ReportDownloadAsync(preset.Id);
                    ApplyEntry(npc, entry);
                    Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.import.success", new { npcName = npc }), 3));
                    Game1.playSound("coin");
                    _preview = null;
                })),
        };
        if (preset.CanDelete)
            actions.Add(new(_i18n.Get("gallery.button.delete").ToString(), new Color(225, 125, 85),
                () => _ = DeleteServerPreset(preset.Id)));
        actions.Add(new(_i18n.Get("button.close").ToString(), new Color(255, 248, 234), () => _preview = null));

        _preview = new GalleryPreviewModal(
            npc,
            _i18n.Get("gallery.card.by", new { author = preset.Author }).ToString(),
            entry, _portraits, _i18n, () => _preview = null, actions);
    }

    private async Task DeleteServerPreset(string id)
    {
        var deleted = await _service.DeletePresetAsync(id);
        Game1.addHUDMessage(new HUDMessage(_i18n.Get(deleted ? "gallery.button.delete" : "gallery.upload.failed"), deleted ? 4 : 3));
        _preview = null;
        if (deleted)
        {
            _presets.Clear();
            _currentPage = 0;
            _hasMore = true;
            await FetchPageAsync();
        }
    }

    // Apply a preset live: farmer presets populate the character sheet; NPC presets
    // become that NPC's active personality override.
    private void ApplyEntry(string npc, NpcOverrideEntry entry)
    {
        if (entry.IsFarmer)
        {
            _applyFarmerSheet?.Invoke(entry);
            return;
        }
        _store.Set(npc, entry);
        _store.Save(npc);
        _onImported();
    }

    // Show a confirmation popup before importing/applying a preset that overrides the
    // game's Data/Characters; run the action directly when there is no such change.
    private void GuardCharacterData(NpcOverrideEntry entry, Action apply)
    {
        if (!entry.HasCharacterDataOverride)
        {
            apply();
            return;
        }
        _warning = new CharacterDataWarning(
            _i18n.Get("gallery.warning.title").ToString(),
            _i18n.Get("gallery.warning.character_data").ToString(),
            _i18n.Get("button.confirm").ToString(),
            _i18n.Get("button.cancel").ToString(),
            () => { _warning = null; apply(); },
            () => { _warning = null; });
    }

    private void OpenLocalPreview((string Id, string Npc, NpcOverrideEntry Entry) preset)
    {
        var npc = preset.Npc;
        var entry = preset.Entry;
        var subtitle = !string.IsNullOrWhiteSpace(entry.SubmissionCredit)
            ? _i18n.Get("gallery.card.by", new { author = entry.SubmissionCredit }).ToString()
            : _i18n.Get("tab.local").ToString();

        var actions = new List<GalleryPreviewModal.PreviewAction>
        {
            new(_i18n.Get("gallery.button.apply").ToString(), new Color(90, 160, 80), () =>
                GuardCharacterData(entry, () =>
                {
                    ApplyEntry(npc, entry);
                    Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.import.success", new { npcName = npc }), 3));
                    Game1.playSound("coin");
                    _preview = null;
                })),
            new(_i18n.Get("gallery.button.upload").ToString(), new Color(120, 190, 100), () =>
            {
                UploadLocal(npc, entry);
                _preview = null;
            }, Confirm: true, ConfirmLabel: _i18n.Get("button.confirm").ToString()),
            new(_i18n.Get("gallery.button.delete").ToString(), new Color(225, 125, 85), () =>
            {
                _presetStore?.Delete(preset.Id);
                ReloadLocal();
                Game1.playSound("trashcan");
                _preview = null;
            }),
            new(_i18n.Get("button.close").ToString(), new Color(255, 248, 234), () => _preview = null),
        };

        _preview = new GalleryPreviewModal(npc, subtitle, entry, _portraits, _i18n, () => _preview = null, actions);
    }

    public void receiveScrollWheelAction(int direction)
    {
        if (_warning != null)
            return;
        if (_editingServer)
            return;
        if (_preview != null)
        {
            _preview.receiveScrollWheelAction(direction);
            return;
        }
        if (_npcFilterOpen)
        {
            _npcDropScroll = Math.Max(0, _npcDropScroll - direction);
            return;
        }
        _scrollY = Math.Clamp(_scrollY - direction, 0, Math.Max(0, _maxScroll));
        if (_mode != 0)
            return;
        var layout = GetGridLayout();
        if (_hasMore && !_loading && _presets.Count > 0)
        {
            var lastRect = GetCardRect(_presets.Count - 1, layout);
            if (lastRect.Top < layout.GridArea.Bottom + 200)
                _ = FetchPageAsync();
        }
    }

    public bool receiveKeyPress(Keys key)
    {
        if (_editingServer)
        {
            if (key == Keys.Enter) SaveServerUrl();
            else if (key == Keys.Escape) CloseServerEditor();
            return true;
        }
        if (_warning != null)
        {
            if (key == Keys.Escape) _warning = null;
            return true;
        }
        if (_preview != null)
        {
            _preview.receiveKeyPress(key);
            return true;
        }
        if (!_search.Selected)
            return false;

        if (key == Keys.Enter)
        {
            Unsubscribe();
            if (_mode == 0) _ = SearchAsync();
        }
        else if (key == Keys.Escape)
        {
            Unsubscribe();
        }
        return true;
    }

    public void Unsubscribe()
    {
        _search.Selected = false;
        if (Game1.keyboardDispatcher.Subscriber == _search)
            Game1.keyboardDispatcher.Subscriber = null;
    }

    private void UpdateMaxScroll()
    {
        var layout = GetGridLayout();
        var rows = (_presets.Count + layout.Columns - 1) / layout.Columns;
        var contentHeight = rows * CardSize + Math.Max(0, rows - 1) * CardRowGap;
        _maxScroll = Math.Max(0, contentHeight - layout.GridArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);
    }

    private sealed class SearchSubscriber : IKeyboardSubscriber
    {
        public bool Selected { get; set; }
        public string Text { get; set; } = "";
        public int MaxLength { get; set; } = 80;

        public void RecieveTextInput(char inputChar)
        {
            if (Selected && !char.IsControl(inputChar) && Text.Length < MaxLength)
                Text += inputChar;
        }

        public void RecieveTextInput(string text)
        {
            if (!Selected || string.IsNullOrEmpty(text))
                return;
            var clean = new string(text.Where(c => !char.IsControl(c)).ToArray());
            Text += clean[..Math.Min(clean.Length, Math.Max(0, MaxLength - Text.Length))];
        }

        public void RecieveCommandInput(char command)
        {
            if (Selected && command == '\b' && Text.Length > 0)
                Text = Text[..^1];
        }

        public void RecieveSpecialInput(Keys key) { }
    }
}

public sealed class GalleryPreviewModal
{
    /// <summary>
    /// A footer action button: label, tint, and what it does. Set <paramref name="Confirm"/>
    /// to require a second click (the button shows <paramref name="ConfirmLabel"/> first) —
    /// used to guard the public Upload action.
    /// </summary>
    public readonly record struct PreviewAction(
        string Label, Color Color, Action Invoke, bool Confirm = false, string ConfirmLabel = "");

    private readonly string _npcName;
    private readonly string _subtitle;
    private readonly NpcOverrideEntry _entry;
    private readonly Dictionary<string, Texture2D?> _portraits;
    private readonly ITranslationHelper _i18n;
    private readonly Action _onClose;
    private readonly IReadOnlyList<PreviewAction> _actions;
    private readonly List<Rectangle> _actionRects = new();

    private Rectangle _modal;
    private Rectangle _textArea;
    private int _scrollY;
    private int _maxScroll;
    private int _armed = -1; // index of a Confirm button awaiting its second click

    public GalleryPreviewModal(
        string npcName,
        string subtitle,
        NpcOverrideEntry entry,
        Dictionary<string, Texture2D?> portraits,
        ITranslationHelper i18n,
        Action onClose,
        IReadOnlyList<PreviewAction> actions)
    {
        _npcName = npcName;
        _subtitle = subtitle;
        _entry = entry;
        _portraits = portraits;
        _i18n = i18n;
        _onClose = onClose;
        _actions = actions;
    }

    public void Draw(SpriteBatch b)
    {
        var viewport = Game1.uiViewport;
        _modal = new Rectangle(
            (viewport.Width - Math.Min(744, viewport.Width - 60)) / 2,
            (viewport.Height - Math.Min(604, viewport.Height - 60)) / 2,
            Math.Min(744, viewport.Width - 60),
            Math.Min(604, viewport.Height - 60));

        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.62f);
        EditorTheme.DrawFrame(b, _modal, Color.White);

        const int portraitSize = 120;
        var portraitRect = new Rectangle(_modal.X + 42, _modal.Y + 38, portraitSize, portraitSize);
        PortraitDraw.Draw(b, portraitRect, _npcName, _portraits.GetValueOrDefault(_npcName));
        PersonalityEditorMenu.DrawBorder(b, portraitRect, new Color(125, 60, 40), 4);

        Utility.drawTextWithShadow(b, _npcName, Game1.dialogueFont,
            new Vector2(portraitRect.Right + 20, _modal.Y + 42), Color.Black);
        if (!string.IsNullOrEmpty(_subtitle))
            b.DrawString(Game1.smallFont, _subtitle, new Vector2(portraitRect.Right + 20, _modal.Y + 76), Color.Gray);

        _textArea = new Rectangle(_modal.X + 42, _modal.Y + 178, _modal.Width - 84, _modal.Height - 252);
        DrawFields(b);
        DrawFooter(b);
    }

    private void DrawFields(SpriteBatch b)
    {
        var previousScissor = b.GraphicsDevice.ScissorRectangle;
        var previousRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = _textArea;

        var y = _textArea.Y - _scrollY;
        if (_entry.IsFarmer)
        {
            // Farmer preset: use the backstory question labels instead of NPC ones.
            y = DrawField(b, _i18n.Get("farmer.field.1"), _entry.CanonicalPersonality, y);
            y = DrawField(b, _i18n.Get("farmer.field.2"), _entry.Lore, y);
            y = DrawField(b, _i18n.Get("farmer.field.3"), _entry.SocialTags, y);
            y = DrawField(b, _i18n.Get("farmer.field.4"), _entry.Appearance, y);
        }
        else
        {
            y = DrawField(b, _i18n.Get("field.appearance"), _entry.Appearance, y);
            y = DrawField(b, _i18n.Get("field.personality"), _entry.CanonicalPersonality, y);
            y = DrawField(b, _i18n.Get("field.lore"), _entry.Lore, y);
            y = DrawField(b, _i18n.Get("field.social_tags"), _entry.SocialTags, y);
            y = DrawCharacterData(b, _entry.CharacterData, y);
        }
        y = DrawField(b, _i18n.Get("field.submission_credit"), _entry.SubmissionCredit, y);

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        b.GraphicsDevice.ScissorRectangle = previousScissor;

        var contentHeight = y + _scrollY - _textArea.Y;
        _maxScroll = Math.Max(0, contentHeight - _textArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);
        if (_maxScroll > 0)
            PersonalityEditorMenu.DrawScrollbar(b, _textArea, _scrollY, _maxScroll);
    }

    private int DrawField(SpriteBatch b, string label, string text, int y)
    {
        if (string.IsNullOrWhiteSpace(text))
            return y;
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(_textArea.X, y), Color.Black);
        y += 32;
        var wrapped = Game1.parseText(text, Game1.smallFont, _textArea.Width - 12);
        b.DrawString(Game1.smallFont, wrapped, new Vector2(_textArea.X + 4, y), new Color(80, 75, 80));
        y += wrapped.Split('\n').Length * (int)Game1.smallFont.MeasureString("A").Y + 18;
        return y;
    }

    // Character Data rendered as readable "Label: value" rows (never raw JSON).
    private int DrawCharacterData(SpriteBatch b, CharacterDataOverride? cd, int y)
    {
        if (cd == null || !cd.HasAnyField)
            return y;

        Utility.drawTextWithShadow(b, _i18n.Get("field.character_data").ToString(), Game1.smallFont,
            new Vector2(_textArea.X, y), Color.Black);
        y += 32;

        y = DrawRow(b, "field.display_name", cd.DisplayName, y);
        y = DrawRow(b, "field.gender", EnumLabel("field.gender", cd.Gender), y);
        y = DrawRow(b, "field.age", EnumLabel("field.age", cd.Age), y);
        y = DrawRow(b, "field.manner", EnumLabel("field.manner", cd.Manner), y);
        y = DrawRow(b, "field.social_anxiety", EnumLabel("field.social_anxiety", cd.SocialAnxiety), y);
        y = DrawRow(b, "field.optimism", EnumLabel("field.optimism", cd.Optimism), y);
        y = DrawRow(b, "field.can_socialize", BoolLabel(cd.CanSocialize), y);
        y = DrawRow(b, "field.can_be_romanced", BoolLabel(cd.CanBeRomanced), y);
        y = DrawRow(b, "field.birthday", Birthday(cd), y);
        return y + 12;
    }

    private int DrawRow(SpriteBatch b, string labelKey, string? value, int y)
    {
        if (string.IsNullOrWhiteSpace(value))
            return y;
        var text = $"{_i18n.Get(labelKey)}: {value}";
        b.DrawString(Game1.smallFont, text, new Vector2(_textArea.X + 4, y), new Color(80, 75, 80));
        return y + (int)Game1.smallFont.MeasureString("A").Y + 6;
    }

    private string? EnumLabel(string prefix, int? value)
        => value == null ? null : _i18n.Get($"{prefix}.{value.Value}").ToString();

    private string? BoolLabel(bool? value)
        => value == null ? null : _i18n.Get(value.Value ? "ui.yes" : "ui.no").ToString();

    private static string? Birthday(CharacterDataOverride cd)
    {
        if (string.IsNullOrWhiteSpace(cd.BirthSeason) && cd.BirthDay == null)
            return null;
        var season = string.IsNullOrWhiteSpace(cd.BirthSeason)
            ? ""
            : char.ToUpperInvariant(cd.BirthSeason[0]) + cd.BirthSeason[1..];
        return $"{season} {cd.BirthDay?.ToString() ?? ""}".Trim();
    }

    private void DrawFooter(SpriteBatch b)
    {
        _actionRects.Clear();
        var count = _actions.Count;
        if (count == 0)
            return;
        const int pad = 16;
        const int gap = 10;
        var y = _modal.Bottom - 76;
        var btnW = (_modal.Width - pad * 2 - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var rect = new Rectangle(_modal.X + pad + i * (btnW + gap), y, btnW, 58);
            _actionRects.Add(rect);
            var armed = i == _armed;
            var label = armed ? _actions[i].ConfirmLabel : _actions[i].Label;
            var color = armed ? new Color(225, 125, 85) : _actions[i].Color;
            EditorTheme.DrawButton(b, rect, label, color, Color.Black);
        }
    }

    public void receiveLeftClick(int x, int y)
    {
        for (var i = 0; i < _actionRects.Count && i < _actions.Count; i++)
        {
            if (!_actionRects[i].Contains(x, y))
                continue;

            var action = _actions[i];
            // First click on a Confirm button just arms it; second click runs it.
            if (action.Confirm && _armed != i)
            {
                _armed = i;
                Game1.playSound("smallSelect");
                return;
            }
            _armed = -1;
            action.Invoke();
            return;
        }
        _armed = -1; // clicking anywhere else cancels a pending confirmation
    }

    public void receiveScrollWheelAction(int direction)
    {
        _scrollY = Math.Clamp(_scrollY - direction, 0, Math.Max(0, _maxScroll));
    }

    public void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
            _onClose();
    }
}

/// <summary>
/// A small confirmation popup shown before importing or applying a preset that
/// overrides the game's Data/Characters. Confirm proceeds; Cancel backs out.
/// </summary>
internal sealed class CharacterDataWarning
{
    private readonly string _title;
    private readonly string _message;
    private readonly string _confirmLabel;
    private readonly string _cancelLabel;
    private readonly Action _onConfirm;
    private readonly Action _onCancel;
    private Rectangle _confirmBtn;
    private Rectangle _cancelBtn;

    public CharacterDataWarning(string title, string message, string confirmLabel, string cancelLabel, Action onConfirm, Action onCancel)
    {
        _title = title;
        _message = message;
        _confirmLabel = confirmLabel;
        _cancelLabel = cancelLabel;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
    }

    public void Draw(SpriteBatch b)
    {
        var vp = Game1.uiViewport;
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        var w = Math.Min(760, vp.Width - 80);
        var wrapped = Game1.parseText(_message, Game1.smallFont, w - 64);
        var msgH = (int)Game1.smallFont.MeasureString(wrapped).Y;
        var h = 96 + msgH + 90;
        var box = new Rectangle((vp.Width - w) / 2, (vp.Height - h) / 2, w, h);
        EditorTheme.DrawFrame(b, box, new Color(255, 248, 234));

        Utility.drawTextWithShadow(b, _title, Game1.dialogueFont,
            new Vector2(box.X + 28, box.Y + 22), new Color(170, 70, 40));
        b.DrawString(Game1.smallFont, wrapped, new Vector2(box.X + 32, box.Y + 78), new Color(70, 50, 35));

        var btnW = Math.Min(230, (w - 96) / 2);
        var btnY = box.Bottom - 74;
        _cancelBtn = new Rectangle(box.X + 32, btnY, btnW, 56);
        _confirmBtn = new Rectangle(box.Right - btnW - 32, btnY, btnW, 56);
        EditorTheme.DrawButton(b, _cancelBtn, _cancelLabel, new Color(255, 248, 234), Color.Black);
        EditorTheme.DrawButton(b, _confirmBtn, _confirmLabel, new Color(225, 150, 70), Color.Black);
    }

    public void receiveLeftClick(int x, int y)
    {
        if (_confirmBtn.Contains(x, y))
        {
            Game1.playSound("smallSelect");
            _onConfirm();
        }
        else if (_cancelBtn.Contains(x, y))
        {
            Game1.playSound("bigDeSelect");
            _onCancel();
        }
    }
}
