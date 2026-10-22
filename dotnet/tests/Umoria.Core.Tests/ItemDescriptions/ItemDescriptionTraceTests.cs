using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Tests.ItemDescriptions;

// Real cross-check of M11's flavour-name shuffle
// (ObjectIdentification.MagicInitializeItemNames) and the full
// itemDescription() port (ItemDescription.Describe) against
// tools/item_description_dump/item_description_dump.cpp, run on the real
// compiled C++ game (baseline:
// dotnet/tests/ItemDescriptionTraces/item_description_dump.txt).
//
// Same shape as ItemMagicTraceTests: no dungeon, every catalog row, a fixed
// series of identification states per row. One ObjectIdentification is kept
// across the three seeds because the original shuffles its global tables in
// place, so each seed's shuffle starts from the previous one's result.
public class ItemDescriptionTraceTests
{
    private static readonly uint[] Seeds = [12345, 31337, 777];

    [Fact]
    public void Descriptions_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ItemDescriptionTraces", "item_description_dump.txt");
        var expected = File.ReadAllLines(path);
        var actual = new List<string>();

        var identification = new ObjectIdentification();

        foreach (var seed in Seeds)
        {
            MoriaRng.SetRandomSeed(seed + 1);
            identification.MagicInitializeItemNames(seed);

            actual.Add($"{seed}.rnd\t{MoriaRng.GetRandomSeed()}");

            DumpTable(actual, seed, "colors", identification.Colors);
            DumpTable(actual, seed, "mushrooms", identification.Mushrooms);
            DumpTable(actual, seed, "woods", identification.Woods);
            DumpTable(actual, seed, "metals", identification.Metals);
            DumpTable(actual, seed, "rocks", identification.Rocks);
            DumpTable(actual, seed, "amulets", identification.Amulets);
            DumpTable(actual, seed, "titles", identification.MagicItemTitles);

            TreasureMagic.MissilesCounter = 0;
            DescribeAll(actual, identification, seed);
        }

        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i] == actual[i], $"line {i + 1}:\n  expected: {expected[i]}\n  actual:   {actual[i]}");
        }
    }

    private static void DumpTable(List<string> lines, uint seed, string name, string[] table)
    {
        for (var i = 0; i < table.Length; i++)
        {
            lines.Add($"{seed}.{name}.{i}\t{table[i]}");
        }
    }

    // Mirrors item_description_dump.cpp's describeAll() step by step.
    private static void DescribeAll(List<string> lines, ObjectIdentification identification, uint seed)
    {
        void Describe(int id, string variant, in Inventory item, bool addPrefix) =>
            lines.Add($"{seed}.{id}.{variant}\t{ItemDescription.Describe(identification, item, addPrefix)}");

        for (var id = 0; id < DataTreasureData.GameObjects.Length; id++)
        {
            Array.Clear(identification.ObjectsIdentified);

            var item = InventoryCatalog.CopyFromCatalog(id);

            Describe(id, "plain", item, true);
            Describe(id, "plain_np", item, false);

            var count = item.ItemsCount;
            item.ItemsCount = 0;
            Describe(id, "none", item, true);
            Describe(id, "none_np", item, false);
            item.ItemsCount = 5;
            Describe(id, "five", item, true);
            Describe(id, "five_np", item, false);
            item.ItemsCount = count;

            identification.ItemSetAsTried(item);
            Describe(id, "tried", item, true);

            var bought = item;
            identification.ItemIdentifyAsStoreBought(ref bought);
            Describe(id, "bought", bought, true);

            TreasureMagic.MagicalAbility(ref item, 40);
            Describe(id, "magic", item, true);

            identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
            Describe(id, "known2", item, true);

            identification.ItemSetAsIdentified(item.CategoryId, item.SubCategoryId);
            Describe(id, "known", item, true);
            Describe(id, "known_np", item, false);

            item.ItemsCount = 0;
            Describe(id, "known_none", item, true);
            item.ItemsCount = count;

            item.Inscription = "hello";
            item.Identification |= IdentificationConfig.IdMagik | IdentificationConfig.IdEmpty | IdentificationConfig.IdDamd;
            Describe(id, "inscribed", item, true);

            item.Inscription = "";
            Describe(id, "flagged", item, true);

            item.Identification = IdentificationConfig.IdKnown2 | IdentificationConfig.IdShowHitDam;
            Describe(id, "showhitdam", item, true);
            item.Identification = IdentificationConfig.IdKnown2 | IdentificationConfig.IdNoShowP1;
            Describe(id, "noshowp1", item, true);
            item.Identification = IdentificationConfig.IdKnown2 | IdentificationConfig.IdShowP1;
            Describe(id, "showp1", item, true);
            item.Identification = IdentificationConfig.IdShowP1;
            Describe(id, "showp1_unknown", item, true);
        }
    }
}
