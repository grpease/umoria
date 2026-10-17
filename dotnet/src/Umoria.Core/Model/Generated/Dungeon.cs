// Hand-translated from src/dungeon.h's Dungeon_t (not tool-generated).
namespace Umoria.Core.Model.Generated;

// Replaces the original's global `Dungeon_t dg` with an explicit, owned
// instance. Deliberately omits Panel_t (UI scroll-panel state).
//
// M7: added GameTurn and GenerateNewLevel, previously omitted as main-loop
// state -- the player status-effect updates read the turn counter (poison
// damage cadence, detect-enchantment rolls) and set the new-level flag
// (death, word of recall).
public sealed class Dungeon
{
    public int Height;
    public int Width;

    // Current turn of the game
    public int GameTurn;

    public int CurrentLevel;

    // A `true` value means a new level will be generated on next loop iteration
    public bool GenerateNewLevel;

    public Tile[,] Floor = new Tile[DungeonConstants.MaxHeight, DungeonConstants.MaxWidth];
}
