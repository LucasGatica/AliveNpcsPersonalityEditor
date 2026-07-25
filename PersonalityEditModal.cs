using AliveNpcsPersonalityEditor.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>Full-screen NPC editor. AliveNpcs personality fields sit on top; the
/// riskier Data/Characters overrides live in a collapsible block at the bottom,
/// gated behind the Character Data disclaimer opt-in from the mod config.</summary>
public sealed class PersonalityEditModal : IClickableMenu
{
    private readonly string _npcName;
    private readonly string _displayName;
    private readonly string _defaultPersonality;
    private readonly Texture2D? _portrait;
    private readonly PersonalityStore _store;
    private readonly PresetStore _presetStore;
    private readonly IAliveNpcsApi _api;
    private readonly EditorConfig _config;
    private readonly IMonitor _monitor;
    private readonly ITranslationHelper _i18n;
    private readonly Action _onClose;

    private MultilineTextBox _nameBox = null!;
    private MultilineTextBox _appearanceBox = null!;
    private MultilineTextBox _personalityBox = null!;
    private MultilineTextBox _loreBox = null!;
    private MultilineTextBox _socialTagsBox = null!;
    private MultilineTextBox _creditBox = null!;
    private ActiveField _activeField;
    private string _originalDisplayName = "";

    private CharacterDataOverride? _existingCharacterData;
    private int _gender = -1;
    private int _manner = -1;
    private int _socialAnxiety = -1;
    private int _optimism = -1;
    private int _age = -1;
    private bool _canSocialize;
    private bool _canBeRomanced;
    private int _originalGender = -1;
    private int _originalManner = -1;
    private int _originalSocialAnxiety = -1;
    private int _originalOptimism = -1;
    private bool _originalCanSocialize;
    private bool _originalCanBeRomanced;

    private Rectangle _portraitCard;
    private Rectangle _scrollArea;
    private Rectangle _cancelButton;
    private Rectangle _resetButton;
    private Rectangle _galleryButton;
    private Rectangle _saveButton;
    private Rectangle _cdHeaderRect;
    private int _scrollY;
    private int _maxScroll;

    // Character Data block is collapsed by default; expanding it requires the
    // disclaimer opt-in to be enabled in the mod config (GMCM).
    private bool _cdExpanded;

    private const int PortraitSize = 194;
    private const int SelectorH = 44;
    private const int TextBoxH = 144;
    private const int ShortBoxH = 64;      // single-line-ish fields (Name, Submission Credit)
    private const int LabelColumnW = 236;

    // Vertical advance per row type, used by the dynamic layout pass.
    private const int TopPad = 8;
    private const int TallTextBlock = 210;   // large text areas (appearance, personality, lore, social tags)
    private const int ShortTextBlock = 132;  // short text field (submission credit)
    private const int SectionGap = 16;
    private const int HeaderH = 46;

    // Character Data section (inside the brown panel).
    private const int CdPad = 22;            // inner padding between the panel frame and its content
    private const int HeaderBlock = 82;      // header row -> first CD row (leaves top padding inside the panel)
    private const int CdNameBlock = 132;     // Display Name (short text) -> uniform gap into the first selector
    private const int CdRowBlock = 76;       // every selector / toggle row (uniform vertical rhythm)

    private static readonly Color Paper = new(255, 248, 234);
    private static readonly Color Border = new(125, 60, 40);
    private static readonly Color Active = new(75, 135, 50);
    private static readonly Color Inactive = new(255, 248, 234);
    private static readonly Color SaveColor = new(125, 200, 105);
    private static readonly Color CancelColor = new(225, 125, 85);
    private static readonly Color ResetColor = new(205, 160, 95);
    private static readonly Color LockedPaper = new(232, 224, 208);
    private static readonly Color CdButtonBrown = new(125, 60, 40);   // expand/collapse button
    private static readonly Color CdPanelBg = new(140, 110, 70);       // background box behind expanded options
    private static readonly Color CdPanelLabel = new(245, 235, 214);   // light label text on the brown panel

    private Rectangle _cdButtonRect;

    // Content bounds inside the Character Data panel, padded off the frame.
    private int CdContentX => _scrollArea.X + CdPad;
    private int CdContentW => _scrollArea.Width - 34 - CdPad * 2;

    private static readonly string[] Genders = { "field.gender.0", "field.gender.1" };
    private static readonly string[] Manners = { "field.manner.0", "field.manner.1", "field.manner.2" };
    private static readonly string[] Anxieties = { "field.social_anxiety.0", "field.social_anxiety.1", "field.social_anxiety.2" };
    private static readonly string[] Optimisms = { "field.optimism.0", "field.optimism.1", "field.optimism.2" };

    private enum ActiveField
    {
        None,
        Name,
        Appearance,
        Personality,
        Lore,
        SocialTags,
        SubmissionCredit
    }

    // Ordered rows in the scrollable content. AliveNpcs fields first, then the
    // Character Data header, then (when expanded) the override fields.
    private enum Row
    {
        Appearance,
        Personality,
        Lore,
        SocialTags,
        Credit,
        CdHeader,
        Name,
        Gender,
        Manner,
        Anxiety,
        Optimism,
        Socialize,
        Romance
    }

    private readonly Dictionary<Row, int> _rowY = new();
    private int _contentHeight;

    public PersonalityEditModal(
        string npcName,
        string displayName,
        string defaultPersonality,
        Texture2D? portrait,
        PersonalityStore store,
        PresetStore presetStore,
        IAliveNpcsApi api,
        EditorConfig config,
        GalleryService? galleryService,
        IMonitor monitor,
        ITranslationHelper i18n,
        Action onClose)
        : base(0, 0, 0, 0)
    {
        _npcName = npcName;
        _displayName = displayName;
        _defaultPersonality = defaultPersonality;
        _portrait = portrait;
        _store = store;
        _presetStore = presetStore;
        _api = api;
        _config = config;
        _ = galleryService;
        _monitor = monitor;
        _i18n = i18n;
        _onClose = onClose;

        RecalculateLayout();
        InitializeTextBoxes();
        LoadState();
    }

    /// <summary>Whether the Character Data overrides may be edited — gated on the
    /// disclaimer opt-in enabled from the mod config (GMCM).</summary>
    private bool CanEditCharacterData => _config.IncludeCharacterDataInPrompt;

    private void RecalculateLayout()
    {
        var viewport = Game1.uiViewport;
        width = Math.Min(1104, viewport.Width - 24);
        height = Math.Min(940, viewport.Height - 24);
        xPositionOnScreen = (viewport.Width - width) / 2;
        yPositionOnScreen = (viewport.Height - height) / 2;

        _portraitCard = new Rectangle(xPositionOnScreen + 40, yPositionOnScreen + 120, PortraitSize, PortraitSize);
        var footerTop = yPositionOnScreen + height - 72;
        _scrollArea = new Rectangle(
            xPositionOnScreen + 284,
            yPositionOnScreen + 120,
            width - 328,
            footerTop - 16 - (yPositionOnScreen + 120));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, _maxScroll));
    }

    // Compute each row's relative Y (independent of scroll) and the total content height.
    private void BuildLayout()
    {
        _rowY.Clear();
        var y = TopPad;
        void Place(Row row, int advance) { _rowY[row] = y; y += advance; }

        Place(Row.Appearance, TallTextBlock);
        Place(Row.Personality, TallTextBlock);
        Place(Row.Lore, TallTextBlock);
        Place(Row.SocialTags, TallTextBlock);
        Place(Row.Credit, ShortTextBlock);

        y += SectionGap;
        Place(Row.CdHeader, HeaderBlock);

        if (_cdExpanded && CanEditCharacterData)
        {
            Place(Row.Name, CdNameBlock);
            Place(Row.Gender, CdRowBlock);
            Place(Row.Manner, CdRowBlock);
            Place(Row.Anxiety, CdRowBlock);
            Place(Row.Optimism, CdRowBlock);
            Place(Row.Socialize, CdRowBlock);
            Place(Row.Romance, CdRowBlock);
        }

        _contentHeight = y + 24;
    }

    private int RowY(Row row) => _rowY.TryGetValue(row, out var v) ? v : -100000;

    private void InitializeTextBoxes()
    {
        var textWidth = Math.Max(220, _scrollArea.Width - 34);
        _nameBox = CreateTextBox(textWidth, 80, ShortBoxH);
        _appearanceBox = CreateTextBox(textWidth, 500);
        _personalityBox = CreateTextBox(textWidth, 700);
        _loreBox = CreateTextBox(textWidth, 600);
        _socialTagsBox = CreateTextBox(textWidth, 300);
        _creditBox = CreateTextBox(textWidth, 120, ShortBoxH);
        BuildLayout();
        LayoutTextBoxes();
    }

    private static MultilineTextBox CreateTextBox(int width, int limit, int height = TextBoxH)
    {
        return new MultilineTextBox(new Rectangle(0, 0, width, height), Game1.smallFont, new Color(80, 70, 75))
        {
            TextLimit = limit,
            BackgroundColor = Paper,
            BorderColor = Border
        };
    }

    private void LayoutTextBoxes()
    {
        var x = _scrollArea.X;
        var width = _scrollArea.Width - 34;
        Relocate(_appearanceBox, x, ContentY(RowY(Row.Appearance) + 36), width);
        Relocate(_personalityBox, x, ContentY(RowY(Row.Personality) + 36), width);
        Relocate(_loreBox, x, ContentY(RowY(Row.Lore) + 36), width);
        Relocate(_socialTagsBox, x, ContentY(RowY(Row.SocialTags) + 36), width);
        Relocate(_creditBox, x, ContentY(RowY(Row.Credit) + 36), width, ShortBoxH);

        if (_cdExpanded && CanEditCharacterData)
            Relocate(_nameBox, CdContentX, ContentY(RowY(Row.Name) + 36), CdContentW, ShortBoxH);
        else
            _nameBox.Bounds = new Rectangle(-100000, -100000, width, ShortBoxH);
    }

    private static void Relocate(MultilineTextBox box, int x, int y, int width, int height = TextBoxH)
    {
        box.Bounds = new Rectangle(x, y, width, height);
    }

    private int ContentY(int relativeY) => _scrollArea.Y + relativeY - _scrollY;

    private void EnsureLayout()
    {
        BuildLayout();
        LayoutTextBoxes();
    }

    private void LoadState()
    {
        LoadBaseCharacterData();
        var entry = _store.Get(_npcName);
        _existingCharacterData = Clone(entry?.CharacterData);
        // Default name = your override, else a mod's detected change / the real original, else the shown name.
        _originalDisplayName = entry?.CharacterData?.DisplayName ?? TryGetBaseDisplayName() ?? _displayName ?? "";
        _nameBox.Text = _originalDisplayName;
        _appearanceBox.Text = entry?.Appearance ?? "";
        _personalityBox.Text = !string.IsNullOrWhiteSpace(entry?.CanonicalPersonality)
            ? entry!.CanonicalPersonality
            : _defaultPersonality;
        _loreBox.Text = entry?.Lore ?? "";
        _socialTagsBox.Text = entry?.SocialTags ?? "";
        _creditBox.Text = entry?.SubmissionCredit ?? "";

        if (entry?.CharacterData is { } data)
        {
            _gender = data.Gender ?? _gender;
            _manner = data.Manner ?? _manner;
            _socialAnxiety = data.SocialAnxiety ?? _socialAnxiety;
            _optimism = data.Optimism ?? _optimism;
            _age = data.Age ?? _age;
            _canSocialize = data.CanSocialize ?? _canSocialize;
            _canBeRomanced = data.CanBeRomanced ?? _canBeRomanced;
        }

        _originalGender = _gender;
        _originalManner = _manner;
        _originalSocialAnxiety = _socialAnxiety;
        _originalOptimism = _optimism;
        _originalCanSocialize = _canSocialize;
        _originalCanBeRomanced = _canBeRomanced;
        SetActiveField(ActiveField.None);
    }

    private string? TryGetBaseDisplayName()
    {
        try
        {
            var name = CharacterDataService.GetEffectiveDisplayName(_npcName);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    private void LoadBaseCharacterData()
    {
        var loadedFromApi = false;

        try
        {
            var snap = CharacterDataService.TryGetBaseSnapshot(_npcName, out var snapshot) ? snapshot : null;
            if (snap is { Length: >= 7 })
            {
                _gender = snap[0];
                _age = snap[1];
                _manner = snap[2];
                _socialAnxiety = snap[3];
                _optimism = snap[4];
                _canSocialize = snap[5] != 0;
                _canBeRomanced = snap[6] != 0;
                loadedFromApi = true;
            }
        }
        catch (Exception ex)
        {
            _monitor.Log($"GetBaseCharacterData failed for {_npcName}: {ex.Message}", LogLevel.Trace);
        }

        // Fallback: read the live asset (fine when no override has ever been applied).
        if (!loadedFromApi)
        {
            try
            {
                var characters = Game1.content.Load<Dictionary<string, StardewValley.GameData.Characters.CharacterData>>("Data/Characters");
                if (characters.TryGetValue(_npcName, out var data))
                {
                    _gender = (int)data.Gender;
                    _manner = (int)data.Manner;
                    _socialAnxiety = (int)data.SocialAnxiety;
                    _optimism = (int)data.Optimism;
                    _age = (int)data.Age;
                    _canSocialize = ResolveCanSocialize(data.CanSocialize);
                    _canBeRomanced = data.CanBeRomanced;
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"Could not read CharacterData for {_npcName}: {ex.Message}", LogLevel.Trace);
            }
        }

        // CanSocialize is an optional game-state-query that DEFAULTS TO TRUE when unset.
        // Resolve it from the raw asset so an unspecified (null) field shows ON, not OFF —
        // the "TRUE"-string check / API 0-or-1 would otherwise render the default as OFF.
        try
        {
            var characters = Game1.content.Load<Dictionary<string, StardewValley.GameData.Characters.CharacterData>>("Data/Characters");
            if (characters.TryGetValue(_npcName, out var cd))
                _canSocialize = ResolveCanSocialize(cd.CanSocialize);
        }
        catch { /* keep whatever was loaded above */ }
    }

    // Interpret the Data/Characters CanSocialize field. Unset (null/empty) means the
    // vanilla default of true; "FALSE"/"TRUE" are the literal queries; anything else is
    // a real game-state-query, evaluated with a true fallback.
    private static bool ResolveCanSocialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return false;
        try { return GameStateQuery.CheckConditions(raw); }
        catch { return true; }
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect,
            new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.72f);
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

        DrawTitle(b);
        DrawPortraitCard(b);
        DrawScrollableContent(b);
        DrawFooter(b);
        drawMouse(b);
    }

    private void DrawTitle(SpriteBatch b)
    {
        var title = _i18n.Get("editor.editing", new { name = _displayName }).ToString();
        var size = Game1.dialogueFont.MeasureString(title);
        Utility.drawTextWithShadow(b, title, Game1.dialogueFont,
            new Vector2(xPositionOnScreen + (width - size.X) / 2f, yPositionOnScreen + 28), Color.Black);
    }

    private void DrawPortraitCard(SpriteBatch b)
    {
        EditorTheme.DrawFrame(b, _portraitCard, Paper);
        var nameSize = Game1.smallFont.MeasureString(_displayName);
        Utility.drawTextWithShadow(b, _displayName, Game1.smallFont,
            new Vector2(_portraitCard.X + (_portraitCard.Width - nameSize.X) / 2f, _portraitCard.Y + 8), Color.Black);

        var portraitRect = new Rectangle(_portraitCard.X + 18, _portraitCard.Y + 43, _portraitCard.Width - 36, _portraitCard.Height - 43);
        PortraitDraw.Draw(b, portraitRect, _npcName, _portrait);
    }

    private void DrawScrollableContent(SpriteBatch b)
    {
        BuildLayout();
        _maxScroll = Math.Max(0, _contentHeight - _scrollArea.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);
        LayoutTextBoxes();

        var previousScissor = b.GraphicsDevice.ScissorRectangle;
        var previousRasterizer = b.GraphicsDevice.RasterizerState;
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null,
            new RasterizerState { ScissorTestEnable = true });
        b.GraphicsDevice.ScissorRectangle = _scrollArea;

        // AliveNpcs personality fields (always visible, on top).
        DrawTextField(b, RowY(Row.Appearance), "field.appearance", _appearanceBox);
        DrawTextField(b, RowY(Row.Personality), "field.personality", _personalityBox);
        DrawTextField(b, RowY(Row.Lore), "field.lore", _loreBox);
        DrawTextField(b, RowY(Row.SocialTags), "field.social_tags", _socialTagsBox);
        DrawTextField(b, RowY(Row.Credit), "field.submission_credit", _creditBox);

        // Character Data overrides (collapsible, gated by the disclaimer opt-in).
        if (_cdExpanded && CanEditCharacterData)
        {
            // Brown background box grouping the override options (matches the original design),
            // with even padding above the first field and below the last.
            var panelTop = ContentY(RowY(Row.Name)) - CdPad;
            var panelBottom = ContentY(RowY(Row.Romance)) + SelectorH + 14 + CdPad;
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
                _scrollArea.X, panelTop, _scrollArea.Width - 34, panelBottom - panelTop, CdPanelBg);
        }

        DrawCdHeader(b);
        if (_cdExpanded && CanEditCharacterData)
        {
            DrawTextField(b, RowY(Row.Name), "field.display_name", _nameBox, CdPanelLabel, labelX: CdContentX);
            DrawSelectorRow(b, RowY(Row.Gender), "field.gender", _gender, Genders, labelColor: CdPanelLabel);
            DrawSelectorRow(b, RowY(Row.Manner), "field.manner", _manner, Manners, labelColor: CdPanelLabel);
            DrawSelectorRow(b, RowY(Row.Anxiety), "field.social_anxiety", _socialAnxiety, Anxieties, wrapLabel: true, labelColor: CdPanelLabel);
            DrawSelectorRow(b, RowY(Row.Optimism), "field.optimism", _optimism, Optimisms, labelColor: CdPanelLabel);
            DrawToggleRow(b, RowY(Row.Socialize), "field.can_socialize", _canSocialize, labelColor: CdPanelLabel);
            DrawToggleRow(b, RowY(Row.Romance), "field.can_be_romanced", _canBeRomanced, _age == 2, labelColor: CdPanelLabel);
        }

        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        b.GraphicsDevice.ScissorRectangle = previousScissor;

        if (_maxScroll > 0)
            PersonalityEditorMenu.DrawScrollbar(b, _scrollArea, _scrollY, _maxScroll);
    }

    // The collapsible section header. Clickable to expand/collapse; shows a locked
    // hint (and blocks expansion) until the Character Data disclaimer is enabled.
    private void DrawCdHeader(SpriteBatch b)
    {
        var y = ContentY(RowY(Row.CdHeader));
        var rect = new Rectangle(_scrollArea.X, y, _scrollArea.Width - 34, HeaderH);
        _cdHeaderRect = rect;

        var locked = !CanEditCharacterData;
        EditorTheme.DrawFrame(b, rect, locked ? LockedPaper : Paper);

        var label = _i18n.Get("field.character_data").ToString();
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(rect.X + 14, rect.Y + (rect.Height - Game1.smallFont.MeasureString(label).Y) / 2f),
            locked ? Color.Gray : Border);

        if (locked)
        {
            // Gray hint on the right, no button.
            _cdButtonRect = Rectangle.Empty;
            var hint = _i18n.Get("field.character_data.locked").ToString();
            var hsize = Game1.smallFont.MeasureString(hint);
            var maxRight = rect.Width - 200;
            if (hsize.X > maxRight)
            {
                hint = Game1.parseText(hint, Game1.smallFont, (int)maxRight);
                hsize = Game1.smallFont.MeasureString(hint);
            }
            Utility.drawTextWithShadow(b, hint, Game1.smallFont,
                new Vector2(rect.Right - hsize.X - 14, rect.Y + (rect.Height - hsize.Y) / 2f), Color.Gray);
            return;
        }

        // Brown expand/collapse button with white font on the right.
        var btnLabel = _i18n.Get(_cdExpanded ? "field.character_data.collapse" : "field.character_data.expand").ToString();
        var bw = (int)Game1.smallFont.MeasureString(btnLabel).X + 34;
        var bh = rect.Height - 12;
        _cdButtonRect = new Rectangle(rect.Right - bw - 8, rect.Y + 6, bw, bh);
        EditorTheme.DrawButton(b, _cdButtonRect, btnLabel, CdButtonBrown, Color.White);
    }

    private void DrawSelectorRow(SpriteBatch b, int relativeY, string labelKey, int selected, string[] options, bool wrapLabel = false, Color? labelColor = null)
    {
        var y = ContentY(relativeY);
        var label = _i18n.Get(labelKey).ToString();
        if (wrapLabel)
            label = Game1.parseText(label, Game1.smallFont, LabelColumnW - 12);
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(CdContentX, y + 5), labelColor ?? Color.Black);

        var area = GetSelectorArea(y);
        var gap = 24;
        var buttonWidth = (area.Width - gap * (options.Length - 1)) / options.Length;
        for (var i = 0; i < options.Length; i++)
        {
            var rect = new Rectangle(area.X + i * (buttonWidth + gap), area.Y, buttonWidth, area.Height);
            DrawChoiceButton(b, rect, _i18n.Get(options[i]), selected == i, false);
        }
    }

    private void DrawToggleRow(SpriteBatch b, int relativeY, string labelKey, bool value, bool disabled = false, Color? labelColor = null)
    {
        var y = ContentY(relativeY);
        var label = Game1.parseText(_i18n.Get(labelKey), Game1.smallFont, LabelColumnW - 10);
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(CdContentX, y), disabled ? Color.Gray : (labelColor ?? Color.Black));
        var rect = new Rectangle(CdContentX + CdContentW - 58, y, 58, SelectorH);
        DrawChoiceButton(b, rect, value ? "ON" : "OFF", value, disabled);
    }

    private void DrawTextField(SpriteBatch b, int relativeY, string labelKey, MultilineTextBox box, Color? labelColor = null, int? labelX = null)
    {
        Utility.drawTextWithShadow(b, _i18n.Get(labelKey), Game1.smallFont,
            new Vector2(labelX ?? _scrollArea.X, ContentY(relativeY)), labelColor ?? Color.Black);
        box.Draw(b);
    }

    // Selector/toggle buttons live inside the padded Character Data panel.
    private Rectangle GetSelectorArea(int absoluteY)
    {
        return new Rectangle(CdContentX + LabelColumnW, absoluteY, CdContentW - LabelColumnW, SelectorH);
    }

    private static void DrawChoiceButton(SpriteBatch b, Rectangle rect, string label, bool selected, bool disabled)
    {
        var background = disabled ? new Color(195, 190, 175) : selected ? Active : Inactive;
        EditorTheme.DrawFrame(b, rect, background);
        if (selected && !disabled)
            EditorTheme.DrawHighlight(b, rect);
        var size = Game1.smallFont.MeasureString(label);
        Utility.drawTextWithShadow(b, label, Game1.smallFont,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f),
            disabled ? Color.DarkGray : selected ? Color.White : Color.Black);
    }

    private void DrawFooter(SpriteBatch b)
    {
        var y = yPositionOnScreen + height - 70;
        var sideWidth = Math.Min(210, width / 6);
        _cancelButton = new Rectangle(xPositionOnScreen + 28, y, sideWidth, 58);
        _saveButton = new Rectangle(xPositionOnScreen + width - 28 - sideWidth, y, sideWidth, 58);

        // Centre pair: Reset (restore defaults) sits directly beside Save to Gallery.
        var galleryWidth = Math.Min(320, width / 4);
        var resetWidth = Math.Min(210, width / 6);
        const int pairGap = 12;
        var pairTotal = resetWidth + pairGap + galleryWidth;
        var pairX = xPositionOnScreen + (width - pairTotal) / 2;
        _resetButton = new Rectangle(pairX, y, resetWidth, 58);
        _galleryButton = new Rectangle(pairX + resetWidth + pairGap, y, galleryWidth, 58);

        DrawFooterButton(b, _cancelButton, _i18n.Get("button.cancel"), CancelColor);
        DrawFooterButton(b, _resetButton, _i18n.Get("button.reset"), ResetColor);
        DrawFooterButton(b, _galleryButton, _i18n.Get("button.save_gallery"), Paper);
        DrawFooterButton(b, _saveButton, _i18n.Get("button.save"), SaveColor);
    }

    private static void DrawFooterButton(SpriteBatch b, Rectangle rect, string label, Color color)
        => EditorTheme.DrawButton(b, rect, label, color, Color.Black);

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        EnsureLayout();
        if (_cancelButton.Contains(x, y))
        {
            Game1.playSound("bigDeSelect");
            Close();
            return;
        }
        if (_resetButton.Contains(x, y))
        {
            // Restore defaults: drop this NPC's override so the game reverts, then
            // reload the fields to show the default values.
            _store.Set(_npcName, null);
            _store.Save(_npcName);
            NotifyReload();
            LoadState();
            Game1.playSound("trashcan");
            return;
        }
        if (_galleryButton.Contains(x, y))
        {
            SaveToGallery();
            return;
        }
        if (_saveButton.Contains(x, y))
        {
            CommitAndSave();
            Game1.playSound("coin");
            Close();
            return;
        }

        if (!_scrollArea.Contains(x, y))
        {
            SetActiveField(ActiveField.None);
            return;
        }

        // Character Data section header: toggle, or block with a hint when locked.
        if (_cdHeaderRect.Contains(x, y))
        {
            SetActiveField(ActiveField.None);
            if (!CanEditCharacterData)
            {
                Game1.playSound("cancel");
                EditorNotify.Info(_i18n.Get("field.character_data.locked"));
                return;
            }
            _cdExpanded = !_cdExpanded;
            Game1.playSound(_cdExpanded ? "shwip" : "breathin");
            EnsureLayout();
            return;
        }

        foreach (var (field, box) in TextBoxes())
        {
            if (!box.Bounds.Contains(x, y))
                continue;
            SetActiveField(field);
            box.SetCursorFromClick(x, y);
            return;
        }

        SetActiveField(ActiveField.None);
        if (HandleSelectorClick(x, y))
            Game1.playSound("smallSelect");
    }

    private bool HandleSelectorClick(int x, int y)
    {
        if (!(_cdExpanded && CanEditCharacterData))
            return false;

        var rows = new (Row Row, string[] Options, Action<int> Set)[]
        {
            (Row.Gender, Genders, value => _gender = value),
            (Row.Manner, Manners, value => _manner = value),
            (Row.Anxiety, Anxieties, value => _socialAnxiety = value),
            (Row.Optimism, Optimisms, value => _optimism = value)
        };

        foreach (var row in rows)
        {
            var area = GetSelectorArea(ContentY(RowY(row.Row)));
            var gap = 24;
            var width = (area.Width - gap * (row.Options.Length - 1)) / row.Options.Length;
            for (var i = 0; i < row.Options.Length; i++)
            {
                if (!new Rectangle(area.X + i * (width + gap), area.Y, width, area.Height).Contains(x, y))
                    continue;
                row.Set(i);
                return true;
            }
        }

        var socialize = new Rectangle(CdContentX + CdContentW - 58, ContentY(RowY(Row.Socialize)), 58, SelectorH);
        if (socialize.Contains(x, y))
        {
            _canSocialize = !_canSocialize;
            return true;
        }
        var romance = new Rectangle(CdContentX + CdContentW - 58, ContentY(RowY(Row.Romance)), 58, SelectorH);
        if (romance.Contains(x, y) && _age != 2)
        {
            _canBeRomanced = !_canBeRomanced;
            return true;
        }
        return false;
    }

    private IEnumerable<(ActiveField Field, MultilineTextBox Box)> TextBoxes()
    {
        yield return (ActiveField.Name, _nameBox);
        yield return (ActiveField.Appearance, _appearanceBox);
        yield return (ActiveField.Personality, _personalityBox);
        yield return (ActiveField.Lore, _loreBox);
        yield return (ActiveField.SocialTags, _socialTagsBox);
        yield return (ActiveField.SubmissionCredit, _creditBox);
    }

    private void SetActiveField(ActiveField field)
    {
        _activeField = field;
        foreach (var (candidate, box) in TextBoxes())
            box.Selected = candidate == field;
        var selected = TextBoxes().FirstOrDefault(pair => pair.Field == field).Box;
        Game1.keyboardDispatcher.Subscriber = selected;
    }

    public override void receiveScrollWheelAction(int direction)
    {
        EnsureLayout();
        var mouse = Game1.getMousePosition();
        foreach (var (_, box) in TextBoxes())
        {
            if (box.Bounds.Contains(mouse.X, mouse.Y) && box.NeedsScroll())
            {
                box.Scroll(direction);
                return;
            }
        }
        _scrollY = Math.Clamp(_scrollY - direction, 0, Math.Max(0, _maxScroll));
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            Close();
            return;
        }
        if (_activeField != ActiveField.None && Game1.keyboardDispatcher.Subscriber is MultilineTextBox box)
            box.RecieveSpecialInput(key);
    }

    private void CommitAndSave()
    {
        var entry = BuildEntry(includeDefaultPersonality: false);
        _store.Set(_npcName, entry.HasAnyField ? entry : null);
        _store.Save(_npcName);
        NotifyReload();
    }

    private NpcOverrideEntry BuildEntry(bool includeDefaultPersonality)
    {
        var personality = _personalityBox.Text.Trim();
        var personalityChanged = !string.IsNullOrWhiteSpace(personality)
            && !string.Equals(personality, _defaultPersonality.Trim(), StringComparison.OrdinalIgnoreCase);

        var entry = new NpcOverrideEntry
        {
            NpcName = _npcName,
            Appearance = _appearanceBox.Text.Trim(),
            CanonicalPersonality = includeDefaultPersonality || personalityChanged ? personality : "",
            Lore = _loreBox.Text.Trim(),
            SocialTags = _socialTagsBox.Text.Trim(),
            SubmissionCredit = _creditBox.Text.Trim(),
            CharacterData = BuildCharacterDataOverride()
        };
        return entry;
    }

    private CharacterDataOverride? BuildCharacterDataOverride()
    {
        var existing = _existingCharacterData;
        var result = new CharacterDataOverride
        {
            DisplayName = ResolveDisplayName(existing),
            Age = existing?.Age,
            BirthSeason = existing?.BirthSeason,
            BirthDay = existing?.BirthDay,
            CanReceiveGifts = existing?.CanReceiveGifts,
            Gender = _gender != _originalGender ? _gender : existing?.Gender,
            Manner = _manner != _originalManner ? _manner : existing?.Manner,
            SocialAnxiety = _socialAnxiety != _originalSocialAnxiety ? _socialAnxiety : existing?.SocialAnxiety,
            Optimism = _optimism != _originalOptimism ? _optimism : existing?.Optimism,
            CanSocialize = _canSocialize != _originalCanSocialize ? _canSocialize : existing?.CanSocialize,
            CanBeRomanced = _canBeRomanced != _originalCanBeRomanced ? _canBeRomanced : existing?.CanBeRomanced
        };
        return result.HasAnyField ? result : null;
    }

    /// <summary>
    /// Return the DisplayName to persist: the edited name when the player changed it
    /// from the value shown on open, otherwise whatever override was already stored
    /// (so an unedited field never clobbers or creates an override).
    /// </summary>
    private string? ResolveDisplayName(CharacterDataOverride? existing)
    {
        var name = _nameBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, _originalDisplayName.Trim(), StringComparison.Ordinal))
            return name;
        return existing?.DisplayName;
    }

    // Saves to the LOCAL preset gallery only. Sharing to the community server is an
    // explicit, opt-in action from the Gallery tab's Local view (privacy by default).
    private void SaveToGallery()
    {
        try
        {
            var entry = BuildEntry(includeDefaultPersonality: true);
            if (!entry.HasAnyField)
            {
                EditorNotify.Info(_i18n.Get("gallery.preset.none"));
                return;
            }

            _presetStore.Save(_npcName, entry);
            EditorNotify.Success(_i18n.Get("gallery.preset.saved", new { npcName = _displayName }));
            Game1.playSound("coin");
        }
        catch (Exception ex)
        {
            _monitor.Log($"Save to gallery failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private void ApplyPreset(NpcOverrideEntry entry)
    {
        _appearanceBox.Text = entry.Appearance ?? "";
        _personalityBox.Text = entry.CanonicalPersonality ?? "";
        _loreBox.Text = entry.Lore ?? "";
        _socialTagsBox.Text = entry.SocialTags ?? "";
        _creditBox.Text = entry.SubmissionCredit ?? "";
        if (!string.IsNullOrWhiteSpace(entry.CharacterData?.DisplayName))
            _nameBox.Text = entry.CharacterData!.DisplayName!;
        _existingCharacterData = Clone(entry.CharacterData);
        if (entry.CharacterData is { } data)
        {
            _gender = data.Gender ?? _gender;
            _manner = data.Manner ?? _manner;
            _socialAnxiety = data.SocialAnxiety ?? _socialAnxiety;
            _optimism = data.Optimism ?? _optimism;
            _age = data.Age ?? _age;
            _canSocialize = data.CanSocialize ?? _canSocialize;
            _canBeRomanced = data.CanBeRomanced ?? _canBeRomanced;
        }
    }

    private static CharacterDataOverride? Clone(CharacterDataOverride? source)
    {
        if (source == null)
            return null;
        return new CharacterDataOverride
        {
            DisplayName = source.DisplayName,
            Gender = source.Gender,
            Age = source.Age,
            Manner = source.Manner,
            SocialAnxiety = source.SocialAnxiety,
            Optimism = source.Optimism,
            BirthSeason = source.BirthSeason,
            BirthDay = source.BirthDay,
            CanSocialize = source.CanSocialize,
            CanReceiveGifts = source.CanReceiveGifts,
            CanBeRomanced = source.CanBeRomanced
        };
    }

    private void NotifyReload()
    {
        try { _api.ReloadCustomPersonalities(); }
        catch (Exception ex) { _monitor.Log($"Reload notify failed: {ex.Message}", LogLevel.Warn); }
    }

    private void Close()
    {
        SetActiveField(ActiveField.None);
        _onClose();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        var values = TextBoxes().ToDictionary(pair => pair.Field, pair => pair.Box.Text);
        RecalculateLayout();
        InitializeTextBoxes();
        _nameBox.Text = values[ActiveField.Name];
        _appearanceBox.Text = values[ActiveField.Appearance];
        _personalityBox.Text = values[ActiveField.Personality];
        _loreBox.Text = values[ActiveField.Lore];
        _socialTagsBox.Text = values[ActiveField.SocialTags];
        _creditBox.Text = values[ActiveField.SubmissionCredit];
        SetActiveField(ActiveField.None);
    }

    protected override void cleanupBeforeExit()
    {
        SetActiveField(ActiveField.None);
        base.cleanupBeforeExit();
    }
}
