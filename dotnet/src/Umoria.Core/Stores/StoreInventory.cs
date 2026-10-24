using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Stores;

// src/store_inventory.cpp -- updating store inventory, pricing objects.
// (M13) storeItemValue() itself was pulled forward into Items.ItemValue by
// M12 (the score adds it up); the per-store markups are here.
//
// Real call graph (read, not assumed): storeItemCreate() makes its stock in
// a borrowed game.treasure.list slot (popt()/pusht()) and runs it through
// magicTreasureMagicalAbility() at LEVEL_TOWN_OBJECTS, so restocking draws
// from the one game RNG stream and can move the dungeon's treasure list
// top-of-stack -- pusht() of the borrowed slot is a no-op swap as long as
// it is still the top, which it always is here.
public static class StoreInventory
{
    // src/store_inventory.cpp:23 storeMaintenance() -- initialize and
    // up-keep the store's inventory. -RAK-
    public static void Maintenance(PlayerWorld w)
    {
        for (var storeId = 0; storeId < StoreConfig.MaxStores; storeId++)
        {
            var store = w.Stores[storeId];

            store.InsultsCounter = 0;
            if (store.UniqueItemsCounter >= StoreConfig.StoreMinAutoSellItems)
            {
                var turnaround = RandomNumber.Next(StoreConfig.StoreStockTurnAround);
                if (store.UniqueItemsCounter >= StoreConfig.StoreMaxAutoBuyItems)
                {
                    turnaround += 1 + store.UniqueItemsCounter - StoreConfig.StoreMaxAutoBuyItems;
                }
                turnaround--;
                while (turnaround >= 0)
                {
                    DestroyItem(w, storeId, RandomNumber.Next(store.UniqueItemsCounter) - 1, false);
                    turnaround--;
                }
            }

            if (store.UniqueItemsCounter <= StoreConfig.StoreMaxAutoBuyItems)
            {
                var turnaround = RandomNumber.Next(StoreConfig.StoreStockTurnAround);
                if (store.UniqueItemsCounter < StoreConfig.StoreMinAutoSellItems)
                {
                    turnaround += StoreConfig.StoreMinAutoSellItems - store.UniqueItemsCounter;
                }

                var maxCost = DataStoreOwnersData.StoreOwners[store.OwnerId].MaxCost;

                turnaround--;
                while (turnaround >= 0)
                {
                    ItemCreate(w, storeId, maxCost);
                    turnaround--;
                }
            }
        }
    }

    // src/store_inventory.cpp:216 storeItemSellPrice() -- asking price for
    // an item -RAK- Returns 0 for an item the store won't take; the min/max
    // prices are only set otherwise (the original leaves them untouched;
    // here they are 0).
    public static int SellPrice(PlayerWorld w, Store store, out int minPrice, out int maxPrice, in Inventory item)
    {
        minPrice = 0;
        maxPrice = 0;

        var price = ItemValue.StoreItemValue(w.Identification, item);

        // check `item.cost` in case it is cursed, check `price` in case it is damaged
        // don't let the item get into the store inventory
        if (item.Cost < 1 || price < 1)
        {
            return 0;
        }

        var owner = DataStoreOwnersData.StoreOwners[store.OwnerId];

        price = price * DataStoresData.RaceGoldAdjustments[owner.Race][w.Py.Misc.RaceId] / 100;
        if (price < 1)
        {
            price = 1;
        }

        maxPrice = price * owner.MaxInflate / 100;
        minPrice = price * owner.MinInflate / 100;

        if (minPrice > maxPrice)
        {
            minPrice = maxPrice;
        }

        return price;
    }

    // src/store_inventory.cpp:245 storeCheckPlayerItemsCount() -- check to
    // see if they will be carrying too many objects -RAK- (despite the name,
    // it is the store's room that is checked)
    public static bool CheckPlayerItemsCount(Store store, in Inventory item)
    {
        if (store.UniqueItemsCounter < StoreConfig.StoreMaxDiscreteItems)
        {
            return true;
        }

        if (!PlayerInventory.ItemStackable(item))
        {
            return false;
        }

        var storeCheck = false;

        for (var i = 0; i < store.UniqueItemsCounter; i++)
        {
            ref var storeItem = ref store.Inventory[i].Item;

            // note: items with sub_category_id of gte ITEM_SINGLE_STACK_MAX only stack
            // if their `sub_category_id`s match
            if (storeItem.CategoryId == item.CategoryId && storeItem.SubCategoryId == item.SubCategoryId && storeItem.ItemsCount + item.ItemsCount < 256 &&
                (item.SubCategoryId < InventoryConfig.ItemGroupMin || storeItem.MiscUse == item.MiscUse))
            {
                storeCheck = true;
            }
        }

        return storeCheck;
    }

    // src/store_inventory.cpp:272 storeItemInsert() -- insert INVEN_MAX at
    // given location
    private static void ItemInsert(Store store, int pos, int itemCost, in Inventory item)
    {
        for (var i = store.UniqueItemsCounter - 1; i >= pos; i--)
        {
            store.Inventory[i + 1] = store.Inventory[i];
        }

        store.Inventory[pos].Item = item;
        store.Inventory[pos].Cost = -itemCost;
        store.UniqueItemsCounter++;
    }

    // src/store_inventory.cpp:285 storeCarryItem() -- add the item in
    // INVEN_MAX to stores inventory. -RAK- indexId is where it went, or -1
    // if the store wouldn't take it.
    public static void CarryItem(PlayerWorld w, int storeId, out int indexId, in Inventory item)
    {
        indexId = -1;

        var store = w.Stores[storeId];

        if (SellPrice(w, store, out _, out var itemCost, item) < 1)
        {
            return;
        }

        var itemId = 0;
        int itemNum = item.ItemsCount;
        int itemCategory = item.CategoryId;
        int itemSubCategory = item.SubCategoryId;

        var flag = false;
        do
        {
            ref var storeItem = ref store.Inventory[itemId].Item;

            if (itemCategory == storeItem.CategoryId)
            {
                if (itemSubCategory == storeItem.SubCategoryId && // Adds to other item
                    itemSubCategory >= InventoryConfig.ItemSingleStackMin && (itemSubCategory < InventoryConfig.ItemGroupMin || storeItem.MiscUse == item.MiscUse))
                {
                    indexId = itemId;
                    storeItem.ItemsCount = (byte)(storeItem.ItemsCount + itemNum);

                    // must set new cost for group items, do this only for items
                    // strictly greater than group_min, not for torches, this
                    // must be recalculated for entire group
                    if (itemSubCategory > InventoryConfig.ItemGroupMin)
                    {
                        _ = SellPrice(w, store, out _, out itemCost, storeItem);
                        store.Inventory[itemId].Cost = -itemCost;
                    }
                    else if (storeItem.ItemsCount > 24)
                    {
                        // must let group objects (except torches) stack over 24
                        // since there may be more than 24 in the group
                        storeItem.ItemsCount = 24;
                    }
                    flag = true;
                }
            }
            else if (itemCategory > storeItem.CategoryId)
            {
                // Insert into list
                ItemInsert(store, itemId, itemCost, item);
                flag = true;
                indexId = itemId;
            }
            itemId++;
        } while (itemId < store.UniqueItemsCounter && !flag);

        // Becomes last item in list
        if (!flag)
        {
            ItemInsert(store, store.UniqueItemsCounter, itemCost, item);
            indexId = store.UniqueItemsCounter - 1;
        }
    }

    // src/store_inventory.cpp:338 storeDestroyItem() -- destroy an item in
    // the stores inventory. Note that if `onlyOneOf` is false, an entire
    // slot is destroyed -RAK-
    public static void DestroyItem(PlayerWorld w, int storeId, int itemId, bool onlyOneOf)
    {
        var store = w.Stores[storeId];
        ref var storeItem = ref store.Inventory[itemId].Item;

        byte number;

        // for single stackable objects, only destroy one half on average,
        // this will help ensure that general store and alchemist have
        // reasonable selection of objects
        if (InventoryActions.ItemSingleStackable(storeItem))
        {
            number = onlyOneOf ? (byte)1 : (byte)RandomNumber.Next(storeItem.ItemsCount);
        }
        else
        {
            number = storeItem.ItemsCount;
        }

        if (number != storeItem.ItemsCount)
        {
            storeItem.ItemsCount -= number;
        }
        else
        {
            for (var i = itemId; i < store.UniqueItemsCounter - 1; i++)
            {
                store.Inventory[i] = store.Inventory[i + 1];
            }
            store.Inventory[store.UniqueItemsCounter - 1] = Store.EmptyRecord();
            store.UniqueItemsCounter--;
        }
    }

    // src/store_inventory.cpp:349 storeItemCreate() -- creates an item and
    // inserts it into store's inven -RAK-
    private static void ItemCreate(PlayerWorld w, int storeId, int maxCost)
    {
        var freeId = w.Treasure.Popt();

        for (var tries = 0; tries <= 3; tries++)
        {
            int id = DataStoresData.StoreChoices[storeId][RandomNumber.Next(StoreConfig.StoreMaxItemTypes) - 1];
            w.Treasure[freeId] = InventoryCatalog.CopyFromCatalog(id);
            TreasureMagic.MagicalAbility(ref w.Treasure[freeId], StoreConfig.LevelTownObjects);

            ref var item = ref w.Treasure[freeId];

            if (CheckPlayerItemsCount(w.Stores[storeId], item))
            {
                // Item must be good: cost > 0.
                if (item.Cost > 0 && item.Cost < maxCost)
                {
                    // equivalent to calling spellIdentifyItem(),
                    // except will not change the objects_identified array.
                    w.Identification.ItemIdentifyAsStoreBought(ref item);

                    CarryItem(w, storeId, out _, item);

                    tries = 10;
                }
            }
        }

        w.Treasure.Pusht(w.Dg, (byte)freeId);
    }
}
