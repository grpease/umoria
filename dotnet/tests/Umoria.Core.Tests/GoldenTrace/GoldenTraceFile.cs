namespace Umoria.Core.Tests.GoldenTrace;

// A single monster line from the trace: "monster <id> <creature_id> <hp>
// <sleep_count> <y> <x> <speed> <distance_from_player> <lit> <stunned> <confused>".
public sealed record GoldenTraceMonster(
    int Id, int CreatureId, int Hp, int SleepCount, int Y, int X, int Speed,
    int DistanceFromPlayer, bool Lit, int StunnedAmount, int ConfusedAmount);

// A single treasure line from the trace: "treasure <id> <y> <x> <item_id>
// <special_name_id> <flags> <category_id> <sprite> <misc_use> <cost>
// <sub_category_id> <items_count> <weight> <to_hit> <to_damage> <ac>
// <to_ac> <dice> <sides> <depth_first_found> <identification>".
public sealed record GoldenTraceTreasure(
    int Id, int Y, int X, int ItemId, int SpecialNameId, uint Flags, int CategoryId, int Sprite, int MiscUse,
    int Cost, int SubCategoryId, int ItemsCount, int Weight, int ToHit, int ToDamage, int Ac, int ToAc,
    int DiceCount, int DiceSides, int DepthFirstFound, int Identification);

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
    public required GoldenTraceTreasure[] Treasures { get; init; }

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

        // line[monsterCountLine + 1 + monsterCount] is "treasures <count>";
        // treasure lines follow.
        var treasureCountLine = monsterCountLine + 1 + monsterCount;
        var treasureCount = int.Parse(lines[treasureCountLine].Split(' ')[1]);
        var treasures = new GoldenTraceTreasure[treasureCount];
        for (var i = 0; i < treasureCount; i++)
        {
            var parts = lines[treasureCountLine + 1 + i].Split(' ');
            treasures[i] = new GoldenTraceTreasure(
                Id: int.Parse(parts[1]),
                Y: int.Parse(parts[2]),
                X: int.Parse(parts[3]),
                ItemId: int.Parse(parts[4]),
                SpecialNameId: int.Parse(parts[5]),
                Flags: uint.Parse(parts[6]),
                CategoryId: int.Parse(parts[7]),
                Sprite: int.Parse(parts[8]),
                MiscUse: int.Parse(parts[9]),
                Cost: int.Parse(parts[10]),
                SubCategoryId: int.Parse(parts[11]),
                ItemsCount: int.Parse(parts[12]),
                Weight: int.Parse(parts[13]),
                ToHit: int.Parse(parts[14]),
                ToDamage: int.Parse(parts[15]),
                Ac: int.Parse(parts[16]),
                ToAc: int.Parse(parts[17]),
                DiceCount: int.Parse(parts[18]),
                DiceSides: int.Parse(parts[19]),
                DepthFirstFound: int.Parse(parts[20]),
                Identification: int.Parse(parts[21]));
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
            Treasures = treasures,
        };
    }
}
