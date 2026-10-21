using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Tests.CharCreate;

namespace Umoria.Core.Tests.StatusEffects;

// Real cross-check: compares Umoria.Core.Character.PlayerStatusEffects (the
// M7 port of game_run.cpp's per-turn player status-effect updates) against a
// text dump captured from the real, compiled C++ game via
// tools/status_effects_dump/status_effects_dump.cpp (built through the
// MSVC+vcpkg toolchain -- see
// dotnet/tests/StatusEffectsTraces/status_effects_dump.txt for the
// checked-in baseline).
//
// The C++ side #includes the unmodified game_run.cpp to reach its static
// status functions; both sides run the same scenarios: a real character
// (M6 creation), a small hand-built test level with no monsters, a
// scenario setup, playDungeon()'s level-entry preamble, then N ticks of the
// status-effect portion of playDungeon()'s loop body, with optional
// per-tick "pokes". One state line per tick (including the RNG state and
// light-flag tile counts), and the full player at the end.
public class StatusEffectsTraceTests
{
    private static readonly Coord PosRoom = new(5, 10);
    private static readonly Coord PosDoorway = new(9, 12);
    private static readonly Coord PosDark = new(15, 40);

    private sealed record Scenario(
        string Name,
        uint Seed,
        int RaceId,
        bool IsMale,
        int ClassChoice,
        Coord Pos,
        int Level,
        int MaxTicks,
        Action<PlayerWorld> Setup,
        Action<PlayerWorld, int>? Poke = null);

    [Fact]
    public void StatusEffects_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "StatusEffectsTraces", "status_effects_dump.txt");
        var expected = File.ReadAllLines(path);

        var actual = new List<string>();
        foreach (var scenario in Scenarios())
        {
            Run(scenario, actual);
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // Same order and values as status_effects_dump.cpp's main().
    private static IEnumerable<Scenario> Scenarios()
    {
        yield return new Scenario("timed_effects", 12345, 0, true, 0, PosRoom, 1, 45, SetupTimedEffects, PokeTimedEffects);
        yield return new Scenario("starvation", 987654321, 3, false, 0, PosDark, 2, 300, SetupStarvation);
        yield return new Scenario("rest_regen", 31337, 0, false, 1, PosDoorway, 1, 400, SetupRestRegen, PokeRestRegen);
        yield return new Scenario("light_source", 12345, 6, false, 0, PosDark, 1, 75, SetupLightSource, PokeLightSource);
        yield return new Scenario("recall_down", 31337, 0, true, 2, PosRoom, 0, 20, SetupRecallDown);
        yield return new Scenario("recall_up", 987654321, 5, false, 0, PosRoom, 5, 20, SetupRecallUp);
        yield return new Scenario("detect_enchantment", 987654321, 1, true, 0, PosRoom, 0, 1200, SetupDetectEnchantment);
        yield return new Scenario("message_interrupt", 12345, 0, false, 3, PosDoorway, 1, 14, SetupMessageInterrupt, PokeMessageInterrupt);

        foreach (var con in new byte[] { 3, 4, 5, 6, 10, 17, 50, 100, 118 })
        {
            yield return new Scenario($"poison_con_{con}", 31337, 7, true, 0, PosRoom, 3, 16, w => SetupPoison(w, con, false));
        }
        yield return new Scenario("poison_invulnerable", 31337, 7, true, 0, PosRoom, 3, 16, w => SetupPoison(w, 10, true));
    }

    private static void Run(Scenario s, List<string> o)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

        var dg = BuildTestLevel();
        var w = new PlayerWorld(py, dg, new GameState(), new MonsterManager(dg), new TreasureList());
        py.Pos = s.Pos;
        dg.CurrentLevel = s.Level;

        s.Setup(w);
        PlayerStatusEffects.EnterLevel(w);

        o.Add($"scenario {s.Name}");
        DumpTick(o, w, 0);

        for (var t = 1; t <= s.MaxTicks && !dg.GenerateNewLevel; t++)
        {
            s.Poke?.Invoke(w, t);
            Tick(w);
            DumpTick(o, w, t);
        }

        o.Add($"final {s.Name}");
        PlayerTrace.Dump(o, py, identification);
    }

    // status_effects_dump.cpp's tick(): the status-effect portion of one
    // playDungeon() loop iteration.
    private static void Tick(PlayerWorld w)
    {
        w.Dg.GameTurn++;

        PlayerStatusEffects.UpdateTimedEffects(w);

        Assert.False(w.Py.Flags.Teleport, "scenario reached random teleportation (M8) -- not covered");

        PlayerStatusEffects.UpdateStatusUpkeep(w);
    }

    // Mirrors buildTestLevel(): granite everywhere, an open unlit area, a
    // perma-lit room with a wall ring and a dark doorway.
    private static Model.Generated.Dungeon BuildTestLevel()
    {
        var dg = new Model.Generated.Dungeon
        {
            Height = DungeonConstants.ScreenHeight,
            Width = DungeonConstants.ScreenWidth,
        };

        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                dg.Floor[y, x] = new Tile { FeatureId = TileConstants.TileGraniteWall };
            }
        }

        for (var y = 1; y <= 20; y++)
        {
            for (var x = 1; x <= 64; x++)
            {
                dg.Floor[y, x].FeatureId = TileConstants.TileDarkFloor;
            }
        }

        for (var y = 2; y <= 9; y++)
        {
            for (var x = 3; x <= 21; x++)
            {
                var ring = y == 2 || y == 9 || x == 3 || x == 21;
                dg.Floor[y, x].PermaLitRoom = true;
                dg.Floor[y, x].FeatureId = ring ? TileConstants.TileGraniteWall : TileConstants.TileLightFloor;
            }
        }

        dg.Floor[PosDoorway.Y, PosDoorway.X].FeatureId = TileConstants.TileDarkFloor;

        return dg;
    }

    // ---- scenario setups / pokes (mirrors of the C++ ones) --------------

    private static int EquipCopyOfFirst(Player py, int lo, int hi, int slot)
    {
        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (py.Inventory[i].CategoryId >= lo && py.Inventory[i].CategoryId <= hi)
            {
                py.Inventory[slot] = py.Inventory[i] with { ItemsCount = 1 };
                py.EquipmentCount++;
                return slot;
            }
        }

        throw new InvalidOperationException($"scenario setup: no pack item in category {lo}..{hi}");
    }

    private static void SetupTimedEffects(PlayerWorld w)
    {
        var f = w.Py.Flags;
        EquipCopyOfFirst(w.Py, TreasureCategory.TvHafted, TreasureCategory.TvSword, PlayerConfig.Equipment.Wield);
        f.Heroism = 6;
        f.SuperHeroism = 4;
        f.Blind = 5;
        f.Confused = 7;
        f.Poisoned = 12;
        f.Fast = 5;
        f.Slow = 8;
        f.Paralysis = 3;
        f.ProtectEvil = 5;
        f.Invulnerability = 6;
        f.Blessed = 7;
        f.HeatResistance = 3;
        f.ColdResistance = 4;
        f.DetectInvisible = 5;
        f.TimedInfra = 6;
    }

    private static void PokeTimedEffects(PlayerWorld w, int tick)
    {
        var py = w.Py;
        switch (tick)
        {
            case 2:
                // Only while blind -- see the C++ pokeTimedEffects() comment.
                py.Flags.Image = 3;
                break;
            case 10:
                py.Stats.Used[PlayerConfig.Attr.Str] = 3;
                py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
                break;
            case 15:
                py.Flags.Afraid = 6;
                break;
            case 18:
                py.Flags.Heroism = 3;
                break;
            case 25:
                py.Flags.Rest = 5;
                py.Flags.Status |= PlayerConfig.Status.PyRest;
                break;
            case 27:
                py.Flags.Confused = 2;
                break;
            case 32:
                py.Stats.Used[PlayerConfig.Attr.Str] = 18;
                py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
                break;
        }
    }

    private static void SetupStarvation(PlayerWorld w)
    {
        var py = w.Py;
        py.Flags.Food = 2010;
        py.Flags.FoodDigested = 15;
        py.Flags.Fast = 40;
        py.Misc.CurrentHp = (short)(py.Misc.MaxHp / 2);
    }

    private static void SetupRestRegen(PlayerWorld w)
    {
        var py = w.Py;
        py.Misc.CurrentHp = 1;
        py.Misc.Mana = 6;
        py.Misc.CurrentMana = 0;
        py.Misc.CurrentManaFraction = 0;
        py.Flags.Rest = -short.MaxValue;
        py.Flags.Status |= PlayerConfig.Status.PyRest;
        py.Flags.FoodDigested--;
        py.Flags.RegenerateHp = true;
    }

    private static void PokeRestRegen(PlayerWorld w, int tick)
    {
        var py = w.Py;
        switch (tick)
        {
            case 60:
                PlayerCore.SearchOn(w);
                break;
            case 90:
                py.Flags.Poisoned = 4;
                break;
            case 140:
                py.Flags.Rest = 20;
                py.Flags.Status |= PlayerConfig.Status.PyRest;
                break;
        }
    }

    private static void SetupLightSource(PlayerWorld w)
    {
        var slot = EquipCopyOfFirst(w.Py, TreasureCategory.TvLight, TreasureCategory.TvLight, PlayerConfig.Equipment.Light);
        w.Py.Inventory[slot].MiscUse = 45;
    }

    private static void PokeLightSource(PlayerWorld w, int tick)
    {
        var py = w.Py;
        switch (tick)
        {
            case 30:
                py.RunningTracker = 1;
                break;
            case 50:
                py.Flags.Blind = 3;
                break;
            case 60:
                py.Inventory[PlayerConfig.Equipment.Light].MiscUse = 2;
                break;
        }
    }

    private static void SetupRecallDown(PlayerWorld w)
    {
        w.Py.Flags.WordOfRecall = 4;
        w.Py.Misc.MaxDungeonDepth = 7;
    }

    private static void SetupRecallUp(PlayerWorld w)
    {
        w.Py.Flags.WordOfRecall = 3;
    }

    private static void SetupPoison(PlayerWorld w, byte con, bool invulnerable)
    {
        w.Py.Flags.Poisoned = 14;
        w.Py.Stats.Used[PlayerConfig.Attr.Con] = con;
        if (invulnerable)
        {
            w.Py.Flags.Invulnerability = 20;
        }
    }

    private static void SetupDetectEnchantment(PlayerWorld w)
    {
        var py = w.Py;
        py.Misc.Level = 40;
        py.Flags.Food = 15000;

        // Starting kit is ID_KNOWN2|ID_STORE_BOUGHT, which itemEnchanted()
        // rejects -- clear it so the detection roll can actually hit.
        var slot = EquipCopyOfFirst(py, TreasureCategory.TvHafted, TreasureCategory.TvSword, PlayerConfig.Equipment.Wield);
        py.Inventory[slot].ToHit = 3;
        py.Inventory[slot].Identification = 0;
        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (py.Inventory[i].CategoryId >= TreasureCategory.TvMinEnchant && py.Inventory[i].CategoryId <= TreasureCategory.TvMaxEnchant)
            {
                py.Inventory[i].ToAc = 2;
                py.Inventory[i].Identification = 0;
                break;
            }
        }
    }

    private static void SetupMessageInterrupt(PlayerWorld w)
    {
        w.Py.Flags.ProtectEvil = 3;
        w.Py.Flags.HeatResistance = 5;
        w.Py.Flags.ColdResistance = 7;
    }

    private static void PokeMessageInterrupt(PlayerWorld w, int tick)
    {
        switch (tick)
        {
            case 2:
                w.Game.CommandCount = 9;
                PlayerCore.SearchOn(w); // movement state with a count -> PY_REPEAT
                break;
            case 8:
                w.Game.CommandCount = 3;
                break;
            case 9:
                w.Py.Flags.Rest = 2;
                w.Py.Flags.Status |= PlayerConfig.Status.PyRest;
                break;
        }
    }

    // ---- per-tick dump (mirrors dumpTick()) -----------------------------

    private static void DumpTick(List<string> o, PlayerWorld w, int tick)
    {
        var py = w.Py;
        var m = py.Misc;
        var f = py.Flags;
        var dg = w.Dg;
        var game = w.Game;
        int B(bool b) => b ? 1 : 0;

        o.Add(
            $"tick {tick} turn={dg.GameTurn} rnd={MoriaRng.GetRandomSeed()} st={f.Status:x8} " +
            $"hp={m.CurrentHp}.{m.CurrentHpFraction}/{m.MaxHp} mana={m.CurrentMana}.{m.CurrentManaFraction}/{m.Mana} " +
            $"food={f.Food} dig={f.FoodDigested} spd={f.Speed} rest={f.Rest} " +
            $"blind={f.Blind} conf={f.Confused} afraid={f.Afraid} pois={f.Poisoned} fast={f.Fast} slow={f.Slow} " +
            $"image={f.Image} para={f.Paralysis} pevil={f.ProtectEvil} invuln={f.Invulnerability} " +
            $"hero={f.Heroism} shero={f.SuperHeroism} bless={f.Blessed} heat={f.HeatResistance} cold={f.ColdResistance} " +
            $"detinv={f.DetectInvisible} seeinv={B(f.SeeInvisible)} infra={f.SeeInfra} tinfra={f.TimedInfra} recall={f.WordOfRecall} " +
            $"bth={m.Bth} bthb={m.BthWithBows} ac={m.Ac} dac={m.DisplayAc} depth={dg.CurrentLevel} " +
            $"maxdepth={m.MaxDungeonDepth} gen={B(dg.GenerateNewLevel)} dead={B(game.CharacterIsDead)} " +
            $"died=[{game.CharacterDiedFrom}] light={py.Inventory[PlayerConfig.Equipment.Light].MiscUse} " +
            $"carry={B(py.CarryingLight)} tlo={B(py.TemporaryLightOnly)} heavy={B(py.WeaponIsHeavy)} " +
            $"heaviness={py.Pack.Heaviness} cmd={game.CommandCount} run={py.RunningTracker} " +
            $"tlit={CountTiles(dg, t => t.TemporaryLight)} plit={CountTiles(dg, t => t.PermanentLight)} " +
            $"fm={CountTiles(dg, t => t.FieldMark)}");
    }

    private static int CountTiles(Model.Generated.Dungeon dg, Func<Tile, bool> flag)
    {
        var count = 0;
        foreach (var tile in dg.Floor)
        {
            if (flag(tile))
            {
                count++;
            }
        }
        return count;
    }
}
