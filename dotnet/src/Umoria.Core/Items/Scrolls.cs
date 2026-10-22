using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Dice;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;

namespace Umoria.Core.Items;

// src/scrolls.cpp -- reading scrolls (M11). Every spell a scroll casts was
// ported in M9/M10 (Spells.*); this is the dispatch plus the enchant/curse
// scrolls, which work on equipment directly.
public static class Scrolls
{
    // src/scrolls.cpp:10 playerCanReadScroll()
    private static bool PlayerCanReadScroll(PlayerWorld w, out int itemPosStart, out int itemPosEnd)
    {
        var py = w.Py;
        itemPosStart = 0;
        itemPosEnd = 0;

        if (py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(w, "You can't see to read the scroll.");
            return false;
        }

        if (PlayerMovement.NoLight(w))
        {
            PlayerCore.PrintMessage(w, "You have no light to read by.");
            return false;
        }

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(w, "You are too confused to read a scroll.");
            return false;
        }

        if (py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "You are not carrying anything!");
            return false;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvScroll1, TreasureCategory.TvScroll2, out itemPosStart, out itemPosEnd))
        {
            PlayerCore.PrintMessage(w, "You are not carrying any scrolls!");
            return false;
        }

        return true;
    }

    // src/scrolls.cpp:39 inventoryItemIdOfCursedEquipment() -- a random worn
    // armour piece, unless one is cursed (then the first cursed one)
    private static int ItemIdOfCursedEquipment(Player py)
    {
        var items = new List<int>(6);

        foreach (var slot in new[]
                 {
                     PlayerConfig.Equipment.Body, PlayerConfig.Equipment.Arm, PlayerConfig.Equipment.Outer,
                     PlayerConfig.Equipment.Hands, PlayerConfig.Equipment.Head,
                     // also enchant boots
                     PlayerConfig.Equipment.Feet,
                 })
        {
            if (py.Inventory[slot].CategoryId != TreasureCategory.TvNothing)
            {
                items.Add(slot);
            }
        }

        var itemId = 0;

        if (items.Count > 0)
        {
            itemId = items[RandomNumber.Next(items.Count) - 1];
        }

        // (sic: head before hands here, the other way round above)
        if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Body))
        {
            itemId = PlayerConfig.Equipment.Body;
        }
        else if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Arm))
        {
            itemId = PlayerConfig.Equipment.Arm;
        }
        else if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Outer))
        {
            itemId = PlayerConfig.Equipment.Outer;
        }
        else if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Head))
        {
            itemId = PlayerConfig.Equipment.Head;
        }
        else if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Hands))
        {
            itemId = PlayerConfig.Equipment.Hands;
        }
        else if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Feet))
        {
            itemId = PlayerConfig.Equipment.Feet;
        }

        return itemId;
    }

    private static void Glows(PlayerWorld w, in Inventory item, string how)
    {
        var desc = ItemDescription.Describe(w.Identification, item, false);
        PlayerCore.PrintMessage(w, Helpers.Truncate($"Your {desc} {how}", Helpers.MoriaObjDescSize));
    }

    // enchant one of a worn item's plusses; on success the curse goes
    private static void EnchantOnce(PlayerWorld w, int slot, Func<Inventory, short> get, Action<short> set, short limit)
    {
        var py = w.Py;
        var plusses = get(py.Inventory[slot]);
        var enchanted = SpellEffects.EnchantItem(ref plusses, limit);
        set(plusses);

        if (enchanted)
        {
            InventoryActions.ItemRemoveCurse(ref py.Inventory[slot]);
            PlayerBonuses.RecalculateBonuses(py);
        }
        else
        {
            PlayerCore.PrintMessage(w, "The enchantment fails.");
        }
    }

    // Bows' and arrows' enchantments should not be limited by their low
    // base damages
    private static short DamageEnchantLimit(in Inventory item)
    {
        return item.CategoryId >= TreasureCategory.TvHafted && item.CategoryId <= TreasureCategory.TvDigging
            ? (short)DiceRoller.MaxRoll(item.Damage)
            : (short)10;
    }

    // src/scrolls.cpp:92 scrollEnchantWeaponToHit()
    private static bool EnchantWeaponToHit(PlayerWorld w)
    {
        var py = w.Py;
        const int slot = PlayerConfig.Equipment.Wield;

        if (py.Inventory[slot].CategoryId == TreasureCategory.TvNothing)
        {
            return false;
        }

        Glows(w, py.Inventory[slot], "glows faintly!");
        EnchantOnce(w, slot, i => i.ToHit, v => py.Inventory[slot].ToHit = v, 10);

        return true;
    }

    // src/scrolls.cpp:116 scrollEnchantWeaponToDamage()
    private static bool EnchantWeaponToDamage(PlayerWorld w)
    {
        var py = w.Py;
        const int slot = PlayerConfig.Equipment.Wield;

        if (py.Inventory[slot].CategoryId == TreasureCategory.TvNothing)
        {
            return false;
        }

        Glows(w, py.Inventory[slot], "glows faintly!");
        EnchantOnce(w, slot, i => i.ToDamage, v => py.Inventory[slot].ToDamage = v, DamageEnchantLimit(py.Inventory[slot]));

        return true;
    }

    // src/scrolls.cpp:150 scrollEnchantItemToAC()
    private static bool EnchantItemToAc(PlayerWorld w)
    {
        var py = w.Py;
        var slot = ItemIdOfCursedEquipment(py);

        if (slot <= 0)
        {
            return false;
        }

        Glows(w, py.Inventory[slot], "glows faintly!");
        EnchantOnce(w, slot, i => i.ToAc, v => py.Inventory[slot].ToAc = v, 10);

        return true;
    }

    // src/scrolls.cpp:176 scrollIdentifyItem()
    private static int IdentifyItem(PlayerWorld w, int itemId, out bool isUsedUp)
    {
        var py = w.Py;

        PlayerCore.PrintMessage(w, "This is an identify scroll.");

        isUsedUp = SpellEffects.IdentifyItem(w);

        // The identify may merge objects, causing the identify scroll
        // to move to a different place.  Check for that here.  It can
        // move arbitrarily far if an identify scroll was used on
        // another identify scroll, but it always moves down.
        while (itemId > 0 && (py.Inventory[itemId].CategoryId != TreasureCategory.TvScroll1 || py.Inventory[itemId].Flags != 0x00000008))
        {
            itemId--;
        }

        return itemId;
    }

    // src/scrolls.cpp:194 scrollRemoveCurse()
    private static bool RemoveCurse(PlayerWorld w)
    {
        if (SpellEffects.RemoveCurseFromAllWornItems(w))
        {
            PlayerCore.PrintMessage(w, "You feel as if someone is watching over you.");
            return true;
        }
        return false;
    }

    // src/scrolls.cpp:202 scrollSummonMonster() / :400 scrollSummonUndead()
    // -- (randomNumber(3) is re-rolled on every pass of the loop test)
    private static bool Summon(PlayerWorld w, bool undead)
    {
        var py = w.Py;
        var identified = false;

        for (var i = 0; i < RandomNumber.Next(3); i++)
        {
            var coord = py.Pos;
            identified |= undead
                ? w.Monsters.SummonUndead(ref coord, py.Pos, py.Flags.Speed)
                : w.Monsters.Summon(ref coord, false, py.Pos, py.Flags.Speed);
        }

        return identified;
    }

    // src/scrolls.cpp:215 scrollTeleportLevel()
    private static void TeleportLevel(PlayerWorld w)
    {
        var dg = w.Dg;

        dg.CurrentLevel += -3 + 2 * RandomNumber.Next(2);
        if (dg.CurrentLevel < 1)
        {
            dg.CurrentLevel = 1;
        }
        dg.GenerateNewLevel = true;
    }

    // src/scrolls.cpp:223 scrollConfuseMonster()
    private static bool ConfuseMonster(PlayerWorld w)
    {
        if (!w.Py.Flags.ConfuseMonster)
        {
            PlayerCore.PrintMessage(w, "Your hands begin to glow.");
            w.Py.Flags.ConfuseMonster = true;
            return true;
        }
        return false;
    }

    // src/scrolls.cpp:232 scrollEnchantWeapon()
    private static bool EnchantWeapon(PlayerWorld w)
    {
        var py = w.Py;
        const int slot = PlayerConfig.Equipment.Wield;

        if (py.Inventory[slot].CategoryId == TreasureCategory.TvNothing)
        {
            return false;
        }

        Glows(w, py.Inventory[slot], "glows brightly!");

        var enchanted = false;

        for (var i = 0; i < RandomNumber.Next(2); i++)
        {
            var toHit = py.Inventory[slot].ToHit;
            if (SpellEffects.EnchantItem(ref toHit, 10))
            {
                enchanted = true;
            }
            py.Inventory[slot].ToHit = toHit;
        }

        var limit = DamageEnchantLimit(py.Inventory[slot]);

        for (var i = 0; i < RandomNumber.Next(2); i++)
        {
            var toDamage = py.Inventory[slot].ToDamage;
            if (SpellEffects.EnchantItem(ref toDamage, limit))
            {
                enchanted = true;
            }
            py.Inventory[slot].ToDamage = toDamage;
        }

        if (enchanted)
        {
            InventoryActions.ItemRemoveCurse(ref py.Inventory[slot]);
            PlayerBonuses.RecalculateBonuses(py);
        }
        else
        {
            PlayerCore.PrintMessage(w, "The enchantment fails.");
        }

        return true;
    }

    // src/scrolls.cpp:280 scrollCurseWeapon()
    private static bool CurseWeapon(PlayerWorld w)
    {
        var py = w.Py;
        ref var item = ref py.Inventory[PlayerConfig.Equipment.Wield];

        if (item.CategoryId == TreasureCategory.TvNothing)
        {
            return false;
        }

        Glows(w, item, "glows black, fades.");

        ObjectIdentification.ItemRemoveMagicNaming(ref item);

        item.ToHit = (short)(-RandomNumber.Next(5) - RandomNumber.Next(5));
        item.ToDamage = (short)(-RandomNumber.Next(5) - RandomNumber.Next(5));
        item.ToAc = 0;

        // Must call playerAdjustBonusesForItem() before set (clear) flags, and
        // must call playerRecalculateBonuses() after set (clear) flags, so that
        // all attributes will be properly turned off.
        PlayerCombat.AdjustBonusesForItem(w, item, -1);
        item.Flags = TreasureConfig.Flags.TrCursed;
        PlayerBonuses.RecalculateBonuses(py);

        return true;
    }

    // src/scrolls.cpp:310 scrollEnchantArmor()
    private static bool EnchantArmor(PlayerWorld w)
    {
        var py = w.Py;
        var slot = ItemIdOfCursedEquipment(py);

        if (slot <= 0)
        {
            return false;
        }

        Glows(w, py.Inventory[slot], "glows brightly!");

        var enchanted = false;

        for (var i = 0; i < RandomNumber.Next(2) + 1; i++)
        {
            var toAc = py.Inventory[slot].ToAc;
            if (SpellEffects.EnchantItem(ref toAc, 10))
            {
                enchanted = true;
            }
            py.Inventory[slot].ToAc = toAc;
        }

        if (enchanted)
        {
            InventoryActions.ItemRemoveCurse(ref py.Inventory[slot]);
            PlayerBonuses.RecalculateBonuses(py);
        }
        else
        {
            PlayerCore.PrintMessage(w, "The enchantment fails.");
        }

        return true;
    }

    // src/scrolls.cpp:344 scrollCurseArmor()
    private static bool CurseArmor(PlayerWorld w)
    {
        var py = w.Py;

        bool Worn(int slot) => py.Inventory[slot].CategoryId != TreasureCategory.TvNothing;

        int itemId;

        // (the randomNumber() rolls only happen for worn slots, left to right)
        if (Worn(PlayerConfig.Equipment.Body) && RandomNumber.Next(4) == 1)
        {
            itemId = PlayerConfig.Equipment.Body;
        }
        else if (Worn(PlayerConfig.Equipment.Arm) && RandomNumber.Next(3) == 1)
        {
            itemId = PlayerConfig.Equipment.Arm;
        }
        else if (Worn(PlayerConfig.Equipment.Outer) && RandomNumber.Next(3) == 1)
        {
            itemId = PlayerConfig.Equipment.Outer;
        }
        else if (Worn(PlayerConfig.Equipment.Head) && RandomNumber.Next(3) == 1)
        {
            itemId = PlayerConfig.Equipment.Head;
        }
        else if (Worn(PlayerConfig.Equipment.Hands) && RandomNumber.Next(3) == 1)
        {
            itemId = PlayerConfig.Equipment.Hands;
        }
        else if (Worn(PlayerConfig.Equipment.Feet) && RandomNumber.Next(3) == 1)
        {
            itemId = PlayerConfig.Equipment.Feet;
        }
        else if (Worn(PlayerConfig.Equipment.Body))
        {
            itemId = PlayerConfig.Equipment.Body;
        }
        else if (Worn(PlayerConfig.Equipment.Arm))
        {
            itemId = PlayerConfig.Equipment.Arm;
        }
        else if (Worn(PlayerConfig.Equipment.Outer))
        {
            itemId = PlayerConfig.Equipment.Outer;
        }
        else if (Worn(PlayerConfig.Equipment.Head))
        {
            itemId = PlayerConfig.Equipment.Head;
        }
        else if (Worn(PlayerConfig.Equipment.Hands))
        {
            itemId = PlayerConfig.Equipment.Hands;
        }
        else if (Worn(PlayerConfig.Equipment.Feet))
        {
            itemId = PlayerConfig.Equipment.Feet;
        }
        else
        {
            itemId = 0;
        }

        if (itemId <= 0)
        {
            return false;
        }

        ref var item = ref py.Inventory[itemId];

        Glows(w, item, "glows black, fades.");

        ObjectIdentification.ItemRemoveMagicNaming(ref item);

        item.Flags = TreasureConfig.Flags.TrCursed;
        item.ToHit = 0;
        item.ToDamage = 0;
        item.ToAc = (short)(-RandomNumber.Next(5) - RandomNumber.Next(5));

        PlayerBonuses.RecalculateBonuses(py);

        return true;
    }

    // src/scrolls.cpp:413 scrollWordOfRecall()
    private static void WordOfRecall(PlayerWorld w)
    {
        var f = w.Py.Flags;

        if (f.WordOfRecall == 0)
        {
            f.WordOfRecall = (short)(25 + RandomNumber.Next(30));
        }
        PlayerCore.PrintMessage(w, "The air about you becomes charged.");
    }

    // src/scrolls.cpp:421 scrollRead() -- scrolls for the reading -RAK-
    public static void Read(PlayerWorld w)
    {
        var py = w.Py;

        w.Game.PlayerFreeTurn = true;

        if (!PlayerCanReadScroll(w, out var itemPosStart, out var itemPosEnd))
        {
            return;
        }

        if (!PlayerCore.GetItemId(w, "Read which scroll?", itemPosStart, itemPosEnd, out var itemId))
        {
            return;
        }

        // From here on, no free turn for the player
        w.Game.PlayerFreeTurn = false;

        var usedUp = true;
        var identified = false;

        // (the original tests the category through a pointer to the slot
        // first chosen, which identify's stack merge doesn't move -- only
        // item_id follows the scroll)
        var slot = itemId;
        var itemFlags = py.Inventory[slot].Flags;

        while (itemFlags != 0)
        {
            var scrollType = Helpers.GetAndClearFirstBit(ref itemFlags) + 1;

            if (py.Inventory[slot].CategoryId == TreasureCategory.TvScroll2)
            {
                scrollType += 32;
            }

            switch (scrollType)
            {
                case 1:
                    identified = EnchantWeaponToHit(w);
                    break;
                case 2:
                    identified = EnchantWeaponToDamage(w);
                    break;
                case 3:
                    identified = EnchantItemToAc(w);
                    break;
                case 4:
                    itemId = IdentifyItem(w, itemId, out usedUp);
                    identified = true;
                    break;
                case 5:
                    identified = RemoveCurse(w);
                    break;
                case 6:
                    identified = DetectionSpells.LightArea(w, py.Pos);
                    break;
                case 7:
                    identified = Summon(w, false);
                    break;
                case 8:
                    PlayerMovement.Teleport(w, 10); // Teleport Short, aka Phase Door
                    identified = true;
                    break;
                case 9:
                    PlayerMovement.Teleport(w, 100); // Teleport Long
                    identified = true;
                    break;
                case 10:
                    TeleportLevel(w);
                    identified = true;
                    break;
                case 11:
                    identified = ConfuseMonster(w);
                    break;
                case 12:
                    DetectionSpells.MapCurrentArea(w);
                    identified = true;
                    break;
                case 13:
                    identified = MonsterLife.Sleep(w, py.Pos);
                    break;
                case 14:
                    TerrainSpells.WardingGlyph(w);
                    identified = true;
                    break;
                case 15:
                    identified = DetectionSpells.DetectTreasureWithinVicinity(w);
                    break;
                case 16:
                    identified = DetectionSpells.DetectObjectsWithinVicinity(w);
                    break;
                case 17:
                    identified = DetectionSpells.DetectTrapsWithinVicinity(w);
                    break;
                case 18:
                    identified = DetectionSpells.DetectSecretDoorsWithinVicinity(w);
                    break;
                case 19:
                    PlayerCore.PrintMessage(w, "This is a mass genocide scroll.");
                    MonsterStatusSpells.MassGenocide(w);
                    identified = true;
                    break;
                case 20:
                    identified = DetectionSpells.DetectInvisibleCreaturesWithinVicinity(w);
                    break;
                case 21:
                    PlayerCore.PrintMessage(w, "There is a high pitched humming noise.");
                    SpellEffects.AggravateMonsters(w, 20);
                    identified = true;
                    break;
                case 22:
                    identified = TerrainSpells.SurroundPlayerWithTraps(w);
                    break;
                case 23:
                    identified = TerrainSpells.DestroyAdjacentDoorsTraps(w);
                    break;
                case 24:
                    identified = TerrainSpells.SurroundPlayerWithDoors(w);
                    break;
                case 25:
                    PlayerCore.PrintMessage(w, "This is a Recharge-Item scroll.");
                    usedUp = SpellEffects.RechargeItem(w, 60);
                    identified = true;
                    break;
                case 26:
                    PlayerCore.PrintMessage(w, "This is a genocide scroll.");
                    MonsterStatusSpells.Genocide(w);
                    identified = true;
                    break;
                case 27:
                    identified = DetectionSpells.DarkenArea(w, py.Pos);
                    break;
                case 28:
                    identified = PlayerMagic.ProtectEvil(py);
                    break;
                case 29:
                    TerrainSpells.CreateFood(w);
                    identified = true;
                    break;
                case 30:
                    identified = DamageSpells.DispelCreature(w, MonsterConfig.Defense.CdUndead, 60);
                    break;
                case 33:
                    identified = EnchantWeapon(w);
                    break;
                case 34:
                    identified = CurseWeapon(w);
                    break;
                case 35:
                    identified = EnchantArmor(w);
                    break;
                case 36:
                    identified = CurseArmor(w);
                    break;
                case 37:
                    identified = Summon(w, true);
                    break;
                case 38:
                    PlayerMagic.Bless(py, RandomNumber.Next(12) + 6);
                    identified = true;
                    break;
                case 39:
                    PlayerMagic.Bless(py, RandomNumber.Next(24) + 12);
                    identified = true;
                    break;
                case 40:
                    PlayerMagic.Bless(py, RandomNumber.Next(48) + 24);
                    identified = true;
                    break;
                case 41:
                    WordOfRecall(w);
                    identified = true;
                    break;
                case 42:
                    TerrainSpells.DestroyArea(w, py.Pos);
                    identified = true;
                    break;
                default:
                    PlayerCore.PrintMessage(w, "Internal error in scroll()");
                    break;
            }
        }

        ItemUse.IdentifyOrTry(w, identified, ref itemId);

        if (usedUp)
        {
            ItemDescription.TypeRemainingCountDescription(w, itemId);
            InventoryDamage.DestroyItem(py, itemId);
        }
    }
}
