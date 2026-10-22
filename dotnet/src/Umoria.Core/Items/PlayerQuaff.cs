using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Items;

// src/player_quaff.cpp -- code for potions (M11)
public static class PlayerQuaff
{
    // src/player_quaff.cpp:10 PotionSpellTypes
    private enum PotionSpellTypes
    {
        Strength = 1,
        Weakness,
        RestoreStrength,
        Intelligence,
        LoseIntelligence,
        RestoreIntelligence,
        Wisdom,
        LoseWisdom,
        RestoreWisdom,
        Charisma,
        Ugliness,
        RestoreCharisma,
        CureLightWounds,
        CureSeriousWounds,
        CureCriticalWounds,
        Healing,
        Constitution,
        GainExperience,
        Sleep,
        Blindness,
        Confusion,
        Poison,
        HasteSelf,
        Slowness,
        // 25 not used
        Dexterity = 26,
        RestoreDexterity,
        RestoreConstitution,
        CureBlindness,
        CureConfusion,
        CurePoison,
        // 32 not used
        // 33 not used
        LoseExperience = 34,
        SaltWater,
        Invulnerability,
        Heroism,
        SuperHeroism,
        Boldness,
        RestoreLifeLevels,
        ResistHeat,
        ResistCold,
        DetectInvisible,
        SlowPoison,
        NeutralizePoison,
        RestoreMana,
        InfraVision,
    }

    // a stat gain/restore case: identified (with its message) only if the
    // stat changed; otherwise `identified` keeps its value
    private static bool StatCase(PlayerWorld w, bool changed, string message, bool identified)
    {
        if (!changed)
        {
            return identified;
        }

        PlayerCore.PrintMessage(w, message);
        return true;
    }

    // src/player_quaff.cpp:60 playerDrinkPotion()
    private static bool DrinkPotion(PlayerWorld w, uint flags, byte itemType)
    {
        var py = w.Py;
        var f = py.Flags;
        var m = py.Misc;

        var identified = false;

        while (flags != 0)
        {
            var potionId = Helpers.GetAndClearFirstBit(ref flags) + 1;

            if (itemType == TreasureCategory.TvPotion2)
            {
                potionId += 32;
            }

            // Potions
            switch ((PotionSpellTypes)potionId)
            {
                case PotionSpellTypes.Strength:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Str, w.Print), "Wow!  What bulging muscles!", identified);
                    break;
                case PotionSpellTypes.Weakness:
                    SpellEffects.LoseStr(w);
                    identified = true;
                    break;
                case PotionSpellTypes.RestoreStrength:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Str, w.Print), "You feel warm all over.", identified);
                    break;
                case PotionSpellTypes.Intelligence:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Int, w.Print), "Aren't you brilliant!", identified);
                    break;
                case PotionSpellTypes.LoseIntelligence:
                    SpellEffects.LoseInt(w);
                    identified = true;
                    break;
                case PotionSpellTypes.RestoreIntelligence:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Int, w.Print), "You have have a warm feeling.", identified);
                    break;
                case PotionSpellTypes.Wisdom:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Wis, w.Print), "You suddenly have a profound thought!", identified);
                    break;
                case PotionSpellTypes.LoseWisdom:
                    SpellEffects.LoseWis(w);
                    identified = true;
                    break;
                case PotionSpellTypes.RestoreWisdom:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Wis, w.Print), "You feel your wisdom returning.", identified);
                    break;
                case PotionSpellTypes.Charisma:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Chr, w.Print), "Gee, ain't you cute!", identified);
                    break;
                case PotionSpellTypes.Ugliness:
                    SpellEffects.LoseChr(w);
                    identified = true;
                    break;
                case PotionSpellTypes.RestoreCharisma:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Chr, w.Print), "You feel your looks returning.", identified);
                    break;
                case PotionSpellTypes.CureLightWounds:
                    identified = SpellEffects.ChangePlayerHitPoints(w, DiceRoller.Roll(new Model.Generated.Dice(2, 7)));
                    break;
                case PotionSpellTypes.CureSeriousWounds:
                    identified = SpellEffects.ChangePlayerHitPoints(w, DiceRoller.Roll(new Model.Generated.Dice(4, 7)));
                    break;
                case PotionSpellTypes.CureCriticalWounds:
                    identified = SpellEffects.ChangePlayerHitPoints(w, DiceRoller.Roll(new Model.Generated.Dice(6, 7)));
                    break;
                case PotionSpellTypes.Healing:
                    identified = SpellEffects.ChangePlayerHitPoints(w, 1000);
                    break;
                case PotionSpellTypes.Constitution:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Con, w.Print), "You feel tingly for a moment.", identified);
                    break;
                case PotionSpellTypes.GainExperience:
                    if (m.Exp < PlayerConfig.PlayerMaxExp)
                    {
                        var exp = (uint)((m.Exp / 2) + 10);
                        if (exp > 100000)
                        {
                            exp = 100000;
                        }
                        m.Exp += (int)exp;

                        PlayerCore.PrintMessage(w, "You feel more experienced.");
                        PlayerCore.DisplayCharacterExperience(w);
                        identified = true;
                    }
                    break;
                case PotionSpellTypes.Sleep:
                    if (!f.FreeAction)
                    {
                        // paralysis must == 0, otherwise could not drink potion
                        PlayerCore.PrintMessage(w, "You fall asleep.");
                        f.Paralysis = (short)(f.Paralysis + RandomNumber.Next(4) + 4);
                        identified = true;
                    }
                    break;
                case PotionSpellTypes.Blindness:
                    if (f.Blind == 0)
                    {
                        PlayerCore.PrintMessage(w, "You are covered by a veil of darkness.");
                        identified = true;
                    }
                    f.Blind = (short)(f.Blind + RandomNumber.Next(100) + 100);
                    break;
                case PotionSpellTypes.Confusion:
                    if (f.Confused == 0)
                    {
                        PlayerCore.PrintMessage(w, "Hey!  This is good stuff!  * Hick! *");
                        identified = true;
                    }
                    f.Confused = (short)(f.Confused + RandomNumber.Next(20) + 12);
                    break;
                case PotionSpellTypes.Poison:
                    if (f.Poisoned == 0)
                    {
                        PlayerCore.PrintMessage(w, "You feel very sick.");
                        identified = true;
                    }
                    f.Poisoned = (short)(f.Poisoned + RandomNumber.Next(15) + 10);
                    break;
                case PotionSpellTypes.HasteSelf:
                    if (f.Fast == 0)
                    {
                        identified = true;
                    }
                    f.Fast = (short)(f.Fast + RandomNumber.Next(25) + 15);
                    break;
                case PotionSpellTypes.Slowness:
                    if (f.Slow == 0)
                    {
                        identified = true;
                    }
                    f.Slow = (short)(f.Slow + RandomNumber.Next(25) + 15);
                    break;
                case PotionSpellTypes.Dexterity:
                    identified = StatCase(w, PlayerStatLogic.StatRandomIncrease(py, PlayerConfig.Attr.Dex, w.Print), "You feel more limber!", identified);
                    break;
                case PotionSpellTypes.RestoreDexterity:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Dex, w.Print), "You feel less clumsy.", identified);
                    break;
                case PotionSpellTypes.RestoreConstitution:
                    identified = StatCase(w, PlayerStatLogic.StatRestore(py, PlayerConfig.Attr.Con, w.Print), "You feel your health returning!", identified);
                    break;
                case PotionSpellTypes.CureBlindness:
                    identified = PlayerMagic.CureBlindness(py);
                    break;
                case PotionSpellTypes.CureConfusion:
                    identified = PlayerMagic.CureConfusion(py);
                    break;
                case PotionSpellTypes.CurePoison:
                    identified = PlayerMagic.CurePoison(py);
                    break;
                // case 33: break; // this is no longer useful, now that there is a 'G'ain magic spells command
                case PotionSpellTypes.LoseExperience:
                    if (m.Exp > 0)
                    {
                        PlayerCore.PrintMessage(w, "You feel your memories fade.");

                        // Lose between 1/5 and 2/5 of your experience
                        var exp = m.Exp / 5;

                        if (m.Exp > short.MaxValue)
                        {
                            var scale = int.MaxValue / m.Exp;
                            exp += (RandomNumber.Next(scale) * m.Exp) / (scale * 5);
                        }
                        else
                        {
                            exp += RandomNumber.Next(m.Exp) / 5;
                        }
                        SpellEffects.LoseExp(w, exp);
                        identified = true;
                    }
                    break;
                case PotionSpellTypes.SaltWater:
                    PlayerMagic.CurePoison(py);
                    if (f.Food > 150)
                    {
                        f.Food = 150;
                    }
                    f.Paralysis = 4;

                    PlayerCore.PrintMessage(w, "The potion makes you vomit!");
                    identified = true;
                    break;
                case PotionSpellTypes.Invulnerability:
                    if (f.Invulnerability == 0)
                    {
                        identified = true;
                    }
                    f.Invulnerability = (short)(f.Invulnerability + RandomNumber.Next(10) + 10);
                    break;
                case PotionSpellTypes.Heroism:
                    if (f.Heroism == 0)
                    {
                        identified = true;
                    }
                    f.Heroism = (short)(f.Heroism + RandomNumber.Next(25) + 25);
                    break;
                case PotionSpellTypes.SuperHeroism:
                    if (f.SuperHeroism == 0)
                    {
                        identified = true;
                    }
                    f.SuperHeroism = (short)(f.SuperHeroism + RandomNumber.Next(25) + 25);
                    break;
                case PotionSpellTypes.Boldness:
                    identified = PlayerMagic.RemoveFear(py);
                    break;
                case PotionSpellTypes.RestoreLifeLevels:
                    identified = SpellEffects.RestorePlayerLevels(w);
                    break;
                case PotionSpellTypes.ResistHeat:
                    if (f.HeatResistance == 0)
                    {
                        identified = true;
                    }
                    f.HeatResistance = (short)(f.HeatResistance + RandomNumber.Next(10) + 10);
                    break;
                case PotionSpellTypes.ResistCold:
                    if (f.ColdResistance == 0)
                    {
                        identified = true;
                    }
                    f.ColdResistance = (short)(f.ColdResistance + RandomNumber.Next(10) + 10);
                    break;
                case PotionSpellTypes.DetectInvisible:
                    if (f.DetectInvisible == 0)
                    {
                        identified = true;
                    }
                    PlayerMagic.DetectInvisible(py, RandomNumber.Next(12) + 12);
                    break;
                case PotionSpellTypes.SlowPoison:
                    identified = SpellEffects.SlowPoison(w);
                    break;
                case PotionSpellTypes.NeutralizePoison:
                    identified = PlayerMagic.CurePoison(py);
                    break;
                case PotionSpellTypes.RestoreMana:
                    if (m.CurrentMana < m.Mana)
                    {
                        m.CurrentMana = m.Mana;
                        PlayerCore.PrintMessage(w, "Your feel your head clear.");
                        // (printCharacterCurrentMana() -- display only)
                        identified = true;
                    }
                    break;
                case PotionSpellTypes.InfraVision:
                    if (f.TimedInfra == 0)
                    {
                        PlayerCore.PrintMessage(w, "Your eyes begin to tingle.");
                        identified = true;
                    }
                    f.TimedInfra = (short)(f.TimedInfra + 100 + RandomNumber.Next(100));
                    break;
                default:
                    // All cases are handled, so this should never be reached!
                    PlayerCore.PrintMessage(w, "Internal error in potion()");
                    break;
            }
        }

        return identified;
    }

    // src/player_quaff.cpp:337 quaff() -- potions for the quaffing -RAK-
    public static void Quaff(PlayerWorld w)
    {
        var py = w.Py;

        w.Game.PlayerFreeTurn = true;

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything.");
            return;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvPotion1, TreasureCategory.TvPotion2, out var itemPosBegin, out var itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any potions.");
            return;
        }

        if (!PlayerCore.GetItemId(w, "Quaff which potion?", itemPosBegin, itemPosEnd, out var itemId))
        {
            return;
        }

        w.Game.PlayerFreeTurn = false;

        bool identified;
        var item = py.Inventory[itemId];

        if (item.Flags == 0)
        {
            PlayerCore.PrintMessage(w, "You feel less thirsty.");
            identified = true;
        }
        else
        {
            identified = DrinkPotion(w, item.Flags, item.CategoryId);
        }

        ItemUse.IdentifyOrTry(w, identified, ref itemId);
        item = py.Inventory[itemId];

        PlayerEat.IngestFood(w, item.MiscUse);
        ItemDescription.TypeRemainingCountDescription(w, itemId);
        InventoryDamage.DestroyItem(py, itemId);
    }
}
