namespace Umoria.Core.Dungeon;

// Stand-in for the src/dungeon.cpp call sites that invoke dungeonLiteSpot()
// (src/dungeon.cpp:392) to redraw a single tile after a state change.
// dungeonLiteSpot() is confirmed pure rendering -- it resolves a glyph via
// caveGetTileSymbol() and writes it to the screen panel, with no
// state-mutating side effect of its own -- so until a terminal/UI milestone
// exists (none is scheduled yet in migration-plan.md), this is a deliberate
// no-op. Kept as an explicit call at each site, rather than silently
// dropped, so wiring up the real renderer later is a one-line change per
// caller instead of a hunt through the codebase.
//
// caveGetTileSymbol() itself is not ported for the same reason, plus it
// reads Player.flags/running_tracker (M6) and config::options (UI) besides.
public static class ScreenRendering
{
    public static void LiteSpot(Model.Generated.Dungeon dg, Model.Generated.Coord coord)
    {
    }

    // Stand-in for the display half of src/ui_io.cpp printMessage(), added
    // in M6 for the spell-allowance messages ("You can learn some new
    // spells now."). The message line, -more- prompt and message history
    // are display-only, so this is a documented no-op until the UI
    // milestone (M14).
    //
    // NOT pure display, though (found in M7): the real printMessage() also
    // zeroes game.command_count for every non-null message, cancelling a
    // repeated command. Code with a PlayerWorld should call
    // Character.PlayerCore.PrintMessage, which does both. M6's
    // PlayerSpells calls this directly -- harmless during character
    // creation (no command count exists yet), but its in-game callers
    // (M10 casting, level gain) need to route through PlayerCore instead.
    public static void PrintMessage(string message)
    {
    }
}
