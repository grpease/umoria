using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Items;

// src/player_eat.cpp -- food code (M11). The item is chosen through
// PlayerCore.GetItemId, like the casting layer's books.
public static class PlayerEat
{
    // src/player_eat.cpp:10 FoodMagicTypes
    private enum FoodMagicTypes
    {
        Poison = 1,
        Blindness,
        Paranoia,
        Confusion,
        Hallucination,
        CurePoison,
        CureBlindness,
        CureParanoia,
        CureConfusion,
        Weakness,
        Unhealth,
        // 12-15 are no longer used
        RestoreStr = 16,
        RestoreCon,
        RestoreInt,
        RestoreWis,
        RestoreDex,
        RestoreChr,
        FirstAid,
        MinorCures,
        LightCures,
        // 25 no longer used
        MajorCures = 26,
        PoisonousFood,
    }

    // src/player_eat.cpp:38 playerEat() -- eat some food. -RAK-
    public static void Eat(PlayerWorld w)
    {
        var py = w.Py;
        var f = py.Flags;

        w.Game.PlayerFreeTurn = true;

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "But you are not carrying anything.");
            return;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvFood, TreasureCategory.TvNever, out var itemPosStart, out var itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any food.");
            return;
        }

        if (!PlayerCore.GetItemId(w, "Eat what?", itemPosStart, itemPosEnd, out var itemId))
        {
            return;
        }

        w.Game.PlayerFreeTurn = false;

        var identified = false;

        var item = py.Inventory[itemId];
        var itemFlags = item.Flags;

        while (itemFlags != 0)
        {
            switch ((FoodMagicTypes)(Helpers.GetAndClearFirstBit(ref itemFlags) + 1))
            {
                case FoodMagicTypes.Poison:
                    f.Poisoned = (short)(f.Poisoned + RandomNumber.Next(10) + item.DepthFirstFound);
                    identified = true;
                    break;
                case FoodMagicTypes.Blindness:
                    f.Blind = (short)(f.Blind + RandomNumber.Next(250) + 10 * item.DepthFirstFound + 100);
                    PlayerCore.DrawCavePanel(w);
                    PlayerCore.PrintMessage(w, "A veil of darkness surrounds you.");
                    identified = true;
                    break;
                case FoodMagicTypes.Paranoia:
                    f.Afraid = (short)(f.Afraid + RandomNumber.Next(10) + item.DepthFirstFound);
                    PlayerCore.PrintMessage(w, "You feel terrified!");
                    identified = true;
                    break;
                case FoodMagicTypes.Confusion:
                    f.Confused = (short)(f.Confused + RandomNumber.Next(10) + item.DepthFirstFound);
                    PlayerCore.PrintMessage(w, "You feel drugged.");
                    identified = true;
                    break;
                case FoodMagicTypes.Hallucination:
                    f.Image = (short)(f.Image + RandomNumber.Next(200) + 25 * item.DepthFirstFound + 200);
                    PlayerCore.PrintMessage(w, "You feel drugged.");
                    identified = true;
                    break;
                case FoodMagicTypes.CurePoison:
                    identified = PlayerMagic.CurePoison(py);
                    break;
                case FoodMagicTypes.CureBlindness:
                    identified = PlayerMagic.CureBlindness(py);
                    break;
                case FoodMagicTypes.CureParanoia:
                    if (f.Afraid > 1)
                    {
                        f.Afraid = 1;
                        identified = true;
                    }
                    break;
                case FoodMagicTypes.CureConfusion:
                    identified = PlayerMagic.CureConfusion(py);
                    break;
                case FoodMagicTypes.Weakness:
                    SpellEffects.LoseStr(w);
                    identified = true;
                    break;
                case FoodMagicTypes.Unhealth:
                    SpellEffects.LoseCon(w);
                    identified = true;
                    break;
                case FoodMagicTypes.RestoreStr:
                    identified |= Restore(w, PlayerConfig.Attr.Str, "You feel your strength returning.");
                    break;
                case FoodMagicTypes.RestoreCon:
                    identified |= Restore(w, PlayerConfig.Attr.Con, "You feel your health returning.");
                    break;
                case FoodMagicTypes.RestoreInt:
                    identified |= Restore(w, PlayerConfig.Attr.Int, "Your head spins a moment.");
                    break;
                case FoodMagicTypes.RestoreWis:
                    identified |= Restore(w, PlayerConfig.Attr.Wis, "You feel your wisdom returning.");
                    break;
                case FoodMagicTypes.RestoreDex:
                    identified |= Restore(w, PlayerConfig.Attr.Dex, "You feel more dexterous.");
                    break;
                case FoodMagicTypes.RestoreChr:
                    identified |= Restore(w, PlayerConfig.Attr.Chr, "Your skin stops itching.");
                    break;
                case FoodMagicTypes.FirstAid:
                    identified = SpellEffects.ChangePlayerHitPoints(w, RandomNumber.Next(6));
                    break;
                case FoodMagicTypes.MinorCures:
                    identified = SpellEffects.ChangePlayerHitPoints(w, RandomNumber.Next(12));
                    break;
                case FoodMagicTypes.LightCures:
                    identified = SpellEffects.ChangePlayerHitPoints(w, RandomNumber.Next(18));
                    break;
                case FoodMagicTypes.MajorCures:
                    identified = SpellEffects.ChangePlayerHitPoints(w, DiceRoller.Roll(new Model.Generated.Dice(3, 12)));
                    break;
                case FoodMagicTypes.PoisonousFood:
                    PlayerCore.TakesHit(w, RandomNumber.Next(18), "poisonous food.");
                    identified = true;
                    break;
                default:
                    // All cases are handled, so this should never be reached!
                    PlayerCore.PrintMessage(w, "Internal error in playerEat()");
                    break;
            }
        }

        ItemUse.IdentifyOrTry(w, identified, ref itemId);
        item = py.Inventory[itemId];

        IngestFood(w, item.MiscUse);

        f.Status &= ~(PlayerConfig.Status.PyWeak | PlayerConfig.Status.PyHungry);

        // (printCharacterHungerStatus() -- display only)

        ItemDescription.TypeRemainingCountDescription(w, itemId);
        InventoryDamage.DestroyItem(py, itemId);
    }

    // the restore-stat cases: identified only if the stat was drained. (In
    // the original each case is its own `if` that sets identified = true,
    // never false -- hence `|=` at the call sites.)
    private static bool Restore(PlayerWorld w, int stat, string message)
    {
        if (!PlayerStatLogic.StatRestore(w.Py, stat, w.Print))
        {
            return false;
        }

        PlayerCore.PrintMessage(w, message);
        return true;
    }

    // src/player_eat.cpp:236 playerIngestFood() -- add to the players food
    // time -RAK-
    public static void IngestFood(PlayerWorld w, int amount)
    {
        var f = w.Py.Flags;

        if (f.Food < 0)
        {
            f.Food = 0;
        }

        f.Food = (short)(f.Food + amount);

        if (f.Food > PlayerConfig.PlayerFoodMax)
        {
            PlayerCore.PrintMessage(w, "You are bloated from overeating.");

            // Calculate how much of amount is responsible for the bloating. Give the
            // player food credit for 1/50, and also slow them for that many turns.
            var extra = f.Food - PlayerConfig.PlayerFoodMax;
            if (extra > amount)
            {
                extra = amount;
            }
            var penalty = extra / 50;

            f.Slow = (short)(f.Slow + penalty);

            if (extra == amount)
            {
                f.Food = (short)(f.Food - amount + penalty);
            }
            else
            {
                f.Food = (short)(PlayerConfig.PlayerFoodMax + penalty);
            }
        }
        else if (f.Food > PlayerConfig.PlayerFoodFull)
        {
            PlayerCore.PrintMessage(w, "You are full.");
        }
    }
}
