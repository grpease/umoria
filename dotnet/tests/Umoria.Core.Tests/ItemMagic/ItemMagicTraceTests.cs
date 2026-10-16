using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Tests.ItemMagic;

// Real cross-check: compares Umoria.Core.Dungeon.TreasureMagic (the M3 port
// of magicTreasureMagicalAbility() and its helpers, src/treasure.cpp)
// against a text dump captured from the real, compiled C++ game via
// tools/item_magic_dump/item_magic_dump.cpp (built through the MSVC+vcpkg
// toolchain -- see dotnet/tests/ItemMagicTraces/item_magic_dump.txt for the
// checked-in baseline).
//
// No dungeon needs to run for this: magicTreasureMagicalAbility() only
// needs a single item_id/level pair, so both sides just call it directly
// for every row of the static game-object catalog (420 rows), across a
// handful of fixed seeds and levels. That exercises every TV_* branch,
// since the real catalog already covers every category/sub_category
// combination the game ships with.
//
// Each side flattens every (seed, level, catalog row) combination to
// "ItemMagic.<seed>_<level>_<catalog_id>.<field><TAB>value" lines (see
// item_magic_dump.cpp's comment for the exact format); this test rebuilds
// that same flattening in C# and diffs the two dictionaries.
public class ItemMagicTraceTests
{
    private static readonly uint[] Seeds = [1, 42, 12345];
    private static readonly int[] Levels = [1, 5, 10, 20, 40];

    [Fact]
    public void MagicalAbility_MatchesCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ItemMagicTraces", "item_magic_dump.txt");
        var expected = ParseBaseline(path);
        var actual = new Dictionary<string, string>();

        TreasureMagic.MissilesCounter = 0;

        foreach (var seed in Seeds)
        {
            foreach (var level in Levels)
            {
                MoriaRng.SetRandomSeed(seed);

                for (var catalogId = 0; catalogId < DataTreasureData.GameObjects.Length; catalogId++)
                {
                    var item = CopyFromCatalog(catalogId);
                    TreasureMagic.MagicalAbility(ref item, level);

                    var row = $"{seed}_{level}_{catalogId}";
                    AddItem(actual, row, item);
                }
            }
        }

        actual["ItemMagic.missiles_counter.value"] = TreasureMagic.MissilesCounter.ToString();

        AssertMatches(expected, actual);
    }

    // Mirrors inventoryItemCopyTo() (src/inventory.cpp:336): builds a fresh
    // item instance from a read-only catalog row, the same way the real
    // game does before handing it to magicTreasureMagicalAbility().
    private static Inventory CopyFromCatalog(int catalogId)
    {
        var from = DataTreasureData.GameObjects[catalogId];

        return new Inventory(
            Id: (ushort)catalogId,
            SpecialNameId: 0,
            Inscription: "",
            Flags: from.Flags,
            CategoryId: from.CategoryId,
            Sprite: from.Sprite,
            MiscUse: from.MiscUse,
            Cost: from.Cost,
            SubCategoryId: from.SubCategoryId,
            ItemsCount: from.ItemsCount,
            Weight: from.Weight,
            ToHit: from.ToHit,
            ToDamage: from.ToDamage,
            Ac: from.Ac,
            ToAc: from.ToAc,
            Damage: from.Damage,
            DepthFirstFound: from.DepthFirstFound,
            Identification: 0);
    }

    // Mirrors item_magic_dump.cpp's dump_item() -- deliberately omits
    // fields magicTreasureMagicalAbility() never touches (id, sprite,
    // inscription, ac, damage), since M0's DataTableTraceTests already
    // verifies those against the catalog they're copied from.
    private static void AddItem(Dictionary<string, string> d, string row, Inventory item)
    {
        d[$"ItemMagic.{row}.special_name_id"] = item.SpecialNameId.ToString();
        d[$"ItemMagic.{row}.flags"] = item.Flags.ToString();
        d[$"ItemMagic.{row}.category_id"] = item.CategoryId.ToString();
        d[$"ItemMagic.{row}.misc_use"] = item.MiscUse.ToString();
        d[$"ItemMagic.{row}.cost"] = item.Cost.ToString();
        d[$"ItemMagic.{row}.sub_category_id"] = item.SubCategoryId.ToString();
        d[$"ItemMagic.{row}.items_count"] = item.ItemsCount.ToString();
        d[$"ItemMagic.{row}.weight"] = item.Weight.ToString();
        d[$"ItemMagic.{row}.to_hit"] = item.ToHit.ToString();
        d[$"ItemMagic.{row}.to_damage"] = item.ToDamage.ToString();
        d[$"ItemMagic.{row}.to_ac"] = item.ToAc.ToString();
        d[$"ItemMagic.{row}.depth_first_found"] = item.DepthFirstFound.ToString();
        d[$"ItemMagic.{row}.identification"] = item.Identification.ToString();
    }

    // ---- baseline parsing + comparison (same as DataTableTraceTests) ----

    private static Dictionary<string, string> ParseBaseline(string path)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(path))
        {
            var tab = line.IndexOf('\t');
            result[line[..tab]] = line[(tab + 1)..];
        }

        return result;
    }

    private static void AssertMatches(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        var mismatches = new List<string>();

        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
            {
                mismatches.Add($"{key}: missing from C# port (expected {expectedValue})");
            }
            else if (actualValue != expectedValue)
            {
                mismatches.Add($"{key}: expected {expectedValue}, got {actualValue}");
            }

            if (mismatches.Count >= 40)
            {
                break;
            }
        }

        if (mismatches.Count < 40)
        {
            foreach (var key in actual.Keys)
            {
                if (!expected.ContainsKey(key))
                {
                    mismatches.Add($"{key}: present in C# port but not in C++ baseline");
                    if (mismatches.Count >= 40)
                    {
                        break;
                    }
                }
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"{mismatches.Count} mismatch(es) (showing up to 40) out of {expected.Count} fields:\n" + string.Join("\n", mismatches));
    }
}
