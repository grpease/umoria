using Umoria.Core.Config;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// The game-state half of src/ui.cpp's screen-panel code (M8): which
// SCREEN_HEIGHT x SCREEN_WIDTH window of the dungeon is "on screen". The
// panel is redrawn (drawDungeonPanel(), rendering only, omitted) when it
// shifts, but the shift itself is state: it can end a run
// (config::options::find_bound), and coordInsidePanel() gates a
// command-interrupting message in playerTunnelWall().
public static class ScreenPanel
{
    // src/dungeon_generate.cpp:1267 -- generateCave()'s panel setup.
    public static void InitializeForLevel(Model.Generated.Dungeon dg)
    {
        dg.Panel.MaxRows = (short)((dg.Height / DungeonConstants.ScreenHeight) * 2 - 2);
        dg.Panel.MaxCols = (short)((dg.Width / DungeonConstants.ScreenWidth) * 2 - 2);

        dg.Panel.Row = dg.Panel.MaxRows;
        dg.Panel.Col = dg.Panel.MaxCols;
    }

    // src/ui.cpp:22 panelBounds() -- calculates current boundaries -RAK-
    private static void PanelBounds(Panel panel)
    {
        panel.Top = panel.Row * (DungeonConstants.ScreenHeight / 2);
        panel.Bottom = panel.Top + DungeonConstants.ScreenHeight - 1;
        panel.RowPrt = panel.Top - 1;
        panel.Left = panel.Col * (DungeonConstants.ScreenWidth / 2);
        panel.Right = panel.Left + DungeonConstants.ScreenWidth - 1;
        panel.ColPrt = panel.Left - 13;
    }

    // src/ui.cpp:34 coordOutsidePanel() -- given an row (y) and col (x),
    // this routine detects when a move off the screen has occurred and
    // figures new borders. -RAK- `force` forces the panel bounds to be
    // recalculated, useful for 'W'here.
    public static bool CoordOutsidePanel(Player py, Model.Generated.Dungeon dg, TreasureList treasure, GameOptions options, Coord coord, bool force)
    {
        var panel = dg.Panel;
        var row = panel.Row;
        var col = panel.Col;

        if (force || coord.Y < panel.Top + 2 || coord.Y > panel.Bottom - 2)
        {
            row = (coord.Y - DungeonConstants.ScreenHeight / 4) / (DungeonConstants.ScreenHeight / 2);

            if (row > panel.MaxRows)
            {
                row = panel.MaxRows;
            }
            else if (row < 0)
            {
                row = 0;
            }
        }

        if (force || coord.X < panel.Left + 3 || coord.X > panel.Right - 3)
        {
            col = (coord.X - DungeonConstants.ScreenWidth / 4) / (DungeonConstants.ScreenWidth / 2);
            if (col > panel.MaxCols)
            {
                col = panel.MaxCols;
            }
            else if (col < 0)
            {
                col = 0;
            }
        }

        if (row != panel.Row || col != panel.Col)
        {
            panel.Row = row;
            panel.Col = col;
            PanelBounds(panel);

            // stop movement if any
            if (options.FindBound)
            {
                DungeonLighting.EndRunning(py, dg, treasure, options);
            }

            // Yes, the coordinates are beyond the current panel boundary
            return true;
        }

        return false;
    }

    // src/ui.cpp:74 coordInsidePanel() -- is the given coordinate within
    // the screen panel boundaries -RAK-
    public static bool CoordInsidePanel(Model.Generated.Dungeon dg, Coord coord)
    {
        var panel = dg.Panel;
        var validY = coord.Y >= panel.Top && coord.Y <= panel.Bottom;
        var validX = coord.X >= panel.Left && coord.X <= panel.Right;

        return validY && validX;
    }
}
