namespace Umoria.Core.Ui;

// Where keypresses come from: the real terminal (Umoria.Console) or a test
// script. ReadKey() blocks until a key is available and returns it, or -1
// for end of input (curses' EOF from getch()).
public interface IKeySource
{
    int ReadKey();
}

// The slice of curses src/ui_io.cpp uses (M14), as a virtual screen: stdscr
// and the spare save_screen window, both Rows x Cols characters, plus the
// cursor. ui_io.cpp is the only file in the original that calls curses, so
// everything above this (GameUi) is a port of the real ui_io.cpp / ui.cpp /
// ui_inventory.cpp code, and a frontend only has to draw Snapshot() and feed
// keys. tools/ui_screen_dump's shim curses.h implements the same semantics
// for the C++ side, so their screens can be compared byte for byte.
//
// Semantics kept from curses, as far as the original relies on them:
//   - move() outside the screen fails (ERR) and leaves the cursor alone;
//   - addch() writes at the cursor and advances it, wrapping to the next line
//     at the right edge (the last cell of the screen stays put);
//   - clrtoeol()/clrtobot() blank from the cursor without moving it;
//   - clear() blanks the screen and homes the cursor;
//   - overwrite() copies the characters only (not the cursor);
//   - addch('\n') clears to the end of the line and moves to the next line,
//     addch('\t') blanks to the next multiple of 8.
// Not kept: attributes, and curses' "^X" rendering of other control
// characters (the game never prints one; hallucination glyphs are 32..126).
public sealed class Terminal
{
    public const int Rows = 24;
    public const int Cols = 80;

    private readonly char[,] _screen = new char[Rows, Cols];
    private readonly char[,] _saved = new char[Rows, Cols];
    private readonly IKeySource _keys;

    public Terminal(IKeySource keys)
    {
        _keys = keys;
        Fill(_screen);
        Fill(_saved);
    }

    // Held while the screen is changed or copied, so a frontend thread can
    // take a consistent Snapshot() while the game thread draws.
    public object Sync { get; } = new();

    public int CursorY { get; private set; }
    public int CursorX { get; private set; }

    // refresh() -- the screen is ready to be shown.
    public event Action? Refreshed;

    // The terminal bell (terminalBellSound()'s write of "\007").
    public event Action? Bell;

    // Called before every key read, with the screen as the player sees it
    // while waiting -- the screen trace's sample point.
    public Action<Terminal>? BeforeKeyRead { get; set; }

    public bool Move(int y, int x)
    {
        if (y < 0 || y >= Rows || x < 0 || x >= Cols)
        {
            return false;
        }

        CursorY = y;
        CursorX = x;
        return true;
    }

    public bool AddCh(char ch)
    {
        if (ch == '\n')
        {
            // curses: clear to the end of the line, then go to the start of
            // the next (the pagers print fgets() lines, newline included)
            ClrToEol();
            if (CursorY >= Rows - 1)
            {
                return false;
            }
            CursorY++;
            CursorX = 0;
            return true;
        }

        if (ch == '\t')
        {
            // curses: blanks up to the next multiple of 8
            do
            {
                if (!AddCh(' '))
                {
                    return false;
                }
            } while (CursorX % 8 != 0);
            return true;
        }

        lock (Sync)
        {
            _screen[CursorY, CursorX] = ch;

            if (CursorX < Cols - 1)
            {
                CursorX++;
            }
            else if (CursorY < Rows - 1)
            {
                CursorX = 0;
                CursorY++;
            }
            else
            {
                // bottom-right corner: curses can't scroll, so it fails
                return false;
            }
        }

        return true;
    }

    public bool AddStr(string text)
    {
        foreach (var ch in text)
        {
            if (!AddCh(ch))
            {
                return false;
            }
        }

        return true;
    }

    public bool MvAddCh(int y, int x, char ch) => Move(y, x) && AddCh(ch);

    public bool MvAddStr(int y, int x, string text) => Move(y, x) && AddStr(text);

    public void ClrToEol()
    {
        lock (Sync)
        {
            for (var x = CursorX; x < Cols; x++)
            {
                _screen[CursorY, x] = ' ';
            }
        }
    }

    public void ClrToBot()
    {
        lock (Sync)
        {
            ClrToEol();

            for (var y = CursorY + 1; y < Rows; y++)
            {
                for (var x = 0; x < Cols; x++)
                {
                    _screen[y, x] = ' ';
                }
            }
        }
    }

    public void Clear()
    {
        lock (Sync)
        {
            Fill(_screen);
        }

        CursorY = 0;
        CursorX = 0;
    }

    // overwrite(stdscr, save_screen)
    public void SaveScreen()
    {
        lock (Sync)
        {
            Array.Copy(_screen, _saved, _screen.Length);
        }
    }

    // overwrite(save_screen, stdscr); touchwin(stdscr)
    public void RestoreScreen()
    {
        lock (Sync)
        {
            Array.Copy(_saved, _screen, _screen.Length);
        }
    }

    public void Refresh() => Refreshed?.Invoke();

    public void RingBell() => Bell?.Invoke();

    // getch() -- blocks for the next key; -1 at end of input.
    public int GetCh()
    {
        BeforeKeyRead?.Invoke(this);
        return _keys.ReadKey();
    }

    public char CharAt(int y, int x)
    {
        lock (Sync)
        {
            return _screen[y, x];
        }
    }

    // The screen as Rows strings of Cols characters.
    public string[] Snapshot()
    {
        lock (Sync)
        {
            var lines = new string[Rows];
            var row = new char[Cols];

            for (var y = 0; y < Rows; y++)
            {
                for (var x = 0; x < Cols; x++)
                {
                    row[x] = _screen[y, x];
                }

                lines[y] = new string(row);
            }

            return lines;
        }
    }

    private static void Fill(char[,] grid)
    {
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Cols; x++)
            {
                grid[y, x] = ' ';
            }
        }
    }
}
