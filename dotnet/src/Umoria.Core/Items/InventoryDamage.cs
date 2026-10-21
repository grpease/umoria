using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Items;

// M9: the src/inventory.cpp half of combat damage -- the elemental damage
// primitives (damageFire/Cold/LightningBolt/Acid/CorrodingGas/PoisonedGas)
// shared by monster attacks, monster breath and the fire/acid/corroding-gas
// traps, the pack-item destruction they cause (inventoryDamageItem(),
// inventoryDestroyItem(), the set*() "Pascal set" predicates, armour
// damage in damageMinusAC()), and the item-eating/disenchanting monster
// attacks (inventoryDiminishLightAttack(), inventoryDiminishChargesAttack(),
// executeDisenchantAttack(), inventoryFindRange()).
//
// M8 deferred the item-destroying traps here pending inventoryDamageItem();
// it turned out to be self-contained (it only needs inventoryDestroyItem()),
// so it is ported now rather than waiting for the rest of M11.
//
// damageMinusAC()'s "Your <armour> resists damage!" / "...is damaged!"
// messages need itemDescription() for their text (M11); like M8's pickup
// messages, they still zero game.command_count, but carry the bare catalog
// name for now.
public static class InventoryDamage
{
    // src/inventory.cpp:21 inventoryDestroyItem() -- destroy an item in the
    // inventory -RAK-
    public static void DestroyItem(Player py, int itemId)
    {
        ref var item = ref py.Inventory[itemId];

        if (item.ItemsCount > 1 && item.SubCategoryId <= InventoryConfig.ItemSingleStackMax)
        {
            item.ItemsCount--;
            py.Pack.Weight = (short)(py.Pack.Weight - item.Weight);
        }
        else
        {
            py.Pack.Weight = (short)(py.Pack.Weight - item.Weight * item.ItemsCount);

            for (var i = itemId; i < py.Pack.UniqueItems - 1; i++)
            {
                py.Inventory[i] = py.Inventory[i + 1];
            }

            py.Inventory[py.Pack.UniqueItems - 1] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
            py.Pack.UniqueItems--;
        }

        py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
    }

    // src/inventory.cpp:94 inventoryDamageItem() -- destroys a type of item
    // on a given percent chance -RAK-
    private static int DamageItem(Player py, Func<Inventory, bool> itemType, int chancePercentage)
    {
        var damage = 0;

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            if (itemType(py.Inventory[i]) && RandomNumber.Next(100) < chancePercentage)
            {
                DestroyItem(py, i);
                damage++;
            }
        }

        return damage;
    }

    // src/inventory.cpp:107 inventoryDiminishLightAttack()
    public static bool DiminishLightAttack(PlayerWorld w, bool noticed)
    {
        ref var item = ref w.Py.Inventory[PlayerConfig.Equipment.Light];

        if (item.MiscUse > 0)
        {
            item.MiscUse = (short)(item.MiscUse - (250 + RandomNumber.Next(250)));

            if (item.MiscUse < 1)
            {
                item.MiscUse = 1;
            }

            if (w.Py.Flags.Blind < 1)
            {
                PlayerCore.PrintMessage(w, "Your light dims.");
            }
            else
            {
                noticed = false;
            }
        }
        else
        {
            noticed = false;
        }

        return noticed;
    }

    // src/inventory.cpp:129 inventoryDiminishChargesAttack()
    public static bool DiminishChargesAttack(PlayerWorld w, byte creatureLevel, ref short monsterHp, bool noticed)
    {
        var py = w.Py;
        ref var item = ref py.Inventory[RandomNumber.Next(py.Pack.UniqueItems) - 1];

        var hasCharges = item.CategoryId == TreasureCategory.TvStaff || item.CategoryId == TreasureCategory.TvWand;

        if (hasCharges && item.MiscUse > 0)
        {
            monsterHp = (short)(monsterHp + creatureLevel * item.MiscUse);
            item.MiscUse = 0;
            if (!ObjectIdentification.SpellItemIdentified(item))
            {
                // itemAppendToInscription()
                item.Identification |= IdentificationConfig.IdEmpty;
            }
            PlayerCore.PrintMessage(w, "Energy drains from your pack!");
        }
        else
        {
            noticed = false;
        }

        return noticed;
    }

    // src/inventory.cpp:148 executeDisenchantAttack()
    public static bool ExecuteDisenchantAttack(Player py)
    {
        var itemId = RandomNumber.Next(7) switch
        {
            1 => PlayerConfig.Equipment.Wield,
            2 => PlayerConfig.Equipment.Body,
            3 => PlayerConfig.Equipment.Arm,
            4 => PlayerConfig.Equipment.Outer,
            5 => PlayerConfig.Equipment.Hands,
            6 => PlayerConfig.Equipment.Head,
            _ => PlayerConfig.Equipment.Feet,
        };

        var success = false;
        ref var item = ref py.Inventory[itemId];

        if (item.ToHit > 0)
        {
            item.ToHit = (short)(item.ToHit - RandomNumber.Next(2));

            // don't send it below zero
            if (item.ToHit < 0)
            {
                item.ToHit = 0;
            }
            success = true;
        }
        if (item.ToDamage > 0)
        {
            item.ToDamage = (short)(item.ToDamage - RandomNumber.Next(2));

            // don't send it below zero
            if (item.ToDamage < 0)
            {
                item.ToDamage = 0;
            }
            success = true;
        }
        if (item.ToAc > 0)
        {
            item.ToAc = (short)(item.ToAc - RandomNumber.Next(2));

            // don't send it below zero
            if (item.ToAc < 0)
            {
                item.ToAc = 0;
            }
            success = true;
        }

        return success;
    }

    // src/inventory.cpp:307 inventoryFindRange() -- finds range of item in
    // inventory list -RAK-
    public static bool FindRange(Player py, int itemIdStart, int itemIdEnd, out int j, out int k)
    {
        j = -1;
        k = -1;

        var atEndOfRange = false;

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            int itemId = py.Inventory[i].CategoryId;

            if (!atEndOfRange)
            {
                if (itemId == itemIdStart || itemId == itemIdEnd)
                {
                    atEndOfRange = true;
                    j = i;
                }
            }
            else if (itemId != itemIdStart && itemId != itemIdEnd)
            {
                k = i - 1;
                break;
            }
        }

        if (atEndOfRange && k == -1)
        {
            k = py.Pack.UniqueItems - 1;
        }

        return atEndOfRange;
    }

    // src/inventory.cpp:381 damageMinusAC() -- AC gets worse -RAK-
    // Note: This routine affects magical AC bonuses so that stores can
    // detect the damage.
    private static bool DamageMinusAc(PlayerWorld w, uint typDam)
    {
        var py = w.Py;
        var items = new List<int>(6);

        foreach (var slot in new[]
                 {
                     PlayerConfig.Equipment.Body, PlayerConfig.Equipment.Arm, PlayerConfig.Equipment.Outer,
                     PlayerConfig.Equipment.Hands, PlayerConfig.Equipment.Head,
                     PlayerConfig.Equipment.Feet, // also affect boots
                 })
        {
            if (py.Inventory[slot].CategoryId != TreasureCategory.TvNothing)
            {
                items.Add(slot);
            }
        }

        var minus = false;

        if (items.Count == 0)
        {
            return minus;
        }

        var itemId = items[RandomNumber.Next(items.Count) - 1];
        ref var item = ref py.Inventory[itemId];

        // (itemDescription() -- M11; see the class comment)
        var description = DataTreasureData.GameObjects[item.Id].Name;

        if ((item.Flags & typDam) != 0)
        {
            minus = true;

            PlayerCore.PrintMessage(w, $"Your {description} resists damage!");
        }
        else if (item.Ac + item.ToAc > 0)
        {
            minus = true;

            PlayerCore.PrintMessage(w, $"Your {description} is damaged!");

            item.ToAc--;
            PlayerBonuses.RecalculateBonuses(py);
        }

        return minus;
    }

    // Functions to emulate the original Pascal sets

    // src/inventory.cpp:448 setNull()
    public static bool SetNull(Inventory item) => false;

    // src/inventory.cpp:453 setCorrodableItems()
    private static bool SetCorrodableItems(Inventory item)
    {
        switch (item.CategoryId)
        {
            case TreasureCategory.TvSword:
            case TreasureCategory.TvHelm:
            case TreasureCategory.TvShield:
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvWand:
                return true;
            default:
                return false;
        }
    }

    // src/inventory.cpp:466 setFlammableItems()
    private static bool SetFlammableItems(Inventory item)
    {
        switch (item.CategoryId)
        {
            case TreasureCategory.TvArrow:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvBoots:
            case TreasureCategory.TvGloves:
            case TreasureCategory.TvCloak:
            case TreasureCategory.TvSoftArmor:
                // Items of (RF) should not be destroyed.
                return (item.Flags & TreasureConfig.Flags.TrResFire) == 0;
            case TreasureCategory.TvStaff:
            case TreasureCategory.TvScroll1:
            case TreasureCategory.TvScroll2:
                return true;
            default:
                return false;
        }
    }

    // src/inventory.cpp:487 setAcidAffectedItems()
    private static bool SetAcidAffectedItems(Inventory item)
    {
        switch (item.CategoryId)
        {
            case TreasureCategory.TvMisc:
            case TreasureCategory.TvChest:
                return true;
            case TreasureCategory.TvBolt:
            case TreasureCategory.TvArrow:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvBoots:
            case TreasureCategory.TvGloves:
            case TreasureCategory.TvCloak:
            case TreasureCategory.TvSoftArmor:
                return (item.Flags & TreasureConfig.Flags.TrResAcid) == 0;
            default:
                return false;
        }
    }

    // src/inventory.cpp:507 setFrostDestroyableItems()
    public static bool SetFrostDestroyableItems(Inventory item)
    {
        return item.CategoryId == TreasureCategory.TvPotion1 || item.CategoryId == TreasureCategory.TvPotion2 || item.CategoryId == TreasureCategory.TvFlask;
    }

    // src/inventory.cpp:511 setLightningDestroyableItems()
    public static bool SetLightningDestroyableItems(Inventory item)
    {
        return item.CategoryId == TreasureCategory.TvRing || item.CategoryId == TreasureCategory.TvWand || item.CategoryId == TreasureCategory.TvSpike;
    }

    // src/inventory.cpp:515 setAcidDestroyableItems()
    public static bool SetAcidDestroyableItems(Inventory item)
    {
        switch (item.CategoryId)
        {
            case TreasureCategory.TvArrow:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvBoots:
            case TreasureCategory.TvGloves:
            case TreasureCategory.TvCloak:
            case TreasureCategory.TvHelm:
            case TreasureCategory.TvShield:
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvSoftArmor:
                return (item.Flags & TreasureConfig.Flags.TrResAcid) == 0;
            case TreasureCategory.TvStaff:
            case TreasureCategory.TvScroll1:
            case TreasureCategory.TvScroll2:
            case TreasureCategory.TvFood:
            case TreasureCategory.TvOpenDoor:
            case TreasureCategory.TvClosedDoor:
                return true;
            default:
                return false;
        }
    }

    // src/inventory.cpp:541 setFireDestroyableItems()
    public static bool SetFireDestroyableItems(Inventory item)
    {
        switch (item.CategoryId)
        {
            case TreasureCategory.TvArrow:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvBoots:
            case TreasureCategory.TvGloves:
            case TreasureCategory.TvCloak:
            case TreasureCategory.TvSoftArmor:
                return (item.Flags & TreasureConfig.Flags.TrResFire) == 0;
            case TreasureCategory.TvStaff:
            case TreasureCategory.TvScroll1:
            case TreasureCategory.TvScroll2:
            case TreasureCategory.TvPotion1:
            case TreasureCategory.TvPotion2:
            case TreasureCategory.TvFlask:
            case TreasureCategory.TvFood:
            case TreasureCategory.TvOpenDoor:
            case TreasureCategory.TvClosedDoor:
                return true;
            default:
                return false;
        }
    }

    // src/inventory.cpp:568 damageCorrodingGas() -- corrode the unsuspecting
    // person's armor -RAK-
    public static void DamageCorrodingGas(PlayerWorld w, string creatureName)
    {
        if (!DamageMinusAc(w, TreasureConfig.Flags.TrResAcid))
        {
            PlayerCore.TakesHit(w, RandomNumber.Next(8), creatureName);
        }

        if (DamageItem(w.Py, SetCorrodableItems, 5) > 0)
        {
            PlayerCore.PrintMessage(w, "There is an acrid smell coming from your pack.");
        }
    }

    // src/inventory.cpp:579 damagePoisonedGas() -- poison gas the idiot.
    // -RAK- (ported with the poison gas trap in M8; moved here in M9 for
    // monster breath)
    public static void DamagePoisonedGas(PlayerWorld w, int damage, string creatureName)
    {
        PlayerCore.TakesHit(w, damage, creatureName);

        w.Py.Flags.Poisoned = (short)(w.Py.Flags.Poisoned + 12 + RandomNumber.Next(damage));
    }

    // src/inventory.cpp:586 damageFire() -- burn the fool up. -RAK-
    public static void DamageFire(PlayerWorld w, int damage, string creatureName)
    {
        var f = w.Py.Flags;

        if (f.ResistantToFire)
        {
            damage /= 3;
        }

        if (f.HeatResistance > 0)
        {
            damage /= 3;
        }

        PlayerCore.TakesHit(w, damage, creatureName);

        if (DamageItem(w.Py, SetFlammableItems, 3) > 0)
        {
            PlayerCore.PrintMessage(w, "There is smoke coming from your pack!");
        }
    }

    // src/inventory.cpp:603 damageCold() -- freeze them to death. -RAK-
    public static void DamageCold(PlayerWorld w, int damage, string creatureName)
    {
        var f = w.Py.Flags;

        if (f.ResistantToCold)
        {
            damage /= 3;
        }

        if (f.ColdResistance > 0)
        {
            damage /= 3;
        }

        PlayerCore.TakesHit(w, damage, creatureName);

        if (DamageItem(w.Py, SetFrostDestroyableItems, 5) > 0)
        {
            PlayerCore.PrintMessage(w, "Something shatters inside your pack!");
        }
    }

    // src/inventory.cpp:620 damageLightningBolt() -- lightning bolt the
    // sucker away. -RAK-
    public static void DamageLightningBolt(PlayerWorld w, int damage, string creatureName)
    {
        if (w.Py.Flags.ResistantToLight)
        {
            damage /= 3;
        }

        PlayerCore.TakesHit(w, damage, creatureName);

        if (DamageItem(w.Py, SetLightningDestroyableItems, 3) > 0)
        {
            PlayerCore.PrintMessage(w, "There are sparks coming from your pack!");
        }
    }

    // src/inventory.cpp:633 damageAcid() -- throw acid on the hapless
    // victim -RAK-
    public static void DamageAcid(PlayerWorld w, int damage, string creatureName)
    {
        var flag = 0;

        if (DamageMinusAc(w, TreasureConfig.Flags.TrResAcid))
        {
            flag = 1;
        }

        if (w.Py.Flags.ResistantToAcid)
        {
            flag += 2;
        }

        PlayerCore.TakesHit(w, damage / (flag + 1), creatureName);

        if (DamageItem(w.Py, SetAcidAffectedItems, 3) > 0)
        {
            PlayerCore.PrintMessage(w, "There is an acrid smell coming from your pack!");
        }
    }
}
