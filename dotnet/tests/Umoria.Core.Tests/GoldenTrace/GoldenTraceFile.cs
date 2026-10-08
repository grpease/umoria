namespace Umoria.Core.Tests.GoldenTrace;

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

        return new GoldenTraceFile
        {
            Seed = seed,
            Level = level,
            RndSeed = rndSeed,
            PlayerStart = playerStart,
            Height = height,
            Width = width,
            Floor = floor,
        };
    }
}
