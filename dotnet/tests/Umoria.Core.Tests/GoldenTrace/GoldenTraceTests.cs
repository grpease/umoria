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
// The C++ harness calls generateCaveLayoutOnlyForTesting(), a test-only
// function added to src/dungeon_generate.cpp that mirrors generateCave()'s
// dungeon-level path and dungeonGenerate() up through picking the player's
// start spot, stopping before monster/object placement -- exactly the
// subset DungeonGenerator implements. Level 1 specifically is used because
// the "unusual room" (vault) selection can never trigger there
// (current_level > randomNumber(DUN_UNUSUAL_ROOMS=300) is never true at
// level 1), so this is a genuine apples-to-apples comparison, not just a
// coincidental partial match.
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

        MoriaRng.SetRandomSeed(expected.Seed);
        var dg = new Model.Generated.Dungeon();
        var generator = new DungeonGenerator(dg, new TreasureList());
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
    }
}
