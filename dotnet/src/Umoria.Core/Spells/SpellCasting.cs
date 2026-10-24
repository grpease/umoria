using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M10: the casting layer on top of src/spells.cpp -- choosing a spell from
// a book (spells.cpp's spellGetId()/castSpellGetId()), the failure chance
// (mage_spells.cpp's spellChanceOfSuccess()), and the two cast commands:
// mage_spells.cpp's getAndCastMagicSpell() + castSpell() and
// player_pray.cpp's pray() + playerRecitePrayer(). Studying (choosing which
// spells to learn) is PlayerSpells.GainSpells.
//
// Input comes through PlayerWorld's hooks, read via PlayerCore (book:
// GetItemId; spell letter and direction: GetCommand / GetDirectionWithMemory;
// the "Cast X?" and not-enough-mana prompts: GetInputConfirmation), so the
// game-state side effects of reading input are kept. The '*' spell list
// (terminalSaveScreen()/displaySpellsList()) and the bell are display.
public static class SpellCasting
{
    // names based on spell_names[62] in data_player.cpp (mage_spells.cpp)
    private enum MageSpellId
    {
        MagicMissile = 1,
        DetectMonsters,
        PhaseDoor,
        LightArea,
        CureLightWounds,
        FindHiddenTrapsDoors,
        StinkingCloud,
        Confusion,
        LightningBolt,
        TrapDoorDestruction,
        Sleep1,
        CurePoison,
        TeleportSelf,
        RemoveCurse,
        FrostBolt,
        WallToMud,
        CreateFood,
        RechargeItem1,
        Sleep2,
        PolymorphOther,
        IdentifyItem,
        Sleep3,
        FireBolt,
        SpeedMonster,
        FrostBall,
        RechargeItem2,
        TeleportOther,
        HasteSelf,
        FireBall,
        WordOfDestruction,
        Genocide,
    }

    // names based on spell_names[62] in data_player.cpp (player_pray.cpp)
    private enum PriestSpellTypes
    {
        DetectEvil = 1,
        CureLightWounds,
        Bless,
        RemoveFear,
        CallLight,
        FindTraps,
        DetectDoorsStairs,
        SlowPoison,
        BlindCreature,
        Portal,
        CureMediumWounds,
        Chant,
        Sanctuary,
        CreateFood,
        RemoveCurse,
        ResistHeadCold,
        NeutralizePoison,
        OrbOfDraining,
        CureSeriousWounds,
        SenseInvisible,
        ProtectFromEvil,
        Earthquake,
        SenseSurroundings,
        CureCriticalWounds,
        TurnUndead,
        Prayer,
        DispelUndead,
        Heal,
        DispelEvil,
        GlyphOfWarding,
        HolyWord,
    }

    private static bool IsMage(Player py) =>
        DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells == SpellConfig.SpellTypeMage;

    private static Model.Generated.Spell[] ClassSpells(Player py) => DataPlayerData.MagicSpells[py.Misc.ClassId - 1];

    // src/mage_spells.cpp:270 spellChanceOfSuccess() -- returns spell
    // chance of failure for class_to_use_mage_spells -RAK-
    public static int SpellChanceOfSuccess(Player py, int spellId)
    {
        var spell = ClassSpells(py)[spellId];

        var chance = spell.FailureChance - 3 * (py.Misc.Level - spell.LevelRequired);

        var stat = IsMage(py) ? PlayerConfig.Attr.Int : PlayerConfig.Attr.Wis;

        chance -= 3 * (PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, stat) - 1);

        if (spell.ManaRequired > py.Misc.CurrentMana)
        {
            chance += 5 * (spell.ManaRequired - py.Misc.CurrentMana);
        }

        if (chance > 95)
        {
            chance = 95;
        }
        else if (chance < 5)
        {
            chance = 5;
        }

        return chance;
    }

    private static int IndexOf(int[] spellIds, int numberOfChoices, int spellId)
    {
        int testSpellId;
        for (testSpellId = 0; testSpellId < numberOfChoices; testSpellId++)
        {
            if (spellId == spellIds[testSpellId])
            {
                break;
            }
        }
        return testSpellId;
    }

    // src/spells.cpp:11 spellGetId() -- returns spell pointer -RAK-
    private static bool SpellGetId(PlayerWorld w, int[] spellIds, int numberOfChoices, out int spellId, out int spellChance, string prompt, int firstSpell)
    {
        var py = w.Py;
        spellId = -1;
        spellChance = 0;

        var str = $"(Spells {(char)(spellIds[0] + 'a' - firstSpell)}-{(char)(spellIds[numberOfChoices - 1] + 'a' - firstSpell)}, *=List, <ESCAPE>=exit) {prompt}";

        var spellFound = false;
        var redraw = false;

        var offset = IsMage(py) ? SpellConfig.NameOffsetSpells : SpellConfig.NameOffsetPrayers;

        while (!spellFound && PlayerCore.GetCommand(w, str, out var spellChoice))
        {
            if (char.IsAsciiLetterUpper(spellChoice))
            {
                spellId = spellChoice - 'A' + firstSpell;

                // verify that this is in spells[], at most 22 entries in class_to_use_mage_spells[]
                if (IndexOf(spellIds, numberOfChoices, spellId) == numberOfChoices)
                {
                    spellId = -2;
                }
                else
                {
                    var spell = ClassSpells(py)[spellId];

                    var question = $"Cast {DataPlayerData.SpellNames[spellId + offset]} ({spell.ManaRequired} mana, {SpellChanceOfSuccess(py, spellId)}% fail)?";
                    if (PlayerCore.GetInputConfirmation(w, question))
                    {
                        spellFound = true;
                    }
                    else
                    {
                        spellId = -1;
                    }
                }
            }
            else if (char.IsAsciiLetterLower(spellChoice))
            {
                spellId = spellChoice - 'a' + firstSpell;

                // verify that this is in spells[], at most 22 entries in class_to_use_mage_spells[]
                if (IndexOf(spellIds, numberOfChoices, spellId) == numberOfChoices)
                {
                    spellId = -2;
                }
                else
                {
                    spellFound = true;
                }
            }
            else if (spellChoice == '*')
            {
                // only do this drawing once
                if (!redraw && w.Ui is { } ui)
                {
                    ui.TerminalSaveScreen();
                    redraw = true;
                    ui.DisplaySpellsList(spellIds, numberOfChoices, false, firstSpell);
                }
            }
            else
            {
                // (isalpha() can't be true here for an ASCII key)
                spellId = -1;
                w.Ui?.TerminalBellSound();
            }

            if (spellId == -2)
            {
                PlayerCore.PrintMessage(w, $"You don't know that {(offset == SpellConfig.NameOffsetSpells ? "spell" : "prayer")}.");
            }
        }

        if (redraw)
        {
            w.Ui?.TerminalRestoreScreen();
        }

        w.Ui?.MessageLineClear();

        if (spellFound)
        {
            spellChance = SpellChanceOfSuccess(py, spellId);
        }

        return spellFound;
    }

    // src/spells.cpp:109 castSpellGetId() -- return spell number and failure
    // chance -RAK-
    // returns -1 if no spells in book
    // returns  1 if choose a spell in book to cast
    // returns  0 if don't choose a spell, i.e. exit with an escape
    public static int CastSpellGetId(PlayerWorld w, string prompt, int itemId, out int spellId, out int spellChance)
    {
        var py = w.Py;
        spellId = -1;
        spellChance = 0;

        // NOTE: `flags` gets set again, since getAndClearFirstBit modified it
        var flags = py.Inventory[itemId].Flags;
        var firstSpell = Helpers.GetAndClearFirstBit(ref flags);

        // Get flags again since getAndClearFirstBit modified variable.
        flags = py.Inventory[itemId].Flags & py.Flags.SpellsLearnt;

        var spells = ClassSpells(py);

        var spellCount = 0;
        var spellList = new int[31];

        while (flags != 0)
        {
            var pos = Helpers.GetAndClearFirstBit(ref flags);

            if (spells[pos].LevelRequired <= py.Misc.Level)
            {
                spellList[spellCount] = pos;
                spellCount++;
            }
        }

        if (spellCount == 0)
        {
            return -1;
        }

        var result = 0;
        if (SpellGetId(w, spellList, spellCount, out spellId, out spellChance, prompt, firstSpell))
        {
            result = 1;
        }

        if (result != 0 && spells[spellId].ManaRequired > py.Misc.CurrentMana)
        {
            result = IsMage(py)
                ? (PlayerCore.GetInputConfirmation(w, "You summon your limited strength to cast this one! Confirm?") ? 1 : 0)
                : (PlayerCore.GetInputConfirmation(w, "The gods may think you presumptuous for this! Confirm?") ? 1 : 0);
        }

        return result;
    }

    // src/mage_spells.cpp:45 canReadSpells()
    private static bool CanReadSpells(PlayerWorld w)
    {
        var py = w.Py;

        if (py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(w, "You can't see to read your spell book!");
            return false;
        }

        if (PlayerMovement.NoLight(w))
        {
            PlayerCore.PrintMessage(w, "You have no light to read by.");
            return false;
        }

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are too confused.");
            return false;
        }

        if (!IsMage(py))
        {
            PlayerCore.PrintMessage(w, "You can't cast spells!");
            return false;
        }

        return true;
    }

    private static int Roll(int count, int sides) => DiceRoller.Roll(new Model.Generated.Dice((byte)count, (byte)sides));

    private static string SpellName(int index) => DataPlayerData.SpellNames[index];

    // src/mage_spells.cpp:69 castSpell()
    private static void CastSpell(PlayerWorld w, int spellId)
    {
        var py = w.Py;
        int dir;

        switch ((MageSpellId)spellId)
        {
            case MageSpellId.MagicMissile:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBolt(w, py.Pos, dir, Roll(2, 6), MagicSpellFlags.MagicMissile, SpellName(0));
                }
                break;
            case MageSpellId.DetectMonsters:
                DetectionSpells.DetectMonsters(w);
                break;
            case MageSpellId.PhaseDoor:
                PlayerMovement.Teleport(w, 10);
                break;
            case MageSpellId.LightArea:
                DetectionSpells.LightArea(w, py.Pos);
                break;
            case MageSpellId.CureLightWounds:
                SpellEffects.ChangePlayerHitPoints(w, Roll(4, 4));
                break;
            case MageSpellId.FindHiddenTrapsDoors:
                DetectionSpells.DetectSecretDoorsWithinVicinity(w);
                DetectionSpells.DetectTrapsWithinVicinity(w);
                break;
            case MageSpellId.StinkingCloud:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBall(w, py.Pos, dir, 12, MagicSpellFlags.PoisonGas, SpellName(6));
                }
                break;
            case MageSpellId.Confusion:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.ConfuseMonster(w, py.Pos, dir);
                }
                break;
            case MageSpellId.LightningBolt:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBolt(w, py.Pos, dir, Roll(4, 8), MagicSpellFlags.Lightning, SpellName(8));
                }
                break;
            case MageSpellId.TrapDoorDestruction:
                TerrainSpells.DestroyAdjacentDoorsTraps(w);
                break;
            case MageSpellId.Sleep1:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.SleepMonster(w, py.Pos, dir);
                }
                break;
            case MageSpellId.CurePoison:
                PlayerMagic.CurePoison(py);
                break;
            case MageSpellId.TeleportSelf:
                PlayerMovement.Teleport(w, py.Misc.Level * 5);
                break;
            case MageSpellId.RemoveCurse:
                for (var id = 22; id < PlayerConfig.PlayerInventorySize; id++)
                {
                    py.Inventory[id].Flags &= ~TreasureConfig.Flags.TrCursed;
                }
                break;
            case MageSpellId.FrostBolt:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBolt(w, py.Pos, dir, Roll(6, 8), MagicSpellFlags.Frost, SpellName(14));
                }
                break;
            case MageSpellId.WallToMud:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    TerrainSpells.WallToMud(w, py.Pos, dir);
                }
                break;
            case MageSpellId.CreateFood:
                TerrainSpells.CreateFood(w);
                break;
            case MageSpellId.RechargeItem1:
                SpellEffects.RechargeItem(w, 20);
                break;
            case MageSpellId.Sleep2:
                MonsterLife.Sleep(w, py.Pos);
                break;
            case MageSpellId.PolymorphOther:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.PolymorphMonster(w, py.Pos, dir);
                }
                break;
            case MageSpellId.IdentifyItem:
                SpellEffects.IdentifyItem(w);
                break;
            case MageSpellId.Sleep3:
                MonsterStatusSpells.SleepAllMonsters(w);
                break;
            case MageSpellId.FireBolt:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBolt(w, py.Pos, dir, Roll(9, 8), MagicSpellFlags.Fire, SpellName(22));
                }
                break;
            case MageSpellId.SpeedMonster:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.SpeedMonster(w, py.Pos, dir, -1);
                }
                break;
            case MageSpellId.FrostBall:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBall(w, py.Pos, dir, 48, MagicSpellFlags.Frost, SpellName(24));
                }
                break;
            case MageSpellId.RechargeItem2:
                SpellEffects.RechargeItem(w, 60);
                break;
            case MageSpellId.TeleportOther:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.TeleportAwayMonsterInDirection(w, py.Pos, dir);
                }
                break;
            case MageSpellId.HasteSelf:
                py.Flags.Fast = (short)(py.Flags.Fast + RandomNumber.Next(20) + py.Misc.Level);
                break;
            case MageSpellId.FireBall:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBall(w, py.Pos, dir, 72, MagicSpellFlags.Fire, SpellName(28));
                }
                break;
            case MageSpellId.WordOfDestruction:
                TerrainSpells.DestroyArea(w, py.Pos);
                break;
            case MageSpellId.Genocide:
                MonsterStatusSpells.Genocide(w);
                break;
            default:
                // All cases are handled, so this should never be reached!
                break;
        }
    }

    // The mana/experience tail shared by getAndCastMagicSpell() and pray()
    // (the two differ only in the order of the experience lines and the
    // faint message).
    private static void PayForSpell(PlayerWorld w, Model.Generated.Spell spell, string faintMessage)
    {
        var py = w.Py;

        if (spell.ManaRequired > py.Misc.CurrentMana)
        {
            PlayerCore.PrintMessage(w, faintMessage);

            py.Flags.Paralysis = (short)RandomNumber.Next(5 * (spell.ManaRequired - py.Misc.CurrentMana));
            py.Misc.CurrentMana = 0;
            py.Misc.CurrentManaFraction = 0;

            if (RandomNumber.Next(3) == 1)
            {
                PlayerCore.PrintMessage(w, "You have damaged your health!");
                PlayerStatLogic.StatRandomDecrease(py, PlayerConfig.Attr.Con, w.Print, w.Ui);
            }
        }
        else
        {
            py.Misc.CurrentMana = (short)(py.Misc.CurrentMana - spell.ManaRequired);
        }

        w.Ui?.PrintCharacterCurrentMana();
    }

    // src/mage_spells.cpp:202 getAndCastMagicSpell() -- throw a magic spell
    // -RAK-
    public static void GetAndCastMagicSpell(PlayerWorld w)
    {
        var py = w.Py;
        w.Game.PlayerFreeTurn = true;

        if (!CanReadSpells(w))
        {
            return;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvMagicBook, TreasureCategory.TvNever, out var i, out var j))
        {
            PlayerCore.PrintMessage(w, "But you are not carrying any spell-books!");
            return;
        }

        if (!PlayerCore.GetItemId(w, "Use which spell-book?", i, j, out var itemVal))
        {
            return;
        }

        var result = CastSpellGetId(w, "Cast which spell?", itemVal, out var choice, out var chance);
        if (result < 0)
        {
            PlayerCore.PrintMessage(w, "You don't know any spells in that book.");
            return;
        }
        if (result == 0)
        {
            return;
        }

        w.Game.PlayerFreeTurn = false;

        var magicSpell = ClassSpells(py)[choice];

        if (RandomNumber.Next(100) < chance)
        {
            PlayerCore.PrintMessage(w, "You failed to get the spell off!");
        }
        else
        {
            CastSpell(w, choice + 1);

            if (!w.Game.PlayerFreeTurn && (py.Flags.SpellsWorked & (1u << choice)) == 0)
            {
                py.Misc.Exp += magicSpell.ExpGainForLearning << 2;
                py.Flags.SpellsWorked |= 1u << choice;

                PlayerCore.DisplayCharacterExperience(w);
            }
        }

        if (w.Game.PlayerFreeTurn)
        {
            return;
        }

        PayForSpell(w, magicSpell, "You faint from the effort!");
    }

    // src/player_pray.cpp:10 playerCanPray()
    private static bool PlayerCanPray(PlayerWorld w, out int itemPosBegin, out int itemPosEnd)
    {
        var py = w.Py;
        itemPosBegin = 0;
        itemPosEnd = 0;

        if (py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(w, "You can't see to read your prayer!");
            return false;
        }

        if (PlayerMovement.NoLight(w))
        {
            PlayerCore.PrintMessage(w, "You have no light to read by.");
            return false;
        }

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are too confused.");
            return false;
        }

        if (DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells != SpellConfig.SpellTypePriest)
        {
            PlayerCore.PrintMessage(w, "Pray hard enough and your prayers may be answered.");
            return false;
        }

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything!");
            return false;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvPrayerBook, TreasureCategory.TvNever, out itemPosBegin, out itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any Holy Books!");
            return false;
        }

        return true;
    }

    // src/player_pray.cpp:80 playerRecitePrayer() -- recite a prayers.
    private static void RecitePrayer(PlayerWorld w, int prayerType)
    {
        var py = w.Py;
        int dir;

        switch ((PriestSpellTypes)(prayerType + 1))
        {
            case PriestSpellTypes.DetectEvil:
                DetectionSpells.DetectEvil(w);
                break;
            case PriestSpellTypes.CureLightWounds:
                SpellEffects.ChangePlayerHitPoints(w, Roll(3, 3));
                break;
            case PriestSpellTypes.Bless:
                PlayerMagic.Bless(py, RandomNumber.Next(12) + 12);
                break;
            case PriestSpellTypes.RemoveFear:
                PlayerMagic.RemoveFear(py);
                break;
            case PriestSpellTypes.CallLight:
                DetectionSpells.LightArea(w, py.Pos);
                break;
            case PriestSpellTypes.FindTraps:
                DetectionSpells.DetectTrapsWithinVicinity(w);
                break;
            case PriestSpellTypes.DetectDoorsStairs:
                DetectionSpells.DetectSecretDoorsWithinVicinity(w);
                break;
            case PriestSpellTypes.SlowPoison:
                SpellEffects.SlowPoison(w);
                break;
            case PriestSpellTypes.BlindCreature:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    MonsterStatusSpells.ConfuseMonster(w, py.Pos, dir);
                }
                break;
            case PriestSpellTypes.Portal:
                PlayerMovement.Teleport(w, py.Misc.Level * 3);
                break;
            case PriestSpellTypes.CureMediumWounds:
                SpellEffects.ChangePlayerHitPoints(w, Roll(4, 4));
                break;
            case PriestSpellTypes.Chant:
                PlayerMagic.Bless(py, RandomNumber.Next(24) + 24);
                break;
            case PriestSpellTypes.Sanctuary:
                MonsterLife.Sleep(w, py.Pos);
                break;
            case PriestSpellTypes.CreateFood:
                TerrainSpells.CreateFood(w);
                break;
            case PriestSpellTypes.RemoveCurse:
                for (var id = 0; id < py.Inventory.Length; id++)
                {
                    // only clear flag for items that are wielded or worn
                    ref var entry = ref py.Inventory[id];
                    if (entry.CategoryId >= TreasureCategory.TvMinWear && entry.CategoryId <= TreasureCategory.TvMaxWear)
                    {
                        entry.Flags &= ~TreasureConfig.Flags.TrCursed;
                    }
                }
                break;
            case PriestSpellTypes.ResistHeadCold:
                py.Flags.HeatResistance = (short)(py.Flags.HeatResistance + RandomNumber.Next(10) + 10);
                py.Flags.ColdResistance = (short)(py.Flags.ColdResistance + RandomNumber.Next(10) + 10);
                break;
            case PriestSpellTypes.NeutralizePoison:
                PlayerMagic.CurePoison(py);
                break;
            case PriestSpellTypes.OrbOfDraining:
                if (PlayerCore.GetDirectionWithMemory(w, null, out dir))
                {
                    DamageSpells.FireBall(w, py.Pos, dir, Roll(3, 6) + py.Misc.Level, MagicSpellFlags.HolyOrb, "Black Sphere");
                }
                break;
            case PriestSpellTypes.CureSeriousWounds:
                SpellEffects.ChangePlayerHitPoints(w, Roll(8, 4));
                break;
            case PriestSpellTypes.SenseInvisible:
                PlayerMagic.DetectInvisible(py, RandomNumber.Next(24) + 24);
                break;
            case PriestSpellTypes.ProtectFromEvil:
                PlayerMagic.ProtectEvil(py);
                break;
            case PriestSpellTypes.Earthquake:
                DamageSpells.Earthquake(w);
                break;
            case PriestSpellTypes.SenseSurroundings:
                DetectionSpells.MapCurrentArea(w);
                break;
            case PriestSpellTypes.CureCriticalWounds:
                SpellEffects.ChangePlayerHitPoints(w, Roll(16, 4));
                break;
            case PriestSpellTypes.TurnUndead:
                MonsterStatusSpells.TurnUndead(w);
                break;
            case PriestSpellTypes.Prayer:
                PlayerMagic.Bless(py, RandomNumber.Next(48) + 48);
                break;
            case PriestSpellTypes.DispelUndead:
                DamageSpells.DispelCreature(w, MonsterConfig.Defense.CdUndead, 3 * py.Misc.Level);
                break;
            case PriestSpellTypes.Heal:
                SpellEffects.ChangePlayerHitPoints(w, 200);
                break;
            case PriestSpellTypes.DispelEvil:
                DamageSpells.DispelCreature(w, MonsterConfig.Defense.CdEvil, 3 * py.Misc.Level);
                break;
            case PriestSpellTypes.GlyphOfWarding:
                TerrainSpells.WardingGlyph(w);
                break;
            case PriestSpellTypes.HolyWord:
                PlayerMagic.RemoveFear(py);
                PlayerMagic.CurePoison(py);
                SpellEffects.ChangePlayerHitPoints(w, 1000);

                for (var i = PlayerConfig.Attr.Str; i <= PlayerConfig.Attr.Chr; i++)
                {
                    PlayerStatLogic.StatRestore(py, i, w.Print, w.Ui);
                }

                DamageSpells.DispelCreature(w, MonsterConfig.Defense.CdEvil, 4 * py.Misc.Level);
                MonsterStatusSpells.TurnUndead(w);

                if (py.Flags.Invulnerability < 3)
                {
                    py.Flags.Invulnerability = 3;
                }
                else
                {
                    py.Flags.Invulnerability++;
                }
                break;
            default:
                // All cases are handled, so this should never be reached!
                break;
        }
    }

    // src/player_pray.cpp:209 pray() -- pray like HELL. -RAK-
    public static void Pray(PlayerWorld w)
    {
        var py = w.Py;
        w.Game.PlayerFreeTurn = true;

        if (!PlayerCanPray(w, out var itemPosBegin, out var itemPosEnd))
        {
            return;
        }

        if (!PlayerCore.GetItemId(w, "Use which Holy Book?", itemPosBegin, itemPosEnd, out var itemId))
        {
            return;
        }

        var result = CastSpellGetId(w, "Recite which prayer?", itemId, out var choice, out var chance);
        if (result < 0)
        {
            PlayerCore.PrintMessage(w, "You don't know any prayers in that book.");
            return;
        }
        if (result == 0)
        {
            return;
        }

        var spell = ClassSpells(py)[choice];

        // NOTE: at least one function called by `playerRecitePrayer()` sets `player_free_turn = true`,
        // e.g. `spellCreateFood()`, so this check is required. -MRC-
        w.Game.PlayerFreeTurn = false;

        if (RandomNumber.Next(100) < chance)
        {
            PlayerCore.PrintMessage(w, "You lost your concentration!");
        }
        else
        {
            RecitePrayer(w, choice);

            if (!w.Game.PlayerFreeTurn)
            {
                if ((py.Flags.SpellsWorked & (1u << choice)) == 0)
                {
                    py.Misc.Exp += spell.ExpGainForLearning << 2;
                    PlayerCore.DisplayCharacterExperience(w);
                    py.Flags.SpellsWorked |= 1u << choice;
                }
            }
        }

        if (w.Game.PlayerFreeTurn)
        {
            return;
        }

        PayForSpell(w, spell, "You faint from fatigue!");
    }
}
