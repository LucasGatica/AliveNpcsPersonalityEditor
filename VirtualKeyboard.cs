using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace AliveNpcsPersonalityEditor;

/// <summary>
/// A touch-friendly on-screen keyboard drawn inside the owning menu.
/// <para>
/// The game's own keyboard (<c>Game1.showTextEntry</c>) cannot be used: it only accepts a vanilla
/// <c>TextBox</c>, and outside gamepad mode it silently declines to open — <c>Game1.textEntry</c>
/// stays null. This is a self-contained replacement that types straight into our
/// <see cref="MultilineTextBox"/>, so it also supports newlines.
/// </para>
/// <para>
/// It carries its own preview strip, mirroring the vanilla keyboard: at small resolutions the panel
/// covers the field being edited, so without a preview the player cannot see what they are typing.
/// </para>
/// </summary>
internal sealed class VirtualKeyboard
{
    // Base metrics, tuned against a 1280-wide UI viewport. Everything is multiplied by _scale at
    // layout time so the keys stay the same size *relative to the window* rather than in raw pixels.
    private const int BaseKeyGap = 6;
    private const int BaseKeyWidth = 64;
    private const int BaseRowHeight = 52;
    private const int BaseMargin = 16;
    private const int BaseBorder = 3;
    private const int BasePreviewPadding = 10;
    private const float ReferenceViewportWidth = 1280f;

    /// <summary>Lines of typed text kept visible in the preview strip.</summary>
    private const int PreviewLines = 3;

    /// <summary>Most of the window the panel may ever occupy vertically.</summary>
    private const float MaxPanelHeightFraction = 0.62f;

    // Flat, high-contrast palette in the spirit of the vanilla keyboard: cream keys, dark outline,
    // lavender for the non-character controls so they read as different at a glance.
    private static readonly Color KeyFace = new(255, 247, 222);
    private static readonly Color KeyFaceControl = new(180, 157, 216);
    private static readonly Color KeyFaceActive = new(140, 112, 190);
    private static readonly Color KeyOutline = new(90, 58, 32);
    private static readonly Color PreviewFill = new(48, 32, 22);
    private static readonly Color PreviewText = new(255, 247, 222);

    private static readonly string[] Rows =
    {
        "1234567890",
        "qwertyuiop",
        "asdfghjkl'",
        // '*' leads the bottom row: it wraps roleplay actions, so it gets a first-class spot.
        "*zxcvbnm,.?"
    };

    private readonly List<KeyButton> _keys = new();
    private MultilineTextBox? _target;
    private bool _shift;
    private Rectangle _panel;
    private Rectangle _preview;

    private float _scale = 1f;
    private int _keyGap;
    private int _rowHeight;
    private int _margin;
    private int _border;
    private Point _layoutViewport;

    private int Scaled(int value) => Math.Max(1, (int)Math.Round(value * _scale));

    /// <summary>
    /// Supplies the three control-key labels. Ported from AliveNpcs, which reads them from its own
    /// generated I18n class; this mod uses SMAPI's translation helper, so it is injected once at
    /// mod entry and falls back to English if it was never set.
    /// </summary>
    public static StardewModdingAPI.ITranslationHelper? Translations { get; set; }

    private static string Label(string key, string fallback)
    {
        return Translations?.Get(key).Default(fallback).ToString() ?? fallback;
    }

    public bool IsOpen { get; private set; }

    /// <summary>True when the keyboard is open and typing into this specific field.</summary>
    public bool IsTargeting(MultilineTextBox box) => IsOpen && ReferenceEquals(_target, box);

    /// <summary>
    /// Whether this point belongs to the keyboard. Clicks here are swallowed, so the owning menu
    /// should treat everything under the panel as unreachable rather than merely hidden.
    /// </summary>
    public bool Contains(int x, int y) => IsOpen && _panel.Contains(x, y);

    public void Open(MultilineTextBox target)
    {
        _target = target;
        _shift = false;
        IsOpen = true;
        Layout();
    }

    public void Close()
    {
        IsOpen = false;
        _target = null;
        _keys.Clear();
    }

    /// <summary>Handles a click. Returns true when the keyboard consumed it.</summary>
    public bool ReceiveLeftClick(int x, int y)
    {
        if (!IsOpen)
            return false;

        foreach (var key in _keys)
        {
            if (!key.Bounds.Contains(x, y))
                continue;

            Press(key);
            return true;
        }

        // Swallow clicks anywhere on the panel so they don't fall through to the menu behind it.
        return _panel.Contains(x, y);
    }

    private void Press(KeyButton key)
    {
        Game1.playSound("smallSelect");

        switch (key.Action)
        {
            case KeyAction.Shift:
                _shift = !_shift;
                return;

            case KeyAction.Backspace:
                _target?.RecieveSpecialInput(Keys.Back);
                return;

            case KeyAction.Space:
                _target?.RecieveTextInput(' ');
                return;

            case KeyAction.Enter:
                _target?.RecieveSpecialInput(Keys.Enter);
                return;

            case KeyAction.Done:
                Close();
                return;

            default:
                var ch = _shift ? char.ToUpperInvariant(key.Character) : key.Character;
                _target?.RecieveTextInput(ch);
                // Shift acts as a one-shot for letters, matching phone keyboards.
                if (_shift && char.IsLetter(ch))
                    _shift = false;
                return;
        }
    }

    private void Layout()
    {
        _keys.Clear();

        var viewport = Game1.uiViewport;
        _layoutViewport = new Point(viewport.Width, viewport.Height);

        var longestRow = Rows.Max(r => r.Length);
        var baseLineHeight = Game1.smallFont.MeasureString("Ay").Y;

        var baseKeysWidth = longestRow * BaseKeyWidth + (longestRow - 1) * BaseKeyGap;
        var basePreviewHeight = (int)(PreviewLines * baseLineHeight) + BasePreviewPadding * 2;
        var basePanelWidth = baseKeysWidth + BaseMargin * 2;
        var basePanelHeight = basePreviewHeight + BaseKeyGap
                              + (Rows.Length + 1) * (BaseRowHeight + BaseKeyGap)
                              + BaseMargin * 2;

        // A fixed 64px key is a comfortable touch target in a 1280-wide window and a cramped one at
        // 4K, so grow with the viewport instead of capping. Then clamp twice: once so the panel fits
        // the window's width, once so it never eats more than MaxPanelHeightFraction of its height.
        var scale = viewport.Width / ReferenceViewportWidth;
        scale = Math.Min(scale, (viewport.Width - BaseMargin * 2f) / basePanelWidth);
        scale = Math.Min(scale, viewport.Height * MaxPanelHeightFraction / basePanelHeight);
        _scale = Math.Clamp(scale, 0.5f, 2.5f);

        _keyGap = Scaled(BaseKeyGap);
        _rowHeight = Scaled(BaseRowHeight);
        _margin = Scaled(BaseMargin);
        _border = Scaled(BaseBorder);

        var keyWidth = Scaled(BaseKeyWidth);
        var keysWidth = longestRow * keyWidth + (longestRow - 1) * _keyGap;
        var previewHeight = Scaled(basePreviewHeight);
        var panelWidth = keysWidth + _margin * 2;
        var panelHeight = previewHeight + _keyGap + (Rows.Length + 1) * (_rowHeight + _keyGap) + _margin * 2;

        _panel = new Rectangle(
            (viewport.Width - panelWidth) / 2,
            viewport.Height - panelHeight - _margin,
            panelWidth,
            panelHeight);

        _preview = new Rectangle(_panel.X + _margin, _panel.Y + _margin, keysWidth, previewHeight);

        var y = _preview.Bottom + _keyGap;
        foreach (var row in Rows)
        {
            var rowWidth = row.Length * keyWidth + (row.Length - 1) * _keyGap;
            var x = _panel.X + (_panel.Width - rowWidth) / 2;
            foreach (var ch in row)
            {
                _keys.Add(new KeyButton(
                    new Rectangle(x, y, keyWidth, _rowHeight),
                    ch.ToString(),
                    KeyAction.Character,
                    ch));
                x += keyWidth + _keyGap;
            }

            y += _rowHeight + _keyGap;
        }

        LayoutControlRow(y, keysWidth);
    }

    /// <summary>
    /// Lays the bottom row out as proportions of the exact key-area width, so the labels always fit.
    /// Fixed unit multiples overflowed the panel and made the labels overlap each other.
    /// </summary>
    private void LayoutControlRow(int y, int keysWidth)
    {
        var controls = new (string Label, KeyAction Action, float Weight)[]
        {
            (Label("ui.keyboardShift", "Shift"), KeyAction.Shift, 1.4f),
            (Label("ui.keyboardSpace", "Space"), KeyAction.Space, 3.2f),
            // Backspace and newline draw icons instead of text: Stardew's font maps '<' to a heart
            // glyph and has no return arrow, so any ASCII/unicode arrow renders wrong.
            ("", KeyAction.Backspace, 1.2f),
            ("", KeyAction.Enter, 1.2f),
            (Label("ui.keyboardDone", "Done"), KeyAction.Done, 1.4f)
        };

        var totalWeight = controls.Sum(c => c.Weight);
        var available = keysWidth - _keyGap * (controls.Length - 1);
        var x = _panel.X + _margin;

        for (var i = 0; i < controls.Length; i++)
        {
            // Last key absorbs the rounding remainder so the row ends flush with the key area.
            var w = i == controls.Length - 1
                ? _panel.X + _margin + keysWidth - x
                : (int)(available * (controls[i].Weight / totalWeight));

            _keys.Add(new KeyButton(
                new Rectangle(x, y, w, _rowHeight),
                controls[i].Label,
                controls[i].Action,
                '\0'));

            x += w + _keyGap;
        }
    }

    public void Draw(SpriteBatch b)
    {
        if (!IsOpen)
            return;

        // Re-layout whenever the window changed size while the keyboard was open, not just when the
        // panel has already spilled off-screen — a window that grew needs bigger keys too.
        if (_layoutViewport.X != Game1.uiViewport.Width || _layoutViewport.Y != Game1.uiViewport.Height)
            Layout();

        IClickableMenu.drawTextureBox(b, _panel.X, _panel.Y, _panel.Width, _panel.Height, Color.White);

        DrawPreview(b);

        foreach (var key in _keys)
        {
            var isControl = key.Action != KeyAction.Character;
            var face = key.Action == KeyAction.Shift && _shift
                ? KeyFaceActive
                : isControl ? KeyFaceControl : KeyFace;

            DrawFlatKey(b, key.Bounds, face, _border);

            if (key.Action == KeyAction.Backspace)
            {
                DrawBackspaceIcon(b, key.Bounds);
                continue;
            }

            if (key.Action == KeyAction.Enter)
            {
                DrawNewlineIcon(b, key.Bounds);
                continue;
            }

            var label = key.Action == KeyAction.Character && _shift
                ? key.Label.ToUpperInvariant()
                : key.Label;

            // Control labels are words, so drop to the small font when one would overflow its key,
            // then shrink below the panel scale if even that does not fit a long translation.
            var font = Game1.smallFont;
            var labelScale = _scale;
            var available = key.Bounds.Width - Scaled(12);
            var size = font.MeasureString(label);

            if (size.X * labelScale > available)
            {
                font = Game1.tinyFont;
                size = font.MeasureString(label);
                labelScale = Math.Min(labelScale, available / Math.Max(1f, size.X));
            }

            b.DrawString(
                font,
                label,
                new Vector2(
                    key.Bounds.X + (key.Bounds.Width - size.X * labelScale) / 2,
                    key.Bounds.Y + (key.Bounds.Height - size.Y * labelScale) / 2),
                KeyOutline,
                0f,
                Vector2.Zero,
                labelScale,
                SpriteEffects.None,
                0f);
        }
    }

    /// <summary>
    /// Draws the button that opens this keyboard, in the same flat lavender style as the keyboard's
    /// own control keys so the two read as one feature.
    /// <para>
    /// It carries a drawn glyph rather than a label: "ABC" overflowed the button, and any word we
    /// put there would have to survive every translation at the same fixed width.
    /// </para>
    /// </summary>
    public static void DrawLauncherButton(SpriteBatch b, Rectangle bounds, bool isActive)
    {
        DrawFlatKey(b, bounds, isActive ? KeyFaceActive : KeyFaceControl);
        DrawKeyboardGlyph(b, bounds, isActive ? KeyFace : KeyOutline);
    }

    /// <summary>A tiny keyboard: two rows of keys over a space bar.</summary>
    private static void DrawKeyboardGlyph(SpriteBatch b, Rectangle bounds, Color color)
    {
        const int columns = 5;
        const int rows = 3;

        var unit = Math.Max(2, Math.Min(bounds.Width, bounds.Height) / 12);
        var step = unit * 2;
        var glyphWidth = columns * step - unit;
        var glyphHeight = rows * step - unit;

        var x0 = bounds.X + (bounds.Width - glyphWidth) / 2;
        var y0 = bounds.Y + (bounds.Height - glyphHeight) / 2;

        for (var row = 0; row < rows - 1; row++)
        {
            for (var col = 0; col < columns; col++)
                b.Draw(Game1.staminaRect, new Rectangle(x0 + col * step, y0 + row * step, unit, unit), color);
        }

        // Space bar: inset one key on each side so it reads as a keyboard, not a grid of dots.
        b.Draw(
            Game1.staminaRect,
            new Rectangle(x0 + step, y0 + (rows - 1) * step, glyphWidth - step * 2 + unit, unit),
            color);
    }

    /// <summary>Pixel left-pointing arrow head, drawn as stacked rectangles.</summary>
    private static void DrawLeftArrowHead(SpriteBatch b, int tipX, int centerY, int unit)
    {
        for (var i = 0; i < 4; i++)
        {
            var half = unit * (i + 1);
            b.Draw(
                Game1.staminaRect,
                new Rectangle(tipX + i * unit, centerY - half, unit, half * 2),
                KeyOutline);
        }
    }

    /// <summary>Backspace: a left arrow. Drawn rather than typed — '&lt;' renders as a heart.</summary>
    private static void DrawBackspaceIcon(SpriteBatch b, Rectangle bounds)
    {
        var unit = Math.Max(2, bounds.Height / 14);
        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;

        DrawLeftArrowHead(b, cx - unit * 5, cy, unit);
        b.Draw(Game1.staminaRect, new Rectangle(cx - unit, cy - unit, unit * 6, unit * 2), KeyOutline);
    }

    /// <summary>Newline: an arrow dropping down then turning left, like a return key.</summary>
    private static void DrawNewlineIcon(SpriteBatch b, Rectangle bounds)
    {
        var unit = Math.Max(2, bounds.Height / 14);
        var cx = bounds.Center.X;
        var cy = bounds.Center.Y;

        // Vertical stem on the right, dropping to the horizontal run.
        b.Draw(Game1.staminaRect, new Rectangle(cx + unit * 4, cy - unit * 4, unit * 2, unit * 6), KeyOutline);
        // Horizontal run leading left.
        b.Draw(Game1.staminaRect, new Rectangle(cx - unit * 2, cy + unit * 2, unit * 8, unit * 2), KeyOutline);
        DrawLeftArrowHead(b, cx - unit * 6, cy + unit * 3, unit);
    }

    /// <summary>Flat key: solid outline with an inset face. Crisper than a nested texture box.</summary>
    private static void DrawFlatKey(SpriteBatch b, Rectangle bounds, Color face, int border = BaseBorder)
    {
        b.Draw(Game1.staminaRect, bounds, KeyOutline);
        b.Draw(
            Game1.staminaRect,
            new Rectangle(
                bounds.X + border,
                bounds.Y + border,
                bounds.Width - border * 2,
                bounds.Height - border * 2),
            face);
    }

    /// <summary>
    /// Shows the text being typed. At low resolutions the panel covers the field itself, so this is
    /// the only way to see what is going in.
    /// </summary>
    private void DrawPreview(SpriteBatch b)
    {
        DrawFlatKey(b, _preview, PreviewFill, _border);

        var text = _target?.Text ?? "";
        var caret = Game1.ticks / 30 % 2 == 0 ? "|" : " ";
        var padding = Scaled(BasePreviewPadding);

        // Wrap at the preview's width expressed in unscaled font units, since the text is drawn with
        // the panel scale applied.
        var wrapWidth = Math.Max(32, (int)((_preview.Width - padding * 2) / _scale));

        // Wrap each authored line separately so the player's own newlines survive.
        var lines = new List<string>();
        foreach (var paragraph in (text + caret).Split('\n'))
            lines.AddRange(Game1.parseText(paragraph, Game1.smallFont, wrapWidth).Split('\n'));

        // Follow the caret: show the last PreviewLines lines, which is where typing is happening.
        var first = Math.Max(0, lines.Count - PreviewLines);
        var lineHeight = Game1.smallFont.MeasureString("Ay").Y * _scale;
        var y = (float)(_preview.Y + padding);

        for (var i = first; i < lines.Count; i++)
        {
            b.DrawString(
                Game1.smallFont,
                lines[i],
                new Vector2(_preview.X + padding, y),
                PreviewText,
                0f,
                Vector2.Zero,
                _scale,
                SpriteEffects.None,
                0f);

            y += lineHeight;
        }
    }

    private enum KeyAction
    {
        Character,
        Shift,
        Backspace,
        Space,
        Enter,
        Done
    }

    private readonly record struct KeyButton(Rectangle Bounds, string Label, KeyAction Action, char Character);
}
