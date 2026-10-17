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

    // Stand-in for src/ui_io.cpp printMessage(), added in M6 for the
    // spell-allowance messages ("You can learn some new spells now.").
    // Same reasoning as LiteSpot: the message line is pure display plus a
    // message-history buffer nothing outside the UI reads, so it's a
    // documented no-op at each real call site until the UI milestone (M14).
    public static void PrintMessage(string message)
    {
    }
}
