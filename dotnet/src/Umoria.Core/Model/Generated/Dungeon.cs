// Hand-translated from src/dungeon.h's Dungeon_t (not tool-generated).
namespace Umoria.Core.Model.Generated;

// Replaces the original's global `Dungeon_t dg` with an explicit, owned
// instance.
//
// M7: added GameTurn and GenerateNewLevel, previously omitted as main-loop
// state -- the player status-effect updates read the turn counter (poison
// damage cadence, detect-enchantment rolls) and set the new-level flag
// (death, word of recall).
//
// M8: added Panel (src/ui.h Panel_t), previously omitted as UI scroll state.
// It is game state after all: coordOutsidePanel() rewrites it on every move
// (and can stop a run when config::options::find_bound is set), and
// coordInsidePanel() gates the "You have found something!" message in
// playerTunnelWall() -- a printMessage() that zeroes game.command_count.
public sealed class Dungeon
{
    public int Height;
    public int Width;

    public Panel Panel = new();

    // Current turn of the game
    public int GameTurn;

    public int CurrentLevel;

    // A `true` value means a new level will be generated on next loop iteration
    public bool GenerateNewLevel;

    public Tile[,] Floor = new Tile[DungeonConstants.MaxHeight, DungeonConstants.MaxWidth];
}

// src/ui.h Panel_t -- the screen panel (the visible window onto the
// dungeon), calculated from the dungeon/screen dimensions.
public sealed class Panel
{
    public int Row;
    public int Col;

    public int Top;
    public int Bottom;
    public int Left;
    public int Right;

    public int ColPrt;
    public int RowPrt;

    public short MaxRows;
    public short MaxCols;
}
