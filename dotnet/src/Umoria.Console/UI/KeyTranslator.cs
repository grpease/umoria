using Terminal.Gui;

namespace Umoria.Console.UI;

// Turns a Terminal.Gui key into the byte curses would have handed the
// original: printable ASCII as itself, Ctrl+letter as its control code,
// <ESCAPE>, <RETURN> and backspace. The cursor and keypad navigation keys
// become the numeric-keypad directions (7 8 9 / 4 6 / 1 2 3), as a
// convenience -- the original disables curses' keypad translation and reads
// the digits (src/ui_io.cpp moriaTerminalInitialize()).
public static class KeyTranslator
{
    public static char? ToMoriaKey(Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.Esc:
                return (char)27;
            case KeyCode.Enter:
                return '\r';
            case KeyCode.Backspace:
                return '\b';
            case KeyCode.Delete:
                return (char)0x7f;
            case KeyCode.Tab:
                return '\t';
            case KeyCode.CursorUp:
                return '8';
            case KeyCode.CursorDown:
                return '2';
            case KeyCode.CursorLeft:
                return '4';
            case KeyCode.CursorRight:
                return '6';
            case KeyCode.Home:
                return '7';
            case KeyCode.End:
                return '1';
            case KeyCode.PageUp:
                return '9';
            case KeyCode.PageDown:
                return '3';
        }

        if (key.IsCtrl && !key.IsAlt)
        {
            var baseKey = key.NoCtrl.NoShift.KeyCode;
            if (baseKey >= KeyCode.A && baseKey <= KeyCode.Z)
            {
                return (char)(baseKey - KeyCode.A + 1);
            }
        }

        var rune = key.AsRune;
        if (rune.Value is >= ' ' and <= '~')
        {
            return (char)rune.Value;
        }

        return null;
    }
}
