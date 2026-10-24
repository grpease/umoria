using System.Text;
using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Stores;
using Umoria.Core.Tests.CharCreate;
using Arena = Umoria.Core.Tests.Casting.SpellCastingTraceTests;

namespace Umoria.Core.Tests.StoreTrace;

// Real cross-check of M13's stores (Umoria.Core.Stores.StoreInventory /
// StoreCommands) against tools/store_dump/store_dump.cpp, run on the real
// compiled C++ game (baseline: dotnet/tests/StoreTraces/store_dump.txt).
// Three sections, each its own test: twelve days of restocking from four
// seeds, the sell-price table, and scripted store visits. The scenarios are
// not copied here: both sides read tools/store_dump/store_scenarios.inc.
// The arena and hashes are the M10 trace's (SpellCastingTraceTests).
public class StoreTraceTests
{
    private static string[] Baseline() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "StoreTraces", "store_dump.txt"));

    private static string[] Section(string[] lines, string first, string? next)
    {
        var start = Array.FindIndex(lines, l => l.StartsWith(first, StringComparison.Ordinal));
        var end = next is null ? lines.Length : Array.FindIndex(lines, l => l.StartsWith(next, StringComparison.Ordinal));
        return lines[start..end];
    }

    [Fact]
    public void Maintenance_MatchesCppBaseline()
    {
        var expected = Section(Baseline(), "maint ", "price ");
        var actual = new List<string>();

        foreach (var seed in new uint[] { 1, 4242, 31337, 987654321 })
        {
            var py = new Player();
            py.Misc.RaceId = (byte)(seed % 8);
            var identification = new ObjectIdentification();
            identification.MagicInitializeItemNames(seed);
            var dg = new Model.Generated.Dungeon();
            var w = new PlayerWorld(py, dg, new GameState(), new MonsterManager(dg), new TreasureList(), identification);
            TreasureMagic.MissilesCounter = 0;

            MoriaRng.SetRandomSeed(seed);
            StoreCommands.InitializeOwners(w);

            actual.Add($"maint {seed} owners rnd={MoriaRng.GetRandomSeed()}");
            for (var s = 0; s < StoreConfig.MaxStores; s++)
            {
                DumpStore(actual, w, s);
            }

            for (var day = 1; day <= 12; day++)
            {
                StoreInventory.Maintenance(w);
                actual.Add($"maint {seed} day {day} rnd={MoriaRng.GetRandomSeed()} tid={w.Treasure.CurrentId}");
                for (var s = 0; s < StoreConfig.MaxStores; s++)
                {
                    DumpStore(actual, w, s);
                }
            }
        }

        PlayerTrace.AssertMatches(expected, actual, "maint ");
    }

    [Fact]
    public void SellPrices_MatchCppBaseline()
    {
        var expected = Section(Baseline(), "price ", "scenario ");
        var actual = new List<string>();

        var py = new Player();
        var dg = new Model.Generated.Dungeon();
        var w = new PlayerWorld(py, dg, new GameState(), new MonsterManager(dg), new TreasureList(), new ObjectIdentification());

        for (var owner = 0; owner < StoreConfig.MaxOwners; owner++)
        {
            py.Misc.RaceId = (byte)(owner % 8);
            var store = new Store { OwnerId = (byte)owner };

            actual.Add($"price owner {owner} race {py.Misc.RaceId}");
            for (var id = 0; id < GameConstants.MaxObjectsInGame; id++)
            {
                var item = InventoryCatalog.CopyFromCatalog(id);
                var price = StoreInventory.SellPrice(w, store, out var minPrice, out var maxPrice, item);
                actual.Add($"price {owner}.{id} {price} {minPrice} {maxPrice}");
            }
        }

        PlayerTrace.AssertMatches(expected, actual, "price owner ");
    }

    [Fact]
    public void StoreVisits_MatchCppBaseline()
    {
        var expected = Section(Baseline(), "scenario ", null);
        var scenarios = ParseScenarios(Path.Combine(AppContext.BaseDirectory, "StoreTraces", "store_scenarios.inc"));

        var actual = new List<string>();
        foreach (var scenario in scenarios)
        {
            Run(scenario, actual);
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // ---- dumps (mirror dumpStore() and hashStore()) ------------------------

    private static uint HashStore(Store store)
    {
        var h = Arena.FnvOffset;
        h = Arena.Mix(h, store.TurnsLeftBeforeClosing);
        h = Arena.Mix(h, store.InsultsCounter);
        h = Arena.Mix(h, store.OwnerId);
        h = Arena.Mix(h, store.UniqueItemsCounter);
        h = Arena.Mix(h, store.GoodPurchases);
        h = Arena.Mix(h, store.BadPurchases);
        foreach (var record in store.Inventory)
        {
            h = Arena.Mix(h, record.Cost);
            h = Arena.HashItem(h, record.Item);
        }
        return h;
    }

    private static void DumpStore(List<string> o, PlayerWorld w, int storeId)
    {
        var store = w.Stores[storeId];
        o.Add($"store {storeId} own={store.OwnerId} ins={store.InsultsCounter} close={store.TurnsLeftBeforeClosing} n={store.UniqueItemsCounter} " +
              $"good={store.GoodPurchases} bad={store.BadPurchases} hash={HashStore(store):x8}");
        for (var i = 0; i < store.UniqueItemsCounter; i++)
        {
            var item = store.Inventory[i].Item;
            var description = ItemDescription.Describe(w.Identification, item, true);
            o.Add($"item {storeId}.{i} cost={store.Inventory[i].Cost} id={item.Id} n={item.ItemsCount} ident={item.Identification} misc={item.MiscUse} " +
                  $"plus={item.ToHit},{item.ToDamage},{item.ToAc} flags={item.Flags:x8} sn={item.SpecialNameId} | {description}");
        }
    }

    // ---- the shared scenario file ------------------------------------------

    private sealed record Scenario(string Name, uint Seed, int RaceId, bool IsMale, int ClassChoice, int Gold, string Script);

    private static List<Scenario> ParseScenarios(string path)
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
            if (fields.Count != 7)
            {
                throw new InvalidOperationException($"bad scenario line: {line}");
            }

            scenarios.Add(new Scenario(
                fields[0].Trim('"'),
                uint.Parse(fields[1]),
                int.Parse(fields[2]),
                bool.Parse(fields[3]),
                int.Parse(fields[4]),
                int.Parse(fields[5]),
                fields[6].Trim('"')));
        }

        return scenarios;
    }

    // splits on commas outside string literals (scripts hold "O0,1:...")
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

    // ---- running a scenario ----------------------------------------------

    private sealed class Inputs
    {
        public readonly List<string?> Lines = [];
        public int LinesUsed;
        public bool ScreenOn = true;
        public readonly List<string> Messages = [];
        public readonly List<string> Screen = [];
        public readonly uint[] StoreHashes = new uint[StoreConfig.MaxStores];
    }

    private static void Run(Scenario s, List<string> o)
    {
        var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

        // startMoria(): magicInitializeItemNames() right after creation
        identification.MagicInitializeItemNames(s.Seed ^ 0x5a5a5a5au);
        TreasureMagic.MissilesCounter = 0;

        // the town: owners, then generateCave()'s storeMaintenance()
        var w = Arena.BuildArena(py, identification, 0);
        py.Pos = new Coord(11, 20);
        w.Dg.Floor[11, 20].CreatureId = 1;
        w.Dg.GameTurn = 100;
        StoreCommands.InitializeOwners(w);
        StoreInventory.Maintenance(w);
        py.Misc.Au = s.Gold;

        var keys = new Arena.KeyScript();
        var inputs = new Inputs();
        Arena.HookInput(w, keys, () => false);

        // inventoryGetInputForItemId()'s key loop hands back the letter
        // typed; GetItemId refuses one outside the range
        w.SelectItem = (_, _, _) =>
        {
            var c = keys.Next();
            return c == Arena.Escape ? null : c - 'a';
        };

        // the stub's scripted getStringInput() lines: trailing blanks
        // removed, cut to the field length
        w.ReadString = (_, maxLength) =>
        {
            if (inputs.LinesUsed >= inputs.Lines.Count)
            {
                throw new InvalidOperationException("string script exhausted");
            }
            var text = inputs.Lines[inputs.LinesUsed++];
            if (text is null)
            {
                return null;
            }
            text = text.TrimEnd(' ');
            return text.Length > maxLength ? text[..maxLength] : text;
        };

        w.MessageLog = inputs.Messages.Add;
        w.ScreenWrite = (row, col, text, clearToEol) => inputs.Screen.Add(clearToEol ? $"{row},{col}\t{text}" : $"{row},{col}=\t{text}");

        o.Add($"scenario {s.Name}");
        var step = 0;
        DumpStep(o, w, keys, inputs, step, "start");

        foreach (var token in s.Script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            RunToken(w, keys, inputs, token);
            DumpStep(o, w, keys, inputs, ++step, token);
        }

        o.Add($"final {s.Name}");
        PlayerTrace.Dump(o, w.Py, identification);
    }

    private static int FirstPackSlotOf(Player py, int id, string token)
    {
        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (py.Inventory[i].Id == id)
            {
                return i;
            }
        }
        throw new InvalidOperationException($"token {token}: object {id} not in the pack");
    }

    // Mirrors resolveKeys().
    private static string ResolveKeys(Player py, string token, int start, int end)
    {
        var keys = new StringBuilder();
        for (var k = start; k < end; k++)
        {
            var c = token[k];
            if (c == '~')
            {
                keys.Append(Arena.Escape);
            }
            else if (c == '%')
            {
                var dot = token.IndexOf('.', k);
                keys.Append((char)('a' + FirstPackSlotOf(py, int.Parse(token[(k + 1)..dot]), token)));
                k = dot;
            }
            else
            {
                keys.Append(c);
            }
        }
        return keys.ToString();
    }

    // Mirrors withInput(): keys up to the first '/', then the lines.
    private static void WithInput(PlayerWorld w, Arena.KeyScript keys, Inputs inputs, string token, int start)
    {
        var slash = token.IndexOf('/', start);
        keys.Set(ResolveKeys(w.Py, token, start, slash < 0 ? token.Length : slash));

        inputs.Lines.Clear();
        inputs.LinesUsed = 0;
        if (slash >= 0)
        {
            foreach (var line in token[(slash + 1)..].Split('/'))
            {
                inputs.Lines.Add(line == "~" ? null : line.Replace('_', ' '));
            }
        }
    }

    private static void RunToken(PlayerWorld w, Arena.KeyScript keys, Inputs inputs, string token)
    {
        var py = w.Py;
        var op = token[0];

        int[] Args() => token[1..].Split(':', ',').Select(int.Parse).ToArray();

        w.Game.PlayerFreeTurn = false;
        keys.Clear();
        inputs.Lines.Clear();
        inputs.LinesUsed = 0;

        switch (op)
        {
            case 'S':
            {
                var colon = token.IndexOf(':');
                WithInput(w, keys, inputs, token, colon + 1);
                StoreCommands.Enter(w, int.Parse(token[1..colon]));
                break;
            }
            case 'm':
            {
                var colon = token.IndexOf(':');
                WithInput(w, keys, inputs, token, colon + 1);
                PlayerMovement.Move(w, int.Parse(token[1..colon]), true);
                break;
            }
            case 'M':
                StoreInventory.Maintenance(w);
                break;
            case 'G':
                py.Misc.Au = Args()[0];
                break;
            case 'C':
                py.Stats.Max[PlayerConfig.Attr.Chr] = (byte)Args()[0];
                py.Stats.Current[PlayerConfig.Attr.Chr] = (byte)Args()[0];
                PlayerStatLogic.SetAndUseStat(py, PlayerConfig.Attr.Chr);
                break;
            case 'T':
                w.Dg.GameTurn = Args()[0];
                break;
            case 'B':
            {
                var a = Args();
                w.Stores[a[0]].GoodPurchases = (ushort)a[1];
                w.Stores[a[0]].BadPurchases = (ushort)a[2];
                break;
            }
            case 'N':
            {
                var a = Args();
                w.Stores[a[0]].InsultsCounter = (short)a[1];
                break;
            }
            case 'P':
            {
                var a = Args();
                Arena.CarryObject(w, a[0], a[1], a.Length > 2 ? (short)a[2] : (short)-1);
                break;
            }
            case 'Q':
            {
                var a = Args();
                var item = InventoryCatalog.CopyFromCatalog(a[0]) with { ItemsCount = (byte)a[1] };
                w.Identification.ItemIdentifyAsStoreBought(ref item);
                PlayerInventory.CarryItem(py, w.Identification, item);
                break;
            }
            case 'H':
            {
                var a = Args();
                ref var item = ref py.Inventory[FirstPackSlotOf(py, a[0], token)];
                item.ToHit = (short)a[1];
                item.ToDamage = (short)a[2];
                item.ToAc = (short)a[3];
                break;
            }
            case 'D':
                w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref py.Inventory[FirstPackSlotOf(py, Args()[0], token)]);
                break;
            case 'K':
                if (Args()[0] < py.Pack.UniqueItems)
                {
                    InventoryDamage.DestroyItem(py, Args()[0]);
                }
                break;
            case 'A':
            {
                var a = Args();
                var item = InventoryCatalog.CopyFromCatalog(a[1]) with { ItemsCount = (byte)(a.Length > 2 ? a[2] : 1) };
                w.Identification.ItemIdentifyAsStoreBought(ref item);
                StoreInventory.CarryItem(w, a[0], out _, item);
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
                break;
            }
            case 'O':
            {
                var a = Args();
                Arena.PlaceObject(w, new Coord(py.Pos.Y + a[0], py.Pos.X + a[1]), a[2]);
                break;
            }
            case 'E':
                while (w.Stores[Args()[0]].UniqueItemsCounter > 0)
                {
                    StoreInventory.DestroyItem(w, Args()[0], 0, false);
                }
                break;
            case 'V':
                inputs.ScreenOn = token[1] == '1';
                break;
            default:
                throw new InvalidOperationException($"bad script token: {token}");
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
        int B(bool b) => b ? 1 : 0;

        o.Add(
            $"step {step} {cmd} rnd={MoriaRng.GetRandomSeed()} turn={w.Dg.GameTurn} au={py.Misc.Au} free={B(w.Game.PlayerFreeTurn)} cmd={w.Game.CommandCount} " +
            $"keys={keys.Used}/{keys.Given} strs={inputs.LinesUsed}/{inputs.Lines.Count} " +
            $"chr={py.Stats.Used[PlayerConfig.Attr.Chr]}/{PlayerStatLogic.StatAdjustmentCharisma(py)} pos={py.Pos.Y},{py.Pos.X} " +
            $"pack={py.Pack.UniqueItems}/{py.Pack.Weight} eq={py.EquipmentCount} heavy={B(py.WeaponIsHeavy)} tid={w.Treasure.CurrentId} " +
            $"inv={Arena.HashInventory(py):x8} ident={HashIdentified(w.Identification):x8} st={py.Flags.Status:x8}");

        for (var s = 0; s < StoreConfig.MaxStores; s++)
        {
            var h = HashStore(w.Stores[s]);
            if (h != inputs.StoreHashes[s])
            {
                inputs.StoreHashes[s] = h;
                DumpStore(o, w, s);
            }
        }

        foreach (var message in inputs.Messages)
        {
            o.Add($"msg {message}");
        }
        inputs.Messages.Clear();

        if (inputs.ScreenOn)
        {
            foreach (var line in inputs.Screen)
            {
                o.Add($"scr {line}");
            }
        }
        inputs.Screen.Clear();
    }
}
