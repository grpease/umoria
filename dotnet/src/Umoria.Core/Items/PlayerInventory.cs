using Umoria.Core.Config;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// The subset of src/inventory.cpp character creation (M6) reaches:
// inventoryCollectAllItemFlags (via playerRecalculateBonuses),
// inventoryCarryItem (starting provisions), and the small stackable/cursed
// predicates those use. The rest of inventory.cpp (dropping, destroying,
// elemental item damage, inventoryCanCarryItem*) stays with M11.
public static class PlayerInventory
{
    // src/inventory.cpp:10 inventoryCollectAllItemFlags()
    public static uint CollectAllItemFlags(Player py)
    {
        uint flags = 0;

        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.Equipment.Light; i++)
        {
            flags |= py.Inventory[i].Flags;
        }

        return flags;
    }

    // src/inventory.cpp:366 inventoryItemStackable()
    public static bool ItemStackable(in Inventory item)
    {
        return item.SubCategoryId >= InventoryConfig.ItemSingleStackMin;
    }

    // src/inventory.cpp:370 inventoryItemIsCursed()
    public static bool ItemIsCursed(in Inventory item)
    {
        return (item.Flags & TreasureConfig.Flags.TrCursed) != 0;
    }

    // src/inventory.cpp:263 inventoryCarryItem() -- add an item to the
    // player's inventory, returning the slot it landed in. Items stack onto a
    // matching slot, or are inserted in category-sorted order.
    public static int CarryItem(Player py, ObjectIdentification identification, Inventory newItem)
    {
        var isKnown = identification.ItemSetColorlessAsIdentified(newItem.CategoryId, newItem.SubCategoryId, newItem.Identification);
        var isAlwaysKnown = ObjectIdentification.ObjectPositionOffset(newItem.CategoryId, newItem.SubCategoryId) == -1;

        int slotId;

        // Now, check to see if player can carry object
        for (slotId = 0; slotId < PlayerConfig.PlayerInventorySize; slotId++)
        {
            var item = py.Inventory[slotId];

            var isSameCategory = newItem.CategoryId == item.CategoryId;
            var isSameSubCategory = newItem.SubCategoryId == item.SubCategoryId;
            var notTooManyItems = newItem.ItemsCount + item.ItemsCount < 256;

            // only stack if both or neither are identified
            var sameKnownStatus = identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification) == isKnown;

            var isStackable = ItemStackable(newItem);
            var isSameGroup = newItem.SubCategoryId < InventoryConfig.ItemGroupMin || item.MiscUse == newItem.MiscUse;

            if (isSameCategory && isSameSubCategory && isStackable && notTooManyItems && isSameGroup && sameKnownStatus)
            {
                py.Inventory[slotId].ItemsCount += newItem.ItemsCount;
                break;
            }

            if ((isSameCategory && newItem.SubCategoryId < item.SubCategoryId && isAlwaysKnown) || newItem.CategoryId > item.CategoryId)
            {
                // For items which are always `is_known`, i.e. never have a 'color',
                // insert them into the inventory in sorted order.
                for (var i = py.Pack.UniqueItems - 1; i >= slotId; i--)
                {
                    py.Inventory[i + 1] = py.Inventory[i];
                }
                py.Inventory[slotId] = newItem;
                py.Pack.UniqueItems++;
                break;
            }
        }

        py.Pack.Weight = (short)(py.Pack.Weight + newItem.ItemsCount * newItem.Weight);
        py.Flags.Status |= PlayerConfig.Status.PyStrWgt;

        return slotId;
    }
}
