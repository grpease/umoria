using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Persistence;
using Umoria.Core.Rng;
using Umoria.Core.Stores;
using Umoria.Core.Tests.CharCreate;
using Umoria.Core.Tests.Casting;

namespace Umoria.Core.Tests.PersistenceTraces;

// Real cross-check of M12 (Umoria.Core.Persistence: GameSave, HighScores,
// CharacterSheet; Items.ItemValue) against
// tools/persistence_dump/persistence_dump.cpp, run on the real compiled C++
// game (baseline: dotnet/tests/PersistenceTraces/persistence_dump.txt).
//
// The port's save and score files are JSON, so their bytes have no
// counterpart; what is compared is what the original defines around them --
// see the harness's header. Each section of the baseline has its own test;
// every method below mirrors the harness function of the same name.
public class PersistenceTraceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "umoria-persistence-" + Guid.NewGuid().ToString("N"));

    public PersistenceTraceTests()
    {
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        Directory.Delete(_workDir, true);
    }

    private static string[] Section(string firstPrefix, string? nextPrefix)
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PersistenceTraces", "persistence_dump.txt"));
        var start = Array.FindIndex(lines, l => l.StartsWith(firstPrefix, StringComparison.Ordinal));
        var end = nextPrefix is null ? lines.Length : Array.FindIndex(lines, l => l.StartsWith(nextPrefix, StringComparison.Ordinal));
        Assert.True(start >= 0 && end > start, $"baseline section {firstPrefix} not found");
        return lines[start..end];
    }

    // ---- value -------------------------------------------------------------

    [Fact]
    public void ItemValues_MatchCppBaseline()
    {
        var actual = new List<string>();
        DumpValues(actual);
        PlayerTrace.AssertMatches(Section("value.", "sheetcase"), actual, "value.");
    }

    private static void DumpValues(List<string> o)
    {
        MoriaRng.SetRandomSeed(4242);
        TreasureMagic.MissilesCounter = 0;

        var identification = new ObjectIdentification();
        void Value(int id, string variant, in Inventory item) =>
            o.Add($"value.{id}.{variant}\t{ItemValue.StoreItemValue(identification, item)}");

        for (var id = 0; id < DataTreasureData.GameObjects.Length; id++)
        {
            Array.Clear(identification.ObjectsIdentified);

            var item = InventoryCatalog.CopyFromCatalog(id);
            var count = item.ItemsCount;

            Value(id, "plain", item);
            item.ItemsCount = 5;
            Value(id, "five", item);
            item.ItemsCount = count;

            identification.ItemSetAsTried(item);
            Value(id, "tried", item);

            TreasureMagic.MagicalAbility(ref item, 40);
            Value(id, "magic", item);

            identification.ItemSetAsIdentified(item.CategoryId, item.SubCategoryId);
            Value(id, "known", item);

            identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
            Value(id, "known2", item);
            item.ItemsCount = 5;
            Value(id, "known2_five", item);
            item.ItemsCount = count;

            item.MiscUse = -3;
            Value(id, "negmisc", item);
            item.ToHit = -1;
            Value(id, "neghit", item);
            item.Identification |= IdentificationConfig.IdDamd;
            Value(id, "damd", item);
        }
    }

    // ---- sheet -------------------------------------------------------------

    [Fact]
    public void CharacterSheets_MatchCppBaseline()
    {
        var actual = new List<string>();
        DumpSheets(actual);
        PlayerTrace.AssertMatches(Section("sheetcase", "scorecase"), actual, "sheetcase");
    }

    private static void WearStartingKit(Player py)
    {
        py.EquipmentCount = 0;
        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            var item = py.Inventory[i];
            var slot = -1;
            if (item.CategoryId >= TreasureCategory.TvBow && item.CategoryId <= TreasureCategory.TvSword)
            {
                slot = PlayerConfig.Equipment.Wield;
            }
            else if (item.CategoryId == TreasureCategory.TvSoftArmor || item.CategoryId == TreasureCategory.TvHardArmor)
            {
                slot = PlayerConfig.Equipment.Body;
            }
            else if (item.CategoryId == TreasureCategory.TvCloak)
            {
                slot = PlayerConfig.Equipment.Outer;
            }
            else if (item.CategoryId == TreasureCategory.TvLight)
            {
                slot = PlayerConfig.Equipment.Light;
            }

            if (slot >= 0 && py.Inventory[slot].CategoryId == TreasureCategory.TvNothing)
            {
                py.Inventory[slot] = item with { ItemsCount = 1 };
                py.EquipmentCount++;
            }
        }
    }

    private static void ApplySheetVariant(PlayerWorld w, int variant)
    {
        var py = w.Py;
        switch (variant)
        {
            case 0:
                w.Dg.CurrentLevel = 0;
                break;
            case 1:
            {
                py.Misc.Level = 20;
                py.Misc.Exp = 5000;
                py.Misc.MaxExp = 6000;
                py.Misc.Au = 12345;
                py.Misc.MaxDungeonDepth = 7;
                w.Dg.CurrentLevel = 3;
                byte[] used = [3, 18, 19, 68, 117, 118];
                used.CopyTo(py.Stats.Used, 0);
                py.Flags.SeeInfra = 3;
                WearStartingKit(py);

                // the wielded weapon, made magical and identified
                var weapon = py.Inventory[PlayerConfig.Equipment.Wield];
                TreasureMagic.MagicalAbility(ref weapon, 30);
                w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref weapon);
                py.Inventory[PlayerConfig.Equipment.Wield] = weapon;

                py.Inventory[0].Inscription = "my snack";
                break;
            }
            case 2:
                py.Misc.Level = 40;
                py.EquipmentCount = 0;
                py.Pack.UniqueItems = 0;
                w.Dg.CurrentLevel = 10;
                break;
            default:
                py.Misc.Level = 45;
                py.MaxScore = 99999;
                py.Flags.SeeInfra = 5;
                py.Misc.Fos = 50;
                py.Misc.Bth = -100;
                py.Misc.StealthFactor = -2;
                py.Misc.Disarm = 200;
                w.Dg.CurrentLevel = 1;
                WearStartingKit(py);
                break;
        }
    }

    private static PlayerWorld WorldFor(Player py, ObjectIdentification identification, GameState? game = null)
    {
        var dg = new Model.Generated.Dungeon();
        return new PlayerWorld(py, dg, game ?? new GameState(), new MonsterManager(dg), new TreasureList(), identification);
    }

    private void DumpSheets(List<string> o)
    {
        var path = Path.Combine(_workDir, "sheet.txt");
        var log = new List<string>();

        void Messages()
        {
            foreach (var msg in log)
            {
                o.Add($"msg\t{msg.Replace(path, "sheet.txt")}");
            }

            log.Clear();
        }

        void SheetFile()
        {
            var lines = File.ReadAllText(path).Split('\n').ToList();
            if (lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            o.AddRange(lines.Select(l => $"sheet\t{l}"));
        }

        const uint seed = 12345;
        var caseIndex = 0;
        PlayerWorld? w = null;

        for (var raceId = 0; raceId < DataPlayerData.CharacterRaces.Length; raceId++)
        {
            var classCount = CharacterCreation.AvailableClasses(raceId).Count;
            foreach (var isMale in new[] { true, false })
            {
                for (var classChoice = 0; classChoice < classCount; classChoice++)
                {
                    var variant = caseIndex % 4;
                    caseIndex++;

                    var (py, identification) = PlayerTrace.CreateCharacter(seed, raceId, isMale, classChoice, 0);
                    identification.MagicInitializeItemNames(777);
                    w = WorldFor(py, identification);
                    w.MessageLog = log.Add;
                    MoriaRng.SetRandomSeed(seed + 1);
                    ApplySheetVariant(w, variant);

                    o.Add($"sheetcase race={raceId} male={(isMale ? 1 : 0)} class_choice={classChoice} variant={variant}");
                    o.Add($"points\t{HighScores.PlayerCalculateTotalPoints(w)}");

                    File.Delete(path);
                    var ok = CharacterSheet.OutputPlayerCharacterToFile(w, path);
                    o.Add($"ok\t{(ok ? 1 : 0)}");
                    Messages();
                    SheetFile();
                }
            }
        }

        // An existing file: refused, then replaced.
        o.Add("sheetcase replace");
        w!.Confirm = _ => false;
        o.Add($"ok\t{(CharacterSheet.OutputPlayerCharacterToFile(w, path) ? 1 : 0)}");
        Messages();
        w.Confirm = _ => true;
        o.Add($"ok\t{(CharacterSheet.OutputPlayerCharacterToFile(w, path) ? 1 : 0)}");
        Messages();
    }

    // ---- scores ------------------------------------------------------------

    [Fact]
    public void Scoreboard_MatchesCppBaseline()
    {
        var actual = new List<string>();
        DumpScores(actual);
        PlayerTrace.AssertMatches(Section("scorecase", "savecase"), actual, "scorecase");
    }

    private static List<(string Name, string[] Entries)> ParseScoreScenarios()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PersistenceTraces", "score_scenarios.inc");
        var scenarios = new List<(string, string[])>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("SCORE_SCENARIO(\"", StringComparison.Ordinal))
            {
                continue;
            }

            // SCORE_SCENARIO("name", "entries")
            var parts = line["SCORE_SCENARIO(\"".Length..line.LastIndexOf("\")", StringComparison.Ordinal)].Split("\", \"");
            scenarios.Add((parts[0], parts[1].Split('|')));
        }

        return scenarios;
    }

    private static void EnterScore(int k, string spec, string path, List<string> log)
    {
        // points:birth:gender:race:class:flags:died (died may hold ':')
        var f = spec.Split(':', 7);
        var points = int.Parse(f[0]);

        var py = new Player();
        var w = WorldFor(py, new ObjectIdentification());
        w.MessageLog = log.Add;

        w.Dg.CurrentLevel = k % 5;
        py.Misc.MaxExp = points - 50 * w.Dg.CurrentLevel;
        py.Misc.DateOfBirth = int.Parse(f[1]);
        py.Misc.Gender = f[2] == "M";
        py.Misc.RaceId = byte.Parse(f[3]);
        py.Misc.ClassId = byte.Parse(f[4]);
        py.Misc.Level = (ushort)(1 + k % 40);
        py.Misc.MaxHp = (short)(10 + k);
        py.Misc.CurrentHp = (short)(k % 3 == 0 ? -1 : 5 + k);
        py.Misc.MaxDungeonDepth = 0;
        py.Misc.Name = k % 4 == 3 ? "Supercalifragilistic22" : $"P{k}";

        w.Game.NoScore = (short)(f[5] == "n" ? 2 : 0);
        w.Game.PanicSave = f[5] == "p";
        w.Game.CharacterDiedFrom = f[6];

        HighScores.RecordNewHighScore(w, path);
    }

    private void DumpScores(List<string> o)
    {
        var path = Path.Combine(_workDir, "scores.json");
        var log = new List<string>();

        foreach (var (name, entries) in ParseScoreScenarios())
        {
            o.Add($"scorecase {name}");

            // An empty score file, as the game's installer leaves it.
            File.WriteAllText(path, "");

            for (var k = 0; k < entries.Length; k++)
            {
                EnterScore(k, entries[k], path, log);
            }

            o.AddRange(log.Select(m => $"msg\t{m}"));
            log.Clear();

            var scores = HighScores.ReadFile(path)?.Entries ?? [];
            foreach (var s in scores)
            {
                o.Add($"entry\tpoints={s.Points} birth={s.BirthDate} uid={s.Uid} mhp={s.Mhp} chp={s.Chp} depth={s.DungeonDepth} " +
                      $"level={s.Level} deepest={s.DeepestDungeonDepth} gender={(char)s.Gender} race={s.Race} class={s.CharacterClass} " +
                      $"name=[{s.Name}] died=[{s.DiedFrom}]");
            }

            // showScoresScreen()'s page layout, around HighScores.FormatScoreLine
            for (var page = 0; page * HighScores.ScoresPerPage < scores.Count; page++)
            {
                for (var i = 0; i < HighScores.ScoresPerPage && page * HighScores.ScoresPerPage + i < scores.Count; i++)
                {
                    var rank = page * HighScores.ScoresPerPage + i + 1;
                    o.Add($"screen\t{i + 2},0\t{HighScores.FormatScoreLine(rank, scores[rank - 1])}");
                }

                o.Add($"screen\t0,0\t{HighScores.ScoreScreenHeader}");
                o.Add("screen\t23,23\t[ press any key to continue ]");
            }
        }
    }

    // ---- save --------------------------------------------------------------

    [Fact]
    public void SaveLoadRoundTrip_MatchesCppBaseline()
    {
        var actual = new List<string>();
        DumpSaves(actual);
        PlayerTrace.AssertMatches(Section("savecase", null), actual, "savecase");
    }

    private sealed record SaveScenario(string Name, bool Dead, bool Eof, bool Winner, bool Panic, short NoScore, bool ToBeWizard, bool Confirm);

    private static readonly SaveScenario[] SaveScenarios =
    [
        new("alive", false, false, false, false, 0, false, false),
        new("dead", true, false, false, false, 0, false, false),
        new("hangup", true, true, false, false, 0, false, false),
        new("resurrect", true, false, false, false, 0, true, true),
        new("resurrect_declined", true, false, false, false, 0, true, false),
        new("retired", true, false, true, false, 0, true, true),
        new("winner_alive", false, false, true, false, 0, false, false),
        new("panic", false, false, false, true, 2, false, false),
        new("wizard_alive", false, false, false, false, 0, true, false),
    ];

    // What a fresh process holds before startMoria() calls loadGame().
    internal static PlayerWorld FreshWorld()
    {
        var py = new Player();
        PlayerStatLogic.InitializeBaseExperienceLevels(py);
        var dg = new Model.Generated.Dungeon { GameTurn = -1, GenerateNewLevel = true };
        TreasureMagic.MissilesCounter = 0;
        return new PlayerWorld(py, dg, new GameState(), new MonsterManager(dg), new TreasureList(), new ObjectIdentification());
    }

    internal static PlayerWorld BuildWorld(uint seed)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(seed, 1, true, 1, 0);

        TreasureMagic.MissilesCounter = 0;
        MoriaRng.SetRandomSeed(seed + 7);
        var dg = new Model.Generated.Dungeon { GameTurn = -1, GenerateNewLevel = true };
        var monsters = new MonsterManager(dg);
        var treasure = new TreasureList();
        py.Pos = new DungeonGenerator(dg, treasure, monsters).Generate(1);

        var w = new PlayerWorld(py, dg, new GameState(), monsters, treasure, identification);

        // the stores (M13): owners, two days of restocking, and counters
        StoreCommands.InitializeOwners(w);
        StoreInventory.Maintenance(w);
        StoreInventory.Maintenance(w);
        w.Stores[2].InsultsCounter = 3;
        w.Stores[3].TurnsLeftBeforeClosing = 4321;
        w.Stores[4].GoodPurchases = 7;
        w.Stores[5].BadPurchases = 2;
        w.Stores[1].Inventory[0].Cost = 55;

        py.Misc.DateOfBirth = 1700000000;
        py.Flags.Status |= PlayerConfig.Status.PySearch | PlayerConfig.Status.PyRest;
        py.Flags.Rest = 5;
        py.Pack.Heaviness = 3;
        py.WeaponIsHeavy = true;
        py.CarryingLight = true;
        py.Inventory[0].Inscription = "hello";

        var recall = w.CreatureRecall;
        recall[5].Kills = 3;
        recall[5].Attacks[2] = 4;
        recall[17].Movement = 0x123;
        recall[17].Spells = 0x80000001u;
        recall[278].Wake = 9;
        recall[278].Ignore = 2;

        var options = w.Game.Options;
        options.FindBound = true;
        options.RunCutCorners = false;
        options.UseRoguelikeKeys = true;
        options.DisplayCounts = false;
        options.HighlightSeams = true;

        w.Game.MagicSeed = seed;
        w.Game.TownSeed = seed + 8762;
        w.Game.CharacterGenerated = true;
        dg.GameTurn = 1234;
        w.Game.CharacterDiedFrom = "(saved)";

        return w;
    }

    private static void DumpWorld(List<string> o, PlayerWorld w, string label)
    {
        var py = w.Py;
        var dg = w.Dg;
        var game = w.Game;
        int B(bool b) => b ? 1 : 0;

        o.Add(label);
        PlayerTrace.Dump(o, py, w.Identification);
        o.Add($"pos\t{py.Pos.Y},{py.Pos.X}");
        o.Add($"max_score\t{py.MaxScore}");
        o.Add($"date_of_birth\t{py.Misc.DateOfBirth}");
        o.Add($"game\tmagic={game.MagicSeed} town={game.TownSeed} noscore={game.NoScore} panic={B(game.PanicSave)} " +
              $"winner={B(game.TotalWinner)} dead={B(game.CharacterIsDead)} generated={B(game.CharacterGenerated)} " +
              $"saved={B(game.CharacterSaved)} wizard={B(game.ToBeWizard)} died=[{game.CharacterDiedFrom}] " +
              $"missiles={TreasureMagic.MissilesCounter}");
        o.Add($"dg\tturn={dg.GameTurn} level={dg.CurrentLevel} height={dg.Height} width={dg.Width} " +
              $"max_rows={dg.Panel.MaxRows} max_cols={dg.Panel.MaxCols}");
        var op = game.Options;
        o.Add($"options\t{B(op.RunCutCorners)}{B(op.RunExamineCorners)}{B(op.RunPrintSelf)}{B(op.FindBound)}" +
              $"{B(op.PromptToPickup)}{B(op.UseRoguelikeKeys)}{B(op.ShowInventoryWeights)}{B(op.HighlightSeams)}" +
              $"{B(op.RunIgnoreDoors)}{B(op.ErrorBeepSound)}{B(op.DisplayCounts)}");
        o.Add($"floor\t{SpellCastingTraceTests.HashFloor(dg)}");

        o.Add($"treasures {w.Treasure.CurrentId - TreasureConfig.MinTreasureListId}");
        for (var i = TreasureConfig.MinTreasureListId; i < w.Treasure.CurrentId; i++)
        {
            var item = w.Treasure[i];
            o.Add($"treasure {i} id={item.Id} sn={item.SpecialNameId} insc=[{item.Inscription}] flags={item.Flags} " +
                  $"cat={item.CategoryId} sprite={item.Sprite} misc={item.MiscUse} cost={item.Cost} sub={item.SubCategoryId} " +
                  $"count={item.ItemsCount} weight={item.Weight} to_hit={item.ToHit} to_dam={item.ToDamage} ac={item.Ac} " +
                  $"to_ac={item.ToAc} dice={item.Damage.Count}x{item.Damage.Sides} depth={item.DepthFirstFound} " +
                  $"ident={item.Identification}");
        }

        for (var s = 0; s < w.Stores.Length; s++)
        {
            var store = w.Stores[s];
            o.Add($"store {s} own={store.OwnerId} ins={store.InsultsCounter} close={store.TurnsLeftBeforeClosing} " +
                  $"n={store.UniqueItemsCounter} good={store.GoodPurchases} bad={store.BadPurchases}");
            for (var i = 0; i < store.UniqueItemsCounter; i++)
            {
                var item = store.Inventory[i].Item;
                o.Add($"stock {s}.{i} cost={store.Inventory[i].Cost} id={item.Id} sn={item.SpecialNameId} insc=[{item.Inscription}] " +
                      $"flags={item.Flags} misc={item.MiscUse} count={item.ItemsCount} to_hit={item.ToHit} to_dam={item.ToDamage} " +
                      $"to_ac={item.ToAc} ident={item.Identification}");
            }
        }

        o.Add($"monsters {w.Monsters.NextFreeMonsterId - MonsterConfig.MonMinIndexId} multiply={w.Monsters.MonsterMultiplyTotal}");
        for (var i = MonsterConfig.MonMinIndexId; i < w.Monsters.NextFreeMonsterId; i++)
        {
            var m = w.Monsters.Monsters[i];
            o.Add($"monster {i} {m.CreatureId} {m.Hp} {m.SleepCount} {m.Pos.Y} {m.Pos.X} {m.Speed} " +
                  $"{m.DistanceFromPlayer} {B(m.Lit)} {m.StunnedAmount} {m.ConfusedAmount}");
        }

        for (var i = 0; i < w.CreatureRecall.Length; i++)
        {
            var r = w.CreatureRecall[i];
            if (r.Movement != 0 || r.Spells != 0 || r.Kills != 0 || r.Deaths != 0 || r.Defenses != 0 || r.Wake != 0 ||
                r.Ignore != 0 || r.Attacks.Any(a => a != 0))
            {
                o.Add($"recall {i} {r.Movement} {r.Spells} {r.Kills} {r.Deaths} {r.Defenses} {r.Wake} {r.Ignore} " +
                      $"{r.Attacks[0]} {r.Attacks[1]} {r.Attacks[2]} {r.Attacks[3]}");
            }
        }
    }

    private void DumpSaves(List<string> o)
    {
        var path = Path.Combine(_workDir, "game.sav");
        var log = new List<string>();

        void Messages()
        {
            o.AddRange(log.Select(m => $"msg\t{m}"));
            log.Clear();
        }

        var seed = 4001u;
        foreach (var scenario in SaveScenarios)
        {
            o.Add($"savecase {scenario.Name}");
            var w = BuildWorld(seed++);
            w.MessageLog = log.Add;
            var py = w.Py;

            w.Game.CharacterIsDead = scenario.Dead;
            w.Game.TotalWinner = scenario.Winner;
            w.Game.PanicSave = scenario.Panic;
            w.Game.NoScore = scenario.NoScore;
            w.Game.EofFlag = scenario.Eof ? 1 : 0;
            if (scenario.Dead)
            {
                w.Game.CharacterDiedFrom = "a Kobold";
                py.Misc.CurrentHp = -5;
                py.Flags.Food = -10;
                py.Flags.Poisoned = 7;
            }

            DumpWorld(o, w, "before");

            File.Delete(path);
            var saved = new GameSave(path).SaveGame(w);
            o.Add($"saved\t{(saved ? 1 : 0)} speed={py.Flags.Speed} rest={py.Flags.Rest} status={py.Flags.Status} " +
                  $"heaviness={py.Pack.Heaviness} turn={w.Dg.GameTurn} saved={(w.Game.CharacterSaved ? 1 : 0)} " +
                  $"dead={(w.Game.CharacterIsDead ? 1 : 0)} rnd={MoriaRng.GetRandomSeed()}");
            Messages();

            w = FreshWorld();
            w.MessageLog = log.Add;
            w.Game.ToBeWizard = scenario.ToBeWizard;
            w.Confirm = _ => scenario.Confirm;

            var result = new GameSave(path).LoadGame(w);
            o.Add($"loaded\t{(result.Outcome == LoadOutcome.Restored ? 1 : 0)} generate={(result.Generate ? 1 : 0)}");
            Messages();

            DumpWorld(o, w, "after");
        }
    }
}
