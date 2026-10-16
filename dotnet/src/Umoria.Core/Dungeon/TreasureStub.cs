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

// From src/treasure.h. Originally only had the door/stair subset dungeon
// generation needs; extended for M3 (item magic) to the full TV_* list,
// since these are plain mechanical constants with no risk of incompleteness
// -- later milestones will want the rest anyway.
public static class TreasureCategory
{
    public const sbyte TvNever = -1; // used by find_range() for non-search
    public const byte TvNothing = 0;
    public const byte TvMisc = 1;
    public const byte TvChest = 2;

    public const byte TvMinWear = 10;
    public const byte TvMinEnchant = 10;
    public const byte TvSlingAmmo = 10;
    public const byte TvBolt = 11;
    public const byte TvArrow = 12;
    public const byte TvSpike = 13;
    public const byte TvLight = 15;
    public const byte TvBow = 20;
    public const byte TvHafted = 21;
    public const byte TvPolearm = 22;
    public const byte TvSword = 23;
    public const byte TvDigging = 25;
    public const byte TvBoots = 30;
    public const byte TvGloves = 31;
    public const byte TvCloak = 32;
    public const byte TvHelm = 33;
    public const byte TvShield = 34;
    public const byte TvHardArmor = 35;
    public const byte TvSoftArmor = 36;
    public const byte TvMaxEnchant = 39;
    public const byte TvAmulet = 40;
    public const byte TvRing = 45;
    public const byte TvMaxWear = 50;

    public const byte TvStaff = 55;
    public const byte TvWand = 65;
    public const byte TvScroll1 = 70;
    public const byte TvScroll2 = 71;
    public const byte TvPotion1 = 75;
    public const byte TvPotion2 = 76;
    public const byte TvFlask = 77;
    public const byte TvFood = 80;
    public const byte TvMagicBook = 90;
    public const byte TvPrayerBook = 91;
    public const byte TvMaxObject = 99;
    public const byte TvGold = 100;
    public const byte TvMaxPickUp = 100;
    public const byte TvInvisTrap = 101;

    public const byte TvMinVisible = 102;
    public const byte TvVisTrap = 102;
    public const byte TvRubble = 103;
    public const byte TvMinDoors = 104;
    public const byte TvOpenDoor = 104;
    public const byte TvClosedDoor = 105;
    public const byte TvUpStair = 107;
    public const byte TvDownStair = 108;
    public const byte TvSecretDoor = 109;
    public const byte TvStoreDoor = 110;
    public const byte TvMaxVisible = 110;
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
