using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Tests.CharCreate;

namespace Umoria.Core.Tests.PlayerActions;

// Real cross-check: compares the M8 port of the player's step, running,
// traps, tunnelling, doors, disarming and stat changes
// (Umoria.Core.Character.PlayerMovement / PlayerRunning / PlayerTraps /
// PlayerTunnel / PlayerDoors / PlayerStatLogic) against a text dump captured
// from the real, compiled C++ game via
// tools/player_actions_dump/player_actions_dump.cpp (built through the
// MSVC+vcpkg toolchain -- see
// dotnet/tests/PlayerActionsTraces/player_actions_dump.txt for the
// checked-in baseline).
//
// Both sides run the same scenarios: a real character (M6 creation), the
// same hand-built 66x198 test level, a scenario setup, playDungeon()'s
// panel/light reset, then a scripted command string (see the C++ file's
// header for the token language). One state line per step, including RNG
// state and hashes of the whole floor, treasure list, monster list and
// inventory; the full player at the end. Then a stats section.
public class PlayerActionsTraceTests
{
    private sealed record Scenario(
        string Name,
        uint Seed,
        int RaceId,
        bool IsMale,
        int ClassChoice,
        Coord Pos,
        int Depth,
        Action<PlayerWorld, int> Setup,
        int Param,
        string Script);

    private static readonly Coord TrapStart = new(50, 20);
    private static readonly Coord TrapSpot = new(50, 21);

    [Fact]
    public void PlayerActions_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PlayerActionsTraces", "player_actions_dump.txt");
        var expected = File.ReadAllLines(path);

        // A true process-lifetime static in the original (0 at start-up);
        // other tests in this process move it.
        TreasureMagic.MissilesCounter = 0;

        var actual = new List<string>();
        foreach (var scenario in Scenarios())
        {
            Run(scenario, actual);
        }
        RunStats(actual);

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // Same order and values as player_actions_dump.cpp's main().
    private static IEnumerable<Scenario> Scenarios()
    {
        var aDoorway = new Coord(13, 15);
        var aInside = new Coord(8, 20);
        var aEast = new Coord(8, 28);
        var bInside = new Coord(22, 80);
        var longTop = new Coord(14, 15);
        var doorsWest = new Coord(40, 34);
        var digWest = new Coord(55, 12);
        var mazeStart = new Coord(3, 111);
        const string mazeScript = "r6 r2 r6 r4 r2 r6 r8 r6 r6 r4 r2 r6 r4";

        yield return new("walk_lit_room", 12345, 0, true, 0, aDoorway, 1, SetupLight, 0, "m8*5 m6*16 m6 m9 m8*4 m2 m3 m1*3 m5");
        yield return new("walk_dark", 31337, 5, false, 0, bInside, 1, SetupDark, 0, "m6*4 m3*3 m4*2 m8 M7");
        yield return new("walk_into_room", 12345, 0, true, 0, new Coord(8, 34), 1, SetupOpenEastDoor, 0, "m4 m4 m4 m4");
        yield return new("walk_room_c", 31337, 0, false, 0, new Coord(58, 15), 3, SetupLight, 0, "m2 m2 m3*4 m9 m8 m8");
        yield return new("run_corridor", 12345, 0, true, 0, aEast, 1, SetupOpenEastDoor, 0, "r6 m6 r6 r2 r2 r8 r6 r6 r4");
        yield return new("run_corridor_nocut", 31337, 1, false, 3, aEast, 1, SetupOpenEastDoor, 1, "r6 m6 r6 r2 r2 r8 r6");
        yield return new("run_print_self", 987654321, 2, true, 0, aEast, 1, SetupOpenEastDoor, 2, "r6 m6 r6 r2");
        yield return new("run_long", 12345, 3, false, 0, longTop, 2, SetupLongRun, 0, "r2 r6 r6 r6 r4");
        yield return new("run_long_find_bound", 31337, 0, true, 4, longTop, 2, SetupLongRun, 1, "r2 r2 r2 r6 r6");
        yield return new("run_dark_room", 4242, 0, false, 0, bInside, 1, SetupDark, 0, "r6 r8 r4 r2 m6*3 r9 r1");
        yield return new("run_lit_open", 4242, 6, true, 0, bInside, 1, SetupLight, 0, "r6 r2 r4 r8 r7");
        yield return new("run_monster", 777, 0, true, 0, new Coord(8, 10), 1, SetupRunMonster, 0, "r6 r4 r6");
        yield return new("run_monster_corridor", 31337, 0, true, 0, new Coord(8, 33), 1, SetupRunMonster, 1, "r6 r4");
        yield return new("run_blind", 4242, 0, false, 0, longTop, 2, SetupBlind, 0, "r2 r6");
        yield return new("run_maze", 12345, 0, true, 0, mazeStart, 1, SetupMaze, 0, mazeScript);
        yield return new("run_maze_known", 31337, 0, false, 4, mazeStart, 1, SetupMaze, 1, mazeScript);
        yield return new("run_maze_nocut", 987654321, 0, true, 0, mazeStart, 1, SetupMaze, 1 | 2, mazeScript);
        yield return new("run_maze_noexamine", 4242, 0, true, 0, mazeStart, 1, SetupMaze, 1 | 4, mazeScript);
        yield return new("run_maze_seams", 777, 0, true, 0, new Coord(3, 118), 1, SetupMaze, 1 | 8, mazeScript);
        yield return new("run_maze_plain", 12345, 0, false, 0, mazeStart, 1, SetupMaze, 1 | 2 | 4, mazeScript);
        yield return new("run_maze_diagonal", 987654321, 0, true, 0, new Coord(12, 138), 1, SetupMaze, 1, "r4 r6 r4 r9 r3 r7 r1 r4");
        yield return new("run_maze_cross", 4242, 0, false, 0, new Coord(8, 145), 1, SetupMaze, 0, "r4 r8 r2 r6 r4 r2 r8 r4");
        yield return new("run_maze_dark", 31337, 0, true, 0, mazeStart, 1, SetupMaze, 16, mazeScript);
        yield return new("run_maze_room", 12345, 0, true, 0, new Coord(8, 156), 1, SetupMaze, 1, "r4 r4 r2 r8 r6 r6");
        yield return new("confused_walk", 777, 0, false, 0, aInside, 1, SetupConfused, 30, "m6*25 m5 m4*5 r6");
        yield return new("search", 12345, 0, true, 0, new Coord(55, 29), 3, SetupSearch, 0, "s*40 S1 m4 S0 m6 s*4 n5 S1 S0 n0");
        yield return new("search_impaired", 4242, 0, true, 0, new Coord(55, 29), 3, SetupSearch, 2, "s*6 h10 s*10");
        yield return new("search_fos", 31337, 3, false, 2, new Coord(55, 24), 3, SetupSearch, 1, "m6 m6 m6 m6");
        yield return new("pickup", 12345, 0, true, 0, bInside, 4, SetupPickup, 0, "m6*5 m2 M4 m4 m6");
        yield return new("pickup_confirm", 31337, 7, false, 0, bInside, 4, SetupPickup, 1, "m6*5 m2 M4 m4");
        yield return new("pickup_full", 987654321, 0, true, 0, bInside, 4, SetupPickup, 2, "m6*5 m2 M4 m4");
        yield return new("pickup_prompt", 4242, 0, true, 0, bInside, 4, SetupPickup, 3, "m6*5 m2");
        yield return new("doors", 12345, 0, true, 0, doorsWest, 2, SetupDoors, 0,
            "o6 m6*5 o6*8 m6*5 n9 o6*6 n0 b6*12 m6*4 o6 b6*10 m6*4 c6 o6 m6 m6 c6 c4 c8 o8 b8 b4 o4 m4");
        yield return new("doors_rogue", 31337, 0, false, 3, doorsWest, 2, SetupDoors, 0, "o6 m6*5 o6*8 m6*5 o6*12 b6*8");
        yield return new("doors_confused", 987654321, 7, true, 0, doorsWest, 2, SetupDoors, 1, "o6*3 b6*20 m6*3 c4 b6*4");
        yield return new("doors_monster", 777, 0, true, 0, new Coord(40, 54), 2, SetupDoorsMonster, 0, "c6 m6 b6 x4 o6 m4");
        yield return new("doors_monster_stuck", 31337, 0, true, 0, new Coord(40, 49), 2, SetupDoorsMonster, 1, "o6 x6 b6 S1 S0");
        yield return new("doors_misc", 4242, 0, false, 0, new Coord(40, 54), 2, SetupDoors, 0, "b6 c6 c6 x6 o6 b6 x8");
        yield return new("doors_confused_lock", 777, 0, true, 0, new Coord(40, 39), 2, SetupDoors, 1, "o6*4");
        yield return new("tunnel_hands", 12345, 0, true, 0, digWest, 3, SetupTunnel, 0, "t8 t4 t6 t2");
        yield return new("tunnel_weapon", 31337, 5, true, 0, digWest, 3, SetupTunnel, 1, "t8*30 m6 m6 t8*30");
        yield return new("tunnel_pick", 12345, 0, true, 0, digWest, 3, SetupTunnel, 2,
            "t8*40 m6 m6 t8*40 m6*4 t8 t6 m6*6 n7 t6*20 n0 m6*5 t6*3 t2 m4*14 t2*40 m4 t8*60 t4");
        yield return new("tunnel_heavy", 987654321, 3, false, 0, digWest, 3, SetupTunnel, 3, "t8*30 m6 m6 t8*20");
        yield return new("tunnel_confused", 4242, 0, true, 0, digWest, 3, SetupTunnel, 4, "t8*30");
        yield return new("tunnel_secret", 31337, 0, true, 0, new Coord(55, 29), 3, SetupTunnel, 2, "t6*3");
        yield return new("tunnel_afraid", 777, 0, true, 0, new Coord(55, 24), 3, SetupDoorsMonster, 2, "t6");
        yield return new("teleport", 12345, 0, true, 0, aInside, 1, SetupLight, 0, "p10 p10 p100 p100 p3 m6 p50");
        yield return new("disarm_floor", 12345, 0, true, 3, TrapStart, 2, SetupDisarmFloor, 0, "x6*10 m6 x6*10 x8");
        yield return new("disarm_floor_confused", 31337, 0, false, 0, TrapStart, 2, SetupDisarmFloor, 1, "x6*6 m6 x6*6");
        yield return new("disarm_level_gain", 987654321, 0, true, 1, TrapStart, 2, SetupDisarmFloor, 2, "x6 m4 x6 m4 x6 m4 x6 m4 x6 m4 x6");
        yield return new("disarm_level_cap", 12345, 0, false, 2, TrapStart, 2, SetupDisarmFloor, 3, "x6 m4 x6 m4 x6 m4 x6 m4 x6 m4 x6");
        yield return new("disarm_floor_priest", 31337, 0, false, 2, TrapStart, 2, SetupDisarmFloor, 0, "x6*10 m4 x6*10");
        yield return new("disarm_blind", 4242, 0, true, 3, TrapStart, 2, SetupDisarmFloor, 4, "x6*8");
        yield return new("disarm_floor_middling", 777, 0, true, 0, TrapStart, 2, SetupDisarmFloor, 5, "x6*12 m4 x6*12");
        yield return new("disarm_chest", 12345, 0, true, 3, TrapStart, 5, SetupDisarmChest, 0, "x6 s*3 x6*8 x4*8");
        yield return new("disarm_chest_clumsy", 31337, 7, true, 0, TrapStart, 5, SetupDisarmChest, 1, "x6 s*5 x6*4 x4*3");
        yield return new("disarm_chest_protected", 987654321, 7, true, 0, TrapStart, 5, SetupDisarmChest, 2, "x6 s*15 x6*6");
        yield return new("disarm_chest_middling", 4242, 0, false, 0, TrapStart, 5, SetupDisarmChest, 3, "x6 s*15 x6*10");
        yield return new("disarm_chest_priest", 777, 0, true, 2, TrapStart, 5, SetupDisarmChest, 0, "x6 s*5 x6*8 x4*8");
        yield return new("bash_chest", 4242, 7, true, 0, TrapStart, 5, SetupDisarmChest, 0, "b6*40 b4*15");
        yield return new("bash_chest_a", 12345, 0, true, 0, TrapStart, 5, SetupDisarmChest, 0, "b6*12");
        yield return new("bash_chest_b", 31337, 0, true, 0, TrapStart, 5, SetupDisarmChest, 0, "b6*12");
        yield return new("bash_chest_c", 987654321, 0, true, 0, TrapStart, 5, SetupDisarmChest, 0, "b6*12");

        uint[] seeds = { 12345, 31337, 987654321 };
        var n = 0;
        foreach (var trap in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 14, 15, 16, 17, 18, 19, 99 })
        {
            yield return new($"trap_{trap}", seeds[n++ % 3], 0, true, 0, TrapStart, 3, SetupTrap, trap, trap == 11 ? "m6 S1 S0" : "m6 m4 m6 m6 S1 S0");
        }

        int[][] variants =
        {
            new[] { TrapFree, 1 }, new[] { TrapFree, 3 }, new[] { TrapFree, 4 }, new[] { TrapFree, 5 }, new[] { TrapFree, 17 },
            new[] { TrapSustain, 7 }, new[] { TrapSustain, 18 },
            new[] { TrapDying, 1 }, new[] { TrapDying, 4 }, new[] { TrapDying, 9 }, new[] { TrapDying, 14 },
            new[] { TrapAsleep, 5 },
            new[] { TrapArmored, 2 }, new[] { TrapArmored, 7 }, new[] { TrapArmored, 17 }, new[] { TrapArmored, 18 },
            new[] { TrapGuarded, 2 }, new[] { TrapGuarded, 9 }, new[] { TrapBacktrap, 9 },
        };
        foreach (var v in variants)
        {
            yield return new($"trap_{v[1]}_v{v[0] / 1000}", seeds[n++ % 3], 0, true, 0, TrapStart, 3, SetupTrap, v[0] + v[1], "m6 m4 m6 S1 S0");
        }
    }

    // ---- test level (mirrors buildTestLevel()) ---------------------------

    private static void Carve(Model.Generated.Dungeon dg, int y1, int x1, int y2, int x2, byte feature)
    {
        for (var y = y1; y <= y2; y++)
        {
            for (var x = x1; x <= x2; x++)
            {
                dg.Floor[y, x].FeatureId = feature;
            }
        }
    }

    private static void CarveRoom(Model.Generated.Dungeon dg, int y1, int x1, int y2, int x2, bool lit)
    {
        for (var y = y1; y <= y2; y++)
        {
            for (var x = x1; x <= x2; x++)
            {
                var ring = y == y1 || y == y2 || x == x1 || x == x2;
                dg.Floor[y, x].PermaLitRoom = lit;
                dg.Floor[y, x].FeatureId = ring ? TileConstants.TileGraniteWall : lit ? TileConstants.TileLightFloor : TileConstants.TileDarkFloor;
            }
        }
    }

    private static int PlaceObject(PlayerWorld w, Coord coord, int objectId)
    {
        var id = w.Treasure.Popt();
        w.Dg.Floor[coord.Y, coord.X].TreasureId = (byte)id;
        w.Treasure[id] = InventoryCatalog.CopyFromCatalog(objectId);
        return id;
    }

    private static void PlaceDoor(PlayerWorld w, Coord coord, int objectId, short miscUse)
    {
        var id = PlaceObject(w, coord, objectId);
        w.Treasure[id].MiscUse = miscUse;
        w.Dg.Floor[coord.Y, coord.X].FeatureId = objectId == DungeonGenerationConfig.Objects.ObjOpenDoor ? TileConstants.TileCorrFloor : TileConstants.TileBlockedFloor;
    }

    private static PlayerWorld BuildTestLevel(Player py, ObjectIdentification identification, int depth)
    {
        var dg = new Model.Generated.Dungeon
        {
            Height = DungeonConstants.MaxHeight,
            Width = DungeonConstants.MaxWidth,
            CurrentLevel = depth,
        };

        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                var edge = y == 0 || y == DungeonConstants.MaxHeight - 1 || x == 0 || x == DungeonConstants.MaxWidth - 1;
                dg.Floor[y, x] = new Tile { FeatureId = edge ? TileConstants.TileBoundaryWall : TileConstants.TileGraniteWall };
            }
        }

        var w = new PlayerWorld(py, dg, new GameState(), new MonsterManager(dg), new TreasureList(), identification);
        var objects = DungeonGenerationConfig.Objects.ObjClosedDoor;

        CarveRoom(dg, 4, 4, 13, 31, true);
        Carve(dg, 13, 15, 13, 15, TileConstants.TileCorrFloor);
        PlaceDoor(w, new Coord(8, 31), objects, 0);

        Carve(dg, 8, 32, 8, 60, TileConstants.TileCorrFloor);
        Carve(dg, 9, 60, 30, 60, TileConstants.TileCorrFloor);
        Carve(dg, 20, 61, 20, 69, TileConstants.TileCorrFloor);

        CarveRoom(dg, 15, 70, 30, 95, false);
        Carve(dg, 20, 70, 20, 70, TileConstants.TileCorrFloor);

        Carve(dg, 14, 15, 45, 15, TileConstants.TileCorrFloor);
        Carve(dg, 45, 16, 45, 190, TileConstants.TileCorrFloor);

        Carve(dg, 40, 30, 40, 60, TileConstants.TileCorrFloor);
        PlaceDoor(w, new Coord(40, 35), DungeonGenerationConfig.Objects.ObjClosedDoor, 0);
        PlaceDoor(w, new Coord(40, 40), DungeonGenerationConfig.Objects.ObjClosedDoor, 5);
        PlaceDoor(w, new Coord(40, 45), DungeonGenerationConfig.Objects.ObjClosedDoor, 60);
        PlaceDoor(w, new Coord(40, 50), DungeonGenerationConfig.Objects.ObjClosedDoor, -8);
        PlaceDoor(w, new Coord(40, 55), DungeonGenerationConfig.Objects.ObjOpenDoor, 0);
        PlaceDoor(w, new Coord(40, 57), DungeonGenerationConfig.Objects.ObjOpenDoor, 1);

        Carve(dg, 50, 10, 50, 100, TileConstants.TileCorrFloor);

        Carve(dg, 55, 10, 55, 40, TileConstants.TileCorrFloor);
        Carve(dg, 54, 12, 54, 12, TileConstants.TileMagmaWall);
        Carve(dg, 54, 14, 54, 14, TileConstants.TileQuartzWall);
        Carve(dg, 54, 18, 54, 18, TileConstants.TileBoundaryWall);
        w.Objects.PlaceRubble(new Coord(55, 25));
        PlaceDoor(w, new Coord(55, 30), DungeonGenerationConfig.Objects.ObjSecretDoor, 0);
        CarveRoom(dg, 56, 8, 61, 22, true);

        Carve(dg, 3, 111, 3, 130, TileConstants.TileCorrFloor);
        Carve(dg, 2, 115, 2, 125, TileConstants.TileMagmaWall);
        Carve(dg, 4, 115, 4, 125, TileConstants.TileMagmaWall);
        Carve(dg, 4, 130, 12, 130, TileConstants.TileCorrFloor);
        Carve(dg, 8, 131, 8, 150, TileConstants.TileCorrFloor);
        Carve(dg, 5, 140, 11, 140, TileConstants.TileCorrFloor);
        Coord[] diagonal = { new(12, 131), new(11, 132), new(10, 133), new(10, 134), new(11, 135), new(12, 136), new(12, 137), new(12, 138) };
        foreach (var c in diagonal)
        {
            dg.Floor[c.Y, c.X].FeatureId = TileConstants.TileCorrFloor;
        }
        CarveRoom(dg, 4, 151, 12, 161, false);
        Carve(dg, 8, 151, 8, 151, TileConstants.TileCorrFloor);

        return w;
    }

    // ---- scenario setups (mirrors of the C++ ones) -----------------------

    private const int TrapFree = 1000;
    private const int TrapSustain = 2000;
    private const int TrapDying = 3000;
    private const int TrapAsleep = 4000;
    private const int TrapArmored = 5000;
    private const int TrapGuarded = 6000;
    private const int TrapBacktrap = 7000;

    private static void EquipCopyOfFirst(Player py, int lo, int hi, int slot)
    {
        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (py.Inventory[i].CategoryId >= lo && py.Inventory[i].CategoryId <= hi)
            {
                py.Inventory[slot] = py.Inventory[i] with { ItemsCount = 1 };
                py.EquipmentCount++;
                return;
            }
        }

        throw new InvalidOperationException($"scenario setup: no pack item in category {lo}..{hi}");
    }

    private static void CarryTorch(PlayerWorld w)
    {
        EquipCopyOfFirst(w.Py, TreasureCategory.TvLight, TreasureCategory.TvLight, PlayerConfig.Equipment.Light);
        w.Py.CarryingLight = true;
    }

    private static void WieldObject(Player py, int objectId)
    {
        py.Inventory[PlayerConfig.Equipment.Wield] = InventoryCatalog.CopyFromCatalog(objectId);
        py.EquipmentCount++;
    }

    private static void PlaceChest(PlayerWorld w, Coord coord, uint chestFlags, bool identified)
    {
        var id = PlaceObject(w, coord, 326); // & Small Wooden Chest
        w.Treasure[id].Flags |= chestFlags;
        if (identified)
        {
            w.Treasure[id].Identification |= IdentificationConfig.IdKnown2;
        }
    }

    private static void PlaceMonster(PlayerWorld w, Coord coord, bool lit)
    {
        w.Monsters.PlaceNew(coord, 1, false, w.Py.Pos, w.Py.Flags.Speed);
        var id = w.Dg.Floor[coord.Y, coord.X].CreatureId;
        w.Monsters.Monsters[id] = w.Monsters.Monsters[id] with { Lit = lit };
    }

    private static void SetupLight(PlayerWorld w, int param) => CarryTorch(w);

    private static void SetupDark(PlayerWorld w, int param) => w.Py.CarryingLight = false;

    private static void SetupMaze(PlayerWorld w, int param)
    {
        var dg = w.Dg;
        var options = w.Game.Options;
        if ((param & 16) == 0)
        {
            CarryTorch(w);
        }
        if ((param & 1) != 0)
        {
            for (var y = 2; y <= 13; y++)
            {
                for (var x = 110; x <= 162; x++)
                {
                    if (dg.Floor[y, x].FeatureId >= TileConstants.MinCaveWall)
                    {
                        dg.Floor[y, x].PermanentLight = true;
                    }
                }
            }
        }
        options.RunCutCorners = (param & 2) == 0;
        options.RunExamineCorners = (param & 4) == 0;
        options.HighlightSeams = (param & 8) != 0;
    }

    private static void SetupOpenEastDoor(PlayerWorld w, int param)
    {
        CarryTorch(w);
        var id = w.Dg.Floor[8, 31].TreasureId;
        w.Treasure[id] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjOpenDoor);
        w.Dg.Floor[8, 31].FeatureId = TileConstants.TileCorrFloor;
        if (param == 1)
        {
            w.Game.Options.RunCutCorners = false;
        }
        if (param == 2)
        {
            w.Game.Options.RunPrintSelf = true;
        }
    }

    private static void SetupLongRun(PlayerWorld w, int param)
    {
        CarryTorch(w);
        w.Game.Options.FindBound = param == 1;
    }

    private static void SetupConfused(PlayerWorld w, int param)
    {
        CarryTorch(w);
        w.Py.Flags.Confused = (short)param;
    }

    private static void SetupSearch(PlayerWorld w, int param)
    {
        CarryTorch(w);
        w.Objects.SetTrap(new Coord(55, 28), 1);
        PlaceChest(w, new Coord(55, 27), TreasureConfig.Chests.ChPoison, false);
        if (param == 1)
        {
            w.Py.Misc.Fos = 1;
        }
        if (param == 2)
        {
            w.Py.Flags.Confused = 10;
            w.Py.CarryingLight = false;
        }
    }

    private static void SetupPickup(PlayerWorld w, int param)
    {
        var py = w.Py;
        CarryTorch(w);
        w.Objects.PlaceGold(new Coord(22, 81));
        PlaceObject(w, new Coord(22, 82), 84); // & Iron Spike~
        var chest = PlaceObject(w, new Coord(22, 83), 331); // & Large Steel Chest
        w.Treasure[chest].Weight = 3000; // too heavy to carry unasked
        w.Objects.PlaceRandomObjectAt(new Coord(22, 84), false);
        w.Objects.PlaceGold(new Coord(22, 85));
        PlaceObject(w, new Coord(23, 85), 84);
        PlaceObject(w, new Coord(23, 84), 84);
        if (param == 1)
        {
            w.Confirm = _ => true;
        }
        if (param == 2)
        {
            while (py.Pack.UniqueItems < PlayerConfig.Equipment.Wield)
            {
                py.Inventory[py.Pack.UniqueItems] = py.Inventory[0];
                py.Pack.UniqueItems++;
            }
        }
        if (param == 3)
        {
            w.Game.Options.PromptToPickup = true;
        }
    }

    private static void SetupTrap(PlayerWorld w, int param)
    {
        var f = w.Py.Flags;
        CarryTorch(w);
        var trap = param % 1000;
        var variant = param - trap;

        if (trap == 99)
        {
            PlaceObject(w, TrapSpot, DungeonGenerationConfig.Objects.ObjScareMon);
        }
        else
        {
            w.Objects.SetTrap(TrapSpot, trap - 1);
        }

        switch (variant)
        {
            case TrapFree:
                f.FreeFall = true;
                f.FreeAction = true;
                break;
            case TrapSustain:
                f.SustainStr = true;
                f.SustainCon = true;
                break;
            case TrapDying:
                w.Py.Misc.CurrentHp = 1;
                break;
            case TrapAsleep:
                f.Paralysis = 2;
                break;
            case TrapArmored:
                w.Py.Misc.Ac = 200;
                break;
            case TrapGuarded:
                f.Invulnerability = 5;
                f.Rest = 10;
                f.Status |= PlayerConfig.Status.PyRest;
                break;
            case TrapBacktrap:
                w.Objects.SetTrap(TrapStart, 1);
                break;
        }
    }

    private static void SetupDisarmFloor(PlayerWorld w, int param)
    {
        var m = w.Py.Misc;
        CarryTorch(w);
        w.Objects.SetTrap(TrapSpot, param >= 2 && param <= 3 ? 0 : 1);
        w.Objects.TrapChangeVisibility(TrapSpot);
        w.Objects.SetTrap(new Coord(50, 23), 6);
        w.Objects.TrapChangeVisibility(new Coord(50, 23));
        if (param == 1)
        {
            w.Py.Flags.Confused = 30;
        }
        if (param == 2)
        {
            m.Exp = (int)(w.Py.BaseExpLevels[m.Level - 1] * m.ExperienceFactor / 100) - 1;
        }
        if (param == 3)
        {
            m.Exp = PlayerConfig.PlayerMaxExp;
        }
        if (param == 4)
        {
            w.Py.Flags.Blind = 20;
        }
        if (param == 5)
        {
            m.Disarm = 30;
        }
    }

    private static void SetupDisarmChest(PlayerWorld w, int param)
    {
        CarryTorch(w);
        var c = TreasureConfig.Chests.ChLocked | TreasureConfig.Chests.ChPoison | TreasureConfig.Chests.ChParalysed | TreasureConfig.Chests.ChLoseStr;
        PlaceChest(w, TrapSpot, c, false);
        PlaceChest(w, new Coord(50, 19), TreasureConfig.Chests.ChExplode | TreasureConfig.Chests.ChSummon, true);
        if (param == 1 || param == 2)
        {
            w.Py.Misc.Disarm = -40;
        }
        if (param == 3)
        {
            w.Py.Misc.Disarm = 5;
        }
        if (param == 2)
        {
            w.Py.Flags.SustainStr = true;
            w.Py.Flags.FreeAction = true;
        }
    }

    private static void SetupDoors(PlayerWorld w, int param)
    {
        CarryTorch(w);
        if (param == 1)
        {
            w.Py.Flags.Confused = 25;
        }
    }

    private static void SetupDoorsMonster(PlayerWorld w, int param)
    {
        CarryTorch(w);
        if (param == 0)
        {
            PlaceMonster(w, new Coord(40, 55), true);
            w.Objects.SetTrap(new Coord(40, 53), 0);
            PlaceMonster(w, new Coord(40, 53), false);
        }
        else if (param == 1)
        {
            PlaceMonster(w, new Coord(40, 50), false);
        }
        else
        {
            PlaceMonster(w, new Coord(55, 25), true);
        }
        w.Py.Flags.Afraid = 50;
    }

    private static void SetupRunMonster(PlayerWorld w, int param)
    {
        CarryTorch(w);
        if (param == 0)
        {
            PlaceMonster(w, new Coord(8, 25), true);
            PlaceMonster(w, new Coord(7, 11), true);
            PlaceObject(w, new Coord(9, 11), DungeonGenerationConfig.Objects.ObjGoldList);
        }
        else
        {
            SetupOpenEastDoor(w, 0);
            PlaceMonster(w, new Coord(8, 45), true);
        }
    }

    private static void SetupBlind(PlayerWorld w, int param)
    {
        CarryTorch(w);
        w.Py.Flags.Blind = 10;
    }

    private static void SetupTunnel(PlayerWorld w, int param)
    {
        var py = w.Py;
        CarryTorch(w);
        PlaceObject(w, new Coord(54, 12), DungeonGenerationConfig.Objects.ObjGoldList);
        if (param == 1)
        {
            EquipCopyOfFirst(py, TreasureCategory.TvHafted, TreasureCategory.TvSword, PlayerConfig.Equipment.Wield);
        }
        if (param >= 2)
        {
            WieldObject(py, 349); // & Pick, TR_TUNNEL
        }
        if (param == 3)
        {
            py.WeaponIsHeavy = true;
            py.Inventory[PlayerConfig.Equipment.Wield].Weight = 400;
        }
        if (param == 4)
        {
            py.Flags.Confused = 40;
        }
    }

    // ---- running a scenario ----------------------------------------------

    private static void Run(Scenario s, List<string> o)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

        var w = BuildTestLevel(py, identification, s.Depth);
        py.Pos = s.Pos;
        w.Dg.Floor[s.Pos.Y, s.Pos.X].CreatureId = 1;

        s.Setup(w, s.Param);

        // generateCave()'s panel setup, then playDungeon()'s preamble.
        var dg = w.Dg;
        dg.Panel = new Panel();
        ScreenPanel.InitializeForLevel(dg);
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;
        DungeonLighting.ResetView(py, dg, w.Treasure, w.Game.Options);

        o.Add($"scenario {s.Name}");
        var step = 0;
        DumpStep(o, w, step, "start");

        try
        {
            RunScript(w, s.Script, o, ref step);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
        {
            // Recorded inline so the line diff shows exactly where the port
            // stopped, rather than failing with no context.
            o.Add($"exception {e.GetType().Name}: {e.Message}");
        }

        o.Add($"final {s.Name}");
        PlayerTrace.Dump(o, py, identification);
    }

    private static void RunScript(PlayerWorld w, string script, List<string> o, ref int step)
    {
        foreach (var raw in script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Dg.GenerateNewLevel)
            {
                break;
            }

            var token = raw;
            var repeat = 1;
            var star = token.IndexOf('*');
            if (star >= 0)
            {
                repeat = int.Parse(token[(star + 1)..]);
                token = token[..star];
            }

            for (var i = 0; i < repeat && !w.Dg.GenerateNewLevel; i++)
            {
                RunToken(w, token, o, ref step);
            }
        }
    }

    private static void AfterStep(PlayerWorld w, string cmd, List<string> o, ref int step)
    {
        DumpStep(o, w, ++step, cmd);
        if (w.Game.TeleportPlayer && !w.Dg.GenerateNewLevel)
        {
            PlayerMovement.Teleport(w, 100);
            DumpStep(o, w, ++step, "teleport");
        }
    }

    private static void WithDirection(PlayerWorld w, int dir, Action<PlayerWorld, int> command)
    {
        PlayerCore.RememberDirection(w, dir);
        command(w, dir);
    }

    private static void RunToken(PlayerWorld w, string token, List<string> o, ref int step)
    {
        var op = token[0];
        var arg = token.Length > 1 ? int.Parse(token[1..]) : 0;

        w.Game.PlayerFreeTurn = false;

        switch (op)
        {
            case 'm':
                PlayerMovement.Move(w, arg, true);
                break;
            case 'M':
                PlayerMovement.Move(w, arg, false);
                break;
            case 'r':
                PlayerRunning.FindInitialize(w, arg);
                AfterStep(w, token, o, ref step);
                for (var n = 0; w.Py.RunningTracker != 0 && !w.Dg.GenerateNewLevel && n < 300; n++)
                {
                    w.Game.PlayerFreeTurn = false;
                    PlayerRunning.RunAndFind(w);
                    AfterStep(w, "run", o, ref step);
                }
                return;
            case 't':
                PlayerTunnel.Tunnel(w, arg);
                break;
            case 'b':
                WithDirection(w, arg, PlayerDoors.Bash);
                break;
            case 'o':
                WithDirection(w, arg, PlayerDoors.OpenClosedObject);
                break;
            case 'c':
                WithDirection(w, arg, PlayerDoors.CloseDoor);
                break;
            case 'x':
                WithDirection(w, arg, PlayerTraps.DisarmTrap);
                break;
            case 's':
                PlayerMovement.Search(w, w.Py.Pos, w.Py.Misc.ChanceInSearch);
                break;
            case 'S':
                if (arg == 1)
                {
                    PlayerCore.SearchOn(w);
                }
                else
                {
                    PlayerCore.SearchOff(w);
                }
                break;
            case 'p':
                PlayerMovement.Teleport(w, arg);
                break;
            case 'n':
                w.Game.CommandCount = (uint)arg;
                break;
            case 'h':
                w.Py.Flags.Image = (short)arg;
                break;
            default:
                throw new InvalidOperationException($"bad script token: {token}");
        }

        AfterStep(w, token, o, ref step);
    }

    // ---- dump (mirrors dumpStep() and the hashes) -------------------------

    private static uint Mix(uint h, int v)
    {
        h ^= unchecked((uint)v);
        h *= 16777619u;
        return h;
    }

    private const uint FnvOffset = 2166136261u;

    private static uint HashFloor(Model.Generated.Dungeon dg)
    {
        var h = FnvOffset;
        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                var t = dg.Floor[y, x];
                int B(bool b) => b ? 1 : 0;
                h = Mix(h, t.CreatureId | (t.TreasureId << 8) | (t.FeatureId << 16) | (B(t.PermaLitRoom) << 24) | (B(t.FieldMark) << 25) |
                           (B(t.PermanentLight) << 26) | (B(t.TemporaryLight) << 27));
            }
        }
        return h;
    }

    private static uint HashItem(uint h, in Inventory item)
    {
        h = Mix(h, item.Id);
        h = Mix(h, item.SpecialNameId);
        h = Mix(h, unchecked((int)item.Flags));
        h = Mix(h, item.CategoryId);
        h = Mix(h, item.Sprite);
        h = Mix(h, item.MiscUse);
        h = Mix(h, item.Cost);
        h = Mix(h, item.SubCategoryId);
        h = Mix(h, item.ItemsCount);
        h = Mix(h, item.Weight);
        h = Mix(h, item.ToHit);
        h = Mix(h, item.ToDamage);
        h = Mix(h, item.Ac);
        h = Mix(h, item.ToAc);
        h = Mix(h, item.Damage.Count);
        h = Mix(h, item.Damage.Sides);
        h = Mix(h, item.DepthFirstFound);
        h = Mix(h, item.Identification);
        return h;
    }

    private static uint HashTreasure(TreasureList treasure)
    {
        var h = FnvOffset;
        for (var i = TreasureConfig.MinTreasureListId; i < treasure.CurrentId; i++)
        {
            h = HashItem(h, treasure[i]);
        }
        return h;
    }

    private static uint HashInventory(Player py)
    {
        var h = FnvOffset;
        foreach (var item in py.Inventory)
        {
            h = HashItem(h, item);
        }
        return h;
    }

    private static uint HashMonsters(MonsterManager monsters)
    {
        var h = FnvOffset;
        for (var i = MonsterConfig.MonMinIndexId; i < monsters.NextFreeMonsterId; i++)
        {
            var m = monsters.Monsters[i];
            h = Mix(h, m.Hp);
            h = Mix(h, m.SleepCount);
            h = Mix(h, m.Speed);
            h = Mix(h, m.CreatureId);
            h = Mix(h, m.Pos.Y);
            h = Mix(h, m.Pos.X);
            h = Mix(h, m.DistanceFromPlayer);
            h = Mix(h, m.Lit ? 1 : 0);
            h = Mix(h, m.StunnedAmount);
            h = Mix(h, m.ConfusedAmount);
        }
        return h;
    }

    private static void DumpStep(List<string> o, PlayerWorld w, int step, string cmd)
    {
        var py = w.Py;
        var m = py.Misc;
        var f = py.Flags;
        var s = py.Stats;
        var dg = w.Dg;
        var p = dg.Panel;
        var game = w.Game;
        int B(bool b) => b ? 1 : 0;
        const int str = PlayerConfig.Attr.Str;
        const int con = PlayerConfig.Attr.Con;

        o.Add(
            $"step {step} {cmd} rnd={MoriaRng.GetRandomSeed()} pos={py.Pos.Y},{py.Pos.X} free={B(game.PlayerFreeTurn)} run={py.RunningTracker} " +
            $"cmd={game.CommandCount} dir={(int)py.PrevDir} panel={p.Row},{p.Col}/{p.Top}-{p.Bottom},{p.Left}-{p.Right} " +
            $"hp={m.CurrentHp}.{m.CurrentHpFraction}/{m.MaxHp} exp={m.Exp}/{m.MaxExp} lvl={m.Level} au={m.Au} st={f.Status:x8} " +
            $"str={s.Current[str]}/{s.Max[str]}/{s.Used[str]} con={s.Current[con]}/{s.Max[con]}/{s.Used[con]} " +
            $"para={f.Paralysis} blind={f.Blind} conf={f.Confused} slow={f.Slow} pois={f.Poisoned} afraid={f.Afraid} spd={f.Speed} " +
            $"dig={f.FoodDigested} tlo={B(py.TemporaryLightOnly)} gen={B(dg.GenerateNewLevel)} depth={dg.CurrentLevel} " +
            $"dead={B(game.CharacterIsDead)} died=[{game.CharacterDiedFrom}] tele={B(game.TeleportPlayer)} " +
            $"tid={w.Treasure.CurrentId} mon={w.Monsters.NextFreeMonsterId} pack={py.Pack.UniqueItems}/{py.Pack.Weight} " +
            $"floor={HashFloor(dg):x8} treas={HashTreasure(w.Treasure):x8} mons={HashMonsters(w.Monsters):x8} inv={HashInventory(py):x8}");
    }

    // ---- stats section (mirrors runStats()) -------------------------------

    private static void DumpStatsLine(List<string> o, Player py, string what, int stat, int value, bool result)
    {
        var s = py.Stats;
        var m = py.Misc;
        o.Add(
            $"{what} stat={stat} v={value} -> {(result ? 1 : 0)} cur={s.Current[stat]} max={s.Max[stat]} mod={s.Modified[stat]} used={s.Used[stat]} " +
            $"rnd={MoriaRng.GetRandomSeed()} st={py.Flags.Status:x8} hp={m.CurrentHp}.{m.CurrentHpFraction}/{m.MaxHp} bth={m.Bth} " +
            $"dis={m.Disarm} ac={m.MagicalAc} mana={m.Mana}");
    }

    private static void RunStats(List<string> o)
    {
        RunStatsFor(o, 4242, 1);
        var py = RunStatsFor(o, 5151, 2);

        o.Add("tables");
        RunTables(o, py);
    }

    // Human, so classChoice is the class id: a mage reaches the spell code
    // through INT, a priest through WIS.
    private static Player RunStatsFor(List<string> o, uint seed, int classChoice)
    {
        o.Add($"stats class={classChoice}");

        var (py, identification) = PlayerTrace.CreateCharacter(seed, 0, false, classChoice, 0);
        var s = py.Stats;

        int[] values = { 3, 4, 10, 17, 18, 19, 28, 50, 88, 100, 108, 115, 116, 117, 118 };

        for (var stat = 0; stat < 6; stat++)
        {
            var savedCur = s.Current[stat];
            var savedMax = s.Max[stat];
            var savedMod = s.Modified[stat];

            foreach (var v in values)
            {
                s.Current[stat] = (byte)v;
                s.Max[stat] = (byte)v;
                DumpStatsLine(o, py, "inc", stat, v, PlayerStatLogic.StatRandomIncrease(py, stat));

                s.Current[stat] = (byte)v;
                s.Max[stat] = (byte)v;
                DumpStatsLine(o, py, "dec", stat, v, PlayerStatLogic.StatRandomDecrease(py, stat));

                s.Max[stat] = (byte)(v + 20 > 118 ? 118 : v + 20);
                DumpStatsLine(o, py, "restore", stat, v, PlayerStatLogic.StatRestore(py, stat));
                DumpStatsLine(o, py, "restore2", stat, v, PlayerStatLogic.StatRestore(py, stat));

                PlayerStatLogic.StatBoost(py, stat, 2);
                DumpStatsLine(o, py, "boost+", stat, v, true);
                PlayerStatLogic.StatBoost(py, stat, -3);
                DumpStatsLine(o, py, "boost-", stat, v, true);
                PlayerStatLogic.StatBoost(py, stat, 1);
            }

            s.Current[stat] = savedCur;
            s.Max[stat] = savedMax;
            s.Modified[stat] = savedMod;
            PlayerStatLogic.SetAndUseStat(py, stat);
        }

        // hit points while heroic / berserk
        const int con = PlayerConfig.Attr.Con;
        py.Flags.Status |= PlayerConfig.Status.PyHero | PlayerConfig.Status.PyShero;
        s.Current[con] = 50;
        s.Max[con] = 50;
        DumpStatsLine(o, py, "hero-inc", con, 50, PlayerStatLogic.StatRandomIncrease(py, con));
        DumpStatsLine(o, py, "hero-dec", con, 50, PlayerStatLogic.StatRandomDecrease(py, con));
        py.Flags.Status &= ~(PlayerConfig.Status.PyHero | PlayerConfig.Status.PyShero);
        DumpStatsLine(o, py, "hero-off", con, 50, PlayerStatLogic.StatRestore(py, con));

        o.Add($"final stats class={classChoice}");
        PlayerTrace.Dump(o, py, identification);

        return py;
    }

    private static void RunTables(List<string> o, Player py)
    {
        var s = py.Stats;

        int[] strs = { 3, 8, 14, 18, 40, 88, 118 };
        int[] dexs = { 3, 9, 10, 18, 19, 67, 68, 107, 108, 117, 118 };
        int[] weights = { 10, 30, 60, 120, 200, 400, 2000 };
        foreach (var str in strs)
        {
            foreach (var dex in dexs)
            {
                s.Used[PlayerConfig.Attr.Str] = (byte)str;
                s.Used[PlayerConfig.Attr.Dex] = (byte)dex;
                var line = $"blows str={str} dex={dex}";
                foreach (var weight in weights)
                {
                    var blows = PlayerStatLogic.AttackBlows(py, weight, out var weightToHit);
                    line += $" {weight}:{blows}/{weightToHit}";
                }
                o.Add(line);
            }
        }

        var charisma = "charisma";
        for (var chr = 0; chr <= 118; chr++)
        {
            s.Used[PlayerConfig.Attr.Chr] = (byte)chr;
            charisma += $" {PlayerStatLogic.StatAdjustmentCharisma(py)}";
        }
        o.Add(charisma);

        for (var v = 3; v <= 118; v++)
        {
            s.Used[PlayerConfig.Attr.Str] = (byte)v;
            s.Used[PlayerConfig.Attr.Dex] = (byte)v;
            o.Add($"adjust v={v} tohit={PlayerStatLogic.ToHitAdjustment(py)} ac={PlayerStatLogic.ArmorClassAdjustment(py)} " +
                  $"dis={PlayerStatLogic.DisarmAdjustment(py)} dam={PlayerStatLogic.DamageAdjustment(py)}");
        }
    }
}
