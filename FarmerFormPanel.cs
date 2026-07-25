using AliveNpcsPersonalityEditor.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// Farmer backstory form panel rendered inside the personality editor on the
/// "Farmer" tab. Four multiline text fields (Who am I, Why moved here, Extra
/// info, At-a-glance) bound to FarmerStore, plus Cancel / Save to Gallery / Save
/// buttons at the bottom.
/// </summary>
public sealed class FarmerFormPanel
{
    private readonly FarmerStore _store;
    private readonly IAliveNpcsApi _api;
    private readonly PresetStore _presetStore;
    private readonly ITranslationHelper _i18n;
    private readonly IMonitor _monitor;
    private readonly System.Func<Rectangle> _getContentArea;
    private readonly System.Action _onCancel;
    private readonly System.Action _onSaved;

    private readonly MultilineTextBox[] _boxes = new MultilineTextBox[4];
    private int _activeField = -1;

    private Rectangle _cancelBtn;
    private Rectangle _saveGalleryBtn;
    private Rectangle _saveBtn;

    private const int Pad = 16;
    private const int FieldSpacing = 16;
    private const int LabelH = 30;
    private const int FooterH = 68;

    private static readonly Color TextColor = new(80, 45, 35);
    private static readonly Color BtnSave = new(125, 200, 105);
    private static readonly Color BtnReset = new(225, 125, 85);
    private static readonly Color BtnGallery = new(255, 248, 234);

    public FarmerFormPanel(
        FarmerStore store,
        IAliveNpcsApi api,
        PresetStore presetStore,
        GalleryService? galleryService,
        IMonitor monitor,
        ITranslationHelper i18n,
        System.Func<Rectangle> getContentArea,
        System.Action? onCancel = null,
        System.Action? onSaved = null)
    {
        _store = store;
        _api = api;
        _presetStore = presetStore;
        _ = galleryService;
        _monitor = monitor;
        _i18n = i18n;
        _getContentArea = getContentArea;
        _onCancel = onCancel ?? (() => { });
        _onSaved = onSaved ?? (() => { });

        for (int i = 0; i < _boxes.Length; i++)
            _boxes[i] = new MultilineTextBox(Rectangle.Empty, Game1.smallFont, TextColor) { TextLimit = 500 };

        LoadFromStore();
    }

    public void LoadFromStore()
    {
        // Prefer AliveNpcs' own sheet (path-independent, always the live truth).
        // Fall back to the local cache only when the base mod has nothing yet.
        var api = TryGetApiSheet();
        _boxes[0].Text = api?[0] ?? _store.Sheet.WhoAmI ?? "";
        _boxes[1].Text = api?[1] ?? _store.Sheet.WhyMovedHere ?? "";
        _boxes[2].Text = api?[2] ?? _store.Sheet.ExtraInfo ?? "";
        _boxes[3].Text = api?[3] ?? _store.Sheet.AtAGlanceDetails ?? "";
        UnsubscribeTextBox();
    }

    private string[]? TryGetApiSheet()
    {
        try
        {
            var sheet = _api.GetCharacterSheet();
            if (sheet is { Length: >= 4 } && sheet.Any(v => !string.IsNullOrWhiteSpace(v)))
                return sheet;
        }
        catch (Exception ex)
        {
            _monitor.Log($"GetCharacterSheet failed: {ex.Message}", LogLevel.Trace);
        }
        return null;
    }

    private static string[] Labels(ITranslationHelper i18n) => new[]
    {
        i18n.Get("farmer.field.1").ToString(),
        i18n.Get("farmer.field.2").ToString(),
        i18n.Get("farmer.field.3").ToString(),
        i18n.Get("farmer.field.4").ToString()
    };

    private (Rectangle area, Rectangle[] fieldRects, Rectangle footer) GetLayout()
    {
        var area = _getContentArea();
        var innerX = area.X + Pad;
        var innerW = area.Width - Pad * 2;
        var startY = area.Y + 8;
        var textBoxH = Math.Max(66, (area.Height - FooterH - 4 * (LabelH + FieldSpacing) - 20) / 4);
        var fieldRects = new Rectangle[4];
        for (int i = 0; i < 4; i++)
        {
            var y = startY + i * (LabelH + textBoxH + FieldSpacing);
            fieldRects[i] = new Rectangle(innerX, y, innerW, LabelH + textBoxH + FieldSpacing - 4);
        }
        var footerY = area.Bottom - FooterH;
        var footer = new Rectangle(innerX, footerY, innerW, FooterH);
        return (area, fieldRects, footer);
    }

    public void Draw(SpriteBatch b)
    {
        var (_, fieldRects, footer) = GetLayout();
        var labels = Labels(_i18n);

        for (int i = 0; i < 4; i++)
        {
            var r = fieldRects[i];
            Utility.drawTextWithShadow(b, labels[i], Game1.smallFont,
                new Vector2(r.X, r.Y), Color.SaddleBrown);

            var boxRect = new Rectangle(r.X, r.Y + LabelH, r.Width, r.Height - LabelH - FieldSpacing + 4);
            _boxes[i].Bounds = boxRect;
            _boxes[i].Draw(b);
        }

        var btnY = footer.Y + 10;
        var sideW = Math.Min(235, footer.Width / 4);
        var galleryW = Math.Min(335, footer.Width / 3);
        _cancelBtn = new Rectangle(footer.X, btnY, sideW, 48);
        _saveGalleryBtn = new Rectangle(footer.X + (footer.Width - galleryW) / 2, btnY, galleryW, 48);
        _saveBtn = new Rectangle(footer.Right - sideW, btnY, sideW, 48);

        DrawButton(b, _cancelBtn, _i18n.Get("button.cancel"), BtnReset);
        DrawButton(b, _saveGalleryBtn, _i18n.Get("button.save_gallery"), BtnGallery);
        DrawButton(b, _saveBtn, _i18n.Get("button.save"), BtnSave);
    }

    private static void DrawButton(SpriteBatch b, Rectangle rect, string label, Color bg)
        => EditorTheme.DrawButton(b, rect, label, bg, Color.Black);

    public void receiveLeftClick(int x, int y)
    {
        var (_, fieldRects, _) = GetLayout();

        for (int i = 0; i < 4; i++)
        {
            var boxRect = new Rectangle(fieldRects[i].X, fieldRects[i].Y + LabelH,
                fieldRects[i].Width, fieldRects[i].Height - LabelH - FieldSpacing + 4);
            if (boxRect.Contains(x, y))
            {
                _boxes[i].Bounds = boxRect;
                SetActiveField(i);
                _boxes[i].SetCursorFromClick(x, y);
                Game1.playSound("smallSelect");
                return;
            }
        }

        if (_cancelBtn.Contains(x, y))
        {
            LoadFromStore();
            _onCancel();
            Game1.playSound("bigDeSelect");
            return;
        }

        if (_saveGalleryBtn.Contains(x, y))
        {
            SaveToGallery();
            return;
        }

        if (_saveBtn.Contains(x, y))
        {
            CommitAndSave();
            Game1.playSound("coin");
            return;
        }

        UnsubscribeTextBox();
    }

    private void SetActiveField(int index)
    {
        _activeField = index;
        for (int i = 0; i < _boxes.Length; i++)
            _boxes[i].Selected = (i == index);
        if (index >= 0)
            Game1.keyboardDispatcher.Subscriber = _boxes[index];
    }

    private void UnsubscribeTextBox()
    {
        _activeField = -1;
        foreach (var box in _boxes)
            box.Selected = false;
        Game1.keyboardDispatcher.Subscriber = null;
    }

    private void CommitAndSave()
    {
        var sheet = new CharacterSheetData
        {
            WhoAmI = _boxes[0].Text.Trim(),
            WhyMovedHere = _boxes[1].Text.Trim(),
            ExtraInfo = _boxes[2].Text.Trim(),
            AtAGlanceDetails = _boxes[3].Text.Trim()
        };

        // Primary path: let AliveNpcs persist through its own manager (correct save
        // directory + live in-memory update), exactly like the in-game F7 menu.
        var savedViaApi = false;
        try
        {
            _api.UpdateCharacterSheet(sheet.WhoAmI, sheet.WhyMovedHere, sheet.ExtraInfo, sheet.AtAGlanceDetails);
            savedViaApi = true;
        }
        catch (Exception ex)
        {
            _monitor.Log($"UpdateCharacterSheet unavailable, using local file fallback: {ex.Message}", LogLevel.Warn);
        }

        // Keep a local cache regardless (harmless; also covers older AliveNpcs builds).
        _store.Save(sheet);

        // If the API path wasn't available, best-effort ask the base mod to reload.
        if (!savedViaApi)
            _onSaved();
    }

    // Saves to the LOCAL preset gallery only; uploading is an explicit opt-in from
    // the Gallery tab's Local view (privacy by default).
    private void SaveToGallery()
    {
        try
        {
            var entry = new NpcOverrideEntry
            {
                NpcName = "Farmer",
                PresetType = "farmer",
                CanonicalPersonality = _boxes[0].Text.Trim(),  // Who am I
                Appearance = _boxes[3].Text.Trim(),            // At-a-glance
                Lore = _boxes[1].Text.Trim(),                  // Why moved here
                SocialTags = _boxes[2].Text.Trim(),            // Extra info
            };
            if (!entry.HasAnyField)
            {
                Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.preset.none"), 3));
                return;
            }
            _presetStore.Save("Farmer", entry);
            Game1.addHUDMessage(new HUDMessage(_i18n.Get("gallery.preset.saved", new { npcName = "Farmer" }), 3));
            Game1.playSound("coin");
        }
        catch (Exception ex)
        {
            _monitor.Log($"SaveToGallery failed: {ex.Message}", LogLevel.Warn);
        }
    }

    public void receiveScrollWheelAction(int direction)
    {
        if (_activeField < 0) return;
        var box = _boxes[_activeField];
        if (box.Bounds.Contains(Game1.getMouseX(), Game1.getMouseY()) && box.NeedsScroll())
        {
            box.Scroll(direction);
        }
    }

    public void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            UnsubscribeTextBox();
            return;
        }
        if (_activeField >= 0)
        {
            var subscriber = Game1.keyboardDispatcher.Subscriber;
            if (subscriber is MultilineTextBox box)
                box.RecieveSpecialInput(key);
        }
    }

    public void Unsubscribe()
    {
        UnsubscribeTextBox();
    }
}
