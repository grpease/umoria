namespace Umoria.Core.Tests.GoldenTrace;

// A single monster line from the trace: "monster <id> <creature_id> <hp>
// <sleep_count> <y> <x> <speed> <distance_from_player> <lit> <stunned> <confused>".
public sealed record GoldenTraceMonster(
    int Id, int CreatureId, int Hp, int SleepCount, int Y, int X, int Speed,
    int DistanceFromPlayer, bool Lit, int StunnedAmount, int ConfusedAmount);

// Parses the text format emitted by tools/golden_trace/golden_trace_harness.cpp
// (and golden_trace_harness.cs's --- see GoldenTraceTests for the C# side ---)
// so both can be diffed against the same checked-in baseline files.
public sealed class GoldenTraceFile
{
    public required uint Seed { get; init; }
    public required int Level { get; init; }
    public required uint RndSeed { get; init; }
    public required (int Y, int X) PlayerStart { get; init; }
    public required int Height { get; init; }
    public required int Width { get; init; }
    public required byte[,] Floor { get; init; }
    public required GoldenTraceMonster[] Monsters { get; init; }

    public static GoldenTraceFile Parse(string path)
    {
        var lines = File.ReadAllLines(path);

        var seed = uint.Parse(lines[0].Split(' ')[1]);
        var level = int.Parse(lines[1].Split(' ')[1]);
        var rndSeed = uint.Parse(lines[2].Split(' ')[1]);

        var playerStartParts = lines[3].Split(' ');
        var playerStart = (int.Parse(playerStartParts[1]), int.Parse(playerStartParts[2]));

        var height = int.Parse(lines[4].Split(' ')[1]);
        var width = int.Parse(lines[5].Split(' ')[1]);

        // lines[6] is the "floor" marker; rows follow.
        var floor = new byte[height, width];
        for (var y = 0; y < height; y++)
        {
            var row = lines[7 + y];
            for (var x = 0; x < width; x++)
            {
                floor[y, x] = Convert.ToByte(row[x].ToString(), 16);
            }
        }

        // line[7 + height] is "monsters <count>"; monster lines follow.
        var monsterCountLine = 7 + height;
        var monsterCount = int.Parse(lines[monsterCountLine].Split(' ')[1]);
        var monsters = new GoldenTraceMonster[monsterCount];
        for (var i = 0; i < monsterCount; i++)
        {
            var parts = lines[monsterCountLine + 1 + i].Split(' ');
            monsters[i] = new GoldenTraceMonster(
                Id: int.Parse(parts[1]),
                CreatureId: int.Parse(parts[2]),
                Hp: int.Parse(parts[3]),
                SleepCount: int.Parse(parts[4]),
                Y: int.Parse(parts[5]),
                X: int.Parse(parts[6]),
                Speed: int.Parse(parts[7]),
                DistanceFromPlayer: int.Parse(parts[8]),
                Lit: parts[9] != "0",
                StunnedAmount: int.Parse(parts[10]),
                ConfusedAmount: int.Parse(parts[11]));
        }

        return new GoldenTraceFile
        {
            Seed = seed,
            Level = level,
            RndSeed = rndSeed,
            PlayerStart = playerStart,
            Height = height,
            Width = width,
            Floor = floor,
            Monsters = monsters,
        };
    }
}
