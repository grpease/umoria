using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Tests.Dungeon;

// Direct unit tests (not a C++ golden-trace cross-check) for the M5
// object/monster-removal primitives from src/dungeon.cpp: these aren't
// reachable from dungeonGenerate() itself (confirmed via grep -- they're
// only called from player actions/spells/monster AI, all later
// milestones), so there's no existing harness call site to extend. They're
// mechanical, already verified line-by-line against the C++ source; these
// tests check the resulting state directly, the same way
// DungeonGeneratorTests.cs's smoke tests do for dungeon generation.
public class DungeonLifecycleTests
{
    private static Model.Generated.Dungeon MakeDungeon(int size = 20)
    {
        return new Model.Generated.Dungeon { Height = size, Width = size, CurrentLevel = 1 };
    }

    // ---- ObjectPlacement.DeleteObject / TreasureList.Pusht ----

    [Fact]
    public void DeleteObject_ClearsTileAndRetargetsTheSwappedSlot()
    {
        var dg = MakeDungeon();
        var treasure = new TreasureList();
        var objects = new ObjectPlacement(dg, treasure);

        var coordA = new Coord(2, 2);
        var coordB = new Coord(3, 3);
        objects.SetTrap(coordA, 0);
        objects.SetTrap(coordB, 1);

        var trapBItemId = treasure[dg.Floor[coordB.Y, coordB.X].TreasureId].Id;

        objects.DeleteObject(coordA);

        Assert.Equal(0, dg.Floor[coordA.Y, coordA.X].TreasureId);
        Assert.False(dg.Floor[coordA.Y, coordA.X].FieldMark);

        // trap B was the top-of-stack slot; deleting A's slot (not the top)
        // must swap B into A's old slot and retarget B's floor cell to it.
        Assert.Equal(1, dg.Floor[coordB.Y, coordB.X].TreasureId);
        Assert.Equal(trapBItemId, treasure[1].Id);
        Assert.Equal(2, treasure.CurrentId);
    }

    [Fact]
    public void DeleteObject_ConvertsBlockedFloorToCorridorFloor()
    {
        var dg = MakeDungeon();
        var treasure = new TreasureList();
        var objects = new ObjectPlacement(dg, treasure);

        var coord = new Coord(5, 5);
        objects.PlaceRubble(coord);
        Assert.Equal(TileConstants.TileBlockedFloor, dg.Floor[coord.Y, coord.X].FeatureId);

        var visible = objects.DeleteObject(coord);

        Assert.Equal(TileConstants.TileCorrFloor, dg.Floor[coord.Y, coord.X].FeatureId);
        Assert.Equal(0, dg.Floor[coord.Y, coord.X].TreasureId);
        Assert.Equal(TreasureConfig.MinTreasureListId, treasure.CurrentId);
        Assert.False(visible);
    }

    [Fact]
    public void DeleteObject_ReturnsCaveTileVisible()
    {
        var dg = MakeDungeon();
        var treasure = new TreasureList();
        var objects = new ObjectPlacement(dg, treasure);

        var coord = new Coord(5, 5);
        objects.SetTrap(coord, 0);
        dg.Floor[coord.Y, coord.X].PermanentLight = true;

        Assert.True(objects.DeleteObject(coord));
    }

    // ---- ObjectPlacement.SummonObject ----

    [Theory]
    [InlineData(1)] // objects only
    [InlineData(2)] // gold only
    public void SummonObject_PlacesItemsWithinTheSearchRadius(int objectType)
    {
        MoriaRng.SetRandomSeed(42);
        var dg = MakeDungeon();
        var treasure = new TreasureList();
        var objects = new ObjectPlacement(dg, treasure);

        var center = new Coord(10, 10);
        objects.SummonObject(center, amount: 3, objectType);

        Assert.Equal(3, treasure.CurrentId - TreasureConfig.MinTreasureListId);

        for (var y = 0; y < dg.Height; y++)
        {
            for (var x = 0; x < dg.Width; x++)
            {
                if (dg.Floor[y, x].TreasureId == 0)
                {
                    continue;
                }

                Assert.InRange(y, center.Y - 2, center.Y + 2);
                Assert.InRange(x, center.X - 2, center.X + 2);
            }
        }
    }

    // ---- DungeonLighting ----

    [Fact]
    public void CaveTileVisible_TrueOnlyWhenLitOrFieldMarked()
    {
        var dg = MakeDungeon();
        var coord = new Coord(3, 3);

        Assert.False(DungeonLighting.CaveTileVisible(dg, coord));

        dg.Floor[coord.Y, coord.X].PermanentLight = true;
        Assert.True(DungeonLighting.CaveTileVisible(dg, coord));
    }

    [Fact]
    public void LightRoom_LitsPermaLitTilesWithinTheScreenQuadrantOnly()
    {
        var dg = MakeDungeon(40);
        var treasure = new TreasureList();

        var inside = new Coord(3, 3);
        dg.Floor[inside.Y, inside.X].PermaLitRoom = true;
        dg.Floor[inside.Y, inside.X].FeatureId = TileConstants.TileDarkFloor;

        // Same quadrant (top=0,left=0,bottom=10,right=32 for coord (5,5)),
        // but outside it (y=15 > bottom=10) -- must NOT be lit.
        var outside = new Coord(15, 5);
        dg.Floor[outside.Y, outside.X].PermaLitRoom = true;
        dg.Floor[outside.Y, outside.X].FeatureId = TileConstants.TileDarkFloor;

        DungeonLighting.LightRoom(dg, treasure, new Coord(5, 5));

        Assert.True(dg.Floor[inside.Y, inside.X].PermanentLight);
        Assert.Equal(TileConstants.TileLightFloor, dg.Floor[inside.Y, inside.X].FeatureId);

        Assert.False(dg.Floor[outside.Y, outside.X].PermanentLight);
        Assert.Equal(TileConstants.TileDarkFloor, dg.Floor[outside.Y, outside.X].FeatureId);
    }

    [Fact]
    public void LightRoom_FieldMarksOnlyVisibleCategoryTreasure()
    {
        var dg = MakeDungeon(40);
        var treasure = new TreasureList();

        var rubbleId = treasure.Popt();
        treasure[rubbleId] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjRubble);
        var rubbleCoord = new Coord(4, 4);
        dg.Floor[rubbleCoord.Y, rubbleCoord.X] = new Tile { PermaLitRoom = true, TreasureId = (byte)rubbleId };

        var miscId = treasure.Popt();
        treasure[miscId] = InventoryCatalog.CopyFromCatalog(0); // catalog row 0 is TvFood (80), outside TvMinVisible..TvMaxVisible
        var miscCoord = new Coord(4, 6);
        dg.Floor[miscCoord.Y, miscCoord.X] = new Tile { PermaLitRoom = true, TreasureId = (byte)miscId };

        Assert.True(treasure[rubbleId].CategoryId is >= TreasureCategory.TvMinVisible and <= TreasureCategory.TvMaxVisible);
        Assert.False(treasure[miscId].CategoryId is >= TreasureCategory.TvMinVisible and <= TreasureCategory.TvMaxVisible);

        DungeonLighting.LightRoom(dg, treasure, new Coord(5, 5));

        Assert.True(dg.Floor[rubbleCoord.Y, rubbleCoord.X].FieldMark);
        Assert.False(dg.Floor[miscCoord.Y, miscCoord.X].FieldMark);
    }

    // ---- MonsterManager ----

    [Fact]
    public void MoveCreatureRecord_SwapsTheFloorCreatureId()
    {
        var dg = MakeDungeon();
        var monsters = new MonsterManager(dg);

        var from = new Coord(5, 5);
        var to = new Coord(6, 6);
        dg.Floor[from.Y, from.X].CreatureId = 7;

        monsters.MoveCreatureRecord(from, to);

        Assert.Equal(0, dg.Floor[from.Y, from.X].CreatureId);
        Assert.Equal(7, dg.Floor[to.Y, to.X].CreatureId);
    }

    [Fact]
    public void DeleteMonster_CompactsTheArrayAndRetargetsTheSwappedMonstersFloorCell()
    {
        var dg = MakeDungeon();
        var monsters = new MonsterManager(dg);

        var coordA = new Coord(2, 2);
        var coordB = new Coord(3, 3);
        var playerPos = new Coord(0, 0);

        Assert.True(monsters.PlaceNew(coordA, 0, false, playerPos, 0));
        Assert.True(monsters.PlaceNew(coordB, 0, false, playerPos, 0));
        Assert.Equal(4, monsters.NextFreeMonsterId);

        monsters.DeleteMonster(2);

        Assert.Equal(0, dg.Floor[coordA.Y, coordA.X].CreatureId);
        Assert.Equal(2, dg.Floor[coordB.Y, coordB.X].CreatureId);
        Assert.Equal(3, monsters.NextFreeMonsterId);
        Assert.Equal(coordB, monsters.Monsters[2].Pos);
    }

    [Fact]
    public void RemoveMonsterFromLevel_ForcesNegativeHpAndDecrementsMultiplyTotal()
    {
        var dg = MakeDungeon();
        var monsters = new MonsterManager(dg) { MonsterMultiplyTotal = 2 };

        var coord = new Coord(5, 5);
        monsters.PlaceNew(coord, 0, false, new Coord(0, 0), 0);

        monsters.RemoveMonsterFromLevel(2);

        Assert.Equal(-1, monsters.Monsters[2].Hp);
        Assert.Equal(0, dg.Floor[coord.Y, coord.X].CreatureId);
        Assert.Equal(1, monsters.MonsterMultiplyTotal);
    }
}
