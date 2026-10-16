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
}
