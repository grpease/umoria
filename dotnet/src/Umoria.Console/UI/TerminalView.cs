using System.Drawing;
using System.Text;
using Terminal.Gui;
using Umoria.Core.Ui;

namespace Umoria.Console.UI;

// Draws the game's virtual 80x24 screen (Umoria.Core.Ui.Terminal) and
// forwards keys to the game thread. Everything on the screen is the ported
// ui_io.cpp / ui.cpp code's; this view only copies characters, so it never
// decides what the game shows. The game thread signals Terminal.Refreshed
// (curses' refresh()) and the redraw is marshalled onto the UI thread.
public sealed class TerminalView : View
{
    private readonly Umoria.Core.Ui.Terminal _terminal;
    private readonly KeyQueue _keys;

    public TerminalView(Umoria.Core.Ui.Terminal terminal, KeyQueue keys)
    {
        _terminal = terminal;
        _keys = keys;

        CanFocus = true;
        Width = Umoria.Core.Ui.Terminal.Cols;
        Height = Umoria.Core.Ui.Terminal.Rows;
        CursorVisibility = CursorVisibility.Default;

        _terminal.Refreshed += () => Application.Invoke(SetNeedsDraw);
        _terminal.Bell += () => System.Console.Beep();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var lines = _terminal.Snapshot();

        for (var row = 0; row < lines.Length; row++)
        {
            Move(0, row);
            foreach (var ch in lines[row])
            {
                AddRune(new Rune(ch));
            }
        }

        return true;
    }

    // the curses cursor (prompts leave it after their text; the main screen
    // leaves it on the player)
    public override Point? PositionCursor()
    {
        Move(_terminal.CursorX, _terminal.CursorY);
        return new Point(_terminal.CursorX, _terminal.CursorY);
    }

    protected override bool OnKeyDown(Key key)
    {
        var moriaKey = KeyTranslator.ToMoriaKey(key);
        if (moriaKey is null)
        {
            return base.OnKeyDown(key);
        }

        _keys.Add(moriaKey.Value);
        return true;
    }
}
