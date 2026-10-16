using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Tests.LineOfSight;

// Real cross-check: compares Umoria.Core.Dungeon.Los.Check (the M5 port of
// los(), src/dungeon_los.cpp) against a text dump captured from the real,
// compiled C++ game via tools/los_dump/los_dump.cpp (built through the
// MSVC+vcpkg toolchain -- see dotnet/tests/LosTraces/los_dump.txt for the
// checked-in baseline).
//
// los() is a pure function of the floor grid's FeatureId values, so no
// dungeon generation or RNG is involved: both sides build a handful of
// small, hand-built grids (open, three wall densities, and isolated
// vertical/horizontal walls) and call the function for every pair of
// coordinates within each one. See los_dump.cpp's comment for the exact
// grid rules this test must mirror.
public class LosTraceTests
{
    private const int GridSize = 10;

    [Fact]
    public void Check_MatchesCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LosTraces", "los_dump.txt");
        var expected = ParseBaseline(path);
        var actual = new Dictionary<string, string>();

        Dump(actual, "open", (_, _) => false);
        Dump(actual, "sparse", (y, x) => (y * 31 + x * 17) % 11 == 0);
        Dump(actual, "medium", (y, x) => (y * 31 + x * 17) % 5 == 0);
        Dump(actual, "dense", (y, x) => (y * 31 + x * 17) % 3 == 0);
        Dump(actual, "vertical_wall", (_, x) => x == GridSize / 2);
        Dump(actual, "horizontal_wall", (y, _) => y == GridSize / 2);

        AssertMatches(expected, actual);
    }

    private static void Dump(Dictionary<string, string> results, string name, Func<int, int, bool> wallAt)
    {
        var dg = new Model.Generated.Dungeon();

        for (var y = 0; y < GridSize; y++)
        {
            for (var x = 0; x < GridSize; x++)
            {
                dg.Floor[y, x].FeatureId = wallAt(y, x) ? TileConstants.TileGraniteWall : TileConstants.TileDarkFloor;
            }
        }

        for (var fromY = 0; fromY < GridSize; fromY++)
        {
            for (var fromX = 0; fromX < GridSize; fromX++)
            {
                for (var toY = 0; toY < GridSize; toY++)
                {
                    for (var toX = 0; toX < GridSize; toX++)
                    {
                        var result = Los.Check(dg, new Coord(fromY, fromX), new Coord(toY, toX));
                        results[$"Los.{name}.{fromY}_{fromX}_{toY}_{toX}"] = result ? "1" : "0";
                    }
                }
            }
        }
    }

    // ---- baseline parsing + comparison (same as ItemMagicTraceTests) ----

    private static Dictionary<string, string> ParseBaseline(string path)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(path))
        {
            var tab = line.IndexOf('\t');
            result[line[..tab]] = line[(tab + 1)..];
        }

        return result;
    }

    private static void AssertMatches(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        var mismatches = new List<string>();

        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                mismatches.Add($"{key}: missing from C# port (expected {expectedValue})");
            }
            else if (actualValue != expectedValue)
            {
                mismatches.Add($"{key}: expected {expectedValue}, got {actualValue}");
            }

            if (mismatches.Count >= 40)
            {
                break;
            }
        }

        if (mismatches.Count < 40)
        {
            foreach (var key in actual.Keys)
            {
                if (!expected.ContainsKey(key))
                {
                    mismatches.Add($"{key}: present in C# port but not in C++ baseline");
                    if (mismatches.Count >= 40)
                    {
                        break;
                    }
                }
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} mismatch(es) (showing up to 40) out of {expected.Count} fields:\n" + string.Join("\n", mismatches));
    }
}
