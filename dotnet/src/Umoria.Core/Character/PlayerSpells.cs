using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Character;

// Spell-allowance and mana bookkeeping from src/player.cpp:
// playerCalculateAllowedSpellsCount() (+ its forget/remember/learnable
// helpers) and playerGainMana(). Character creation (M6) reaches both --
// through playerSetAndUseStat(A_INT/A_WIS) for casters, and again from
// startMoria()'s new-character path. Neither consumes RNG.
//
// playerGainSpells() (actually choosing/learning spells -- an interactive
// menu for mages, a random pick for priests) is NOT ported: it's only
// reachable from the in-game study command, which belongs to the casting
// layer (M10).
public static class PlayerSpells
{
    private static (string MagicType, int Offset) MagicTypeFor(int stat)
    {
        return stat == PlayerConfig.Attr.Int
            ? ("spell", SpellConfig.NameOffsetSpells)
            : ("prayer", SpellConfig.NameOffsetPrayers);
    }

    // src/player.cpp:1455 eliminateKnownSpellsGreaterThanLevel()
    //
    // NOTE: the original walks bit 31 first and indexes msp_ptr[31], one past
    // the end of magic_spells' 31-entry rows. That's only reached if bit 31 of
    // spells_learnt is ever set, which nothing does (learnable spells are
    // masked to 0x7FFFFFFF), so it's unreachable; ported as-is rather than
    // "fixed", so a divergence would surface as an exception, not silently.
    private static void EliminateKnownSpellsGreaterThanLevel(Player py, Spell[] spells, string magicType, int offset)
    {
        uint mask = 0x80000000;

        for (var i = 31; mask != 0; mask >>= 1, i--)
        {
            if ((mask & py.Flags.SpellsLearnt) != 0)
            {
                if (spells[i].LevelRequired > py.Misc.Level)
                {
                    py.Flags.SpellsLearnt &= ~mask;
                    py.Flags.SpellsForgotten |= mask;

                    ScreenRendering.PrintMessage($"You have forgotten the {magicType} of {DataPlayerData.SpellNames[i + offset]}.");
                }
                else
                {
                    break;
                }
            }
        }
    }

    // src/player.cpp:1474 numberOfSpellsAllowed()
    private static int NumberOfSpellsAllowed(Player py, int stat)
    {
        var levels = py.Misc.Level - DataPlayerData.Classes[py.Misc.ClassId].MinLevelForSpellCasting + 1;

        switch (PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, stat))
        {
            case 1:
            case 2:
            case 3:
                return 1 * levels;
            case 4:
            case 5:
                return 3 * levels / 2;
            case 6:
                return 2 * levels;
            case 7:
                return 5 * levels / 2;
            default:
                return 0;
        }
    }

    // src/player.cpp:1503 numberOfSpellsKnown()
    private static int NumberOfSpellsKnown(Player py)
    {
        var known = 0;

        for (uint mask = 0x1; mask != 0; mask <<= 1)
        {
            if ((mask & py.Flags.SpellsLearnt) != 0)
            {
                known++;
            }
        }

        return known;
    }

    // src/player.cpp:1517 rememberForgottenSpells() -- remember forgotten
    // spells in the order that they were learned.
    private static int RememberForgottenSpells(Player py, Spell[] spells, int allowedSpells, int newSpells, string magicType, int offset)
    {
        for (var n = 0; py.Flags.SpellsForgotten != 0 && newSpells != 0 && n < allowedSpells && n < 32; n++)
        {
            // order ID is (i+1)th spell learned
            int orderId = py.Flags.SpellsLearnedOrder[n];

            // shifting by amounts greater than number of bits in long gives
            // an undefined result, so don't shift for unknown spells
            var mask = orderId == 99 ? 0u : (uint)(1L << orderId);

            if ((mask & py.Flags.SpellsForgotten) != 0)
            {
                if (spells[orderId].LevelRequired <= py.Misc.Level)
                {
                    newSpells--;
                    py.Flags.SpellsForgotten &= ~mask;
                    py.Flags.SpellsLearnt |= mask;

                    ScreenRendering.PrintMessage($"You have remembered the {magicType} of {DataPlayerData.SpellNames[orderId + offset]}.");
                }
                else
                {
                    allowedSpells++;
                }
            }
        }

        return newSpells;
    }

    // src/player.cpp:1552 learnableSpells()
    private static int LearnableSpells(Player py, Spell[] spells, int newSpells)
    {
        var spellFlag = 0x7FFFFFFFu & ~py.Flags.SpellsLearnt;

        var id = 0;
        uint mask = 0x1;

        for (var i = 0; spellFlag != 0; mask <<= 1, i++)
        {
            if ((spellFlag & mask) != 0)
            {
                spellFlag &= ~mask;
                if (spells[i].LevelRequired <= py.Misc.Level)
                {
                    id++;
                }
            }
        }

        return newSpells > id ? id : newSpells;
    }

    // src/player.cpp:1577 forgetSpells() -- forget spells in the opposite
    // order that they were learned. newSpells is always negative.
    private static void ForgetSpells(Player py, int newSpells, string magicType, int offset)
    {
        for (var i = 31; newSpells != 0 && py.Flags.SpellsLearnt != 0; i--)
        {
            // orderID is the (i+1)th spell learned
            int orderId = py.Flags.SpellsLearnedOrder[i];

            var mask = orderId == 99 ? 0u : (uint)(1L << orderId);

            if ((mask & py.Flags.SpellsLearnt) != 0)
            {
                py.Flags.SpellsLearnt &= ~mask;
                py.Flags.SpellsForgotten |= mask;
                newSpells++;

                ScreenRendering.PrintMessage($"You have forgotten the {magicType} of {DataPlayerData.SpellNames[orderId + offset]}.");
            }
        }
    }

    // src/player.cpp:1606 playerCalculateAllowedSpellsCount() -- calculate
    // number of spells player should have, and learn/forget spells until that
    // number is met. -JEW-
    public static void CalculateAllowedSpellsCount(Player py, int stat)
    {
        var spells = DataPlayerData.MagicSpells[py.Misc.ClassId - 1];
        var (magicType, offset) = MagicTypeFor(stat);

        // check to see if know any spells greater than level, eliminate them
        EliminateKnownSpellsGreaterThanLevel(py, spells, magicType, offset);

        // calc number of spells allowed
        var numAllowed = NumberOfSpellsAllowed(py, stat);
        var numKnown = NumberOfSpellsKnown(py);
        var newSpells = numAllowed - numKnown;

        if (newSpells > 0)
        {
            newSpells = RememberForgottenSpells(py, spells, numAllowed, newSpells, magicType, offset);

            // If `new_spells_to_learn` is still greater than zero
            if (newSpells > 0)
            {
                newSpells = LearnableSpells(py, spells, newSpells);
            }
        }
        else if (newSpells < 0)
        {
            ForgetSpells(py, newSpells, magicType, offset);
            newSpells = 0;
        }

        if (newSpells != py.Flags.NewSpellsToLearn)
        {
            if (newSpells > 0 && py.Flags.NewSpellsToLearn == 0)
            {
                ScreenRendering.PrintMessage($"You can learn some new {magicType}s now.");
            }

            py.Flags.NewSpellsToLearn = (byte)newSpells;
            py.Flags.Status |= PlayerConfig.Status.PyStudy;
        }
    }

    // src/player.cpp:981 newMana()
    private static int NewMana(Player py, int stat)
    {
        var levels = py.Misc.Level - DataPlayerData.Classes[py.Misc.ClassId].MinLevelForSpellCasting + 1;

        switch (PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, stat))
        {
            case 1:
            case 2:
                return 1 * levels;
            case 3:
                return 3 * levels / 2;
            case 4:
                return 2 * levels;
            case 5:
                return 5 * levels / 2;
            case 6:
                return 3 * levels;
            case 7:
                return 4 * levels;
            default:
                return 0;
        }
    }

    // src/player.cpp:1004 playerGainMana() -- gain some mana if you know at
    // least one spell. -RAK-
    public static void GainMana(Player py, int stat)
    {
        var m = py.Misc;

        if (py.Flags.SpellsLearnt != 0)
        {
            var newMana = NewMana(py, stat);

            // increment mana by one, so that first level chars have 2 mana
            if (newMana > 0)
            {
                newMana++;
            }

            // mana can be zero when creating character
            if (m.Mana != newMana)
            {
                if (m.Mana != 0)
                {
                    // change current mana proportionately to change of max mana,
                    // divide first to avoid overflow, little loss of accuracy
                    var value = (((int)m.CurrentMana << 16) + m.CurrentManaFraction) / m.Mana * newMana;
                    m.CurrentMana = (short)(value >> 16);
                    m.CurrentManaFraction = (ushort)(value & 0xFFFF);
                }
                else
                {
                    m.CurrentMana = (short)newMana;
                    m.CurrentManaFraction = 0;
                }

                m.Mana = (short)newMana;

                // can't print mana here, may be in store or inventory mode
                py.Flags.Status |= PlayerConfig.Status.PyMana;
            }
        }
        else if (m.Mana != 0)
        {
            m.Mana = 0;
            m.CurrentMana = 0;

            // can't print mana here, may be in store or inventory mode
            py.Flags.Status |= PlayerConfig.Status.PyMana;
        }
    }
}
