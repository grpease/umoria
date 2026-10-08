namespace Umoria.Core.Dungeon;

// Minimal stand-in for game.treasure.list[]/Inventory_t -- the original
// game's full item/treasure system (inventory.cpp, treasure.cpp,
// identification.cpp) is deferred to the full migration. This stub exists
// only so door/stair/gold placement (which the real dungeonGenerate()
// performs inline) has somewhere to record "a treasure_id slot is occupied
// and what kind of thing it is" without pulling in the whole item catalog.
//
// CategoryId mirrors just the handful of TV_* category ids dungeon
// generation itself inspects (doors, stairs) -- see TreasureCategory.
public struct TreasureStub
{
    public byte CategoryId;
}

public static class TreasureCategory
{
    // From src/treasure.h
    public const byte TvOpenDoor = 104;
    public const byte TvClosedDoor = 105;
    public const byte TvMinDoors = 104;
    public const byte TvUpStair = 107;
    public const byte TvDownStair = 108;
    public const byte TvSecretDoor = 109;
}

// Port of popt()/game.treasure.list from src/game_objects.cpp: a simple
// bump allocator over a fixed-size slot array. The real popt() also
// compacts the list when full (compactObjects()); the POC's single level
// never places enough objects to hit that path, so it's omitted.
public sealed class TreasureList
{
    public const int LevelMaxObjects = 100;

    private readonly TreasureStub[] _slots = new TreasureStub[LevelMaxObjects];
    private int _nextFreeId = 1; // slot 0 means "no treasure" (Tile.TreasureId == 0)

    public ref TreasureStub this[int id] => ref _slots[id];

    public int Allocate()
    {
        return _nextFreeId++;
    }
}
