using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Dungeon;

// Port of the random-object-placement subset of src/game_objects.cpp
// (itemGetRandomObjectId, itemBiggerThanChest) and src/dungeon.cpp
// (dungeonSetTrap, dungeonPlaceRubble, dungeonPlaceGold,
// dungeonPlaceRandomObjectAt, dungeonAllocateAndPlaceObject,
// dungeonPlaceRandomObjectNear) -- M4 per migration-plan.md -- plus
// dungeonDeleteObject() and dungeonSummonObject(), added in M5 once
// Los.Check()/ScreenRendering.LiteSpot() made them reachable.
// popt()/pusht()/compactObjects() live on TreasureList itself.
//
// Confirmed NOT ported here:
//   - object_type == 2 in dungeonAllocateAndPlaceObject's switch: the
//     original comment says it's "no longer used" and no call site ever
//     passes it, so the case is dropped entirely (not even dead code).
//   - the vault-containing unusual room types' own direct
//     dungeonPlaceRandomObjectAt/dungeonPlaceRandomObjectNear call sites --
//     still excluded along with the rest of the vault branch (see
//     DungeonGenerator's class comment).
public sealed class ObjectPlacement
{
    private readonly Model.Generated.Dungeon _dg;
    private readonly TreasureList _treasure;

    // src/game_run.cpp:initializeTreasureLevels() -- cumulative histogram
    // of object count by depth_first_found, plus an O(n) counting sort of
    // catalog ids by that same depth. Deterministic (built from the static
    // DataTreasureData.GameObjects table, no RNG draws); rebuilt per
    // instance rather than shared/static, same choice MonsterManager makes
    // for monster_levels.
    private readonly int[] _treasureLevels;
    private readonly int[] _sortedObjects;

    public ObjectPlacement(Model.Generated.Dungeon dg, TreasureList treasure)
    {
        _dg = dg;
        _treasure = treasure;
        (_treasureLevels, _sortedObjects) = BuildTreasureLevels();
    }

    private static (int[] levels, int[] sorted) BuildTreasureLevels()
    {
        var catalog = DataTreasureData.GameObjects;

        var levels = new int[GameConstants.TreasureMaxLevels + 1];
        for (var i = 0; i < GameConstants.MaxDungeonObjects; i++)
        {
            levels[catalog[i].DepthFirstFound]++;
        }

        for (var i = 1; i <= GameConstants.TreasureMaxLevels; i++)
        {
            levels[i] += levels[i - 1];
        }

        var sorted = new int[GameConstants.MaxDungeonObjects];
        var indexes = new int[GameConstants.TreasureMaxLevels + 1];
        for (var i = 0; i < indexes.Length; i++)
        {
            indexes[i] = 1;
        }

        for (var i = 0; i < GameConstants.MaxDungeonObjects; i++)
        {
            var level = catalog[i].DepthFirstFound;
            var objectId = levels[level] - indexes[level];
            sorted[objectId] = i;
            indexes[level]++;
        }

        return (levels, sorted);
    }

    // src/dungeon.cpp:217 dungeonSetTrap()
    public void SetTrap(Coord coord, int subTypeId)
    {
        var id = _treasure.Popt();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _treasure[id] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjTrapList + subTypeId);
    }

    // src/dungeon.cpp:225 trapChangeVisibility() -- change a trap from
    // invisible to visible -RAK- Note: Secret doors are handled here
    // (added M8: searching, and stepping on a trap)
    public void TrapChangeVisibility(Coord coord)
    {
        var treasureId = _dg.Floor[coord.Y, coord.X].TreasureId;

        ref var item = ref _treasure[treasureId];

        if (item.CategoryId == TreasureCategory.TvInvisTrap)
        {
            item.CategoryId = TreasureCategory.TvVisTrap;
            ScreenRendering.LiteSpot(_dg, coord);
            return;
        }

        // change secret door to closed door
        if (item.CategoryId == TreasureCategory.TvSecretDoor)
        {
            var closedDoor = DataTreasureData.GameObjects[DungeonGenerationConfig.Objects.ObjClosedDoor];
            item.Id = DungeonGenerationConfig.Objects.ObjClosedDoor;
            item.CategoryId = closedDoor.CategoryId;
            item.Sprite = closedDoor.Sprite;
            ScreenRendering.LiteSpot(_dg, coord);
        }
    }

    // src/dungeon.cpp:246 dungeonPlaceRubble()
    public void PlaceRubble(Coord coord)
    {
        var id = _treasure.Popt();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        _dg.Floor[coord.Y, coord.X].FeatureId = TileConstants.TileBlockedFloor;
        _treasure[id] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjRubble);
    }

    // src/dungeon.cpp:254 dungeonPlaceGold() -- the "something roll beneath
    // your feet" message (creature_id == 1 check) is pure UI output with no
    // RNG draw or state effect, so it's omitted here, same as elsewhere in
    // this port.
    public void PlaceGold(Coord coord)
    {
        var id = _treasure.Popt();

        var goldTypeId = ((RandomNumber.Next(_dg.CurrentLevel + 2) + 2) / 2) - 1;

        if (RandomNumber.Next(TreasureConfig.TreasureChanceOfGreatItem) == 1)
        {
            goldTypeId += RandomNumber.Next(_dg.CurrentLevel + 1);
        }

        if (goldTypeId >= DungeonGenerationConfig.Objects.MaxGoldTypes)
        {
            goldTypeId = DungeonGenerationConfig.Objects.MaxGoldTypes - 1;
        }

        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;

        var item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjGoldList + goldTypeId);
        item.Cost += (8 * RandomNumber.Next(item.Cost)) + RandomNumber.Next(8);
        _treasure[id] = item;
    }

    // src/dungeon.cpp:277 dungeonPlaceRandomObjectAt()
    public void PlaceRandomObjectAt(Coord coord, bool mustBeSmall)
    {
        var id = _treasure.Popt();
        _dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;

        var objectId = GetRandomObjectId(_dg.CurrentLevel, mustBeSmall);
        var item = InventoryCatalog.CopyFromCatalog(_sortedObjects[objectId]);

        TreasureMagic.MagicalAbility(ref item, _dg.CurrentLevel);

        _treasure[id] = item;
    }

    // src/dungeon.cpp:293 dungeonAllocateAndPlaceObject()
    public void AllocateAndPlaceObject(Func<byte, bool> setFunction, int objectType, int number, Coord playerPos)
    {
        for (var i = 0; i < number; i++)
        {
            // don't put an object beneath the player, this could cause
            // problems if player is standing under rubble, or on a trap.
            Coord coord;
            do
            {
                coord = new Coord(RandomNumber.Next(_dg.Height) - 1, RandomNumber.Next(_dg.Width) - 1);
            } while (!setFunction(_dg.Floor[coord.Y, coord.X].FeatureId) ||
                     _dg.Floor[coord.Y, coord.X].TreasureId != 0 ||
                     (coord.Y == playerPos.Y && coord.X == playerPos.X));

            switch (objectType)
            {
                case 1:
                    SetTrap(coord, RandomNumber.Next(DungeonGenerationConfig.Objects.MaxTraps) - 1);
                    break;
                case 3:
                    PlaceRubble(coord);
                    break;
                case 4:
                    PlaceGold(coord);
                    break;
                case 5:
                    PlaceRandomObjectAt(coord, false);
                    break;
            }
        }
    }

    // src/dungeon.cpp:327 dungeonPlaceRandomObjectNear()
    public void PlaceRandomObjectNear(Coord coord, int tries)
    {
        do
        {
            for (var i = 0; i <= 10; i++)
            {
                var at = new Coord(coord.Y - 3 + RandomNumber.Next(5), coord.X - 4 + RandomNumber.Next(7));

                if (CoordHelpers.InBounds(_dg, at) &&
                    _dg.Floor[at.Y, at.X].FeatureId <= TileConstants.MaxCaveFloor &&
                    _dg.Floor[at.Y, at.X].TreasureId == 0)
                {
                    if (RandomNumber.Next(100) < 75)
                    {
                        PlaceRandomObjectAt(at, false);
                    }
                    else
                    {
                        PlaceGold(at);
                    }

                    i = 9;
                }
            }

            tries--;
        } while (tries != 0);
    }

    // src/game_objects.cpp:120 itemGetRandomObjectId()
    private int GetRandomObjectId(int level, bool mustBeSmall)
    {
        if (level == 0)
        {
            return RandomNumber.Next(_treasureLevels[0]) - 1;
        }

        if (level >= GameConstants.TreasureMaxLevels)
        {
            level = GameConstants.TreasureMaxLevels;
        }
        else if (RandomNumber.Next(TreasureConfig.TreasureChanceOfGreatItem) == 1)
        {
            level = (level * GameConstants.TreasureMaxLevels / RandomNumber.Next(GameConstants.TreasureMaxLevels)) + 1;
            if (level > GameConstants.TreasureMaxLevels)
            {
                level = GameConstants.TreasureMaxLevels;
            }
        }

        int objectId;

        do
        {
            if (RandomNumber.Next(2) == 1)
            {
                objectId = RandomNumber.Next(_treasureLevels[level]) - 1;
            }
            else
            {
                // Choose three objects, pick the highest level.
                objectId = RandomNumber.Next(_treasureLevels[level]) - 1;

                var j = RandomNumber.Next(_treasureLevels[level]) - 1;
                if (objectId < j)
                {
                    objectId = j;
                }

                j = RandomNumber.Next(_treasureLevels[level]) - 1;
                if (objectId < j)
                {
                    objectId = j;
                }

                var foundLevel = DataTreasureData.GameObjects[_sortedObjects[objectId]].DepthFirstFound;

                if (foundLevel == 0)
                {
                    objectId = RandomNumber.Next(_treasureLevels[0]) - 1;
                }
                else
                {
                    objectId = RandomNumber.Next(_treasureLevels[foundLevel] - _treasureLevels[foundLevel - 1]) - 1 + _treasureLevels[foundLevel - 1];
                }
            }
        } while (mustBeSmall && ItemBiggerThanChest(DataTreasureData.GameObjects[_sortedObjects[objectId]]));

        return objectId;
    }

    // src/game_objects.cpp:101 itemBiggerThanChest()
    private static bool ItemBiggerThanChest(DungeonObject obj)
    {
        switch (obj.CategoryId)
        {
            case TreasureCategory.TvChest:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvSoftArmor:
            case TreasureCategory.TvStaff:
                return true;
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvSword:
            case TreasureCategory.TvDigging:
                return obj.Weight > 150;
            default:
                return false;
        }
    }

    // src/dungeon.cpp:601 dungeonDeleteObject() -- became portable once M5
    // added Pusht()/LiteSpot(); the "something roll beneath your feet"
    // message is UI output only and has no RNG/state effect, so (as
    // elsewhere in this port) it's omitted.
    public bool DeleteObject(Coord coord)
    {
        var tile = _dg.Floor[coord.Y, coord.X];

        if (tile.FeatureId == TileConstants.TileBlockedFloor)
        {
            tile.FeatureId = TileConstants.TileCorrFloor;
        }

        _treasure.Pusht(_dg, tile.TreasureId);

        tile.TreasureId = 0;
        tile.FieldMark = false;

        _dg.Floor[coord.Y, coord.X] = tile;

        ScreenRendering.LiteSpot(_dg, coord);

        return DungeonLighting.CaveTileVisible(_dg, coord);
    }

    // src/dungeon.cpp:548 dungeonSummonObject() -- creates objects near the
    // given coordinate. object_type meanings: 1/5 = objects, anything else
    // = gold, 3/7 = 50/50 objects-or-gold; object_type >= 4 also means
    // must_be_small when placing an object (matches the original's inline
    // `object_type >= 4` argument).
    public int SummonObject(Coord coord, int amount, int objectType)
    {
        var realType = (objectType == 1 || objectType == 5) ? 1 : 256;

        var result = 0;

        do
        {
            for (var tries = 0; tries <= 20; tries++)
            {
                var at = new Coord(coord.Y - 3 + RandomNumber.Next(5), coord.X - 3 + RandomNumber.Next(5));

                if (CoordHelpers.InBounds(_dg, at) && Los.Check(_dg, coord, at))
                {
                    if (_dg.Floor[at.Y, at.X].FeatureId <= TileConstants.MaxOpenSpace && _dg.Floor[at.Y, at.X].TreasureId == 0)
                    {
                        if (objectType == 3 || objectType == 7)
                        {
                            realType = RandomNumber.Next(100) < 50 ? 1 : 256;
                        }

                        if (realType == 1)
                        {
                            PlaceRandomObjectAt(at, objectType >= 4);
                        }
                        else
                        {
                            PlaceGold(at);
                        }

                        ScreenRendering.LiteSpot(_dg, at);

                        if (DungeonLighting.CaveTileVisible(_dg, at))
                        {
                            result += realType;
                        }

                        tries = 20;
                    }
                }
            }

            amount--;
        } while (amount != 0);

        return result;
    }
}
