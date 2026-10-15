using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Xunit;

namespace Umoria.Core.Tests;

// NOTE: these are sanity/smoke tests, not the golden-trace regression test
// the migration plan calls for -- no C++ toolchain is available in this
// environment to build the baseline harness and compare against. They
// confirm the port runs without crashing/looping and produces a
// structurally sane, deterministic level; they do NOT confirm the output
// matches the original game for a given seed.
public class DungeonGeneratorTests
{
    private static (Model.Generated.Dungeon dg, Coord start) GenerateLevel(uint seed, int level)
    {
        MoriaRng.SetRandomSeed(seed);
        var dg = new Model.Generated.Dungeon();
        var generator = new DungeonGenerator(dg, new TreasureList(), new MonsterManager(dg));
        var start = generator.Generate(level);
        return (dg, start);
    }

    [Fact]
    public void Generate_SameSeed_ProducesIdenticalLevel()
    {
        var (dg1, start1) = GenerateLevel(12345, 1);
        var (dg2, start2) = GenerateLevel(12345, 1);

        Assert.Equal(start1, start2);

        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                Assert.Equal(dg1.Floor[y, x].FeatureId, dg2.Floor[y, x].FeatureId);
            }
        }
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentLevels()
    {
        var (dg1, _) = GenerateLevel(1, 1);
        var (dg2, _) = GenerateLevel(2, 1);

        var anyDifferent = false;
        for (var y = 0; y < DungeonConstants.MaxHeight && !anyDifferent; y++)
        {
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                if (dg1.Floor[y, x].FeatureId != dg2.Floor[y, x].FeatureId)
                {
                    anyDifferent = true;
                    break;
                }
            }
        }

        Assert.True(anyDifferent, "Two different seeds produced identical floor layouts");
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(999999u)]
    public void Generate_PlayerStartSpot_IsOpenAndWalkable(uint seed)
    {
        var (dg, start) = GenerateLevel(seed, 1);

        Assert.InRange(start.Y, 1, DungeonConstants.MaxHeight - 2);
        Assert.InRange(start.X, 1, DungeonConstants.MaxWidth - 2);

        var tile = dg.Floor[start.Y, start.X];
        Assert.True(tile.FeatureId < TileConstants.MinClosedSpace, $"start tile feature_id {tile.FeatureId} is not open space");
        Assert.Equal(0, tile.CreatureId);
        Assert.Equal(0, tile.TreasureId);
    }

    [Fact]
    public void Generate_BoundaryWalls_SurroundTheLevel()
    {
        var (dg, _) = GenerateLevel(7, 1);

        for (var x = 0; x < dg.Width; x++)
        {
            Assert.Equal(TileConstants.TileBoundaryWall, dg.Floor[0, x].FeatureId);
            Assert.Equal(TileConstants.TileBoundaryWall, dg.Floor[dg.Height - 1, x].FeatureId);
        }

        for (var y = 0; y < dg.Height; y++)
        {
            Assert.Equal(TileConstants.TileBoundaryWall, dg.Floor[y, 0].FeatureId);
            Assert.Equal(TileConstants.TileBoundaryWall, dg.Floor[y, dg.Width - 1].FeatureId);
        }
    }

    [Fact]
    public void Generate_ProducesAMixOfFloorsAndWalls_NotAllOneTile()
    {
        var (dg, _) = GenerateLevel(55, 1);

        var floorCount = 0;
        var wallCount = 0;

        for (var y = 1; y < dg.Height - 1; y++)
        {
            for (var x = 1; x < dg.Width - 1; x++)
            {
                var id = dg.Floor[y, x].FeatureId;
                if (id <= TileConstants.MaxCaveFloor)
                {
                    floorCount++;
                }
                else if (id >= TileConstants.MinCaveWall)
                {
                    wallCount++;
                }
            }
        }

        Assert.True(floorCount > 100, $"expected a substantial amount of floor, got {floorCount}");
        Assert.True(wallCount > 100, $"expected a substantial amount of wall, got {wallCount}");
    }
}
