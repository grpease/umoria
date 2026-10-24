using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// src/store_inventory.cpp:59 storeItemValue() and its get*BuyPrice()
// helpers -- what an item is worth to a shop keeper, given what the player
// knows about it. Pulled forward from M13 (stores) because
// playerCalculateTotalPoints() (scores.cpp, M12) adds it up over the
// inventory. It is pure: no RNG, no store state (the per-store markups are
// storeItemSellPrice()'s, M13).
public static class ItemValue
{
    // src/store_inventory.cpp:59 storeItemValue()
    public static int StoreItemValue(ObjectIdentification identification, in Inventory item)
    {
        int value;
        var category = item.CategoryId;

        if ((item.Identification & IdentificationConfig.IdDamd) != 0)
        {
            // don't purchase known cursed items
            value = 0;
        }
        else if ((category >= TreasureCategory.TvBow && category <= TreasureCategory.TvSword) ||
                 (category >= TreasureCategory.TvBoots && category <= TreasureCategory.TvSoftArmor))
        {
            value = GetWeaponArmorBuyPrice(item);
        }
        else if (category >= TreasureCategory.TvSlingAmmo && category <= TreasureCategory.TvSpike)
        {
            value = GetAmmoBuyPrice(item);
        }
        else if (category == TreasureCategory.TvScroll1 || category == TreasureCategory.TvScroll2 ||
                 category == TreasureCategory.TvPotion1 || category == TreasureCategory.TvPotion2)
        {
            value = GetPotionScrollBuyPrice(identification, item);
        }
        else if (category == TreasureCategory.TvFood)
        {
            value = GetFoodBuyPrice(identification, item);
        }
        else if (category == TreasureCategory.TvAmulet || category == TreasureCategory.TvRing)
        {
            value = GetRingAmuletBuyPrice(identification, item);
        }
        else if (category == TreasureCategory.TvStaff || category == TreasureCategory.TvWand)
        {
            value = GetWandStaffBuyPrice(identification, item);
        }
        else if (category == TreasureCategory.TvDigging)
        {
            value = GetPickShovelBuyPrice(item);
        }
        else
        {
            value = item.Cost;
        }

        // Multiply value by number of items if it is a group stack item.
        // Do not include torches here.
        if (item.SubCategoryId > InventoryConfig.ItemGroupMin)
        {
            value = value * item.ItemsCount;
        }

        return value;
    }

    // src/store_inventory.cpp:92 getWeaponArmorBuyPrice()
    private static int GetWeaponArmorBuyPrice(in Inventory item)
    {
        if (!ObjectIdentification.SpellItemIdentified(item))
        {
            return DataTreasureData.GameObjects[item.Id].Cost;
        }

        if (item.CategoryId >= TreasureCategory.TvBow && item.CategoryId <= TreasureCategory.TvSword)
        {
            if (item.ToHit < 0 || item.ToDamage < 0 || item.ToAc < 0)
            {
                return 0;
            }

            return item.Cost + (item.ToHit + item.ToDamage + item.ToAc) * 100;
        }

        if (item.ToAc < 0)
        {
            return 0;
        }

        return item.Cost + item.ToAc * 100;
    }

    // src/store_inventory.cpp:112 getAmmoBuyPrice()
    private static int GetAmmoBuyPrice(in Inventory item)
    {
        if (!ObjectIdentification.SpellItemIdentified(item))
        {
            return DataTreasureData.GameObjects[item.Id].Cost;
        }

        if (item.ToHit < 0 || item.ToDamage < 0 || item.ToAc < 0)
        {
            return 0;
        }

        // use 5, because missiles generally appear in groups of 20,
        // so 20 * 5 == 100, which is comparable to weapon bonus above
        return item.Cost + (item.ToHit + item.ToDamage + item.ToAc) * 5;
    }

    // src/store_inventory.cpp:126 getPotionScrollBuyPrice()
    private static int GetPotionScrollBuyPrice(ObjectIdentification identification, in Inventory item)
    {
        if (!identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            return 20;
        }

        return item.Cost;
    }

    // src/store_inventory.cpp:134 getFoodBuyPrice()
    private static int GetFoodBuyPrice(ObjectIdentification identification, in Inventory item)
    {
        if (item.SubCategoryId < InventoryConfig.ItemSingleStackMin + ObjectIdentification.MaxMushrooms &&
            !identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            return 1;
        }

        return item.Cost;
    }

    // src/store_inventory.cpp:142 getRingAmuletBuyPrice()
    private static int GetRingAmuletBuyPrice(ObjectIdentification identification, in Inventory item)
    {
        // player does not know what type of ring/amulet this is
        if (!identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            return 45;
        }

        // player knows what type of ring, but does not know whether it
        // is cursed or not, if refuse to buy cursed objects here, then
        // player can use this to 'identify' cursed objects
        if (!ObjectIdentification.SpellItemIdentified(item))
        {
            return DataTreasureData.GameObjects[item.Id].Cost;
        }

        return item.Cost;
    }

    // src/store_inventory.cpp:158 getWandStaffBuyPrice()
    private static int GetWandStaffBuyPrice(ObjectIdentification identification, in Inventory item)
    {
        if (!identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            if (item.CategoryId == TreasureCategory.TvWand)
            {
                return 50;
            }

            return 70;
        }

        if (ObjectIdentification.SpellItemIdentified(item))
        {
            return item.Cost + (item.Cost / 20) * item.MiscUse;
        }

        return item.Cost;
    }

    // src/store_inventory.cpp:174 getPickShovelBuyPrice()
    private static int GetPickShovelBuyPrice(in Inventory item)
    {
        if (!ObjectIdentification.SpellItemIdentified(item))
        {
            return DataTreasureData.GameObjects[item.Id].Cost;
        }

        if (item.MiscUse < 0)
        {
            return 0;
        }

        // some digging tools start with non-zero `misc_use` values, so only
        // multiply the plusses by 100, make sure result is positive
        var value = item.Cost + (item.MiscUse - DataTreasureData.GameObjects[item.Id].MiscUse) * 100;

        if (value < 0)
        {
            value = 0;
        }

        return value;
    }
}
