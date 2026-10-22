using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// The subset of src/player_stats.cpp character creation (M6) reaches: base
// exp levels, hit-point recalculation, the stat->bonus adjustment tables,
// and playerSetAndUseStat() (which fans out into bonus recalculation, spell
// allowance and hit points). M8 added the rest of the file: the stat
// gain/loss/restore/boost family, attack blows and the charisma price
// adjustment. displayCharacterStats() (after a stat change) is display
// only.
//
// SetAndUseStat() on INT/WIS reaches PlayerSpells' allowance update, whose
// messages zero game.command_count like any printMessage(): every
// stat-changing entry point takes an optional message sink for that, which
// in-game callers set to PlayerWorld.Print (M11 -- before that they were
// display only, which no trace could see: none had a count pending).
public static class PlayerStatLogic
{
    // src/player_stats.cpp:11 playerInitializeBaseExperienceLevels()
    private static readonly uint[] BaseExperienceLevels =
    {
        10, 25, 45, 70, 100, 140, 200, 280, 380, 500, 650, 850, 1100, 1400, 1800, 2300, 2900, 3600, 4400, 5400,
        6800, 8400, 10200, 12500, 17500, 25000, 35000, 50000, 75000, 100000, 150000, 200000, 300000, 400000, 500000, 750000, 1500000, 2500000, 5000000, 10000000,
    };

    public static void InitializeBaseExperienceLevels(Player py)
    {
        for (var i = 0; i < PlayerConfig.PlayerMaxLevel; i++)
        {
            py.BaseExpLevels[i] = BaseExperienceLevels[i];
        }
    }

    // src/player_stats.cpp:24 playerCalculateHitPoints()
    public static void CalculateHitPoints(Player py)
    {
        var hp = py.BaseHpLevels[py.Misc.Level - 1] + (StatAdjustmentConstitution(py) * py.Misc.Level);

        // Always give at least one point per level + 1
        if (hp < py.Misc.Level + 1)
        {
            hp = py.Misc.Level + 1;
        }

        if ((py.Flags.Status & PlayerConfig.Status.PyHero) != 0)
        {
            hp += 10;
        }

        if ((py.Flags.Status & PlayerConfig.Status.PyShero) != 0)
        {
            hp += 20;
        }

        // MHP can equal zero while character is being created
        if (hp != py.Misc.MaxHp && py.Misc.MaxHp != 0)
        {
            // Change current hit points proportionately to change of MHP,
            // divide first to avoid overflow, little loss of accuracy
            var value = (((int)py.Misc.CurrentHp << 16) + py.Misc.CurrentHpFraction) / py.Misc.MaxHp * hp;
            py.Misc.CurrentHp = (short)(value >> 16);
            py.Misc.CurrentHpFraction = (ushort)(value & 0xFFFF);
            py.Misc.MaxHp = (short)hp;

            // can't print hit points here, may be in store or inventory mode
            py.Flags.Status |= PlayerConfig.Status.PyHp;
        }
    }

    // src/player_stats.cpp:116 playerStatAdjustmentWisdomIntelligence()
    public static int StatAdjustmentWisdomIntelligence(Player py, int stat)
    {
        int value = py.Stats.Used[stat];

        if (value > 117) return 7;
        if (value > 107) return 6;
        if (value > 87) return 5;
        if (value > 67) return 4;
        if (value > 17) return 3;
        if (value > 14) return 2;
        if (value > 7) return 1;
        return 0;
    }

    // src/player_stats.cpp:206 playerStatAdjustmentConstitution()
    public static int StatAdjustmentConstitution(Player py)
    {
        int con = py.Stats.Used[PlayerConfig.Attr.Con];

        if (con < 7) return con - 7;
        if (con < 17) return 0;
        if (con == 17) return 1;
        if (con < 94) return 2;
        if (con < 117) return 3;
        return 4;
    }

    // src/player_stats.cpp:232 playerModifyStat()
    private static byte ModifyStat(Player py, int stat, short amount)
    {
        var newStat = py.Stats.Current[stat];

        var loop = amount < 0 ? -amount : amount;

        for (var i = 0; i < loop; i++)
        {
            if (amount > 0)
            {
                if (newStat < 18)
                {
                    newStat++;
                }
                else if (newStat < 108)
                {
                    newStat += 10;
                }
                else
                {
                    newStat = 118;
                }
            }
            else
            {
                if (newStat > 27)
                {
                    newStat -= 10;
                }
                else if (newStat > 18)
                {
                    newStat = 18;
                }
                else if (newStat > 3)
                {
                    newStat--;
                }
            }
        }

        return newStat;
    }

    // src/player_stats.cpp:261 playerSetAndUseStat() -- set the value of the
    // stat which is actually used. -CJS-
    public static void SetAndUseStat(Player py, int stat, Action<string>? printMessage = null)
    {
        py.Stats.Used[stat] = ModifyStat(py, stat, py.Stats.Modified[stat]);

        var spellType = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;

        if (stat == PlayerConfig.Attr.Str)
        {
            py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
            PlayerBonuses.RecalculateBonuses(py);
        }
        else if (stat == PlayerConfig.Attr.Dex)
        {
            PlayerBonuses.RecalculateBonuses(py);
        }
        else if (stat == PlayerConfig.Attr.Int && spellType == SpellConfig.SpellTypeMage)
        {
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int, printMessage);
            PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
        }
        else if (stat == PlayerConfig.Attr.Wis && spellType == SpellConfig.SpellTypePriest)
        {
            PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis, printMessage);
            PlayerSpells.GainMana(py, PlayerConfig.Attr.Wis);
        }
        else if (stat == PlayerConfig.Attr.Con)
        {
            CalculateHitPoints(py);
        }
    }

    // src/player_stats.cpp:58 playerAttackBlowsDexterity()
    private static int AttackBlowsDexterity(int dexterity)
    {
        if (dexterity < 10) return 0;
        if (dexterity < 19) return 1;
        if (dexterity < 68) return 2;
        if (dexterity < 108) return 3;
        if (dexterity < 118) return 4;
        return 5;
    }

    // src/player_stats.cpp:78 playerAttackBlowsStrength()
    private static int AttackBlowsStrength(int strength, int weight)
    {
        var adjWeight = strength * 10 / weight;

        if (adjWeight < 2) return 0;
        if (adjWeight < 3) return 1;
        if (adjWeight < 4) return 2;
        if (adjWeight < 5) return 3;
        if (adjWeight < 7) return 4;
        if (adjWeight < 9) return 5;
        return 6;
    }

    // src/player_stats.cpp:101 playerAttackBlows() -- weapon weight VS
    // strength and dexterity -RAK-
    public static int AttackBlows(Player py, int weight, out int weightToHit)
    {
        weightToHit = 0;

        int playerStrength = py.Stats.Used[PlayerConfig.Attr.Str];

        if (playerStrength * 15 < weight)
        {
            weightToHit = playerStrength * 15 - weight;
            return 1;
        }

        var dexterity = AttackBlowsDexterity(py.Stats.Used[PlayerConfig.Attr.Dex]);
        var strength = AttackBlowsStrength(playerStrength, weight);

        // (src/data_tables.cpp blows_table)
        return DataTablesData.BlowsTable[strength][dexterity];
    }

    // src/player_stats.cpp:142 playerStatAdjustmentCharisma() -- percent
    // decrease or increase in price of goods -RAK-
    public static int StatAdjustmentCharisma(Player py)
    {
        int charisma = py.Stats.Used[PlayerConfig.Attr.Chr];

        if (charisma > 117) return 90;
        if (charisma > 107) return 92;
        if (charisma > 87) return 94;
        if (charisma > 67) return 96;
        if (charisma > 18) return 98;

        return charisma switch
        {
            18 => 100,
            17 => 101,
            16 => 102,
            15 => 103,
            14 => 104,
            13 => 106,
            12 => 108,
            11 => 110,
            10 => 112,
            9 => 114,
            8 => 116,
            7 => 118,
            6 => 120,
            5 => 122,
            4 => 125,
            3 => 130,
            _ => 100,
        };
    }

    // src/player_stats.cpp:283 playerStatRandomIncrease() -- increases a
    // stat by one randomized level -RAK-
    public static bool StatRandomIncrease(Player py, int stat, Action<string>? printMessage = null)
    {
        int newStat = py.Stats.Current[stat];

        if (newStat >= 118)
        {
            return false;
        }

        if (newStat >= 18 && newStat < 116)
        {
            // stat increases by 1/6 to 1/3 of difference from max
            var gain = ((118 - newStat) / 3 + 1) >> 1;

            newStat += RandomNumber.Next(gain) + gain;
        }
        else
        {
            newStat++;
        }

        py.Stats.Current[stat] = (byte)newStat;

        if (newStat > py.Stats.Max[stat])
        {
            py.Stats.Max[stat] = (byte)newStat;
        }

        SetAndUseStat(py, stat, printMessage);
        // (displayCharacterStats() -- display)

        return true;
    }

    // src/player_stats.cpp:312 playerStatRandomDecrease() -- decreases a
    // stat by one randomized level -RAK-
    public static bool StatRandomDecrease(Player py, int stat, Action<string>? printMessage = null)
    {
        int newStat = py.Stats.Current[stat];

        if (newStat <= 3)
        {
            return false;
        }

        if (newStat >= 19 && newStat < 117)
        {
            var loss = (((118 - newStat) >> 1) + 1) >> 1;
            newStat += -RandomNumber.Next(loss) - loss;

            if (newStat < 18)
            {
                newStat = 18;
            }
        }
        else
        {
            newStat--;
        }

        py.Stats.Current[stat] = (byte)newStat;

        SetAndUseStat(py, stat, printMessage);
        // (displayCharacterStats() -- display)

        return true;
    }

    // src/player_stats.cpp:339 playerStatRestore() -- restore a stat.
    // Return true only if this actually makes a difference.
    public static bool StatRestore(Player py, int stat, Action<string>? printMessage = null)
    {
        var newStat = py.Stats.Max[stat] - py.Stats.Current[stat];

        if (newStat == 0)
        {
            return false;
        }

        py.Stats.Current[stat] = (byte)(py.Stats.Current[stat] + newStat);

        SetAndUseStat(py, stat, printMessage);
        // (displayCharacterStats() -- display)

        return true;
    }

    // src/player_stats.cpp:356 playerStatBoost() -- boost a stat
    // artificially (by wearing something).
    public static void StatBoost(Player py, int stat, int amount, Action<string>? printMessage = null)
    {
        py.Stats.Modified[stat] = (short)(py.Stats.Modified[stat] + amount);

        SetAndUseStat(py, stat, printMessage);

        // can not call displayCharacterStats() here:
        //   might be in a store,
        //   might be in inventoryExecuteCommand()
        py.Flags.Status |= PlayerConfig.Status.PyStr << stat;
    }

    // src/player_stats.cpp:366 playerToHitAdjustment()
    public static short ToHitAdjustment(Player py)
    {
        short total;

        int dexterity = py.Stats.Used[PlayerConfig.Attr.Dex];
        if (dexterity < 4) total = -3;
        else if (dexterity < 6) total = -2;
        else if (dexterity < 8) total = -1;
        else if (dexterity < 16) total = 0;
        else if (dexterity < 17) total = 1;
        else if (dexterity < 18) total = 2;
        else if (dexterity < 69) total = 3;
        else if (dexterity < 118) total = 4;
        else total = 5;

        int strength = py.Stats.Used[PlayerConfig.Attr.Str];
        if (strength < 4) total -= 3;
        else if (strength < 5) total -= 2;
        else if (strength < 7) total -= 1;
        else if (strength < 18) total -= 0;
        else if (strength < 94) total += 1;
        else if (strength < 109) total += 2;
        else if (strength < 117) total += 3;
        else total += 4;

        return total;
    }

    // src/player_stats.cpp:413 playerArmorClassAdjustment()
    public static short ArmorClassAdjustment(Player py)
    {
        int stat = py.Stats.Used[PlayerConfig.Attr.Dex];

        if (stat < 4) return -4;
        if (stat == 4) return -3;
        if (stat == 5) return -2;
        if (stat == 6) return -1;
        if (stat < 15) return 0;
        if (stat < 18) return 1;
        if (stat < 59) return 2;
        if (stat < 94) return 3;
        if (stat < 117) return 4;
        return 5;
    }

    // src/player_stats.cpp:444 playerDisarmAdjustment()
    public static short DisarmAdjustment(Player py)
    {
        int stat = py.Stats.Used[PlayerConfig.Attr.Dex];

        if (stat < 4) return -8;
        if (stat == 4) return -6;
        if (stat == 5) return -4;
        if (stat == 6) return -2;
        if (stat == 7) return -1;
        if (stat < 13) return 0;
        if (stat < 16) return 1;
        if (stat < 18) return 2;
        if (stat < 59) return 4;
        if (stat < 94) return 5;
        if (stat < 117) return 6;
        return 8;
    }

    // src/player_stats.cpp:479 playerDamageAdjustment()
    public static short DamageAdjustment(Player py)
    {
        int stat = py.Stats.Used[PlayerConfig.Attr.Str];

        if (stat < 4) return -2;
        if (stat < 5) return -1;
        if (stat < 16) return 0;
        if (stat < 17) return 1;
        if (stat < 18) return 2;
        if (stat < 94) return 3;
        if (stat < 109) return 4;
        if (stat < 117) return 5;
        return 6;
    }
}
