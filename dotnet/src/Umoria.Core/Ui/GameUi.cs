using Umoria.Core.Character;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Ui;

// The terminal UI (M14): src/ui_io.cpp, src/ui.cpp and the display half of
// src/ui_inventory.cpp, ported over the virtual curses screen (Terminal).
//
// It plugs into the port the way the C++ display calls are wired into the
// original's game logic:
//   - Attach() binds the PlayerWorld input hooks (ReadCommand, Confirm,
//     ConfirmWithAbort, ReadString, ScreenWrite) to the real prompts, and
//     sets PlayerWorld.Ui, which the game logic's display call sites call
//     through (`w.Ui?.X()` -- headless runs and the older trace tests leave
//     it null, so their behaviour is unchanged).
//   - Code that has only the dungeon (the light and panel code, which run
//     below PlayerWorld) reaches the UI through ScreenRendering, keyed by
//     the Dungeon object.
//
// One deliberate departure from the original, cosmetic only: hallucination
// glyphs come from a cosmetic RNG, not the game RNG, so rendering never
// changes the game's random number stream (the M7 open decision, settled in
// M14). caveGetTileSymbol()'s callers in the running algorithm still draw
// the game RNG exactly (TileSymbol.Get).
//
// Not drawn (yet): the bolt/ball/breath and thrown-object animations
// (panelPutTile('*') + putQIO() per square, with no delay between frames,
// so the original only shows them on a slow terminal).
public sealed partial class GameUi : IDungeonRenderer
{
    private readonly Random _cosmeticRng;

    public GameUi(PlayerWorld w, Terminal terminal, int? cosmeticSeed = null)
    {
        W = w;
        Term = terminal;
        _cosmeticRng = cosmeticSeed is null ? new Random() : new Random(cosmeticSeed.Value);
    }

    public PlayerWorld W { get; }
    public Terminal Term { get; }

    // Binds the world's input hooks and display call sites to this UI.
    public void Attach()
    {
        W.Ui = this;
        ScreenRendering.Attach(W.Dg, this);

        W.ReadCommand = prompt => GetCommand(prompt, out var key) ? key : null;
        W.Confirm = GetInputConfirmation;
        W.ConfirmWithAbort = prompt => GetInputConfirmationWithAbort(0, prompt);

        // The original prints each prompt and then reads the text right
        // after it (getStringInput() at the prompt's end), so the line is read
        // at the cursor -- the game logic writes the prompt through
        // ScreenWrite first, as the original's putStringClearToEOL() does.
        W.ReadString = (_, maxLength) => GetStringInput(Term.CursorY, Term.CursorX, maxLength);

        W.ScreenWrite = (row, col, text, clearToEol) =>
        {
            if (clearToEol)
            {
                PutStringClearToEol(text, row, col);
            }
            else
            {
                PutString(text, row, col);
            }
        };
    }

    // A dungeon tile's glyph for the screen: caveGetTileSymbol() with the
    // cosmetic RNG (see the class comment).
    private char TileGlyph(Coord coord) => TileSymbol.Get(W, coord, n => _cosmeticRng.Next(n) + 1);

    // ---- IDungeonRenderer (ScreenRendering's call sites) --------------------

    // src/dungeon.cpp:392 dungeonLiteSpot() -- lights up given location -RAK-
    public void LiteSpot(Coord coord)
    {
        if (!CoordInsidePanel(coord))
        {
            return;
        }

        PanelPutTile(TileGlyph(coord), coord.Y, coord.X);
    }

    // panelPutTile(caveGetTileSymbol(coord), coord)
    public void RedrawTile(Coord coord) => PanelPutTile(TileGlyph(coord), coord.Y, coord.X);

    // panelPutTile(ch, coord)
    public void PutTile(char ch, Coord coord) => PanelPutTile(ch, coord.Y, coord.X);

    void IDungeonRenderer.DrawDungeonPanel() => DrawDungeonPanel();

    // src/ui.cpp:74 coordInsidePanel() -- is the given coordinate within the
    // screen panel boundaries -RAK-
    private bool CoordInsidePanel(Coord coord)
    {
        var panel = W.Dg.Panel;

        var validY = coord.Y >= panel.Top && coord.Y <= panel.Bottom;
        var validX = coord.X >= panel.Left && coord.X <= panel.Right;

        return validY && validX;
    }
}
