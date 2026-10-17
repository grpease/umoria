using System.Text;
using Umoria.Core.Character;
using Umoria.Core.Data.Generated;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Tests.CharCreate;

// Real cross-check: compares Umoria.Core.Character.CharacterCreation (the M6
// port of src/character.cpp plus startMoria()'s new-character tail) against
// a text dump captured from the real, compiled C++ game via
// tools/char_create_dump/char_create_dump.cpp (built through the MSVC+vcpkg
// toolchain -- see dotnet/tests/CharCreateTraces/char_create_dump.txt for
// the checked-in baseline).
//
// The C++ side drives the real, unmodified characterCreate() with scripted
// keypresses (its ui_io.cpp is swapped for a headless stub); this side feeds
// the same answers in as CharacterChoices. Same matrix on both sides: 3
// seeds x 8 races x 2 genders x every class each race allows, with the
// re-roll count cycling 0/1/2 per case. Each case dumps every deterministic
// Player_t field (all but date_of_birth, which is wall-clock time), the full
// 34-slot inventory, any nonzero objects_identified[] entries, and the final
// RNG state -- the latter catches any extra or missing RNG draw even if it
// happened not to change a visible field.
public class CharCreateTraceTests
{
    private static readonly uint[] Seeds = [12345, 987654321, 31337];

    [Fact]
    public void CharacterCreation_MatchesCppBaseline()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CharCreateTraces", "char_create_dump.txt");
        var expected = File.ReadAllLines(path);

        var actual = new List<string>();
        var caseIndex = 0;

        foreach (var seed in Seeds)
        {
            for (var raceId = 0; raceId < DataPlayerData.CharacterRaces.Length; raceId++)
            {
                var classCount = CharacterCreation.AvailableClasses(raceId).Count;

                foreach (var isMale in new[] { true, false })
                {
                    for (var classChoice = 0; classChoice < classCount; classChoice++)
                    {
                        var rerolls = caseIndex % 3;
                        caseIndex++;

                        var py = new Player();
                        var identification = new ObjectIdentification();
                        PlayerStatLogic.InitializeBaseExperienceLevels(py);

                        MoriaRng.SetRandomSeed(seed);

                        CharacterCreation.Create(py, new CharacterChoices(raceId, isMale, classChoice, "Tester", rerolls));
                        CharacterCreation.InitializeNewCharacter(py, identification);

                        actual.Add($"case seed={seed} race={raceId} male={(isMale ? 1 : 0)} class_choice={classChoice} rerolls={rerolls}");
                        Dump(actual, py, identification);
                    }
                }
            }
        }

        AssertMatches(expected, actual);
    }

    [Fact]
    public void AvailableClasses_HumanCanChooseEveryClass()
    {
        // src/data_player.cpp: Human's classes_bit_field is 0x3F.
        Assert.Equal([0, 1, 2, 3, 4, 5], CharacterCreation.AvailableClasses(0));
    }

    [Fact]
    public void Create_RejectsClassTheRaceDoesNotOffer()
    {
        var py = new Player();
        MoriaRng.SetRandomSeed(1);

        var classCount = CharacterCreation.AvailableClasses(7).Count; // one past the last offered
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterCreation.Create(py, new CharacterChoices(7, true, classCount, "Tester")));
    }

    // Mirrors char_create_dump.cpp's dumpPlayer() line for line.
    private static void Dump(List<string> o, Player py, ObjectIdentification identification)
    {
        void F(string name, long value) => o.Add($"{name}\t{value}");
        void FI(string name, int index, long value) => o.Add($"{name}.{index}\t{value}");
        long B(bool b) => b ? 1 : 0;

        var m = py.Misc;
        var s = py.Stats;
        var f = py.Flags;

        F("rnd_seed", MoriaRng.GetRandomSeed());

        o.Add($"misc.name\t[{m.Name}]");
        F("misc.gender", B(m.Gender));
        F("misc.au", m.Au);
        F("misc.max_exp", m.MaxExp);
        F("misc.exp", m.Exp);
        F("misc.exp_fraction", m.ExpFraction);
        F("misc.age", m.Age);
        F("misc.height", m.Height);
        F("misc.weight", m.Weight);
        F("misc.level", m.Level);
        F("misc.max_dungeon_depth", m.MaxDungeonDepth);
        F("misc.chance_in_search", m.ChanceInSearch);
        F("misc.fos", m.Fos);
        F("misc.bth", m.Bth);
        F("misc.bth_with_bows", m.BthWithBows);
        F("misc.mana", m.Mana);
        F("misc.max_hp", m.MaxHp);
        F("misc.plusses_to_hit", m.PlussesToHit);
        F("misc.plusses_to_damage", m.PlussesToDamage);
        F("misc.ac", m.Ac);
        F("misc.magical_ac", m.MagicalAc);
        F("misc.display_to_hit", m.DisplayToHit);
        F("misc.display_to_damage", m.DisplayToDamage);
        F("misc.display_ac", m.DisplayAc);
        F("misc.display_to_ac", m.DisplayToAc);
        F("misc.disarm", m.Disarm);
        F("misc.saving_throw", m.SavingThrow);
        F("misc.social_class", m.SocialClass);
        F("misc.stealth_factor", m.StealthFactor);
        F("misc.class_id", m.ClassId);
        F("misc.race_id", m.RaceId);
        F("misc.hit_die", m.HitDie);
        F("misc.experience_factor", m.ExperienceFactor);
        F("misc.current_mana", m.CurrentMana);
        F("misc.current_mana_fraction", m.CurrentManaFraction);
        F("misc.current_hp", m.CurrentHp);
        F("misc.current_hp_fraction", m.CurrentHpFraction);
        for (var i = 0; i < 4; i++)
        {
            o.Add($"misc.history.{i}\t[{m.History[i]}]");
        }

        for (var i = 0; i < 6; i++)
        {
            FI("stats.max", i, s.Max[i]);
            FI("stats.current", i, s.Current[i]);
            FI("stats.modified", i, s.Modified[i]);
            FI("stats.used", i, s.Used[i]);
        }

        F("flags.status", f.Status);
        F("flags.rest", f.Rest);
        F("flags.blind", f.Blind);
        F("flags.paralysis", f.Paralysis);
        F("flags.confused", f.Confused);
        F("flags.food", f.Food);
        F("flags.food_digested", f.FoodDigested);
        F("flags.protection", f.Protection);
        F("flags.speed", f.Speed);
        F("flags.fast", f.Fast);
        F("flags.slow", f.Slow);
        F("flags.afraid", f.Afraid);
        F("flags.poisoned", f.Poisoned);
        F("flags.image", f.Image);
        F("flags.protect_evil", f.ProtectEvil);
        F("flags.invulnerability", f.Invulnerability);
        F("flags.heroism", f.Heroism);
        F("flags.super_heroism", f.SuperHeroism);
        F("flags.blessed", f.Blessed);
        F("flags.heat_resistance", f.HeatResistance);
        F("flags.cold_resistance", f.ColdResistance);
        F("flags.detect_invisible", f.DetectInvisible);
        F("flags.word_of_recall", f.WordOfRecall);
        F("flags.see_infra", f.SeeInfra);
        F("flags.timed_infra", f.TimedInfra);
        F("flags.see_invisible", B(f.SeeInvisible));
        F("flags.teleport", B(f.Teleport));
        F("flags.free_action", B(f.FreeAction));
        F("flags.slow_digest", B(f.SlowDigest));
        F("flags.aggravate", B(f.Aggravate));
        F("flags.resistant_to_fire", B(f.ResistantToFire));
        F("flags.resistant_to_cold", B(f.ResistantToCold));
        F("flags.resistant_to_acid", B(f.ResistantToAcid));
        F("flags.regenerate_hp", B(f.RegenerateHp));
        F("flags.resistant_to_light", B(f.ResistantToLight));
        F("flags.free_fall", B(f.FreeFall));
        F("flags.sustain_str", B(f.SustainStr));
        F("flags.sustain_int", B(f.SustainInt));
        F("flags.sustain_wis", B(f.SustainWis));
        F("flags.sustain_con", B(f.SustainCon));
        F("flags.sustain_dex", B(f.SustainDex));
        F("flags.sustain_chr", B(f.SustainChr));
        F("flags.confuse_monster", B(f.ConfuseMonster));
        F("flags.new_spells_to_learn", f.NewSpellsToLearn);
        F("flags.spells_learnt", f.SpellsLearnt);
        F("flags.spells_worked", f.SpellsWorked);
        F("flags.spells_forgotten", f.SpellsForgotten);
        for (var i = 0; i < 32; i++)
        {
            FI("flags.spells_learned_order", i, f.SpellsLearnedOrder[i]);
        }

        for (var i = 0; i < py.BaseHpLevels.Length; i++)
        {
            FI("base_hp_levels", i, py.BaseHpLevels[i]);
        }
        for (var i = 0; i < py.BaseExpLevels.Length; i++)
        {
            FI("base_exp_levels", i, py.BaseExpLevels[i]);
        }

        F("temporary_light_only", B(py.TemporaryLightOnly));
        F("pack.unique_items", py.Pack.UniqueItems);
        F("pack.weight", py.Pack.Weight);
        F("pack.heaviness", py.Pack.Heaviness);
        F("equipment_count", py.EquipmentCount);
        F("weapon_is_heavy", B(py.WeaponIsHeavy));
        F("carrying_light", B(py.CarryingLight));

        for (var i = 0; i < py.Inventory.Length; i++)
        {
            var item = py.Inventory[i];
            o.Add($"inventory.{i}\tid={item.Id} sn={item.SpecialNameId} insc=[{item.Inscription}] flags={item.Flags} " +
                  $"cat={item.CategoryId} sprite={item.Sprite} misc={item.MiscUse} cost={item.Cost} sub={item.SubCategoryId} " +
                  $"count={item.ItemsCount} weight={item.Weight} to_hit={item.ToHit} to_dam={item.ToDamage} ac={item.Ac} " +
                  $"to_ac={item.ToAc} dice={item.Damage.Count}x{item.Damage.Sides} depth={item.DepthFirstFound} " +
                  $"ident={item.Identification}");
        }

        for (var i = 0; i < identification.ObjectsIdentified.Length; i++)
        {
            if (identification.ObjectsIdentified[i] != 0)
            {
                FI("objects_identified", i, identification.ObjectsIdentified[i]);
            }
        }
    }

    private static void AssertMatches(string[] expected, List<string> actual)
    {
        var currentCase = "";
        var mismatches = new StringBuilder();
        var count = 0;

        for (var i = 0; i < Math.Max(expected.Length, actual.Count); i++)
        {
            var e = i < expected.Length ? expected[i] : "<missing>";
            var a = i < actual.Count ? actual[i] : "<missing>";

            if (e.StartsWith("case ", StringComparison.Ordinal))
            {
                currentCase = e;
            }

            if (e != a)
            {
                if (count < 20)
                {
                    mismatches.AppendLine($"line {i + 1} ({currentCase}): expected '{e}', got '{a}'");
                }
                count++;
            }
        }

        Assert.True(count == 0, $"{count} mismatched lines (first 20):\n{mismatches}");
    }
}
