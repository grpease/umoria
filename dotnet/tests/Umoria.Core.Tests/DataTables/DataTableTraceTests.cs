using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Tests.DataTables;

// Real cross-check: compares the mechanically-extracted C# data tables
// (dotnet/src/Umoria.Core/Data/Generated) against a text dump captured from
// the real, compiled C++ game via tools/data_table_dump/data_table_dump.cpp
// (built through the MSVC+vcpkg toolchain -- see
// dotnet/tests/DataTableTraces/data_table_dump.txt for the checked-in
// baseline and golden_trace_harness's own comment for how that toolchain is
// set up).
//
// This is deliberately independent of
// dotnet/tools/CppModelExtractor/extract_data_tables.py: that tool also
// parses these same six src/data_*.cpp files (with libclang) to produce the
// C# tables below, so comparing against its own parse would only prove the
// extractor agrees with itself. The C++ dump instead comes from the real
// MSVC compiler and linker, independent of the extractor entirely.
//
// Each side flattens every row to "table.row.field<TAB>value" lines (see
// data_table_dump.cpp's comment for the exact format); this test rebuilds
// that same flattening from the generated C# tables and diffs the two
// dictionaries.
public class DataTableTraceTests
{
    [Fact]
    public void GeneratedTables_MatchCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "DataTableTraces", "data_table_dump.txt");
        var expected = ParseBaseline(path);
        var actual = new Dictionary<string, string>();

        AddArray(actual, "CreaturesList", DataCreaturesData.CreaturesList, AddCreature);
        AddArray(actual, "MonsterAttacks", DataCreaturesData.MonsterAttacks, AddMonsterAttack);

        AddArray(actual, "GameObjects", DataTreasureData.GameObjects, AddDungeonObject);
        AddStringArray(actual, "SpecialItemNames", DataTreasureData.SpecialItemNames);

        AddJagged2D(actual, "ClassRankTitles", DataPlayerData.ClassRankTitles);
        AddArray(actual, "CharacterRaces", DataPlayerData.CharacterRaces, AddRace);
        AddArray(actual, "CharacterBackgrounds", DataPlayerData.CharacterBackgrounds, AddBackground);
        AddArray(actual, "Classes", DataPlayerData.Classes, AddClass);
        AddNumeric2D(actual, "ClassLevelAdj", DataPlayerData.ClassLevelAdj);
        AddSpell2D(actual, "MagicSpells", DataPlayerData.MagicSpells);
        AddStringArray(actual, "SpellNames", DataPlayerData.SpellNames);
        AddNumeric2D(actual, "ClassBaseProvisions", DataPlayerData.ClassBaseProvisions);

        AddStringArray(actual, "RecallDescriptionAttackType", DataRecallData.RecallDescriptionAttackType);
        AddStringArray(actual, "RecallDescriptionAttackMethod", DataRecallData.RecallDescriptionAttackMethod);
        AddStringArray(actual, "RecallDescriptionHowMuch", DataRecallData.RecallDescriptionHowMuch);
        AddStringArray(actual, "RecallDescriptionMove", DataRecallData.RecallDescriptionMove);
        AddStringArray(actual, "RecallDescriptionSpell", DataRecallData.RecallDescriptionSpell);
        AddStringArray(actual, "RecallDescriptionBreath", DataRecallData.RecallDescriptionBreath);
        AddStringArray(actual, "RecallDescriptionWeakness", DataRecallData.RecallDescriptionWeakness);

        AddArray(actual, "StoreOwners", DataStoreOwnersData.StoreOwners, AddStoreOwner);
        AddStringArray(actual, "SpeechSaleAccepted", DataStoreOwnersData.SpeechSaleAccepted);
        AddStringArray(actual, "SpeechSellingHaggleFinal", DataStoreOwnersData.SpeechSellingHaggleFinal);
        AddStringArray(actual, "SpeechSellingHaggle", DataStoreOwnersData.SpeechSellingHaggle);
        AddStringArray(actual, "SpeechBuyingHaggleFinal", DataStoreOwnersData.SpeechBuyingHaggleFinal);
        AddStringArray(actual, "SpeechBuyingHaggle", DataStoreOwnersData.SpeechBuyingHaggle);
        AddStringArray(actual, "SpeechInsultedHagglingDone", DataStoreOwnersData.SpeechInsultedHagglingDone);
        AddStringArray(actual, "SpeechGetOutOfMyStore", DataStoreOwnersData.SpeechGetOutOfMyStore);
        AddStringArray(actual, "SpeechHagglingTryAgain", DataStoreOwnersData.SpeechHagglingTryAgain);
        AddStringArray(actual, "SpeechSorry", DataStoreOwnersData.SpeechSorry);

        AddNumeric2D(actual, "RaceGoldAdjustments", DataStoresData.RaceGoldAdjustments);
        AddNumeric2D(actual, "StoreChoices", DataStoresData.StoreChoices);

        AddStringArray(actual, "Colors", DataTablesData.Colors);
        AddStringArray(actual, "Mushrooms", DataTablesData.Mushrooms);
        AddStringArray(actual, "Woods", DataTablesData.Woods);
        AddStringArray(actual, "Metals", DataTablesData.Metals);
        AddStringArray(actual, "Rocks", DataTablesData.Rocks);
        AddStringArray(actual, "Amulets", DataTablesData.Amulets);
        AddStringArray(actual, "Syllables", DataTablesData.Syllables);
        AddNumeric2D(actual, "BlowsTable", DataTablesData.BlowsTable);
        AddNumericArray(actual, "NormalTable", DataTablesData.NormalTable);

        AssertMatches(expected, actual);
    }

    // ---- row flatteners (mirror tools/data_table_dump/data_table_dump.cpp) ----

    private static void AddCreature(Dictionary<string, string> d, string table, int i, Creature c)
    {
        d[$"{table}.{i}.name"] = c.Name;
        d[$"{table}.{i}.movement"] = c.Movement.ToString();
        d[$"{table}.{i}.spells"] = c.Spells.ToString();
        d[$"{table}.{i}.defenses"] = c.Defenses.ToString();
        d[$"{table}.{i}.kill_exp_value"] = c.KillExpValue.ToString();
        d[$"{table}.{i}.sleep_counter"] = c.SleepCounter.ToString();
        d[$"{table}.{i}.area_affect_radius"] = c.AreaAffectRadius.ToString();
        d[$"{table}.{i}.ac"] = c.Ac.ToString();
        d[$"{table}.{i}.speed"] = c.Speed.ToString();
        d[$"{table}.{i}.sprite"] = c.Sprite.ToString();
        d[$"{table}.{i}.hit_die.dice"] = c.HitDie.Count.ToString();
        d[$"{table}.{i}.hit_die.sides"] = c.HitDie.Sides.ToString();
        for (var k = 0; k < c.Damage.Length; k++)
        {
            d[$"{table}.{i}.damage[{k}]"] = c.Damage[k].ToString();
        }
        d[$"{table}.{i}.level"] = c.Level.ToString();
    }

    private static void AddMonsterAttack(Dictionary<string, string> d, string table, int i, MonsterAttack a)
    {
        d[$"{table}.{i}.type_id"] = a.TypeId.ToString();
        d[$"{table}.{i}.description_id"] = a.DescriptionId.ToString();
        d[$"{table}.{i}.dice.dice"] = a.Dice.Count.ToString();
        d[$"{table}.{i}.dice.sides"] = a.Dice.Sides.ToString();
    }

    private static void AddDungeonObject(Dictionary<string, string> d, string table, int i, DungeonObject o)
    {
        d[$"{table}.{i}.name"] = o.Name;
        d[$"{table}.{i}.flags"] = o.Flags.ToString();
        d[$"{table}.{i}.category_id"] = o.CategoryId.ToString();
        d[$"{table}.{i}.sprite"] = o.Sprite.ToString();
        d[$"{table}.{i}.misc_use"] = o.MiscUse.ToString();
        d[$"{table}.{i}.cost"] = o.Cost.ToString();
        d[$"{table}.{i}.sub_category_id"] = o.SubCategoryId.ToString();
        d[$"{table}.{i}.items_count"] = o.ItemsCount.ToString();
        d[$"{table}.{i}.weight"] = o.Weight.ToString();
        d[$"{table}.{i}.to_hit"] = o.ToHit.ToString();
        d[$"{table}.{i}.to_damage"] = o.ToDamage.ToString();
        d[$"{table}.{i}.ac"] = o.Ac.ToString();
        d[$"{table}.{i}.to_ac"] = o.ToAc.ToString();
        d[$"{table}.{i}.damage.dice"] = o.Damage.Count.ToString();
        d[$"{table}.{i}.damage.sides"] = o.Damage.Sides.ToString();
        d[$"{table}.{i}.depth_first_found"] = o.DepthFirstFound.ToString();
    }

    private static void AddRace(Dictionary<string, string> d, string table, int i, Race r)
    {
        d[$"{table}.{i}.name"] = r.Name;
        d[$"{table}.{i}.str_adjustment"] = r.StrAdjustment.ToString();
        d[$"{table}.{i}.int_adjustment"] = r.IntAdjustment.ToString();
        d[$"{table}.{i}.wis_adjustment"] = r.WisAdjustment.ToString();
        d[$"{table}.{i}.dex_adjustment"] = r.DexAdjustment.ToString();
        d[$"{table}.{i}.con_adjustment"] = r.ConAdjustment.ToString();
        d[$"{table}.{i}.chr_adjustment"] = r.ChrAdjustment.ToString();
        d[$"{table}.{i}.base_age"] = r.BaseAge.ToString();
        d[$"{table}.{i}.max_age"] = r.MaxAge.ToString();
        d[$"{table}.{i}.male_height_base"] = r.MaleHeightBase.ToString();
        d[$"{table}.{i}.male_height_mod"] = r.MaleHeightMod.ToString();
        d[$"{table}.{i}.male_weight_base"] = r.MaleWeightBase.ToString();
        d[$"{table}.{i}.male_weight_mod"] = r.MaleWeightMod.ToString();
        d[$"{table}.{i}.female_height_base"] = r.FemaleHeightBase.ToString();
        d[$"{table}.{i}.female_height_mod"] = r.FemaleHeightMod.ToString();
        d[$"{table}.{i}.female_weight_base"] = r.FemaleWeightBase.ToString();
        d[$"{table}.{i}.female_weight_mod"] = r.FemaleWeightMod.ToString();
        d[$"{table}.{i}.disarm_chance_base"] = r.DisarmChanceBase.ToString();
        d[$"{table}.{i}.search_chance_base"] = r.SearchChanceBase.ToString();
        d[$"{table}.{i}.stealth"] = r.Stealth.ToString();
        d[$"{table}.{i}.fos"] = r.Fos.ToString();
        d[$"{table}.{i}.base_to_hit"] = r.BaseToHit.ToString();
        d[$"{table}.{i}.base_to_hit_bows"] = r.BaseToHitBows.ToString();
        d[$"{table}.{i}.saving_throw_base"] = r.SavingThrowBase.ToString();
        d[$"{table}.{i}.hit_points_base"] = r.HitPointsBase.ToString();
        d[$"{table}.{i}.infra_vision"] = r.InfraVision.ToString();
        d[$"{table}.{i}.exp_factor_base"] = r.ExpFactorBase.ToString();
        d[$"{table}.{i}.classes_bit_field"] = r.ClassesBitField.ToString();
    }

    private static void AddBackground(Dictionary<string, string> d, string table, int i, Background b)
    {
        d[$"{table}.{i}.info"] = b.Info;
        d[$"{table}.{i}.roll"] = b.Roll.ToString();
        d[$"{table}.{i}.chart"] = b.Chart.ToString();
        d[$"{table}.{i}.next"] = b.Next.ToString();
        d[$"{table}.{i}.bonus"] = b.Bonus.ToString();
    }

    private static void AddClass(Dictionary<string, string> d, string table, int i, CharacterClass c)
    {
        d[$"{table}.{i}.title"] = c.Title;
        d[$"{table}.{i}.hit_points"] = c.HitPoints.ToString();
        d[$"{table}.{i}.disarm_traps"] = c.DisarmTraps.ToString();
        d[$"{table}.{i}.searching"] = c.Searching.ToString();
        d[$"{table}.{i}.stealth"] = c.Stealth.ToString();
        d[$"{table}.{i}.fos"] = c.Fos.ToString();
        d[$"{table}.{i}.base_to_hit"] = c.BaseToHit.ToString();
        d[$"{table}.{i}.base_to_hit_with_bows"] = c.BaseToHitWithBows.ToString();
        d[$"{table}.{i}.saving_throw"] = c.SavingThrow.ToString();
        d[$"{table}.{i}.strength"] = c.Strength.ToString();
        d[$"{table}.{i}.intelligence"] = c.Intelligence.ToString();
        d[$"{table}.{i}.wisdom"] = c.Wisdom.ToString();
        d[$"{table}.{i}.dexterity"] = c.Dexterity.ToString();
        d[$"{table}.{i}.constitution"] = c.Constitution.ToString();
        d[$"{table}.{i}.charisma"] = c.Charisma.ToString();
        d[$"{table}.{i}.class_to_use_mage_spells"] = c.ClassToUseMageSpells.ToString();
        d[$"{table}.{i}.experience_factor"] = c.ExperienceFactor.ToString();
        d[$"{table}.{i}.min_level_for_spell_casting"] = c.MinLevelForSpellCasting.ToString();
    }

    private static void AddStoreOwner(Dictionary<string, string> d, string table, int i, StoreOwner o)
    {
        d[$"{table}.{i}.name"] = o.Name;
        d[$"{table}.{i}.max_cost"] = o.MaxCost.ToString();
        d[$"{table}.{i}.max_inflate"] = o.MaxInflate.ToString();
        d[$"{table}.{i}.min_inflate"] = o.MinInflate.ToString();
        d[$"{table}.{i}.haggles_per"] = o.HagglesPer.ToString();
        d[$"{table}.{i}.race"] = o.Race.ToString();
        d[$"{table}.{i}.max_insults"] = o.MaxInsults.ToString();
    }

    // ---- generic helpers ----

    private static void AddArray<T>(Dictionary<string, string> d, string table, T[] rows, Action<Dictionary<string, string>, string, int, T> addRow)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            addRow(d, table, i, rows[i]);
        }
    }

    private static void AddStringArray(Dictionary<string, string> d, string table, string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            d[$"{table}.{i}.value"] = values[i] ?? "<null>";
        }
    }

    private static void AddNumericArray<T>(Dictionary<string, string> d, string table, T[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            d[$"{table}.{i}.value"] = values[i]!.ToString()!;
        }
    }

    private static void AddNumeric2D<T>(Dictionary<string, string> d, string table, T[][] rows)
    {
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                d[$"{table}.{r}.value[{c}]"] = rows[r][c]!.ToString()!;
            }
        }
    }

    private static void AddJagged2D(Dictionary<string, string> d, string table, string?[][] rows)
    {
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                d[$"{table}.{r}.value[{c}]"] = rows[r][c] ?? "<null>";
            }
        }
    }

    private static void AddSpell2D(Dictionary<string, string> d, string table, Spell[][] rows)
    {
        for (var cls = 0; cls < rows.Length; cls++)
        {
            for (var lvl = 0; lvl < rows[cls].Length; lvl++)
            {
                var s = rows[cls][lvl];
                d[$"{table}.0.{cls}.{lvl}.level_required"] = s.LevelRequired.ToString();
                d[$"{table}.0.{cls}.{lvl}.mana_required"] = s.ManaRequired.ToString();
                d[$"{table}.0.{cls}.{lvl}.failure_chance"] = s.FailureChance.ToString();
                d[$"{table}.0.{cls}.{lvl}.exp_gain_for_learning"] = s.ExpGainForLearning.ToString();
            }
        }
    }

    // ---- baseline parsing + comparison ----

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
                mismatches.Add($"{key}: missing from generated tables (expected {expectedValue})");
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
                    mismatches.Add($"{key}: present in generated tables but not in C++ baseline");
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
