using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Character;

// Spell-allowance and mana bookkeeping from src/player.cpp:
// playerCalculateAllowedSpellsCount() (+ its forget/remember/learnable
// helpers) and playerGainMana(). Character creation (M6) reaches both --
// through playerSetAndUseStat(A_INT/A_WIS) for casters, and again from
// startMoria()'s new-character path. Neither consumes RNG.
//
// playerGainSpells() (actually choosing/learning spells -- an interactive
// menu for mages, a random pick for priests) is the in-game study command;
// it joined in M10 with the casting layer, as GainSpells.
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
    private static void EliminateKnownSpellsGreaterThanLevel(Player py, Spell[] spells, string magicType, int offset, Action<string> print)
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

                    print($"You have forgotten the {magicType} of {DataPlayerData.SpellNames[i + offset]}.");
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
    private static int RememberForgottenSpells(Player py, Spell[] spells, int allowedSpells, int newSpells, string magicType, int offset, Action<string> print)
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

                    print($"You have remembered the {magicType} of {DataPlayerData.SpellNames[orderId + offset]}.");
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
    private static void ForgetSpells(Player py, int newSpells, string magicType, int offset, Action<string> print)
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

                print($"You have forgotten the {magicType} of {DataPlayerData.SpellNames[orderId + offset]}.");
            }
        }
    }

    // src/player.cpp:1606 playerCalculateAllowedSpellsCount() -- calculate
    // number of spells player should have, and learn/forget spells until that
    // number is met. -JEW-
    //
    // Its messages go through printMessage(), which zeroes
    // game.command_count; in-game callers pass PlayerWorld.Print for that
    // (M11). Without one -- character creation, which has no command count
    // yet -- they are display only.
    public static void CalculateAllowedSpellsCount(Player py, int stat, Action<string>? printMessage = null)
    {
        var print = printMessage ?? ScreenRendering.PrintMessage;
        var spells = DataPlayerData.MagicSpells[py.Misc.ClassId - 1];
        var (magicType, offset) = MagicTypeFor(stat);

        // check to see if know any spells greater than level, eliminate them
        EliminateKnownSpellsGreaterThanLevel(py, spells, magicType, offset, print);

        // calc number of spells allowed
        var numAllowed = NumberOfSpellsAllowed(py, stat);
        var numKnown = NumberOfSpellsKnown(py);
        var newSpells = numAllowed - numKnown;

        if (newSpells > 0)
        {
            newSpells = RememberForgottenSpells(py, spells, numAllowed, newSpells, magicType, offset, print);

            // If `new_spells_to_learn` is still greater than zero
            if (newSpells > 0)
            {
                newSpells = LearnableSpells(py, spells, newSpells);
            }
        }
        else if (newSpells < 0)
        {
            ForgetSpells(py, newSpells, magicType, offset, print);
            newSpells = 0;
        }

        if (newSpells != py.Flags.NewSpellsToLearn)
        {
            if (newSpells > 0 && py.Flags.NewSpellsToLearn == 0)
            {
                print($"You can learn some new {magicType}s now.");
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

    // src/player.cpp:803 playerCanRead() (M10)
    private static bool CanRead(PlayerWorld w)
    {
        if (w.Py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(w, "You can't see to read your spell book!");
            return false;
        }

        if (PlayerMovement.NoLight(w))
        {
            PlayerCore.PrintMessage(w, "You have no light to read by.");
            return false;
        }

        return true;
    }

    // src/player.cpp:817 lastKnownSpell() (M10)
    private static int LastKnownSpell(Player py)
    {
        for (var lastKnown = 0; lastKnown < 32; lastKnown++)
        {
            if (py.Flags.SpellsLearnedOrder[lastKnown] == 99)
            {
                return lastKnown;
            }
        }

        // We should never actually reach this, but just in case... -MRC-
        return 0;
    }

    // src/player.cpp:829 playerDetermineLearnableSpells() (M10)
    private static uint DetermineLearnableSpells(Player py)
    {
        uint spellFlag = 0;

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (py.Inventory[i].CategoryId == TreasureCategory.TvMagicBook)
            {
                spellFlag |= py.Inventory[i].Flags;
            }
        }

        return spellFlag;
    }

    // src/player.cpp:841 playerGainSpells() -- gain spells when player wants
    // to -JW- (M10: the study command). Mages choose from a menu (keys via
    // PlayerCore.GetCommand); priests are given random prayers.
    //
    // NOTE: the original's spell_bank[] is a 31-entry stack array whose
    // removal loop copies spell_bank[spell_id] (one past the last valid
    // entry, uninitialized) down; the copied value is never read again. The
    // port sizes the bank at 32 zeroed entries so that copy stays in bounds.
    public static void GainSpells(PlayerWorld w)
    {
        var py = w.Py;

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are too confused.");
            return;
        }

        int newSpells = py.Flags.NewSpellsToLearn;
        var diffSpells = 0;

        var spells = DataPlayerData.MagicSpells[py.Misc.ClassId - 1];

        int stat;
        int offset;

        // Priests don't need light because they get spells from their god, so only
        // fail when can't see if player has SPELL_TYPE_MAGE spells. This check is done below.
        if (DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells == SpellConfig.SpellTypeMage)
        {
            // People with SPELL_TYPE_MAGE spells can't learn spell_bank if they can't read their books.
            if (!CanRead(w))
            {
                return;
            }
            stat = PlayerConfig.Attr.Int;
            offset = SpellConfig.NameOffsetSpells;
        }
        else
        {
            stat = PlayerConfig.Attr.Wis;
            offset = SpellConfig.NameOffsetPrayers;
        }

        var lastKnown = LastKnownSpell(py);

        if (newSpells == 0)
        {
            PlayerCore.PrintMessage(w, $"You can't learn any new {(stat == PlayerConfig.Attr.Int ? "spell" : "prayer")}s!");

            w.Game.PlayerFreeTurn = true;
            return;
        }

        uint spellFlag;

        // determine which spells player can learn
        // mages need the book to learn a spell, priests do not need the book
        if (stat == PlayerConfig.Attr.Int)
        {
            spellFlag = DetermineLearnableSpells(py);
        }
        else
        {
            spellFlag = 0x7FFFFFFF;
        }

        // clear bits for spells already learned
        spellFlag &= ~py.Flags.SpellsLearnt;

        var spellId = 0;
        var spellBank = new int[32];
        uint mask = 0x1;

        for (var i = 0; spellFlag != 0; mask <<= 1, i++)
        {
            if ((spellFlag & mask) != 0)
            {
                spellFlag &= ~mask;
                if (spells[i].LevelRequired <= py.Misc.Level)
                {
                    spellBank[spellId] = i;
                    spellId++;
                }
            }
        }

        if (newSpells > spellId)
        {
            PlayerCore.PrintMessage(w, "You seem to be missing a book.");

            diffSpells = newSpells - spellId;
            newSpells = spellId;
        }

        if (newSpells == 0)
        {
            // do nothing
        }
        else if (stat == PlayerConfig.Attr.Int)
        {
            // get to choose which mage spells will be learned
            w.Ui?.TerminalSaveScreen();
            w.Ui?.DisplaySpellsList(spellBank, spellId, false, -1);

            while (newSpells != 0 && PlayerCore.GetCommand(w, "Learn which spell?", out var query))
            {
                var c = query - 'a';

                // test j < 23 in case i is greater than 22, only 22 spells
                // are actually shown on the screen, so limit choice to those
                if (c >= 0 && c < spellId && c < 22)
                {
                    newSpells--;

                    py.Flags.SpellsLearnt |= 1u << spellBank[c];
                    py.Flags.SpellsLearnedOrder[lastKnown] = (byte)spellBank[c];
                    lastKnown++;

                    for (; c <= spellId - 1; c++)
                    {
                        spellBank[c] = spellBank[c + 1];
                    }

                    spellId--;

                    w.Ui?.EraseLine(c + 1, 31);
                    w.Ui?.DisplaySpellsList(spellBank, spellId, false, -1);
                }
                else
                {
                    w.Ui?.TerminalBellSound();
                }
            }

            w.Ui?.TerminalRestoreScreen();
        }
        else
        {
            // pick a prayer at random
            while (newSpells != 0)
            {
                var id = RandomNumber.Next(spellId) - 1;
                py.Flags.SpellsLearnt |= 1u << spellBank[id];
                py.Flags.SpellsLearnedOrder[lastKnown] = (byte)spellBank[id];
                lastKnown++;

                PlayerCore.PrintMessage(w, $"You have learned the prayer of {DataPlayerData.SpellNames[spellBank[id] + offset]}.");

                for (; id <= spellId - 1; id++)
                {
                    spellBank[id] = spellBank[id + 1];
                }

                spellId--;
                newSpells--;
            }
        }

        py.Flags.NewSpellsToLearn = (byte)(newSpells + diffSpells);

        if (py.Flags.NewSpellsToLearn == 0)
        {
            py.Flags.Status |= PlayerConfig.Status.PyStudy;
        }

        // set the mana for first level characters when they learn their first spell.
        if (py.Misc.Mana == 0)
        {
            GainMana(py, stat);
        }
    }
}
