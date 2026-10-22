using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Spells;

// M9: the non-damaging src/spells.cpp effects that monster AI and combat
// reach directly -- spellTeleportAwayMonster() (monster teleport spells,
// thieves vanishing), spellTeleportPlayerTo() (teleport-to), and the
// aggravate and experience-drain attacks (spellAggravateMonsters(),
// spellLoseEXP()). The rest of spells.cpp's utility/status group is M10.
//
// M10 added the player self-effects: hit point change, the six stat-loss
// spells, slow poison, enchant, remove curse, restore levels, identify and
// recharge. Identify's "<slot> <description>" message carries the real item
// description since M11 (bare catalog name before that).
public static class SpellEffects
{
    // src/spells.cpp:432 spellAggravateMonsters() -- get all the monsters on
    // the level pissed off. -RAK-
    public static bool AggravateMonsters(PlayerWorld w, int affectDistance)
    {
        var aggravated = false;
        var monsters = w.Monsters.Monsters;

        for (var id = w.Monsters.NextFreeMonsterId - 1; id >= MonsterConfig.MonMinIndexId; id--)
        {
            ref var monster = ref monsters[id];
            monster.SleepCount = 0;

            if (monster.DistanceFromPlayer <= affectDistance && monster.Speed < 2)
            {
                monster.Speed++;
                aggravated = true;
            }
        }

        if (aggravated)
        {
            PlayerCore.PrintMessage(w, "You hear a sudden stirring in the distance!");
        }

        return aggravated;
    }

    // src/spells.cpp:1631 spellTeleportAwayMonster()
    public static void TeleportAwayMonster(PlayerWorld w, int monsterId, int distanceFromPlayer)
    {
        var dg = w.Dg;
        var counter = 0;

        ref var monster = ref w.Monsters.Monsters[monsterId];
        Coord coord;

        do
        {
            do
            {
                coord = new Coord(
                    monster.Pos.Y + (RandomNumber.Next(2 * distanceFromPlayer + 1) - (distanceFromPlayer + 1)),
                    monster.Pos.X + (RandomNumber.Next(2 * distanceFromPlayer + 1) - (distanceFromPlayer + 1)));
            } while (!CoordHelpers.InBounds(dg, coord));

            counter++;
            if (counter > 9)
            {
                counter = 0;
                distanceFromPlayer += 5;
            }
        } while (dg.Floor[coord.Y, coord.X].FeatureId >= TileConstants.MinClosedSpace || dg.Floor[coord.Y, coord.X].CreatureId != 0);

        w.Monsters.MoveCreatureRecord(monster.Pos, coord);
        ScreenRendering.LiteSpot(dg, monster.Pos);

        monster.Pos = coord;

        // this is necessary, because the creature is
        // not currently visible in its new position.
        monster.Lit = false;
        monster.DistanceFromPlayer = (byte)CoordHelpers.DistanceBetween(w.Py.Pos, coord);

        MonsterAi.UpdateVisibility(w, monsterId);
    }

    // src/spells.cpp:1665 spellTeleportPlayerTo() -- teleport player to
    // spell casting creature -RAK-
    public static void TeleportPlayerTo(PlayerWorld w, Coord coord)
    {
        var dg = w.Dg;
        var py = w.Py;

        var distance = 1;
        var counter = 0;

        Coord rndCoord;

        do
        {
            rndCoord = new Coord(
                coord.Y + (RandomNumber.Next(2 * distance + 1) - (distance + 1)),
                coord.X + (RandomNumber.Next(2 * distance + 1) - (distance + 1)));
            counter++;
            if (counter > 9)
            {
                counter = 0;
                distance++;
            }
        } while (!CoordHelpers.InBounds(dg, rndCoord) || dg.Floor[rndCoord.Y, rndCoord.X].FeatureId >= TileConstants.MinClosedSpace ||
                 dg.Floor[rndCoord.Y, rndCoord.X].CreatureId >= 2);

        w.Monsters.MoveCreatureRecord(py.Pos, rndCoord);

        for (var y = py.Pos.Y - 1; y <= py.Pos.Y + 1; y++)
        {
            for (var x = py.Pos.X - 1; x <= py.Pos.X + 1; x++)
            {
                dg.Floor[y, x].TemporaryLight = false;
                ScreenRendering.LiteSpot(dg, new Coord(y, x));
            }
        }

        ScreenRendering.LiteSpot(dg, py.Pos);

        py.Pos = rndCoord;

        DungeonLighting.ResetView(py, dg, w.Treasure, w.Game.Options);

        // light creatures
        MonsterAi.UpdateMonsters(w, false);
    }

    // src/spells.cpp:2166 spellLoseEXP()
    //
    // playerCalculateAllowedSpellsCount()'s "forgotten spells" messages go
    // through PlayerSpells' display-only PrintMessage (see M7/M8); the one
    // in-game caller here, the lose-experience attack, has already zeroed
    // game.command_count with its own message, so that is exact.
    public static void LoseExp(PlayerWorld w, int adjustment)
    {
        var py = w.Py;

        if (adjustment > py.Misc.Exp)
        {
            py.Misc.Exp = 0;
        }
        else
        {
            py.Misc.Exp -= adjustment;
        }
        PlayerCore.DisplayCharacterExperience(w);

        var exp = 0;
        while ((int)(py.BaseExpLevels[exp] * py.Misc.ExperienceFactor / 100) <= py.Misc.Exp)
        {
            exp++;
        }

        // increment exp once more, because level 1 exp is stored in player_base_exp_levels[0]
        exp++;

        if (py.Misc.Level != exp)
        {
            py.Misc.Level = (ushort)exp;

            PlayerStatLogic.CalculateHitPoints(py);

            var spellType = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;

            if (spellType == SpellConfig.SpellTypeMage)
            {
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int, w.Print);
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
            }
            else if (spellType == SpellConfig.SpellTypePriest)
            {
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis, w.Print);
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Wis);
            }
            // (printCharacterLevel(), printCharacterTitle() -- display)
        }
    }

    // src/spells.cpp:1909 spellChangePlayerHitPoints() -- change players hit
    // points in some manner -RAK-
    public static bool ChangePlayerHitPoints(PlayerWorld w, int adjustment)
    {
        var m = w.Py.Misc;

        if (m.CurrentHp >= m.MaxHp)
        {
            return false;
        }

        m.CurrentHp = (short)(m.CurrentHp + adjustment);
        if (m.CurrentHp > m.MaxHp)
        {
            m.CurrentHp = m.MaxHp;
            m.CurrentHpFraction = 0;
        }
        // (printCharacterCurrentHitPoints() -- display)

        adjustment /= 5;

        if (adjustment < 3)
        {
            PlayerCore.PrintMessage(w, adjustment == 0 ? "You feel a little better." : "You feel better.");
        }
        else
        {
            PlayerCore.PrintMessage(w, adjustment < 7 ? "You feel much better." : "You feel very good.");
        }

        return true;
    }

    private static void LoseStat(PlayerWorld w, int stat, bool sustained, string lost, string resisted)
    {
        if (!sustained)
        {
            PlayerStatLogic.StatRandomDecrease(w.Py, stat, w.Print);
            PlayerCore.PrintMessage(w, lost);
        }
        else
        {
            PlayerCore.PrintMessage(w, resisted);
        }
    }

    // src/spells.cpp:2106 spellLoseSTR() -- lose a strength point. -RAK-
    public static void LoseStr(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Str, w.Py.Flags.SustainStr, "You feel very sick.", "You feel sick for a moment,  it passes.");

    // src/spells.cpp:2116 spellLoseINT() -- lose an intelligence point. -RAK-
    public static void LoseInt(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Int, w.Py.Flags.SustainInt, "You become very dizzy.", "You become dizzy for a moment,  it passes.");

    // src/spells.cpp:2126 spellLoseWIS() -- lose a wisdom point. -RAK-
    public static void LoseWis(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Wis, w.Py.Flags.SustainWis, "You feel very naive.", "You feel naive for a moment,  it passes.");

    // src/spells.cpp:2136 spellLoseDEX() -- lose a dexterity point. -RAK-
    public static void LoseDex(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Dex, w.Py.Flags.SustainDex, "You feel very sore.", "You feel sore for a moment,  it passes.");

    // src/spells.cpp:2146 spellLoseCON() -- lose a constitution point. -RAK-
    public static void LoseCon(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Con, w.Py.Flags.SustainCon, "You feel very sick.", "You feel sick for a moment,  it passes.");

    // src/spells.cpp:2156 spellLoseCHR() -- lose a charisma point. -RAK-
    public static void LoseChr(PlayerWorld w) =>
        LoseStat(w, PlayerConfig.Attr.Chr, w.Py.Flags.SustainChr, "Your skin starts to itch.", "Your skin starts to itch, but feels better now.");

    // src/spells.cpp:2202 spellSlowPoison() -- slow poison -RAK-
    public static bool SlowPoison(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.Poisoned > 0)
        {
            f.Poisoned = (short)(f.Poisoned / 2);
            if (f.Poisoned < 1)
            {
                f.Poisoned = 1;
            }
            PlayerCore.PrintMessage(w, "The effect of the poison has been reduced.");
            return true;
        }

        return false;
    }

    // src/spells.cpp:2289 spellEnchantItem() -- enchants a plus onto an
    // item. -RAK- `maxBonusLimit` is the maximum bonus allowed; usually 10,
    // but weapon's maximum damage when enchanting melee weapons to damage.
    public static bool EnchantItem(ref short plusses, short maxBonusLimit)
    {
        // avoid randomNumber(0) call
        if (maxBonusLimit <= 0)
        {
            return false;
        }

        var chance = 0;

        if (plusses > 0)
        {
            chance = plusses;

            // very rarely allow enchantment over limit
            if (RandomNumber.Next(100) == 1)
            {
                chance = RandomNumber.Next(chance) - 1;
            }
        }

        if (RandomNumber.Next(maxBonusLimit) > chance)
        {
            plusses += 1;
            return true;
        }

        return false;
    }

    // src/spells.cpp:2315 spellRemoveCurseFromAllWornItems() -- removes
    // curses from items in inventory -RAK-
    public static bool RemoveCurseFromAllWornItems(PlayerWorld w)
    {
        var py = w.Py;
        var removed = false;

        for (var id = PlayerConfig.Equipment.Wield; id <= PlayerConfig.Equipment.Outer; id++)
        {
            if (PlayerInventory.ItemIsCursed(py.Inventory[id]))
            {
                py.Inventory[id].Flags &= ~TreasureConfig.Flags.TrCursed;
                PlayerBonuses.RecalculateBonuses(py);
                removed = true;
            }
        }

        return removed;
    }

    // src/spells.cpp:2330 spellRestorePlayerLevels() -- restores any drained
    // experience -RAK-
    public static bool RestorePlayerLevels(PlayerWorld w)
    {
        var m = w.Py.Misc;

        if (m.MaxExp > m.Exp)
        {
            PlayerCore.PrintMessage(w, "You feel your life energies returning.");

            // this while loop is not redundant, ptr_exp may reduce the exp level
            while (m.Exp < m.MaxExp)
            {
                m.Exp = m.MaxExp;
                PlayerCore.DisplayCharacterExperience(w);
            }

            return true;
        }

        return false;
    }

    // src/spells.cpp:405 spellIdentifyItem() -- identify an object -RAK-
    public static bool IdentifyItem(PlayerWorld w)
    {
        var py = w.Py;

        if (!PlayerCore.GetItemId(w, "Item you wish identified?", 0, PlayerConfig.PlayerInventorySize, out var itemId))
        {
            return false;
        }

        w.Identification.ItemIdentify(w, ref itemId);

        ref var item = ref py.Inventory[itemId];
        w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);

        var description = ItemDescription.Describe(w.Identification, item, true);

        if (itemId >= PlayerConfig.Equipment.Wield)
        {
            PlayerBonuses.RecalculateBonuses(py);
            PlayerCore.PrintMessage(w, Helpers.Truncate($"{ItemDescription.PlayerItemWearingDescription(itemId)}: {description}", Helpers.MoriaObjDescSize));
        }
        else
        {
            PlayerCore.PrintMessage(w, Helpers.Truncate($"{(char)(itemId + 97)} {description}", Helpers.MoriaObjDescSize));
        }

        return true;
    }

    // src/spells.cpp:1102 spellRechargeItem() -- recharge a wand, staff, or
    // rod. Sometimes the item breaks. -RAK-
    public static bool RechargeItem(PlayerWorld w, int numberOfCharges)
    {
        var py = w.Py;

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvStaff, TreasureCategory.TvWand, out var itemPosStart, out var itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You have nothing to recharge.");
            return false;
        }

        if (!PlayerCore.GetItemId(w, "Recharge which item?", itemPosStart, itemPosEnd, out var itemId))
        {
            return false;
        }

        ref var item = ref py.Inventory[itemId];

        // recharge  I = recharge(20) = 1/6  failure for empty 10th level wand
        // recharge II = recharge(60) = 1/10 failure for empty 10th level wand
        //
        // make it harder to recharge high level, and highly charged wands,
        // note that `fail_chance` can be negative, so check its value before
        // trying to call randomNumber().
        var failChance = numberOfCharges + 50 - item.DepthFirstFound - item.MiscUse;

        // Automatic failure.
        if (failChance < 19)
        {
            failChance = 1;
        }
        else
        {
            failChance = RandomNumber.Next(failChance / 10);
        }

        if (failChance == 1)
        {
            PlayerCore.PrintMessage(w, "There is a bright flash of light.");
            InventoryDamage.DestroyItem(py, itemId);
        }
        else
        {
            numberOfCharges = (numberOfCharges / (item.DepthFirstFound + 2)) + 1;
            item.MiscUse = (short)(item.MiscUse + 2 + RandomNumber.Next(numberOfCharges));

            if (ObjectIdentification.SpellItemIdentified(item))
            {
                ObjectIdentification.SpellItemRemoveIdentification(ref item);
            }

            ObjectIdentification.ItemIdentificationClearEmpty(ref item);
        }

        return true;
    }
}
