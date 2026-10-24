using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;
using Umoria.Core.Tests.CharCreate;

namespace Umoria.Core.Tests.Casting;

// Real cross-check: compares the M10 port of the rest of spells.cpp and the
// casting layer (Umoria.Core.Spells.DetectionSpells / MonsterStatusSpells /
// TerrainSpells / SpellEffects / SpellCasting, Character.PlayerMagic,
// PlayerSpells.GainSpells, PlayerCore's input wrappers) against a text dump
// captured from the real, compiled C++ game via
// tools/spell_casting_dump/spell_casting_dump.cpp (built through the
// MSVC+vcpkg toolchain -- see
// dotnet/tests/SpellCastingTraces/spell_casting_dump.txt for the checked-in
// baseline).
//
// Same arena and monster spec language as the M9 trace; the casting tokens
// carry a key script that both sides feed to their prompts (the C++ side
// through the headless stub's getKeyInput(), this side through
// PlayerWorld.ReadCommand / SelectItem), and how many keys each command read
// is part of the dump. Three lines per step -- player/RNG state with hashes,
// the spell-related player state, every monster -- and the full player at
// the end.
public class SpellCastingTraceTests
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
        int Exp,
        string Monsters,
        string Script);

    [Fact]
    public void SpellCasting_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SpellCastingTraces", "spell_casting_dump.txt");
        var expected = File.ReadAllLines(path);

        // A true process-lifetime static in the original (0 at start-up);
        // other tests in this process move it.
        TreasureMagic.MissilesCounter = 0;

        var actual = new List<string>();
        foreach (var scenario in Scenarios())
        {
            Run(scenario, actual);
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // ---- setup bits (spell_casting_dump.cpp's SetupBits) ------------------
    //
    // These and the arena/setup/hash helpers below are internal because the
    // M11 item-use trace reuses them, as item_use_dump.cpp copies them.

    internal const int STough = 1;
    internal const int SDark = 2;
    internal const int SBlind = 4;
    internal const int SHurt = 8;
    internal const int SBooks = 16;
    internal const int SLearn = 32;
    internal const int SMana = 64;
    internal const int SStaff = 128;
    internal const int STraps = 256;
    internal const int SCursed = 512;
    internal const int SAiling = 1024;
    internal const int SDrained = 2048;
    internal const int SSustain = 4096;
    internal const int SRubble = 8192;
    internal const int SWeapon = 16384;
    internal const int SItems = 32768;
    internal const int SNearItems = 65536;
    internal const int SUnknown = 131072;
    internal const int SRubbleRow = 262144;

    private const int T = STough;
    private const int Caster = STough | SBooks | SLearn | SMana;
    private const int MaxExp = 9999999;

    // Same order and values as spell_casting_dump.cpp's main().
    private static IEnumerable<Scenario> Scenarios()
    {
        var p = new Coord(11, 20);
        var doors = new Coord(11, 85);
        var dark = new Coord(11, 120);

        // ---- the casting layer ----
        yield return new("mage_study", 12345, 0, true, 1, p, 3, T, 0, "77@11,25", "Ga C1a6 C1a6 C1a6 G w");
        yield return new("priest_study", 31337, 0, true, 2, p, 3, T, 0, "", "G G w");
        yield return new("mage_study_blind", 777, 0, true, 1, p, 3, T | SBlind, 0, "", "G");
        yield return new("mage_study_confused", 4242, 0, false, 1, p, 3, T, 0, "", "f5 G");
        yield return new("mage_study_levels", 987654321, 0, true, 1, p, 3, T | SBooks, 40000, "", "Gacb~ G~ Ga~");
        yield return new("priest_study_levels", 12345, 0, false, 2, p, 3, T, 40000, "", "G G");
        yield return new("cast_refusals", 31337, 0, true, 1, p, 3, T | SLearn, 3000, "", "f5 C0 f0 x5 C0");
        yield return new("cast_in_dark", 777, 0, true, 1, dark, 3, T | SLearn | SDark, 3000, "", "C0 P0");
        yield return new("priest_casts_mage", 4242, 0, true, 2, p, 3, T | SLearn, 3000, "", "C0 P~");
        yield return new("mage_prays", 987654321, 0, true, 1, p, 3, T | SLearn, 3000, "", "P0");
        yield return new("cast_menu_keys", 12345, 0, true, 1, p, 3, T | SLearn, 3000, "77@11,25", "C~ C1~ C1z~ C1Z~ C1*a6 C1g~ C1a~ y C1A6 n C1A~");
        yield return new("cast_low_mana", 31337, 0, true, 1, p, 3, T | SLearn, 6000, "77@11,25", "M1 n C1g6 y C1g6 M0 C1a6 M2 C1a6 w*3");
        yield return new("pray_low_mana", 777, 0, false, 2, p, 3, T | SLearn | SHurt, 6000, "", "M0 n P1b y P1b P1c M1 P1b P1b");
        yield return new("cast_fail_often", 4242, 0, true, 1, p, 3, T | SLearn | SMana, 3000, "77@11,25h300", "C1a6*12");
        yield return new("unknown_book", 987654321, 0, true, 1, p, 3, T | SBooks, 0, "", "C2");

        // ---- every mage spell ----
        yield return new("mage_book1", 12345, 0, true, 1, p, 5, Caster | SHurt | STraps, MaxExp, "77@11,25 16@5,20 34@15,22",
            "M999 C1a6*3 M999 C1b*3 M999 C1d*3 M999 C1e*3 M999 C1f*3 M999 C1g6*3 M999 C1c*3");
        yield return new("mage_book2", 31337, 0, true, 1, p, 5, Caster | SCursed | SAiling | STraps, MaxExp, "77@11,25 16@11,30 63@5,20 22@15,20",
            "M999 C2a6*3 M999 C2b6*3 M999 C2c*3 M999 C2d8*3 M999 C2e*3 M999 C2g*3 M999 C2h2*3 M999 C2i4*3 M999 C2f*3");
        yield return new("mage_book3", 777, 0, true, 1, p, 5, Caster | SStaff | SItems, MaxExp, "77@11,25 16@11,30 22@12,21 13@10,19",
            "M999 C3a*3 M999 C3b&*3 M999 C3c*3 M999 C3d6*3 M999 C3ea*3 M999 C3f*3 M999 C3g6*3 M999 C3h6*3 M999 C3a*3");
        yield return new("mage_book4", 4242, 0, true, 1, p, 5, Caster | SStaff, MaxExp, "77@11,25 16@11,28 16@9,28 278@15,30z30000 22@5,24",
            "M999 C4a6*3 M999 C4b&*3 M999 C4c6*3 M999 C4d*3 M999 C4e6*3 M999 C4gk*6 M999 C4gB*6 M999 C4f*3");
        yield return new("mage_teleport_self", 987654321, 0, false, 1, p, 5, Caster, MaxExp, "77@11,25", "C2f C1c C2f M0 y C4e6 C4e6");
        yield return new("mage_wall_to_mud", 12345, 0, true, 1, new Coord(11, 58), 5, Caster | SRubble, MaxExp, "149@11,55", "C2i6 C2i4 C2i8 C2i2 C2i6 C2i6");
        yield return new("mage_recharge_until_broken", 31337, 0, true, 1, p, 5, Caster | SStaff, MaxExp, "", "C4b&*8 C3b&*4");
        yield return new("mage_create_food", 777, 0, true, 1, p, 5, Caster, MaxExp, "", "C3a C3a m6 C3a");
        yield return new("mage_identify", 4242, 0, true, 1, p, 5, Caster | SItems | SStaff, MaxExp, "", "C3ea C3eb C3ec C3ed C3e~");
        yield return new("rogue_spells", 987654321, 0, true, 3, p, 5, Caster, MaxExp, "77@11,25", "M999 C1a6~*3 M999 C1b~*3 M999 C1c~*3 M999 C2a6~*3 M999 C3e~*3");

        // ---- every prayer ----
        yield return new("priest_book1", 12345, 0, false, 2, p, 5, Caster | SHurt | SAiling | STraps, MaxExp, "77@11,25 34@9,23",
            "M999 P1a*3 M999 P1b*3 M999 P1c*3 M999 P1d*3 M999 P1e*3 M999 P1f*3 M999 P1g*3 M999 P1h*3");
        yield return new("priest_book2", 31337, 0, true, 2, p, 5, Caster | SHurt | SCursed | SAiling, MaxExp, "77@11,25 16@11,21 22@10,21",
            "M999 P2a6*3 M999 P2c*3 M999 P2d*3 M999 P2e*3 M999 P2f*3 M999 P2g*3 M999 P2h*3 M999 P2b*3");
        yield return new("priest_book3", 777, 0, true, 2, p, 5, Caster | SHurt | SAiling, MaxExp, "77@11,25 63@11,28 87@9,25 16@13,24",
            "M999 P3a*3 M999 P3b6*3 M999 P3c*3 M999 P3d*3 M999 P3e*3 M999 P3e*3 M999 P3i*3 M999 P3h*3 M999 P3g*3 M999 P3f*3");
        yield return new("priest_book4", 4242, 0, true, 2, p, 5, Caster | SHurt | SAiling, MaxExp, "77@11,25 63@11,28 87@9,25 16@13,24 278@15,30z30000",
            "M999 P4a*3 M999 P4b*3 M999 P4c*3 M999 P4d*3 M999 P4e*3 M999 P4f*3 M999 P4f*3");
        yield return new("paladin_prayers", 987654321, 0, true, 5, p, 5, Caster | SHurt, MaxExp, "77@11,25", "M999 P1b*3 M999 P1c*3 M999 P2a6*3");
        yield return new("priest_glyph_food", 12345, 0, true, 2, p, 5, Caster, MaxExp, "", "P4e P2f m6 P2f P4e");

        // ---- monster-status spells, directly ----
        yield return new("status_rays", 31337, 0, true, 0, p, 5, T, 0, "77@11,24 16@11,30 22@11,36 63@11,42", "S39:6 S40:6 S26:6:-1 S26:6:2 S39:6 S40:6 S37:6 S38:6 S12:6 w*3");
        yield return new("status_nosleep", 777, 0, true, 0, p, 5, T, 0, "8@11,23 27@11,26c4 64@9,20", "S40:6 S39:6 S39:6 S40:8 S39:8");
        yield return new("status_resisters", 4242, 0, true, 0, p, 30, T, 0, "239@11,24 205@9,20", "S39:6 S40:6 S26:6:-1 S38:6 S38:8 S39:8 S40:8");
        yield return new("status_miss", 987654321, 0, true, 0, p, 5, T, 0, "77@5,24", "S39:6 S40:8 S26:2:1 S37:4 S38:6 S12:6");
        yield return new("status_all", 12345, 0, true, 0, p, 5, T, 0, "77@11,24 16@11,30z200 63@9,24 87@13,24 8@12,21 22@5,40", "U S35 S14:-1 S14:1 S36 S35 S14:-1");
        yield return new("status_all_dark", 31337, 0, true, 0, dark, 5, T | SDark, 0, "77@11,124 16@11,130 63@9,124", "S35 S14:-1 S14:1 S36");
        yield return new("mass_genocide", 777, 0, true, 0, p, 5, T, 0, "77@11,24 16@11,30 278@15,30 22@5,58", "S13 w");
        yield return new("mass_polymorph", 4242, 0, true, 0, p, 5, T, 0, "77@11,24 16@11,30 278@15,30 22@5,58", "S15 w S15");
        yield return new("clone", 987654321, 0, true, 0, p, 5, T, 0, "77@11,24z100 17@9,20", "S12:6 S12:6 S12:8 S12:8 w*2");

        // ---- terrain, light and object spells, directly ----
        yield return new("light_line_corridor", 12345, 0, true, 0, doors, 5, T | SDark, 0, "17@11,90 41@11,95 18@11,83", "S7:6 S7:4 S7:8 S7:6");
        yield return new("starlite_dark_room", 31337, 0, true, 0, dark, 5, T | SDark, 0, "17@11,124 36@7,116 18@15,124 47@11,116 77@8,123", "S8 S8");
        yield return new("starlite_lit_room", 777, 0, true, 0, p, 5, T, 0, "17@11,24 36@7,16 18@15,24", "S8");
        yield return new("disarm_ray_doors", 4242, 0, true, 0, doors, 5, T, 0, "", "S9:4 S9:4 m4 m4 S9:4");
        yield return new("destroy_doors_ray", 987654321, 0, true, 0, doors, 5, T, 0, "", "S10:4 S10:6");
        yield return new("disarm_ray_traps", 12345, 0, true, 0, p, 5, T | STraps, 0, "", "S9:6 S9:7 S9:3 S9:9 S10:8 S10:1");
        yield return new("build_wall", 31337, 0, true, 0, p, 5, T | SNearItems, 0, "77@11,24 163@5,20 250@17,20 18@11,14", "S11:6 S11:8 S11:2 S11:4 S11:9");
        yield return new("wall_to_mud_stone", 777, 0, true, 0, new Coord(11, 58), 5, T | SRubble, 0, "149@11,55 167@9,58 166@13,58", "S33:4 S33:8 S33:2 S33:6 S33:4");
        yield return new("surround", 4242, 0, true, 0, p, 5, T | SNearItems, 0, "77@11,24", "S5 S43 S6 S43 S6");
        yield return new("surround_traps_step", 987654321, 0, true, 0, p, 5, T, 0, "", "S5 m6");
        yield return new("destroy_area", 12345, 0, true, 0, p, 5, T | STraps, 0, "77@11,24 16@11,40 278@15,30 22@5,50", "S31 w");
        yield return new("destroy_area_town", 31337, 0, true, 0, p, 0, T, 0, "77@11,24", "S31");
        yield return new("darken_room", 777, 0, true, 0, p, 5, T, 0, "", "S4 S32 S4");
        yield return new("darken_corridor", 4242, 0, true, 0, doors, 5, T, 0, "", "S4 S32 S4 S7:6 S4");
        yield return new("light_area_town", 987654321, 0, true, 0, p, 0, T, 0, "", "S32 x5 S32 S4");
        yield return new("map_area", 12345, 0, true, 0, new Coord(11, 90), 5, T | STraps, 0, "", "S34 S34");
        yield return new("detect_all", 31337, 0, true, 0, p, 5, T | STraps, 0, "34@9,23 77@11,30 60@13,24 16@5,10 16@11,150 34@9,150", "S1 S2 S44 S45 S3 S29 S30 S1 S45");
        yield return new("detect_none", 777, 0, true, 0, dark, 5, T | SDark, 0, "", "S1 S2 S44 S45 S3 S29 S30");
        yield return new("create_food_glyph", 4242, 0, true, 0, p, 5, T, 0, "", "S41 S41 m6 S42 S42 m4 S42");

        // ---- player self-effects, directly ----
        yield return new("self_effects", 987654321, 0, true, 0, p, 5, T | SHurt | SAiling | SCursed | SWeapon | SDrained, 40000,
            "", "S25:4 S25:15 S25:40 S25:1000 S25:5 S16 S17 S18 S19 S20 S21 S22:10*6 S22:0 S23 S23 S24 S24 S27 S28 f5 S27 x5 S28");
        yield return new("self_sustained", 12345, 0, true, 0, p, 5, T | SSustain, 0, "", "S16 S17 S18 S19 S20 S21");
        yield return new("enchant_high", 31337, 0, true, 0, p, 5, T | SWeapon, 0, "", "S22:3*12 S22:15*200");

        // ---- added after a coverage pass ----
        yield return new("identify_merge", 12345, 0, true, 1, p, 5, Caster | SUnknown, MaxExp, "",
            "M999 C3ea~*2 M999 C3eb~*2 M999 C3ec~*2 M999 C3ed~*2 M999 C3ee~*2 M999 C3ef~*2 M999 C3eg~*2 M999 C3eh~*2 M999 C3ei~*2 M999 C3ej~*2 M999 C3ek~*2 M999 C3el~*2 M999 C3em~*2");
        yield return new("pray_refusals", 31337, 0, true, 2, p, 5, Caster, MaxExp, "", "x5 P0 x0 f5 P0 f0 B P0 K0*40 P0");
        yield return new("mage_no_books", 777, 0, true, 1, p, 5, Caster, MaxExp, "", "B C0");
        yield return new("unknown_prayer_book", 4242, 0, false, 2, p, 3, T | SBooks, 0, "", "P2");
        yield return new("mage_study_missing", 987654321, 0, true, 1, p, 3, T, 40000, "", "Gaaaaaaaaaaa~");
        yield return new("mage_study_dark", 12345, 0, true, 1, dark, 3, T | SDark, 0, "", "G");
        yield return new("turn_undead_high", 31337, 0, true, 0, p, 30, T, MaxExp, "63@11,24 87@9,22 224@13,24 34@11,17 205@15,20", "U S36 S36 S36");
        yield return new("holy_word", 777, 0, true, 2, p, 5, Caster | SHurt | SAiling, MaxExp, "", "M999 P4f*12");
        yield return new("genocide", 4242, 0, true, 1, p, 5, Caster, MaxExp, "16@11,24 16@11,30 16@5,40 278@15,30z30000", "M999 C4gk*8 M999 C4gB*4 M999 C4g~*6");
        yield return new("confuse_resist", 987654321, 0, true, 0, p, 5, T, 0, "166@11,24 153@9,20", "S39:6*6 S40:6*4 S39:8*4");
        yield return new("recharge_paths", 12345, 0, true, 1, p, 5, Caster | SStaff, MaxExp, "", "M999 C3b~*4 Q60 M999 C3b& I M999 C3b&*3 Q55 M999 C4b& M999 C4b&");
        yield return new("slow_poison", 31337, 0, true, 0, p, 5, T | SAiling, 0, "", "S46*7");
        yield return new("slow_poison_none", 777, 0, true, 0, p, 5, T, 0, "", "S46");
        yield return new("wall_to_mud_walls", 4242, 0, true, 0, p, 5, T, 0, "", "S33:7 S33:1 S33:9 S33:3 S33:8 S33:4");
        yield return new("wall_to_mud_rubble_row", 987654321, 0, true, 0, p, 5, T | SRubbleRow, 0, "", "S33:6*16");
        yield return new("surround_doors_items", 12345, 0, true, 0, p, 5, T | SNearItems, 0, "", "S6");
        yield return new("destroy_adjacent_chest", 31337, 0, true, 0, p, 5, T | STraps, 0, "", "S43 S43");
        yield return new("destroy_ray_chest", 777, 0, true, 0, p, 5, T | STraps, 0, "", "S10:7 S10:9");
    }

    // ---- arena (mirrors buildArena(), M9's) --------------------------------

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

    internal static int PlaceObject(PlayerWorld w, Coord coord, int objectId)
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

    internal static PlayerWorld BuildArena(Player py, ObjectIdentification identification, int depth)
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

    internal static void EquipObject(Player py, int objectId, int slot)
    {
        py.Inventory[slot] = InventoryCatalog.CopyFromCatalog(objectId);
        py.EquipmentCount++;
    }

    internal static void CarryObject(PlayerWorld w, int objectId, int count, short miscUse)
    {
        var item = InventoryCatalog.CopyFromCatalog(objectId) with { ItemsCount = (byte)count };
        if (miscUse >= 0)
        {
            item.MiscUse = miscUse;
        }
        PlayerInventory.CarryItem(w.Py, w.Identification, item);
    }

    internal static void Setup(PlayerWorld w, int param, int exp)
    {
        var py = w.Py;
        var f = py.Flags;

        if ((param & SDark) == 0)
        {
            EquipCopyOfFirst(py, TreasureCategory.TvLight, TreasureCategory.TvLight, PlayerConfig.Equipment.Light);
            py.CarryingLight = true;
        }
        if ((param & SWeapon) != 0)
        {
            EquipObject(py, 37, PlayerConfig.Equipment.Wield); // & Broad Sword
            py.Inventory[PlayerConfig.Equipment.Wield].ToHit = 2;
        }
        if ((param & SCursed) != 0)
        {
            EquipObject(py, 103, PlayerConfig.Equipment.Body); // Soft Leather Armor
            EquipObject(py, 140, PlayerConfig.Equipment.Left); // Resist Fire (ring)
            py.Inventory[PlayerConfig.Equipment.Body].Flags |= TreasureConfig.Flags.TrCursed;
            py.Inventory[PlayerConfig.Equipment.Left].Flags |= TreasureConfig.Flags.TrCursed;
            if ((param & SWeapon) != 0)
            {
                py.Inventory[PlayerConfig.Equipment.Wield].Flags |= TreasureConfig.Flags.TrCursed;
            }
        }
        if ((param & (SWeapon | SCursed)) != 0)
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
        if ((param & SItems) != 0)
        {
            CarryObject(w, 268, 3, -1); // & Flask~ of Oil
            CarryObject(w, 237, 2, -1); // Cure Light Wounds (potion)
            CarryObject(w, 176, 2, -1); // Identify (scroll)
            CarryObject(w, 140, 1, -1); // Resist Fire (ring)
        }
        if ((param & SStaff) != 0)
        {
            CarryObject(w, 293, 1, 5); // Light (staff)
            CarryObject(w, 282, 1, 6); // Magic Missile (wand)
        }
        if ((param & SBooks) != 0)
        {
            var first = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells == SpellConfig.SpellTypeMage ? 319 : 323;
            for (var book = first; book < first + 3; book++)
            {
                CarryObject(w, book, 1, -1);
            }
        }
        if (exp > 0)
        {
            // playerGainLevel() halves the excess on each level gained, so
            // repeat until the level settles
            int level;
            do
            {
                level = py.Misc.Level;
                py.Misc.Exp = exp;
                PlayerCore.DisplayCharacterExperience(w);
            } while (py.Misc.Level != level);
        }
        if ((param & SDrained) != 0)
        {
            py.Misc.Exp /= 3;
        }
        if ((param & SLearn) != 0)
        {
            var spells = DataPlayerData.MagicSpells[py.Misc.ClassId - 1];
            var order = 0;
            f.SpellsLearnt = 0;
            for (var i = 0; i < 31; i++)
            {
                if (spells[i].LevelRequired <= py.Misc.Level)
                {
                    f.SpellsLearnt |= 1u << i;
                    f.SpellsLearnedOrder[order++] = (byte)i;
                }
            }
            f.NewSpellsToLearn = 0;
        }
        if ((param & SHurt) != 0)
        {
            py.Misc.CurrentHp = (short)(py.Misc.MaxHp / 4);
        }
        if ((param & SAiling) != 0)
        {
            f.Poisoned = 30;
            f.Afraid = 20;
            for (var stat = 0; stat < 6; stat++)
            {
                py.Stats.Current[stat] = (byte)(py.Stats.Max[stat] - 2);
                PlayerStatLogic.SetAndUseStat(py, stat);
            }
        }
        if ((param & SSustain) != 0)
        {
            f.SustainStr = true;
            f.SustainInt = true;
            f.SustainWis = true;
            f.SustainDex = true;
            f.SustainCon = true;
            f.SustainChr = true;
        }
        if ((param & SBlind) != 0)
        {
            f.Blind = 50;
            f.Status |= PlayerConfig.Status.PyBlind;
        }
        if ((param & STraps) != 0)
        {
            var pos = py.Pos;
            w.Objects.SetTrap(new Coord(pos.Y, pos.X + 3), 0);
            w.Objects.SetTrap(new Coord(pos.Y - 2, pos.X + 5), 4);
            w.Objects.SetTrap(new Coord(pos.Y + 1, pos.X - 1), 7);
            PlaceDoor(w, new Coord(pos.Y + 1, pos.X + 2), DungeonGenerationConfig.Objects.ObjSecretDoor, 0);
            var chest = PlaceObject(w, new Coord(pos.Y - 2, pos.X - 2), 331); // & Large Steel Chest
            w.Treasure[chest].Flags |= TreasureConfig.Chests.ChLocked | 0x00000010;
            chest = PlaceObject(w, new Coord(pos.Y - 1, pos.X + 1), 331);
            w.Treasure[chest].Flags |= 0x00000020;
            PlaceObject(w, new Coord(pos.Y + 3, pos.X - 5), DungeonGenerationConfig.Objects.ObjDownStair);
            PlaceObject(w, new Coord(pos.Y - 3, pos.X - 5), DungeonGenerationConfig.Objects.ObjUpStair);
            PlaceObject(w, new Coord(pos.Y - 3, pos.X + 6), DungeonGenerationConfig.Objects.ObjGoldList);
            PlaceObject(w, new Coord(pos.Y + 4, pos.X + 4), 237);
            PlaceObject(w, new Coord(pos.Y - 4, pos.X + 2), 21);
        }
        if ((param & SNearItems) != 0)
        {
            PlaceObject(w, new Coord(py.Pos.Y - 1, py.Pos.X - 1), 237);
            PlaceObject(w, new Coord(py.Pos.Y + 1, py.Pos.X + 1), 268);
            PlaceObject(w, new Coord(py.Pos.Y, py.Pos.X - 1), 176);
        }
        if ((param & SUnknown) != 0)
        {
            // two stacks each of two unknown potion kinds -- dungeon-found and
            // store-bought don't stack -- for identify's merge (one kind with
            // the dungeon stack first, one with it second), and a cursed ring
            // for its "damned" inscription
            CarryObject(w, 243, 2, -1); // Constitution (potion)
            var bought = InventoryCatalog.CopyFromCatalog(243);
            w.Identification.ItemIdentifyAsStoreBought(ref bought);
            PlayerInventory.CarryItem(py, w.Identification, bought);
            bought = InventoryCatalog.CopyFromCatalog(251); // Dexterity (potion)
            w.Identification.ItemIdentifyAsStoreBought(ref bought);
            PlayerInventory.CarryItem(py, w.Identification, bought);
            CarryObject(w, 251, 2, -1);
            var ring = InventoryCatalog.CopyFromCatalog(140); // Resist Fire (ring)
            ring.Flags |= TreasureConfig.Flags.TrCursed;
            PlayerInventory.CarryItem(py, w.Identification, ring);
        }
        if ((param & SRubbleRow) != 0)
        {
            for (var x = py.Pos.X + 2; x <= py.Pos.X + 16; x++)
            {
                w.Objects.PlaceRubble(new Coord(py.Pos.Y, x));
            }
        }
        if ((param & SRubble) != 0)
        {
            w.Objects.PlaceRubble(new Coord(py.Pos.Y, py.Pos.X + 2));
            w.Objects.PlaceRubble(new Coord(py.Pos.Y - 2, py.Pos.X));
            w.Objects.PlaceRubble(new Coord(py.Pos.Y + 2, py.Pos.X));
        }
        if ((param & SMana) != 0)
        {
            // last: level gain recomputes mana
            py.Misc.Mana = 200;
            py.Misc.CurrentMana = 200;
        }
    }

    internal static void PlaceMonsters(PlayerWorld w, string spec)
    {
        foreach (var token in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
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
                    default:
                        throw new InvalidOperationException($"bad monster modifier in {token}");
                }
            }
        }
    }

    // ---- scripted input (mirrors the stub's key script) ---------------------

    internal const char Escape = (char)27;

    internal sealed class KeyScript
    {
        private string _keys = "";
        private int _pos;

        public int Used => _pos;
        public int Given => _keys.Length;

        public void Set(string keys)
        {
            _keys = keys;
            _pos = 0;
        }

        public void Clear() => Set("");

        public char Next()
        {
            if (_pos >= _keys.Length)
            {
                throw new InvalidOperationException("key script exhausted");
            }
            return _keys[_pos++];
        }
    }

    internal static void HookInput(PlayerWorld w, KeyScript keys, Func<bool> confirm)
    {
        w.ReadCommand = _ =>
        {
            var c = keys.Next();
            return c == Escape ? null : c;
        };

        // inventoryGetInputForItemId()'s key loop, for the lower-case
        // letters and <ESCAPE> the scripts use; anything outside the range
        // rings the bell and reads the next key.
        w.SelectItem = (_, start, end) =>
        {
            while (true)
            {
                var c = keys.Next();
                if (c == Escape)
                {
                    return null;
                }
                var id = c - 'a';
                if (id >= start && id <= end)
                {
                    return id;
                }
            }
        };

        w.Confirm = _ => confirm();
    }

    private static char FirstInRange(Player py, int lo, int hi, string token)
    {
        if (!InventoryDamage.FindRange(py, lo, hi, out var i, out _))
        {
            throw new InvalidOperationException($"token {token}: nothing in category range {lo}..{hi}");
        }
        return (char)('a' + i);
    }

    // Mirrors resolveKeys().
    private static string ResolveKeys(Player py, string token, int bookCategory)
    {
        var keys = new System.Text.StringBuilder();
        var k = 1;
        if (bookCategory != 0)
        {
            var book = token[k++];
            if (book == '~')
            {
                keys.Append(Escape);
            }
            else if (book >= '1' && book <= '4')
            {
                keys.Append((char)(FirstInRange(py, bookCategory, TreasureCategory.TvNever, token) + (book - '1')));
            }
            else if (book != '0')
            {
                throw new InvalidOperationException($"bad book key in {token}");
            }
        }
        for (; k < token.Length; k++)
        {
            var c = token[k];
            if (c == '~')
            {
                keys.Append(Escape);
            }
            else if (c == '&')
            {
                keys.Append(InventoryDamage.FindRange(py, TreasureCategory.TvStaff, TreasureCategory.TvWand, out var i, out _) ? (char)('a' + i) : 'a');
            }
            else
            {
                keys.Append(c);
            }
        }
        return keys.ToString();
    }

    // ---- running a scenario ----------------------------------------------

    private static void Run(Scenario s, List<string> o)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

        var w = BuildArena(py, identification, s.Depth);
        py.Pos = s.Pos;
        w.Dg.Floor[s.Pos.Y, s.Pos.X].CreatureId = 1;

        var keys = new KeyScript();
        var confirmAnswer = false;
        HookInput(w, keys, () => confirmAnswer);

        Setup(w, s.Setup, s.Exp);
        PlaceMonsters(w, s.Monsters);

        // generateCave()'s panel setup, then playDungeon()'s preamble.
        var dg = w.Dg;
        dg.Panel = new Panel();
        ScreenPanel.InitializeForLevel(dg);
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;
        DungeonLighting.ResetView(w.Py, dg, w.Treasure, w.Game.Options);

        o.Add($"scenario {s.Name}");
        var step = 0;
        DumpStep(o, w, keys, step, "start");

        foreach (var raw in s.Script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Dg.GenerateNewLevel)
            {
                break;
            }

            // `*N` repeats, but only with digits after it: '*' is also the
            // spell menu's list key
            var token = raw;
            var repeat = 1;
            var star = token.LastIndexOf('*');
            if (star >= 0 && star + 1 < token.Length && token[(star + 1)..].All(char.IsAsciiDigit))
            {
                repeat = int.Parse(token[(star + 1)..]);
                token = token[..star];
            }

            for (var i = 0; i < repeat && !w.Dg.GenerateNewLevel; i++)
            {
                RunToken(w, keys, token, ref confirmAnswer);
                DumpStep(o, w, keys, ++step, token);
            }
        }

        o.Add($"final {s.Name}");
        PlayerTrace.Dump(o, w.Py, identification);
    }

    private static void DirectSpell(PlayerWorld w, int n, int a, int b)
    {
        var pos = w.Py.Pos;

        switch (n)
        {
            case 1: DetectionSpells.DetectTreasureWithinVicinity(w); break;
            case 2: DetectionSpells.DetectObjectsWithinVicinity(w); break;
            case 3: DetectionSpells.DetectInvisibleCreaturesWithinVicinity(w); break;
            case 4: DetectionSpells.DarkenArea(w, pos); break;
            case 5: TerrainSpells.SurroundPlayerWithTraps(w); break;
            case 6: TerrainSpells.SurroundPlayerWithDoors(w); break;
            case 7: TerrainSpells.LightLine(w, pos, a); break;
            case 8: TerrainSpells.Starlite(w, pos); break;
            case 9: TerrainSpells.DisarmAllInDirection(w, pos, a); break;
            case 10: TerrainSpells.DestroyDoorsTrapsInDirection(w, pos, a); break;
            case 11: TerrainSpells.BuildWall(w, pos, a); break;
            case 12: MonsterStatusSpells.CloneMonster(w, pos, a); break;
            case 13: MonsterStatusSpells.MassGenocide(w); break;
            case 14: MonsterStatusSpells.SpeedAllMonsters(w, a); break;
            case 15: MonsterStatusSpells.MassPolymorph(w); break;
            case 16: SpellEffects.LoseStr(w); break;
            case 17: SpellEffects.LoseInt(w); break;
            case 18: SpellEffects.LoseWis(w); break;
            case 19: SpellEffects.LoseDex(w); break;
            case 20: SpellEffects.LoseCon(w); break;
            case 21: SpellEffects.LoseChr(w); break;
            case 22:
                var toHit = w.Py.Inventory[PlayerConfig.Equipment.Wield].ToHit;
                SpellEffects.EnchantItem(ref toHit, (short)a);
                w.Py.Inventory[PlayerConfig.Equipment.Wield].ToHit = toHit;
                break;
            case 23: SpellEffects.RemoveCurseFromAllWornItems(w); break;
            case 24: SpellEffects.RestorePlayerLevels(w); break;
            case 25: SpellEffects.ChangePlayerHitPoints(w, a); break;
            case 26: MonsterStatusSpells.SpeedMonster(w, pos, a, b); break;
            case 27: PlayerMagic.CureConfusion(w.Py); break;
            case 28: PlayerMagic.CureBlindness(w.Py); break;
            case 29: DetectionSpells.DetectEvil(w); break;
            case 30: DetectionSpells.DetectMonsters(w); break;
            case 31: TerrainSpells.DestroyArea(w, pos); break;
            case 32: DetectionSpells.LightArea(w, pos); break;
            case 33: TerrainSpells.WallToMud(w, pos, a); break;
            case 34: DetectionSpells.MapCurrentArea(w); break;
            case 35: MonsterStatusSpells.SleepAllMonsters(w); break;
            case 36: MonsterStatusSpells.TurnUndead(w); break;
            case 37: MonsterStatusSpells.TeleportAwayMonsterInDirection(w, pos, a); break;
            case 38: MonsterStatusSpells.PolymorphMonster(w, pos, a); break;
            case 39: MonsterStatusSpells.ConfuseMonster(w, pos, a); break;
            case 40: MonsterStatusSpells.SleepMonster(w, pos, a); break;
            case 41: TerrainSpells.CreateFood(w); break;
            case 42: TerrainSpells.WardingGlyph(w); break;
            case 43: TerrainSpells.DestroyAdjacentDoorsTraps(w); break;
            case 44: DetectionSpells.DetectTrapsWithinVicinity(w); break;
            case 45: DetectionSpells.DetectSecretDoorsWithinVicinity(w); break;
            case 46: SpellEffects.SlowPoison(w); break;
            default: throw new InvalidOperationException($"bad direct spell {n}");
        }
    }

    private static void RunToken(PlayerWorld w, KeyScript keys, string token, ref bool confirmAnswer)
    {
        var op = token[0];
        var py = w.Py;

        int[] Args() => token.Length > 1 ? token[1..].Split(':').Select(int.Parse).ToArray() : Array.Empty<int>();

        w.Game.PlayerFreeTurn = false;
        var takesTurn = true;
        keys.Clear();

        switch (op)
        {
            case 'w':
                break;
            case 'm':
                PlayerMovement.Move(w, Args()[0], true);
                break;
            case 'C':
                keys.Set(ResolveKeys(py, token, TreasureCategory.TvMagicBook));
                SpellCasting.GetAndCastMagicSpell(w);
                break;
            case 'P':
                keys.Set(ResolveKeys(py, token, TreasureCategory.TvPrayerBook));
                SpellCasting.Pray(w);
                break;
            case 'G':
                keys.Set(ResolveKeys(py, token, 0));
                PlayerSpells.GainSpells(w);
                break;
            case 'S':
                var args = Args();
                int Arg(int i) => i < args.Length ? args[i] : 0;
                DirectSpell(w, Arg(0), Arg(1), Arg(2));
                break;
            case 'y':
                confirmAnswer = true;
                takesTurn = false;
                break;
            case 'n':
                confirmAnswer = false;
                takesTurn = false;
                break;
            case 'M':
                py.Misc.CurrentMana = (short)Args()[0];
                takesTurn = false;
                break;
            case 'x':
                py.Flags.Blind = (short)Args()[0];
                py.Flags.Status |= PlayerConfig.Status.PyBlind;
                takesTurn = false;
                break;
            case 'f':
                py.Flags.Confused = (short)Args()[0];
                takesTurn = false;
                break;
            case 'U':
                MonsterAi.UpdateMonsters(w, false);
                takesTurn = false;
                break;
            case 'K':
                if (Args()[0] < py.Pack.UniqueItems)
                {
                    InventoryDamage.DestroyItem(py, Args()[0]);
                }
                takesTurn = false;
                break;
            case 'B':
                while (InventoryDamage.FindRange(py, TreasureCategory.TvMagicBook, TreasureCategory.TvPrayerBook, out var bi, out _))
                {
                    InventoryDamage.DestroyItem(py, bi);
                }
                takesTurn = false;
                break;
            case 'Q':
                if (InventoryDamage.FindRange(py, TreasureCategory.TvStaff, TreasureCategory.TvWand, out var qi, out _))
                {
                    py.Inventory[qi].MiscUse = (short)Args()[0];
                }
                takesTurn = false;
                break;
            case 'I':
                if (InventoryDamage.FindRange(py, TreasureCategory.TvStaff, TreasureCategory.TvWand, out var ii, out _))
                {
                    w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref py.Inventory[ii]);
                }
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

    internal static uint Mix(uint h, int v)
    {
        h ^= unchecked((uint)v);
        h *= 16777619u;
        return h;
    }

    internal const uint FnvOffset = 2166136261u;

    internal static uint HashFloor(Model.Generated.Dungeon dg)
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

    internal static uint HashItem(uint h, in Inventory item)
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

    internal static uint HashTreasure(TreasureList treasure)
    {
        var h = FnvOffset;
        for (var i = TreasureConfig.MinTreasureListId; i < treasure.CurrentId; i++)
        {
            h = HashItem(h, treasure[i]);
        }
        return h;
    }

    internal static uint HashInventory(Player py)
    {
        var h = FnvOffset;
        foreach (var item in py.Inventory)
        {
            h = HashItem(h, item);
        }
        return h;
    }

    internal static uint HashRecall(Recall[] recall)
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

    private static void DumpStep(List<string> o, PlayerWorld w, KeyScript keys, int step, string cmd)
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
            $"hp={m.CurrentHp}/{m.MaxHp} mana={m.CurrentMana}.{m.CurrentManaFraction}/{m.Mana} exp={m.Exp}.{m.ExpFraction}/{m.MaxExp} lvl={m.Level} au={m.Au} st={f.Status:x8} " +
            $"stats={used[PlayerConfig.Attr.Str]},{used[PlayerConfig.Attr.Int]},{used[PlayerConfig.Attr.Wis]},{used[PlayerConfig.Attr.Dex]},{used[PlayerConfig.Attr.Con]},{used[PlayerConfig.Attr.Chr]} " +
            $"para={f.Paralysis} blind={f.Blind} conf={f.Confused} slow={f.Slow} pois={f.Poisoned} afraid={f.Afraid} spd={f.Speed} " +
            $"gen={B(dg.GenerateNewLevel)} dead={B(game.CharacterIsDead)} win={B(game.TotalWinner)} " +
            $"tid={w.Treasure.CurrentId} mon={w.Monsters.NextFreeMonsterId} mult={w.Monsters.MonsterMultiplyTotal} " +
            $"pack={py.Pack.UniqueItems}/{py.Pack.Weight} light={py.Inventory[PlayerConfig.Equipment.Light].MiscUse} " +
            $"floor={HashFloor(dg):x8} treas={HashTreasure(w.Treasure):x8} inv={HashInventory(py):x8} recall={HashRecall(w.CreatureRecall):x8}");

        o.Add(
            $"spl keys={keys.Used}/{keys.Given} learnt={f.SpellsLearnt:x8} worked={f.SpellsWorked:x8} forgot={f.SpellsForgotten:x8} new={f.NewSpellsToLearn} " +
            $"fast={f.Fast} bless={f.Blessed} dinv={f.DetectInvisible} pevil={f.ProtectEvil} invuln={f.Invulnerability} hres={f.HeatResistance} cres={f.ColdResistance} " +
            $"prev={(int)py.PrevDir}");

        var line = new System.Text.StringBuilder("mons");
        for (var i = MonsterConfig.MonMinIndexId; i < w.Monsters.NextFreeMonsterId; i++)
        {
            var mon = w.Monsters.Monsters[i];
            line.Append($" {i}:{mon.CreatureId}@{mon.Pos.Y},{mon.Pos.X}/{mon.Hp}/{mon.SleepCount}/{mon.StunnedAmount}/{mon.ConfusedAmount}/{mon.Speed}/{mon.DistanceFromPlayer}/{B(mon.Lit)}");
        }
        o.Add(line.ToString());
    }
}
