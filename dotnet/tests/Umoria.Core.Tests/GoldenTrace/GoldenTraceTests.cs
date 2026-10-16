using Umoria.Core.Dungeon;
using Umoria.Core.Rng;
using Xunit;

namespace Umoria.Core.Tests.GoldenTrace;

// Real golden-trace verification: compares the C# DungeonGenerator's output
// against output captured from the actual C++ game via
// tools/golden_trace/golden_trace_harness.cpp (built through the MSVC+vcpkg
// toolchain -- see dotnet/tests/GoldenTraces/*.txt for the checked-in
// baselines and how they were produced).
//
// Through M3, the C++ harness called generateCaveLayoutOnlyForTesting(), a
// test-only function that stopped before object placement, since the .NET
// port didn't implement it yet. M4 ports object placement, so the harness
// now calls the real generateCave() directly, and this test exercises
// DungeonGenerator/MonsterManager/ObjectPlacement/TreasureMagic together --
// everything generateCave() does except the vault branch. Level 1
// specifically is used because the "unusual room" (vault) selection can
// never trigger there (current_level > randomNumber(DUN_UNUSUAL_ROOMS=300)
// is never true at level 1), so this is a genuine apples-to-apples
// comparison, not just a coincidental partial match.
public class GoldenTraceTests
{
    [Theory]
    [InlineData("seed12345_level1.txt")]
    [InlineData("seed7_level1.txt")]
    [InlineData("seed999999_level1.txt")]
    public void DungeonGenerator_MatchesCppBaseline(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenTraces", fileName);
        var expected = GoldenTraceFile.Parse(path);

        // missiles_counter (TreasureMagic.MissilesCounter) is a true
        // process-global in the original, so each baseline file was
        // captured by its own golden_trace_harness.exe invocation -- a
        // fresh process, with the counter starting at 0. Reset it here too,
        // so this test doesn't depend on what other tests left behind in
        // the shared static (same reason ItemMagicTraceTests resets it).
        TreasureMagic.MissilesCounter = 0;
        MoriaRng.SetRandomSeed(expected.Seed);
        var dg = new Model.Generated.Dungeon();
        var monsters = new MonsterManager(dg);
        var treasure = new TreasureList();
        var generator = new DungeonGenerator(dg, treasure, monsters);
        var playerStart = generator.Generate(expected.Level);

        Assert.Equal(expected.Height, dg.Height);
        Assert.Equal(expected.Width, dg.Width);
        Assert.Equal(expected.PlayerStart, (playerStart.Y, playerStart.X));
        Assert.Equal(expected.RndSeed, MoriaRng.GetRandomSeed());

        var mismatches = new List<string>();
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                if (dg.Floor[y, x].FeatureId != expected.Floor[y, x])
                {
                    mismatches.Add($"({y},{x}): expected {expected.Floor[y, x]:x}, got {dg.Floor[y, x].FeatureId:x}");
                    if (mismatches.Count >= 20)
                    {
                        break;
                    }
                }
            }

            if (mismatches.Count >= 20)
            {
                break;
            }
        }

        Assert.True(mismatches.Count == 0, $"floor mismatches (first {mismatches.Count}):\n" + string.Join("\n", mismatches));

        Assert.Equal(expected.Monsters.Length, monsters.NextFreeMonsterId - Config.MonsterConfig.MonMinIndexId);

        var monsterMismatches = new List<string>();
        foreach (var expectedMonster in expected.Monsters)
        {
            var actual = monsters.Monsters[expectedMonster.Id];
            var actualFields = (
                CreatureId: (int)actual.CreatureId, Hp: (int)actual.Hp, SleepCount: (int)actual.SleepCount,
                Y: actual.Pos.Y, X: actual.Pos.X, Speed: (int)actual.Speed,
                DistanceFromPlayer: (int)actual.DistanceFromPlayer, Lit: actual.Lit,
                StunnedAmount: (int)actual.StunnedAmount, ConfusedAmount: (int)actual.ConfusedAmount);
            var expectedFields = (
                expectedMonster.CreatureId, expectedMonster.Hp, expectedMonster.SleepCount, expectedMonster.Y,
                expectedMonster.X, expectedMonster.Speed, expectedMonster.DistanceFromPlayer, expectedMonster.Lit,
                expectedMonster.StunnedAmount, expectedMonster.ConfusedAmount);

            if (!actualFields.Equals(expectedFields))
            {
                monsterMismatches.Add($"monster {expectedMonster.Id}: expected {expectedFields}, got {actualFields}");
            }
        }

        Assert.True(monsterMismatches.Count == 0, $"monster mismatches:\n" + string.Join("\n", monsterMismatches));

        Assert.Equal(expected.Treasures.Length, treasure.CurrentId - Config.TreasureConfig.MinTreasureListId);

        var treasureMismatches = new List<string>();
        foreach (var expectedTreasure in expected.Treasures)
        {
            var actual = treasure[expectedTreasure.Id];
            var actualFields = (
                ItemId: (int)actual.Id, SpecialNameId: (int)actual.SpecialNameId, Flags: actual.Flags,
                CategoryId: (int)actual.CategoryId, Sprite: (int)actual.Sprite, MiscUse: (int)actual.MiscUse,
                Cost: actual.Cost, SubCategoryId: (int)actual.SubCategoryId, ItemsCount: (int)actual.ItemsCount,
                Weight: (int)actual.Weight, ToHit: (int)actual.ToHit, ToDamage: (int)actual.ToDamage,
                Ac: (int)actual.Ac, ToAc: (int)actual.ToAc, DiceCount: (int)actual.Damage.Count,
                DiceSides: (int)actual.Damage.Sides, DepthFirstFound: (int)actual.DepthFirstFound,
                Identification: (int)actual.Identification);
            var expectedFields = (
                expectedTreasure.ItemId, expectedTreasure.SpecialNameId, expectedTreasure.Flags,
                expectedTreasure.CategoryId, expectedTreasure.Sprite, expectedTreasure.MiscUse, expectedTreasure.Cost,
                expectedTreasure.SubCategoryId, expectedTreasure.ItemsCount, expectedTreasure.Weight,
                expectedTreasure.ToHit, expectedTreasure.ToDamage, expectedTreasure.Ac, expectedTreasure.ToAc,
                expectedTreasure.DiceCount, expectedTreasure.DiceSides, expectedTreasure.DepthFirstFound,
                expectedTreasure.Identification);

            if (!actualFields.Equals(expectedFields))
            {
                treasureMismatches.Add($"treasure {expectedTreasure.Id}: expected {expectedFields}, got {actualFields}");
            }

            // The floor grid dump only encodes feature_id, not treasure_id,
            // so position is cross-checked separately here rather than via
            // the floor-grid comparison above.
            if (dg.Floor[expectedTreasure.Y, expectedTreasure.X].TreasureId != expectedTreasure.Id)
            {
                treasureMismatches.Add($"treasure {expectedTreasure.Id}: expected at ({expectedTreasure.Y},{expectedTreasure.X})");
            }
        }

        Assert.True(treasureMismatches.Count == 0, $"treasure mismatches:\n" + string.Join("\n", treasureMismatches));
    }
}
