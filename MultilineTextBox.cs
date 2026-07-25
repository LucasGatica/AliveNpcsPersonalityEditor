using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;

namespace AliveNpcsPersonalityEditor;

/// <summary>A small multiline editor for menus. The game's built-in TextBox only renders one line.</summary>
internal sealed class MultilineTextBox : IKeyboardSubscriber
{
    private readonly SpriteFont _font;
    private readonly Color _textColor;
    private string _text = "";
    private int _cursor;
    private int _scrollLine; // first visible line index

    public Rectangle Bounds { get; set; }
    public Color TextColor => _textColor;
    public string Text
    {
        get => _text;
        set
        {
            _text = (value ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            _cursor = _text.Length;
            _scrollLine = 0;
        }
    }
    public int TextLimit { get; set; } = 100000;
    public bool Selected { get; set; }
    public Color BackgroundColor { get; set; } = new(255, 248, 232);
    public Color BorderColor { get; set; } = new(125, 60, 40);

    // Inner padding that keeps text clear of the themed wooden frame border.
    private const int PadX = 16;
    private const int PadY = 14;
    private int InnerHeight => Math.Max(1, Bounds.Height - PadY * 2);
    private int LineHeight => (int)_font.MeasureString("A").Y;
    private int VisibleLines => Math.Max(1, InnerHeight / LineHeight);

    public void Scroll(int direction)
    {
        _scrollLine -= Math.Sign(direction);
        var lines = GetWrappedLines();
        _scrollLine = Math.Clamp(_scrollLine, 0, Math.Max(0, lines.Count - VisibleLines));
    }

    public bool NeedsScroll()
    {
        return GetWrappedLines().Count > VisibleLines;
    }

    public MultilineTextBox(Rectangle bounds, SpriteFont font, Color textColor)
    {
        Bounds = bounds;
        _font = font;
        _textColor = textColor;
    }

    public void Draw(SpriteBatch b)
    {
        var lines = GetWrappedLines();
        var lineHeight = LineHeight;
        var visibleLines = VisibleLines;

        // Only clamp scroll to caret when selected; allow free scroll when not
        if (Selected)
        {
            var cursorLine = FindCursorLine(lines);
            if (cursorLine < _scrollLine)
                _scrollLine = cursorLine;
            else if (cursorLine >= _scrollLine + visibleLines)
                _scrollLine = cursorLine - visibleLines + 1;
        }
        _scrollLine = Math.Clamp(_scrollLine, 0, Math.Max(0, lines.Count - visibleLines));

        var firstLine = _scrollLine;

        // Themed sunken input frame (honours UI-reskin mods).
        EditorTheme.DrawInputFrame(b, Bounds, Selected);

        for (var i = firstLine; i < lines.Count && i < firstLine + visibleLines; i++)
        {
            var line = lines[i];
            var display = Text.Substring(line.Start, line.Length).TrimEnd();
            var y = Bounds.Y + PadY + (i - firstLine) * lineHeight;
            b.DrawString(_font, display, new Vector2(Bounds.X + PadX, y), _textColor);
        }

        if (Selected && DateTime.UtcNow.Millisecond < 500)
        {
            var caretLine = FindCursorLine(lines);
            var curLine = Math.Clamp(caretLine, firstLine, firstLine + visibleLines - 1);
            var current = lines[curLine];
            var charactersBeforeCursor = Math.Clamp(_cursor - current.Start, 0, current.Length);
            var before = Text.Substring(current.Start, charactersBeforeCursor);
            var caretX = Bounds.X + PadX + (int)_font.MeasureString(before).X;
            var caretY = Bounds.Y + PadY + (curLine - firstLine) * lineHeight;
            b.Draw(Game1.staminaRect, new Rectangle(caretX, caretY, 2, lineHeight), _textColor);
        }

        // Draw scrollbar if content overflows
        if (lines.Count > visibleLines)
        {
            var barX = Bounds.Right - PadX + 2;
            var barY = Bounds.Y + PadY;
            var barH = InnerHeight;
            var thumbH = Math.Max(12, barH * visibleLines / lines.Count);
            var thumbY = barY + (int)((float)_scrollLine / Math.Max(1, lines.Count - visibleLines) * (barH - thumbH));
            b.Draw(Game1.staminaRect, new Rectangle(barX, barY, 3, barH), Color.Black * 0.1f);
            b.Draw(Game1.staminaRect, new Rectangle(barX, thumbY, 3, thumbH), _textColor * 0.4f);
        }
    }

    public void SetCursorFromClick(int x, int y)
    {
        var lines = GetWrappedLines();
        var lineHeight = LineHeight;
        var visibleLines = VisibleLines;
        var firstLine = Math.Clamp(_scrollLine, 0, Math.Max(0, lines.Count - visibleLines));
        var lineIndex = Math.Clamp(firstLine + (y - Bounds.Y - PadY) / lineHeight, firstLine, Math.Min(firstLine + visibleLines - 1, lines.Count - 1));
        var line = lines[lineIndex];
        var targetX = Math.Max(0, x - Bounds.X - PadX);
        var position = line.Start;

        while (position < line.Start + line.Length
            && _font.MeasureString(Text.Substring(line.Start, position - line.Start + 1)).X <= targetX)
        {
            position++;
        }

        _cursor = position;
    }

    public void RecieveTextInput(char inputChar)
    {
        if (!char.IsControl(inputChar))
            Insert(inputChar.ToString());
    }

    public void RecieveTextInput(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Insert(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
    }

    public void RecieveCommandInput(char command)
    {
        if (command == '\b')
            Backspace();
    }

    public void RecieveSpecialInput(Keys key)
    {
        switch (key)
        {
            case Keys.Delete:
                if (_cursor < Text.Length)
                    _text = Text.Remove(_cursor, 1);
                break;
            case Keys.Left:
                _cursor = Math.Max(0, _cursor - 1);
                break;
            case Keys.Right:
                _cursor = Math.Min(Text.Length, _cursor + 1);
                break;
            case Keys.Up:
                MoveToAdjacentLine(-1);
                break;
            case Keys.Down:
                MoveToAdjacentLine(1);
                break;
            case Keys.Home:
                MoveToLineStart();
                break;
            case Keys.End:
                MoveToLineEnd();
                break;
            case Keys.Enter:
                Insert("\n");
                break;
        }
    }

    private void MoveToAdjacentLine(int direction)
    {
        var lines = GetWrappedLines();
        if (lines.Count <= 1) return;

        var currentLine = FindCursorLine(lines);
        var targetLine = Math.Clamp(currentLine + direction, 0, lines.Count - 1);
        if (targetLine == currentLine) return;

        var cur = lines[currentLine];
        var charsBeforeCursor = Math.Clamp(_cursor - cur.Start, 0, cur.Length);
        var pixelOffset = _font.MeasureString(Text.Substring(cur.Start, charsBeforeCursor)).X;

        var target = lines[targetLine];
        var position = target.Start;
        while (position < target.Start + target.Length
            && _font.MeasureString(Text.Substring(target.Start, position - target.Start + 1)).X <= pixelOffset)
        {
            position++;
        }
        _cursor = position;
    }

    private void MoveToLineStart()
    {
        var lines = GetWrappedLines();
        var currentLine = FindCursorLine(lines);
        _cursor = lines[currentLine].Start;
    }

    private void MoveToLineEnd()
    {
        var lines = GetWrappedLines();
        var currentLine = FindCursorLine(lines);
        var line = lines[currentLine];
        _cursor = line.Start + line.Length;
    }

    private void Insert(string value)
    {
        if (Text.Length >= TextLimit)
            return;

        value = value[..Math.Min(value.Length, TextLimit - Text.Length)];
        _text = Text.Insert(_cursor, value);
        _cursor += value.Length;
    }

    private void Backspace()
    {
        if (_cursor == 0)
            return;

        _text = Text.Remove(_cursor - 1, 1);
        _cursor--;
    }

    private int FindCursorLine(IReadOnlyList<WrappedLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (_cursor <= line.Start + line.Length || i == lines.Count - 1)
                return i;
        }

        return lines.Count - 1;
    }

    private List<WrappedLine> GetWrappedLines()
    {
        var lines = new List<WrappedLine>();
        var maxWidth = Bounds.Width - PadX * 2;
        var start = 0;

        while (start < Text.Length)
        {
            var newline = Text.IndexOf('\n', start);
            var paragraphEnd = newline >= 0 ? newline : Text.Length;
            var position = start;

            if (position == paragraphEnd)
                lines.Add(new WrappedLine(start, 0));

            while (position < paragraphEnd)
            {
                var end = position;
                var lastWhitespace = -1;
                while (end < paragraphEnd && _font.MeasureString(Text.Substring(position, end - position + 1)).X <= maxWidth)
                {
                    if (char.IsWhiteSpace(Text[end]))
                        lastWhitespace = end;
                    end++;
                }

                if (end == position)
                    end++;

                if (end < paragraphEnd && lastWhitespace >= position)
                {
                    lines.Add(new WrappedLine(position, lastWhitespace - position));
                    position = lastWhitespace + 1;
                }
                else
                {
                    lines.Add(new WrappedLine(position, end - position));
                    position = end;
                }
            }

            start = newline >= 0 ? newline + 1 : Text.Length;
        }

        if (lines.Count == 0)
            lines.Add(new WrappedLine(0, 0));
        else if (Text.EndsWith('\n'))
            lines.Add(new WrappedLine(Text.Length, 0));

        return lines;
    }

    private readonly record struct WrappedLine(int Start, int Length);
}
