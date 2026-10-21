using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;
using Umoria.Core.Tests.CharCreate;

namespace Umoria.Core.Tests.MonsterCombat;

// Real cross-check: compares the M9 port of monster AI, monster and player
// melee, monster spells and the damage-spell primitives
// (Umoria.Core.Creatures.MonsterAi / MonsterAttacks / MonsterSpells /
// MonsterLife, Character.PlayerCombat, Items.InventoryDamage,
// Spells.DamageSpells / SpellEffects) against a text dump captured from the
// real, compiled C++ game via tools/monster_combat_dump/monster_combat_dump.cpp
// (built through the MSVC+vcpkg toolchain -- see
// dotnet/tests/MonsterCombatTraces/monster_combat_dump.txt for the
// checked-in baseline).
//
// Both sides run the same scenarios: a real character (M6 creation), the
// same hand-built arena, the scenario's setup bits and monster spec, then a
// scripted command string where each turn-taking command is followed by
// updateMonsters(true) (see the C++ file's header for the token language).
// Two lines per step -- player/RNG state with hashes of the floor,
// treasure, inventory and monster memory, then every monster -- and the
// full player at the end.
public class MonsterCombatTraceTests
{
    private sealed record Scenario(
        string Name,
        uint Seed,
        int RaceId,
        bool IsMale,
        int ClassChoice,
        Coord Pos,
        int Depth,
        int Setup,
        string Monsters,
        string Script);

    [Fact]
    public void MonsterCombat_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MonsterCombatTraces", "monster_combat_dump.txt");
        var expected = File.ReadAllLines(path);

        // A true process-lifetime static in the original (0 at start-up);
        // other tests in this process move it.
        TreasureMagic.MissilesCounter = 0;

        var actual = new List<string>();
        foreach (var scenario in Scenarios())
        {
            Run(scenario, actual);
        }
        RunTraps(actual);

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // ---- setup bits (monster_combat_dump.cpp's SetupBits) -----------------

    private const int SWeapon = 1;
    private const int SArmour = 2;
    private const int STough = 4;
    private const int SItems = 8;
    private const int SProtEvil = 16;
    private const int SConfuse = 32;
    private const int SSeeInvis = 64;
    private const int SInfra = 128;
    private const int SAggravate = 256;
    private const int SSustain = 512;
    private const int SFreeAction = 1024;
    private const int SResist = 2048;
    private const int SDark = 4096;
    private const int SArrows = 8192;
    private const int SBlind = 16384;
    private const int SFrail = 32768;
    private const int SGlyph = 65536;
    private const int SChest = 131072;
    private const int SFloorItems = 262144;
    private const int SEnchanted = 524288;
    private const int SExperienced = 1048576;
    private const int SFast = 2097152;
    private const int SLockedChest = 4194304;
    private const int SMana = 8388608;
    private const int SEgoArrows = 16777216;
    private const int SNearItems = 33554432;
    private const int SManyItems = 67108864;
    private const int SFeeble = 134217728;
    private const int SNimble = 268435456;
    private const int SPoor = 536870912;
    private const int SGlyphUnder = 1073741824;

    private const int Tough = STough;
    private const int Fighter = SWeapon | SArmour | STough;
    private const int Loaded = SWeapon | SArmour | STough | SItems;

    // Same order and values as monster_combat_dump.cpp's main().
    private static IEnumerable<Scenario> Scenarios()
    {
        var p = new Coord(11, 20);
        var doors = new Coord(11, 85);
        var dark = new Coord(11, 120);

        // ---- melee: the player attacking ----
        yield return new("melee_orc", 12345, 0, true, 0, p, 3, Fighter, "77@11,24", "w*3 m6*6 w*2");
        yield return new("melee_kobolds", 31337, 5, false, 0, p, 2, Fighter, "16@11,25 16@9,25 22@13,25", "w*4 m6*8 w*3");
        yield return new("melee_bare_hands", 987654321, 0, true, 3, p, 1, Tough | SFeeble, "1@11,21", "m6*12");
        yield return new("melee_mage_dark", 4242, 0, false, 1, dark, 3, SWeapon | STough | SDark, "77@11,122", "U m6*8 w*2");
        yield return new("melee_arrows", 777, 0, true, 4, p, 3, SArrows | STough, "77@11,21", "m6*8");
        yield return new("melee_enchanted", 12345, 0, true, 5, p, 4, Fighter | SEnchanted, "57@11,21 46@10,21", "m6*6 m9*6");
        yield return new("melee_kill_level", 31337, 0, true, 0, p, 3, Fighter | SEnchanted, "47@11,21h1 18@10,21h1 81@12,21h1", "m6 m9 m3 w");
        yield return new("bash_monster", 987654321, 7, true, 0, p, 3, Fighter, "77@11,21", "b6*10 w*6");
        yield return new("bash_dark", 4242, 0, true, 0, dark, 3, SArmour | STough | SDark, "16@11,121", "b6*8");
        yield return new("confuse_touch", 777, 0, false, 0, p, 3, Fighter | SConfuse | SExperienced | SEnchanted, "77@11,22h300", "w m6 w*5 c m6*2 w*3 c m6 c m6 c m6 c m6 c m6");
        yield return new("confuse_on_hit", 12345, 0, true, 2, p, 3, STough | SConfuse, "16@11,21 63@12,21", "w*4 c w*3 c w*3 c w*3 c w*3 c w*3");
        yield return new("protect_evil", 31337, 0, true, 0, p, 6, Tough | SProtEvil | SExperienced, "77@11,21 16@10,21 29@12,21 37@12,19", "w*12");
        yield return new("death_by_orcs", 987654321, 3, false, 1, p, 5, SFrail, "77@11,21 77@10,21 77@12,21", "w*10");

        // ---- movement ----
        yield return new("sleepers", 4242, 0, true, 0, p, 3, Tough, "77@11,30z200 16@5,30z50 22@15,30z500 37@11,40z120", "w*30");
        yield return new("sleepers_aggravate", 777, 0, true, 0, p, 3, Tough | SAggravate, "77@11,30z200 16@5,30z50", "w*6");
        yield return new("sleepers_resting", 12345, 3, true, 2, p, 3, Tough, "77@11,30z30 16@5,30z30", "r40 w*20 r0 w*4");
        yield return new("sleepers_troll", 31337, 7, true, 0, p, 3, Tough, "77@11,30z30 16@5,25z30", "w*25");
        yield return new("random_movers", 987654321, 0, true, 0, p, 2, Tough, "11@11,30 5@9,30 30@13,30 41@5,30 70@17,30", "w*25");
        yield return new("fast_player", 4242, 0, false, 0, p, 2, Tough | SFast, "17@11,26 77@5,30 21@15,30", "w*20");
        yield return new("worms_breed", 777, 0, true, 0, p, 3, Loaded, "17@11,25 32@5,40 45@15,40 36@11,35", "w*80");
        yield return new("compact_mainloop", 12345, 0, true, 0, p, 3, Tough, "F8:107 278@20,165 17@11,25 17@9,25 53@13,25 13@11,30", "w*20 K w*10 K w*10");
        yield return new("compact_in_update", 31337, 0, true, 0, p, 3, Tough, "F8:110 17@11,25 17@9,25 53@13,25 13@11,30 13@12,30", "w*120");
        yield return new("eats_other", 987654321, 0, true, 0, p, 3, Tough | SItems, "13@11,22 13@10,22 20@11,24", "w*25");
        yield return new("picks_up", 4242, 0, false, 0, p, 1, Tough | SFloorItems, "0@11,27 20@9,27", "w*20");
        yield return new("doors_kobold", 777, 0, true, 0, doors, 2, Tough, "16@11,66h30 31@11,63h30", "w*45");
        yield return new("doors_bashers", 12345, 0, true, 0, new Coord(11, 73), 2, Tough, "29@11,67h60 42@11,68h60 77@11,77", "w*40");
        yield return new("glyph", 31337, 0, true, 0, p, 3, Tough | SGlyph, "77@11,23 16@11,24", "w*15");
        yield return new("ghosts", 987654321, 0, true, 0, p, 5, Tough | SItems, "34@11,25 60@9,25 87@13,25", "w*30");
        yield return new("ghosts_seen", 4242, 0, true, 0, p, 5, Tough | SItems | SSeeInvis, "34@11,25 60@9,25 12@13,22", "w*30");
        yield return new("out_of_wall", 777, 0, true, 0, new Coord(8, 36), 3, Tough, "77@8,40 21@15,46h3", "w*4");
        yield return new("dig_out", 12345, 0, false, 0, new Coord(8, 36), 3, Tough, "77@15,46h200 22@8,40", "w*6");
        yield return new("infravision", 31337, 0, true, 0, dark, 3, Tough | SDark | SInfra, "77@11,124 16@11,130 12@9,123", "w*15");
        yield return new("blind_player", 987654321, 0, true, 0, p, 3, Tough | SBlind, "77@11,24", "w*8");
        yield return new("quylthulg", 4242, 0, true, 0, p, 20, Tough, "174@11,21", "w*60");

        // ---- monster attack types ----
        yield return new("atk_confusion", 777, 0, true, 0, p, 1, Tough, "8@11,21 74@10,21", "w*10");
        yield return new("atk_fear", 12345, 0, true, 0, p, 2, Tough, "26@11,21", "w*10");
        yield return new("atk_poison", 31337, 0, true, 0, p, 3, Tough, "38@11,21 71@12,21", "w*10");
        yield return new("atk_paralysis", 987654321, 0, true, 0, p, 5, Tough, "61@11,21 18@10,21", "w*14");
        yield return new("atk_paralysis_free", 4242, 0, true, 0, p, 5, Tough | SFreeAction, "61@11,21 18@10,21", "w*10");
        yield return new("atk_lose_stats", 777, 0, true, 0, p, 7, Tough, "73@11,21 47@10,21 45@12,21 87@10,20 227@12,20", "w*20");
        yield return new("atk_lose_stats_sustained", 12345, 0, true, 0, p, 7, Tough | SSustain, "73@11,21 47@10,21 45@12,21 87@10,20 227@12,20", "w*30");
        yield return new("atk_shrieker", 31337, 0, true, 0, p, 2, Tough, "19@11,21 77@5,40z300 16@17,40z300", "w*12");
        yield return new("atk_disenchant", 987654321, 0, true, 0, p, 5, SWeapon | STough | SEnchanted, "67@11,21", "w*40");
        yield return new("atk_elements", 4242, 0, true, 0, p, 6, Loaded, "51@11,21 62@10,21 69@12,21 59@10,19", "w*20");
        yield return new("atk_elements_resist", 777, 0, true, 0, p, 6, Loaded | SResist, "51@11,21 62@10,21 69@12,21 59@10,19", "w*12");
        yield return new("atk_lightning", 12345, 0, true, 0, p, 27, Loaded | SManyItems, "211@11,21", "w*30");
        yield return new("atk_blind", 31337, 0, true, 0, p, 7, Tough, "81@11,21", "w*10");
        yield return new("atk_steal_gold", 987654321, 0, true, 0, p, 2, Tough | SPoor, "0@11,21 23@10,21 40@12,21", "w*12");
        yield return new("atk_steal_items", 4242, 0, true, 0, p, 2, Tough | SItems, "4@11,21 277@10,21", "w*14");
        yield return new("atk_paralysed_thief", 777, 0, true, 0, p, 2, Tough | SItems, "4@11,21 0@10,21", "p20 w*8");
        yield return new("atk_eat", 12345, 0, true, 0, p, 5, Tough | SItems, "20@11,21 60@10,21 49@12,21 64@10,20", "w*20");
        yield return new("atk_drain_exp", 31337, 0, true, 2, p, 12, Tough | SExperienced, "119@11,21 193@10,21", "w*14");
        yield return new("atk_lich", 987654321, 0, true, 1, p, 34, Loaded | SExperienced, "239@11,22", "w*20");
        yield return new("atk_insults", 4242, 0, true, 0, p, 3, Tough, "48@11,21 277@10,21", "w*10");

        // ---- monster spells ----
        yield return new("spell_novices", 777, 0, true, 1, p, 3, Tough, "25@11,26 24@9,26", "w*40");
        yield return new("spell_drain_mana", 12345, 0, false, 1, p, 3, Tough | SExperienced | SMana, "18@11,22 47@10,23 67@12,23 39@13,22", "w*30");
        yield return new("spell_casters", 31337, 0, true, 0, p, 12, Tough, "102@11,26 121@9,26 120@13,26", "w*40");
        yield return new("spell_casters_free", 987654321, 0, true, 0, p, 12, Tough | SFreeAction, "102@11,26 121@9,26", "w*30");
        yield return new("spell_summoners", 4242, 0, true, 0, p, 13, Tough, "116@11,26 131@9,26 159@13,30", "w*40");
        yield return new("spell_undead", 777, 0, true, 0, p, 35, Tough, "246@11,26", "w*40");
        yield return new("breath_frost_gas", 12345, 0, true, 0, p, 16, Loaded, "137@11,24 156@9,24 13@11,21 13@10,21", "w*30");
        yield return new("breath_acid_light", 31337, 0, true, 0, p, 25, Loaded | SManyItems | SEnchanted, "176@11,24 201@9,24 13@11,21", "w*30");
        yield return new("breath_fire", 987654321, 0, true, 0, p, 27, Loaded, "210@11,24 179@9,24 13@11,21 13@12,21", "w*30");
        yield return new("breath_dragon", 4242, 0, true, 0, p, 36, Loaded | SResist, "253@11,24 13@11,21 13@10,22", "w*30");

        // ---- the player's damage spells ----
        yield return new("bolts", 777, 0, true, 1, p, 3, Tough, "11@11,24 15@11,27 56@11,30 69@11,33 77@11,36", "B6:5:20 B6:4:20 B6:1:15 B6:0:12 B6:3:30 B6:2:30 B6:6:30 B6:5:200");
        yield return new("bolts_miss", 12345, 0, true, 1, p, 3, Tough, "77@5,24", "B8:5:20 B2:5:20 B4:1:5");
        yield return new("balls", 31337, 0, true, 1, p, 3, Tough | SFloorItems, "77@11,27 16@10,27 16@12,27 13@11,28 22@11,40", "L6:5:30 L6:1:30 L6:4:20 L6:3:25 L6:2:20 L6:6:60");
        yield return new("ball_wall", 987654321, 0, true, 1, new Coord(11, 55), 3, Tough, "16@10,59 16@12,59", "L6:5:40 L6:5:40");
        yield return new("monster_hp_spells", 4242, 0, true, 2, p, 5, Tough, "77@11,24 63@9,20 22@11,30", "H6:10 H6:-5 H6:100 D8 D6 D6 D2");
        yield return new("dispel", 777, 0, true, 2, p, 5, Tough, "63@11,24 63@9,24 77@13,24", "X8:30 X8:30 X4:60");
        yield return new("earthquake", 12345, 0, true, 0, p, 5, Tough | SFloorItems, "77@11,24 16@9,15 34@13,20 174@11,14 8@12,21 8@12,19 8@10,19 77@9,21 16@13,23 16@12,25 22@10,17 29@8,20", "E E E E");
        yield return new("sleep_adjacent", 31337, 0, true, 2, p, 5, Tough, "77@11,21 16@10,21 8@12,21 63@12,19", "U Z w*4");
        yield return new("aggravate", 987654321, 0, true, 0, p, 3, Tough, "77@11,30z200 16@5,30z50 22@15,50", "A20 w*3");
        yield return new("balrog_win", 4242, 0, true, 0, p, 50, Tough, "278@11,24", "H6:5000");

        // ---- added after a coverage pass ----
        yield return new("undead_confused", 777, 0, true, 0, p, 7, Tough, "63@11,25c8 34@1,10c30 87@13,25c5", "w*15");
        yield return new("stun_recovery", 12345, 0, true, 0, p, 34, Tough, "77@11,25s3 239@9,25s6 22@13,25s1", "w*10");
        yield return new("glyph_break", 31337, 0, true, 0, new Coord(11, 62), 50, Tough | SItems | SGlyph, "277@11,64", "w*40");
        yield return new("glyph_under", 987654321, 0, true, 0, p, 3, Tough | SGlyphUnder, "8@11,21 77@10,21", "w*20");
        yield return new("atk_descriptions", 4242, 0, true, 0, p, 9, Tough | SItems, "9@11,21 94@10,21 97@12,21", "w*15");
        yield return new("atk_disenchant_plain", 777, 0, true, 0, p, 5, Fighter, "67@11,21", "w*15");
        yield return new("crits", 12345, 0, true, 0, p, 5, Fighter | SEnchanted | SExperienced, "62@11,21h30000", "W69 m6*80");
        yield return new("bash_quylthulg", 31337, 0, true, 0, p, 20, Fighter | SExperienced, "174@11,21h30000", "b6*30");
        yield return new("bash_stun_cap", 987654321, 7, true, 0, p, 5, Fighter, "77@11,21h250", "b6*20");
        yield return new("ego_arrows", 4242, 0, true, 0, p, 30, Tough | SEgoArrows | SExperienced, "216@11,21h300s20 63@10,21h300s20 58@12,21h300s20 77@10,20h300s20 51@12,20h300s20 11@11,19h300s20", "m9*3 m3*3 m8*3 m2*3 m4*3 m6*6");
        yield return new("bolts_resist", 777, 0, true, 1, p, 5, Tough, "56@11,22 69@11,26", "B6:4:30 B6:4:30 B6:4:30 B6:5:30 L6:5:30 L6:4:30");
        yield return new("breath_kill_order", 12345, 0, true, 0, p, 16, Loaded | SNearItems, "13@11,21 13@12,21 137@11,24 13@10,21", "w*30");
        yield return new("breath_kills_player", 31337, 0, true, 0, p, 16, SItems | SFrail, "137@11,23", "w*20");
        yield return new("cannibal_oozes", 987654321, 0, true, 0, p, 22, Tough, "186@11,25 186@9,25 13@10,26 13@12,26 17@11,27 17@10,24", "w*150");

        yield return new("atk_steal_nimble", 777, 0, true, 0, p, 2, Tough | SItems | SNimble, "4@11,21 0@10,21", "w*14");
        yield return new("atk_sustain_dex_wis", 12345, 0, true, 0, p, 7, Tough | SSustain, "45@11,21 45@12,21 87@10,21 87@10,20", "w*30");
        yield return new("vision_lost", 31337, 0, true, 0, new Coord(11, 58), 3, Tough, "77@8,45z500 16@14,50z500", "U m6*5 x20 U");
        yield return new("balrog_melee", 987654321, 0, true, 0, p, 50, Tough | SItems, "278@11,21", "w*2");
        yield return new("breath_corner", 4242, 0, true, 0, new Coord(2, 3), 16, Loaded, "137@4,6 156@2,7", "w*25");

        // ---- objects: chests ----
        yield return new("chest_open", 777, 0, true, 0, p, 5, Tough | SChest, "", "o6 m6");
        yield return new("chest_locked", 12345, 0, true, 3, p, 5, Tough | SLockedChest, "", "o6*6 m6");
    }

    // ---- arena (mirrors buildArena()) ------------------------------------

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

    private static PlayerWorld BuildArena(Player py, ObjectIdentification identification, int depth)
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

        // Room A (lit), a granite pillar and an enclosed 3x3 granite block
        CarveRoom(dg, 1, 1, 21, 61, true);
        Carve(dg, 8, 40, 8, 40, TileConstants.TileGraniteWall);
        Carve(dg, 14, 45, 16, 47, TileConstants.TileGraniteWall);

        // door corridor east to room B: closed, locked, stuck and secret doors
        Carve(dg, 11, 61, 11, 100, TileConstants.TileCorrFloor);
        PlaceDoor(w, new Coord(11, 65), DungeonGenerationConfig.Objects.ObjClosedDoor, 0);
        PlaceDoor(w, new Coord(11, 70), DungeonGenerationConfig.Objects.ObjClosedDoor, 10);
        PlaceDoor(w, new Coord(11, 75), DungeonGenerationConfig.Objects.ObjClosedDoor, -5);
        PlaceDoor(w, new Coord(11, 80), DungeonGenerationConfig.Objects.ObjSecretDoor, 0);

        // Room B (dark)
        CarveRoom(dg, 1, 100, 21, 170, false);
        Carve(dg, 11, 100, 11, 100, TileConstants.TileCorrFloor);

        return w;
    }

    // ---- setup (mirrors setup() and placeMonsters()) -----------------------

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

    private static void EquipObject(Player py, int objectId, int slot)
    {
        py.Inventory[slot] = InventoryCatalog.CopyFromCatalog(objectId);
        py.EquipmentCount++;
    }

    private static void CarryObject(PlayerWorld w, int objectId, int count, short miscUse)
    {
        var item = InventoryCatalog.CopyFromCatalog(objectId) with { ItemsCount = (byte)count };
        if (miscUse >= 0)
        {
            item.MiscUse = miscUse;
        }
        PlayerInventory.CarryItem(w.Py, w.Identification, item);
    }

    private static void Setup(PlayerWorld w, int param)
    {
        var py = w.Py;
        var f = py.Flags;
        var east = new Coord(py.Pos.Y, py.Pos.X + 1);

        if ((param & SDark) == 0)
        {
            EquipCopyOfFirst(py, TreasureCategory.TvLight, TreasureCategory.TvLight, PlayerConfig.Equipment.Light);
            py.CarryingLight = true;
        }
        if ((param & SWeapon) != 0)
        {
            EquipCopyOfFirst(py, TreasureCategory.TvHafted, TreasureCategory.TvSword, PlayerConfig.Equipment.Wield);
        }
        if ((param & SArrows) != 0)
        {
            EquipObject(py, 80, PlayerConfig.Equipment.Wield); // & Arrow~
            py.Inventory[PlayerConfig.Equipment.Wield].ItemsCount = 3;
        }
        if ((param & SEgoArrows) != 0)
        {
            EquipObject(py, 80, PlayerConfig.Equipment.Wield); // & Arrow~
            ref var arrows = ref py.Inventory[PlayerConfig.Equipment.Wield];
            arrows.ItemsCount = 10;
            arrows.MiscUse = 1;
            arrows.Flags |= TreasureConfig.Flags.TrEgoWeapon | TreasureConfig.Flags.TrStr | TreasureConfig.Flags.TrDex |
                            TreasureConfig.Flags.TrSearch | TreasureConfig.Flags.TrStealth | TreasureConfig.Flags.TrSpeed |
                            TreasureConfig.Flags.TrInfra;
        }
        if ((param & SArmour) != 0)
        {
            EquipObject(py, 103, PlayerConfig.Equipment.Body);
            EquipObject(py, 126, PlayerConfig.Equipment.Arm);
            EquipObject(py, 95, PlayerConfig.Equipment.Head);
            EquipObject(py, 93, PlayerConfig.Equipment.Feet);
            EquipObject(py, 124, PlayerConfig.Equipment.Hands);
            EquipObject(py, 123, PlayerConfig.Equipment.Outer);
        }
        if ((param & SEnchanted) != 0)
        {
            py.Inventory[PlayerConfig.Equipment.Wield].ToHit = 3;
            py.Inventory[PlayerConfig.Equipment.Wield].ToDamage = 3;
            foreach (var slot in new[]
                     {
                         PlayerConfig.Equipment.Body, PlayerConfig.Equipment.Arm, PlayerConfig.Equipment.Head,
                         PlayerConfig.Equipment.Feet, PlayerConfig.Equipment.Hands, PlayerConfig.Equipment.Outer,
                     })
            {
                py.Inventory[slot].ToAc = 2;
            }
            py.Inventory[PlayerConfig.Equipment.Body].Flags |= TreasureConfig.Flags.TrResAcid;
        }
        if ((param & (SWeapon | SArmour | SEnchanted | SArrows | SEgoArrows)) != 0)
        {
            PlayerBonuses.RecalculateBonuses(py);
        }
        if ((param & STough) != 0)
        {
            for (var i = 0; i < PlayerConfig.PlayerMaxLevel; i++)
            {
                py.BaseHpLevels[i] = (ushort)(600 + 10 * i);
            }
            py.Misc.MaxHp = 600;
            py.Misc.CurrentHp = 600;
        }
        if ((param & SFrail) != 0)
        {
            py.Misc.CurrentHp = 3;
        }
        if ((param & SItems) != 0)
        {
            CarryObject(w, 268, 3, -1); // & Flask~ of Oil
            CarryObject(w, 237, 2, -1); // Cure Light Wounds (potion)
            CarryObject(w, 176, 2, -1); // Identify (scroll)
            CarryObject(w, 282, 1, 6); // Magic Missile (wand)
            CarryObject(w, 293, 1, 5); // Light (staff)
            CarryObject(w, 140, 1, -1); // Resist Fire (ring)
            CarryObject(w, 21, 2, -1); // & Ration~ of Food
        }
        if ((param & SManyItems) != 0)
        {
            for (var ring = 132; ring <= 137; ring++)
            {
                CarryObject(w, ring, 1, -1); // six rings
            }
            CarryObject(w, 270, 1, 4); // Lightning Bolts (wand)
            CarryObject(w, 84, 5, -1); // & Iron Spike~
        }
        if ((param & SFeeble) != 0)
        {
            py.Misc.PlussesToDamage = -30;
        }
        if ((param & SNimble) != 0)
        {
            py.Stats.Used[PlayerConfig.Attr.Dex] = 118;
        }
        if ((param & SPoor) != 0)
        {
            py.Misc.Au = 3;
        }

        if ((param & SProtEvil) != 0)
        {
            f.ProtectEvil = 50;
        }
        if ((param & SConfuse) != 0)
        {
            f.ConfuseMonster = true;
        }
        if ((param & SSeeInvis) != 0)
        {
            f.SeeInvisible = true;
        }
        if ((param & SInfra) != 0)
        {
            f.SeeInfra = 5;
        }
        if ((param & SAggravate) != 0)
        {
            f.Aggravate = true;
        }
        if ((param & SSustain) != 0)
        {
            f.SustainStr = true;
            f.SustainDex = true;
            f.SustainCon = true;
            f.SustainInt = true;
            f.SustainWis = true;
        }
        if ((param & SFreeAction) != 0)
        {
            f.FreeAction = true;
        }
        if ((param & SResist) != 0)
        {
            f.ResistantToFire = true;
            f.ResistantToCold = true;
            f.ResistantToAcid = true;
            f.ResistantToLight = true;
            f.HeatResistance = 20;
            f.ColdResistance = 20;
        }
        if ((param & SBlind) != 0)
        {
            f.Blind = 50;
            f.Status |= PlayerConfig.Status.PyBlind;
        }
        if ((param & SGlyph) != 0)
        {
            PlaceObject(w, east, DungeonGenerationConfig.Objects.ObjScareMon);
        }
        if ((param & SGlyphUnder) != 0)
        {
            PlaceObject(w, py.Pos, DungeonGenerationConfig.Objects.ObjScareMon);
        }
        if ((param & SNearItems) != 0)
        {
            PlaceObject(w, new Coord(py.Pos.Y - 1, py.Pos.X - 1), 237);
            PlaceObject(w, new Coord(py.Pos.Y + 1, py.Pos.X - 1), 268);
            PlaceObject(w, new Coord(py.Pos.Y, py.Pos.X - 2), 176);
            PlaceObject(w, new Coord(py.Pos.Y - 2, py.Pos.X), 21);
            PlaceObject(w, new Coord(py.Pos.Y + 2, py.Pos.X), 103);
        }
        if ((param & (SChest | SLockedChest)) != 0)
        {
            var id = PlaceObject(w, east, 331); // & Large Steel Chest
            if ((param & SLockedChest) != 0)
            {
                w.Treasure[id].Flags |= TreasureConfig.Chests.ChLocked | TreasureConfig.Chests.ChSummon;
            }
        }
        if ((param & SFloorItems) != 0)
        {
            PlaceObject(w, new Coord(py.Pos.Y - 1, py.Pos.X + 3), 237);
            PlaceObject(w, new Coord(py.Pos.Y, py.Pos.X + 3), 176);
            PlaceObject(w, new Coord(py.Pos.Y + 1, py.Pos.X + 3), 268);
            PlaceObject(w, new Coord(py.Pos.Y, py.Pos.X + 5), 21);
            PlaceObject(w, new Coord(py.Pos.Y + 2, py.Pos.X + 4), 282);
        }
        if ((param & SExperienced) != 0)
        {
            py.Misc.Exp = 3000;
            PlayerCore.DisplayCharacterExperience(w);
        }
        if ((param & SFast) != 0)
        {
            f.Speed = -1;
        }
        if ((param & SMana) != 0)
        {
            // last: SExperienced's level gain recomputes mana
            py.Misc.Mana = 30;
            py.Misc.CurrentMana = 30;
        }
    }

    private static void PlaceMonsters(PlayerWorld w, string spec)
    {
        foreach (var token in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token[0] == 'F')
            {
                var parts = token[1..].Split(':');
                var creatureId = int.Parse(parts[0]);
                var count = int.Parse(parts[1]);
                for (var k = 0; k < count; k++)
                {
                    w.Monsters.PlaceNew(new Coord(2 + k / 60, 105 + k % 60), creatureId, false, w.Py.Pos, w.Py.Flags.Speed);
                }
                continue;
            }

            var at = token.IndexOf('@');
            var comma = token.IndexOf(',');
            var end = comma + 1;
            while (end < token.Length && char.IsDigit(token[end]))
            {
                end++;
            }

            var cid = int.Parse(token[..at]);
            var coord = new Coord(int.Parse(token[(at + 1)..comma]), int.Parse(token[(comma + 1)..end]));
            if (!w.Monsters.PlaceNew(coord, cid, false, w.Py.Pos, w.Py.Flags.Speed))
            {
                throw new InvalidOperationException($"could not place monster {token}");
            }
            ref var monster = ref w.Monsters.Monsters[w.Dg.Floor[coord.Y, coord.X].CreatureId];

            var i = end;
            while (i < token.Length)
            {
                var key = token[i++];
                var start = i;
                while (i < token.Length && (char.IsDigit(token[i]) || token[i] == '-'))
                {
                    i++;
                }
                var value = int.Parse(token[start..i]);
                switch (key)
                {
                    case 'z':
                        monster.SleepCount = (short)value;
                        break;
                    case 'h':
                        monster.Hp = (short)value;
                        break;
                    case 'c':
                        monster.ConfusedAmount = (byte)value;
                        break;
                    case 's':
                        monster.StunnedAmount = (byte)value;
                        break;
                    default:
                        throw new InvalidOperationException($"bad monster modifier in {token}");
                }
            }
        }
    }

    // ---- running a scenario ----------------------------------------------

    private static PlayerWorld Prepare(uint seed, int raceId, bool isMale, int classChoice, Coord pos, int depth, int setup,
        out ObjectIdentification identification)
    {
        (var py, identification) = PlayerTrace.CreateCharacter(seed, raceId, isMale, classChoice, 0);

        var w = BuildArena(py, identification, depth);
        py.Pos = pos;
        w.Dg.Floor[pos.Y, pos.X].CreatureId = 1;

        Setup(w, setup);
        return w;
    }

    private static void EnterLevel(PlayerWorld w)
    {
        // generateCave()'s panel setup, then playDungeon()'s preamble.
        var dg = w.Dg;
        dg.Panel = new Panel();
        ScreenPanel.InitializeForLevel(dg);
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;
        DungeonLighting.ResetView(w.Py, dg, w.Treasure, w.Game.Options);
    }

    private static void Run(Scenario s, List<string> o)
    {
        var w = Prepare(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, s.Pos, s.Depth, s.Setup, out var identification);
        PlaceMonsters(w, s.Monsters);
        EnterLevel(w);

        RunScripted(w, identification, s.Name, s.Script, o);
    }

    // The fire, acid and corroding-gas traps (types 13, 12, 10), deferred
    // in M8 until the damage primitives existed.
    private static void RunTraps(List<string> o)
    {
        int[] traps = { 10, 12, 13 };
        int[] setups = { Loaded, Loaded | SResist, SItems | SFrail };
        uint[] seeds = { 12345, 31337, 987654321 };
        var p = new Coord(11, 20);

        foreach (var trap in traps)
        {
            for (var v = 0; v < 3; v++)
            {
                var w = Prepare(seeds[v], 0, true, 0, p, 3, setups[v], out var identification);
                w.Objects.SetTrap(new Coord(p.Y, p.X + 1), trap - 1);
                EnterLevel(w);

                RunScripted(w, identification, $"trap_{trap}_v{v}", "m6 m4 m6 m4 m6", o);
            }
        }
    }

    private static void RunScripted(PlayerWorld w, ObjectIdentification identification, string name, string script, List<string> o)
    {
        o.Add($"scenario {name}");
        var step = 0;
        DumpStep(o, w, step, "start");

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
                RunToken(w, token);
                DumpStep(o, w, ++step, token);
            }
        }

        o.Add($"final {name}");
        PlayerTrace.Dump(o, w.Py, identification);
    }

    private static void RunToken(PlayerWorld w, string token)
    {
        var op = token[0];
        var args = token.Length > 1 ? token[1..].Split(':').Select(int.Parse).ToArray() : Array.Empty<int>();
        int Arg(int i) => i < args.Length ? args[i] : 0;
        var a = Arg(0);
        var b = Arg(1);
        var c = Arg(2);
        var py = w.Py;

        w.Game.PlayerFreeTurn = false;
        var takesTurn = true;

        switch (op)
        {
            case 'w':
                break;
            case 'm':
                PlayerMovement.Move(w, a, true);
                break;
            case 'b':
                PlayerCore.RememberDirection(w, a);
                PlayerDoors.Bash(w, a);
                break;
            case 'o':
                PlayerCore.RememberDirection(w, a);
                PlayerDoors.OpenClosedObject(w, a);
                break;
            case 't':
                PlayerTunnel.Tunnel(w, a);
                break;
            case 'B':
                DamageSpells.FireBolt(w, py.Pos, a, c, (MagicSpellFlags)b, "bolt");
                break;
            case 'L':
                DamageSpells.FireBall(w, py.Pos, a, c, (MagicSpellFlags)b, "ball");
                break;
            case 'H':
                DamageSpells.ChangeMonsterHitPoints(w, py.Pos, a, b);
                break;
            case 'D':
                DamageSpells.DrainLifeFromMonster(w, py.Pos, a);
                break;
            case 'X':
                DamageSpells.DispelCreature(w, a, b);
                break;
            case 'E':
                DamageSpells.Earthquake(w);
                takesTurn = false;
                break;
            case 'Z':
                MonsterLife.Sleep(w, py.Pos);
                break;
            case 'A':
                SpellEffects.AggravateMonsters(w, a);
                break;
            case 'c':
                py.Flags.ConfuseMonster = true;
                takesTurn = false;
                break;
            case 'r':
                py.Flags.Rest = (short)a;
                takesTurn = false;
                break;
            case 'p':
                py.Flags.Paralysis = (short)a;
                takesTurn = false;
                break;
            case 'K':
                w.Monsters.CompactMonsters();
                takesTurn = false;
                break;
            case 'U':
                MonsterAi.UpdateMonsters(w, false);
                takesTurn = false;
                break;
            case 'x':
                py.Flags.Blind = (short)a;
                py.Flags.Status |= PlayerConfig.Status.PyBlind;
                takesTurn = false;
                break;
            case 'W':
                py.Inventory[PlayerConfig.Equipment.Wield] = InventoryCatalog.CopyFromCatalog(a);
                PlayerBonuses.RecalculateBonuses(py);
                takesTurn = false;
                break;
            default:
                throw new InvalidOperationException($"bad script token: {token}");
        }

        if (w.Game.TeleportPlayer && !w.Dg.GenerateNewLevel)
        {
            PlayerMovement.Teleport(w, 100);
        }

        if (takesTurn && !w.Game.PlayerFreeTurn && !w.Dg.GenerateNewLevel)
        {
            w.Dg.GameTurn++;
            MonsterAi.UpdateMonsters(w, true);
        }
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

    private static uint HashRecall(Recall[] recall)
    {
        var h = FnvOffset;
        foreach (var memory in recall)
        {
            h = Mix(h, unchecked((int)memory.Movement));
            h = Mix(h, unchecked((int)memory.Spells));
            h = Mix(h, memory.Kills);
            h = Mix(h, memory.Deaths);
            h = Mix(h, memory.Defenses);
            h = Mix(h, memory.Wake);
            h = Mix(h, memory.Ignore);
            foreach (var attack in memory.Attacks)
            {
                h = Mix(h, attack);
            }
        }
        return h;
    }

    private static void DumpStep(List<string> o, PlayerWorld w, int step, string cmd)
    {
        var py = w.Py;
        var m = py.Misc;
        var f = py.Flags;
        var used = py.Stats.Used;
        var dg = w.Dg;
        var game = w.Game;
        int B(bool b) => b ? 1 : 0;

        o.Add(
            $"step {step} {cmd} rnd={MoriaRng.GetRandomSeed()} turn={dg.GameTurn} pos={py.Pos.Y},{py.Pos.X} free={B(game.PlayerFreeTurn)} cmd={game.CommandCount} " +
            $"hp={m.CurrentHp}/{m.MaxHp} mana={m.CurrentMana}.{m.CurrentManaFraction} exp={m.Exp}.{m.ExpFraction}/{m.MaxExp} lvl={m.Level} au={m.Au} st={f.Status:x8} " +
            $"stats={used[PlayerConfig.Attr.Str]},{used[PlayerConfig.Attr.Int]},{used[PlayerConfig.Attr.Wis]},{used[PlayerConfig.Attr.Dex]},{used[PlayerConfig.Attr.Con]} " +
            $"para={f.Paralysis} blind={f.Blind} conf={f.Confused} slow={f.Slow} pois={f.Poisoned} afraid={f.Afraid} spd={f.Speed} cm={B(f.ConfuseMonster)} " +
            $"gen={B(dg.GenerateNewLevel)} dead={B(game.CharacterIsDead)} died=[{game.CharacterDiedFrom}] win={B(game.TotalWinner)} " +
            $"tid={w.Treasure.CurrentId} mon={w.Monsters.NextFreeMonsterId} mult={w.Monsters.MonsterMultiplyTotal} " +
            $"pack={py.Pack.UniqueItems}/{py.Pack.Weight} light={py.Inventory[PlayerConfig.Equipment.Light].MiscUse} " +
            $"floor={HashFloor(dg):x8} treas={HashTreasure(w.Treasure):x8} inv={HashInventory(py):x8} recall={HashRecall(w.CreatureRecall):x8}");

        var line = new System.Text.StringBuilder("mons");
        for (var i = MonsterConfig.MonMinIndexId; i < w.Monsters.NextFreeMonsterId; i++)
        {
            var mon = w.Monsters.Monsters[i];
            line.Append($" {i}:{mon.CreatureId}@{mon.Pos.Y},{mon.Pos.X}/{mon.Hp}/{mon.SleepCount}/{mon.StunnedAmount}/{mon.ConfusedAmount}/{mon.Speed}/{mon.DistanceFromPlayer}/{B(mon.Lit)}");
        }
        o.Add(line.ToString());
    }
}
