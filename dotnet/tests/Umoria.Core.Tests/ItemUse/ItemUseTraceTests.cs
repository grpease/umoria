using System.Text;
using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Tests.Casting;
using Umoria.Core.Tests.CharCreate;
using Arena = Umoria.Core.Tests.Casting.SpellCastingTraceTests;

namespace Umoria.Core.Tests.ItemUse;

// Real cross-check of M11's item commands (Umoria.Core.Items.PlayerEat /
// PlayerQuaff / Scrolls / Staves / PlayerThrow / InventoryCommands /
// InventoryActions, with ItemDescription in context) against
// tools/item_use_dump/item_use_dump.cpp, run on the real compiled C++ game
// (baseline: dotnet/tests/ItemUseTraces/item_use_dump.txt).
//
// The scenarios are not copied here: both sides read
// tools/item_use_dump/item_use_scenarios.inc (the C++ side #includes it,
// this side parses it). The arena, setup bits, monster specs, key script and
// hashes are the M10 trace's (SpellCastingTraceTests), reused. On top of
// M10's checks this compares every printMessage() text (PlayerWorld.MessageLog
// here, the stub's log there), a line of item-related state per step, and
// the description of every carried item at the end.
public class ItemUseTraceTests
{
    internal sealed record Scenario(
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
    public void ItemUse_MatchesCppBaseline()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ItemUseTraces", "item_use_dump.txt"));
        var scenarios = ParseScenarios(Path.Combine(AppContext.BaseDirectory, "ItemUseTraces", "item_use_scenarios.inc"));

        // A true process-lifetime static in the original (0 at start-up);
        // other tests in this process move it.
        TreasureMagic.MissilesCounter = 0;

        var actual = new List<string>();
        foreach (var scenario in scenarios)
        {
            Run(scenario, actual);
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // ---- the shared scenario file ------------------------------------------

    private static readonly Dictionary<string, int> Symbols = new()
    {
        ["T"] = Arena.STough,
        ["S_TOUGH"] = Arena.STough,
        ["S_DARK"] = Arena.SDark,
        ["S_BLIND"] = Arena.SBlind,
        ["S_HURT"] = Arena.SHurt,
        ["S_BOOKS"] = Arena.SBooks,
        ["S_LEARN"] = Arena.SLearn,
        ["S_MANA"] = Arena.SMana,
        ["S_STAFF"] = Arena.SStaff,
        ["S_TRAPS"] = Arena.STraps,
        ["S_CURSED"] = Arena.SCursed,
        ["S_AILING"] = Arena.SAiling,
        ["S_DRAINED"] = Arena.SDrained,
        ["S_SUSTAIN"] = Arena.SSustain,
        ["S_RUBBLE"] = Arena.SRubble,
        ["S_WEAPON"] = Arena.SWeapon,
        ["S_ITEMS"] = Arena.SItems,
        ["S_NEAR_ITEMS"] = Arena.SNearItems,
        ["S_UNKNOWN"] = Arena.SUnknown,
        ["S_RUBBLE_ROW"] = Arena.SRubbleRow,
    };

    private static readonly Dictionary<string, Coord> Positions = new()
    {
        ["P"] = new Coord(11, 20),
        ["DARK"] = new Coord(11, 120),
        ["GEN"] = new Coord(-1, -1), // M14's UI scenarios: a generated level
    };

    // (also parses M14's ui_screen_scenarios.inc, which has the same fields)
    internal static List<Scenario> ParseScenarios(string path)
    {
        var scenarios = new List<Scenario>();

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith("{\"", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = SplitFields(line[1..line.LastIndexOf('}')]);
            if (fields.Count != 11)
            {
                throw new InvalidOperationException($"bad scenario line: {line}");
            }

            scenarios.Add(new Scenario(
                Unquote(fields[0]),
                uint.Parse(fields[1]),
                int.Parse(fields[2]),
                bool.Parse(fields[3]),
                int.Parse(fields[4]),
                Positions[fields[5]],
                int.Parse(fields[6]),
                fields[7].Split('|').Select(t => t.Trim()).Aggregate(0, (bits, t) => bits | (Symbols.TryGetValue(t, out var v) ? v : int.Parse(t))),
                int.Parse(fields[8]),
                Unquote(fields[9]),
                Unquote(fields[10])));
        }

        return scenarios;
    }

    // splits on commas outside string literals (the scripts hold none, but
    // stay safe)
    private static List<string> SplitFields(string text)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inString = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inString = !inString;
            }

            if (c == ',' && !inString)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }
        fields.Add(current.ToString().Trim());

        return fields;
    }

    private static string Unquote(string field) => field.Trim('"');

    // ---- running a scenario ----------------------------------------------

    private sealed class Inputs
    {
        public bool Confirm;
        public int ConfirmWithAbort;
        public string Name = "";
        public readonly List<string> Messages = new();
    }

    private static void Run(Scenario s, List<string> o)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

        // startMoria(): magicInitializeItemNames() right after creation
        identification.MagicInitializeItemNames(s.Seed ^ 0x5a5a5a5au);

        var w = Arena.BuildArena(py, identification, s.Depth);
        py.Pos = s.Pos;
        w.Dg.Floor[s.Pos.Y, s.Pos.X].CreatureId = 1;

        var keys = new Arena.KeyScript();
        var inputs = new Inputs();
        Arena.HookInput(w, keys, () => inputs.Confirm);
        w.ConfirmWithAbort = _ => inputs.ConfirmWithAbort;

        // the stub's getStringInput(): the scripted text, trailing blanks
        // removed, cut to the field length
        w.ReadString = (_, maxLength) =>
        {
            var text = inputs.Name.TrimEnd(' ');
            return text.Length > maxLength ? text[..maxLength] : text;
        };

        Arena.Setup(w, s.Setup, s.Exp);
        Arena.PlaceMonsters(w, s.Monsters);

        // generateCave()'s panel setup, then playDungeon()'s preamble.
        var dg = w.Dg;
        dg.Panel = new Panel();
        ScreenPanel.InitializeForLevel(dg);
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;
        DungeonLighting.ResetView(w.Py, dg, w.Treasure, w.Game.Options);

        // (logging starts here, as the C++ side clears its log here)
        w.MessageLog = inputs.Messages.Add;

        o.Add($"scenario {s.Name}");
        var step = 0;
        DumpStep(o, w, keys, inputs, step, "start");

        foreach (var raw in s.Script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Dg.GenerateNewLevel)
            {
                break;
            }

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
                RunToken(w, keys, inputs, token);
                DumpStep(o, w, keys, inputs, ++step, token);
            }
        }

        o.Add($"final {s.Name}");
        PlayerTrace.Dump(o, w.Py, identification);

        for (var i = 0; i < PlayerConfig.PlayerInventorySize; i++)
        {
            if (py.Inventory[i].CategoryId != TreasureCategory.TvNothing)
            {
                o.Add($"desc {i} {ItemDescription.Describe(identification, py.Inventory[i], true)}");
            }
        }
    }

    // Mirrors resolveKeys().
    private static string ResolveKeys(Player py, string token, int start)
    {
        var keys = new StringBuilder();
        for (var k = start; k < token.Length; k++)
        {
            var c = token[k];
            if (c == '~')
            {
                keys.Append(Arena.Escape);
            }
            else if (c == '%')
            {
                var end = token.IndexOf('.', k);
                var id = int.Parse(token[(k + 1)..end]);
                var slot = -1;
                for (var i = 0; i < py.Pack.UniqueItems; i++)
                {
                    if (py.Inventory[i].Id == id)
                    {
                        slot = i;
                        break;
                    }
                }
                if (slot < 0)
                {
                    throw new InvalidOperationException($"token {token}: object {id} not in the pack");
                }
                keys.Append((char)('a' + slot));
                k = end;
            }
            else
            {
                keys.Append(c);
            }
        }
        return keys.ToString();
    }

    private static void RunToken(PlayerWorld w, Arena.KeyScript keys, Inputs inputs, string token)
    {
        var op = token[0];
        var py = w.Py;

        int[] Args() => token[1..].Split(':', ',').Select(int.Parse).ToArray();

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
            case 'e':
                keys.Set(ResolveKeys(py, token, 1));
                PlayerEat.Eat(w);
                break;
            case 'q':
                keys.Set(ResolveKeys(py, token, 1));
                PlayerQuaff.Quaff(w);
                break;
            case 'r':
                keys.Set(ResolveKeys(py, token, 1));
                Scrolls.Read(w);
                break;
            case 'u':
                keys.Set(ResolveKeys(py, token, 1));
                Staves.StaffUse(w);
                break;
            case 'a':
                keys.Set(ResolveKeys(py, token, 1));
                Staves.WandAim(w);
                break;
            case 't':
                keys.Set(ResolveKeys(py, token, 1));
                PlayerThrow.ThrowItem(w);
                break;
            case 'I':
                w.Game.DoingInventoryCommand = (char)0;
                keys.Set(ResolveKeys(py, token, 2));
                InventoryCommands.ExecuteCommand(w, token[1]);
                break;
            case '{':
                var colon = token.IndexOf(':');
                keys.Set(ResolveKeys(py, token[..colon], 1));
                inputs.Name = token[(colon + 1)..];
                InventoryCommands.ItemInscribe(w);
                takesTurn = false;
                break;
            case 'y':
                inputs.Confirm = true;
                takesTurn = false;
                break;
            case 'n':
                inputs.Confirm = false;
                takesTurn = false;
                break;
            case 'A':
                inputs.ConfirmWithAbort = Args()[0];
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
            case 'F':
                py.Flags.Food = (short)Args()[0];
                takesTurn = false;
                break;
            case 'H':
                py.Misc.CurrentHp = (short)Args()[0];
                takesTurn = false;
                break;
            case 'M':
                py.Misc.CurrentMana = (short)Args()[0];
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
            case 'P':
            {
                var a = Args();
                Arena.CarryObject(w, a[0], a[1], a.Length > 2 ? (short)a[2] : (short)-1);
                takesTurn = false;
                break;
            }
            case 'G':
            {
                var a = Args();
                var item = InventoryCatalog.CopyFromCatalog(a[0]);
                TreasureMagic.MagicalAbility(ref item, a[1]);
                PlayerInventory.CarryItem(py, w.Identification, item);
                takesTurn = false;
                break;
            }
            case 'X':
            {
                var a = Args();
                if (py.Inventory[a[1]].CategoryId == TreasureCategory.TvNothing)
                {
                    py.EquipmentCount++;
                }
                py.Inventory[a[1]] = InventoryCatalog.CopyFromCatalog(a[0]);
                PlayerBonuses.RecalculateBonuses(py);
                takesTurn = false;
                break;
            }
            case 'Z':
                py.Inventory[Args()[0]].Flags |= TreasureConfig.Flags.TrCursed;
                takesTurn = false;
                break;
            case 'D':
                for (var i = 0; i < py.Pack.UniqueItems; i++)
                {
                    if (py.Inventory[i].Id == Args()[0])
                    {
                        w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref py.Inventory[i]);
                        break;
                    }
                }
                takesTurn = false;
                break;
            case 'O':
            {
                var a = Args();
                Arena.PlaceObject(w, new Coord(py.Pos.Y + a[0], py.Pos.X + a[1]), a[2]);
                takesTurn = false;
                break;
            }
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

    // ---- dump (mirrors dumpStep()) -----------------------------------------

    private static uint HashIdentified(ObjectIdentification identification)
    {
        var h = Arena.FnvOffset;
        foreach (var flag in identification.ObjectsIdentified)
        {
            h = Arena.Mix(h, flag);
        }
        return h;
    }

    private static void DumpStep(List<string> o, PlayerWorld w, Arena.KeyScript keys, Inputs inputs, int step, string cmd)
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
            $"floor={Arena.HashFloor(dg):x8} treas={Arena.HashTreasure(w.Treasure):x8} inv={Arena.HashInventory(py):x8} recall={Arena.HashRecall(w.CreatureRecall):x8}");

        o.Add(
            $"itm keys={keys.Used}/{keys.Given} food={f.Food} image={f.Image} fast={f.Fast} hero={f.Heroism} shero={f.SuperHeroism} invuln={f.Invulnerability} " +
            $"recall={f.WordOfRecall} infra={f.TimedInfra} cmon={B(f.ConfuseMonster)} hres={f.HeatResistance} cres={f.ColdResistance} dinv={f.DetectInvisible} " +
            $"bless={f.Blessed} pevil={f.ProtectEvil} heavy={B(py.WeaponIsHeavy)} eq={py.EquipmentCount} ac={m.Ac}/{m.MagicalAc} hit={m.PlussesToHit} dam={m.PlussesToDamage} " +
            $"screen={(int)game.Screen.CurrentScreenId} wear={game.Screen.WearLowId}-{game.Screen.WearHighId} doing={(int)game.DoingInventoryCommand} prev={(int)py.PrevDir} " +
            $"ident={HashIdentified(w.Identification):x8} died={game.CharacterDiedFrom}");

        var line = new StringBuilder("mons");
        for (var i = MonsterConfig.MonMinIndexId; i < w.Monsters.NextFreeMonsterId; i++)
        {
            var mon = w.Monsters.Monsters[i];
            line.Append($" {i}:{mon.CreatureId}@{mon.Pos.Y},{mon.Pos.X}/{mon.Hp}/{mon.SleepCount}/{mon.StunnedAmount}/{mon.ConfusedAmount}/{mon.Speed}/{mon.DistanceFromPlayer}/{B(mon.Lit)}");
        }
        o.Add(line.ToString());

        foreach (var message in inputs.Messages)
        {
            o.Add($"msg {message}");
        }
        inputs.Messages.Clear();
    }
}
