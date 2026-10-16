using Umoria.Core.Config;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

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

// Port of game.treasure (src/game.h) and popt() (src/game_objects.cpp:70) --
// the real item-instance list, replacing the bump-allocator TreasureStub
// that stood in for it through M2/M3.
//
// pusht()/compactObjects() are NOT ported: both are confirmed unreachable
// for anything through M4. compactObjects() needs Player.Pos (M6, no
// Player singleton exists yet) and dungeonDeleteObject()/dungeonLiteSpot()
// (M5, pure rendering); its only caller is popt() when the list is full,
// which is also the original's own "extremely rare" path (see
// LevelMaxObjects' comment). pusht()'s only other callers are
// dungeonDeleteObject() itself, save/load (M12), and store restocking
// (M13) -- none in scope yet.
public sealed class TreasureList
{
    public const int LevelMaxObjects = GameConstants.LevelMaxObjects;

    public Inventory[] List { get; } = new Inventory[LevelMaxObjects];
    public int CurrentId { get; private set; } = TreasureConfig.MinTreasureListId;

    public ref Inventory this[int id] => ref List[id];

    // src/game_objects.cpp:70 popt()
    public int Popt()
    {
        if (CurrentId == LevelMaxObjects)
        {
            // See the class comment above for why compactObjects() isn't
            // ported yet. Throwing (rather than silently wrapping or
            // overwriting) makes it loud if a future milestone's object
            // count ever gets this high before M5/M6 land.
            throw new NotImplementedException(
                "compactObjects() is not ported yet (deferred to M5/M6); treasure allocation should never reach capacity before then.");
        }

        return CurrentId++;
    }
}
