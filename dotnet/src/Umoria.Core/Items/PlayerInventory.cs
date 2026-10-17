using Umoria.Core.Config;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// The subset of src/inventory.cpp character creation (M6) reaches:
// inventoryCollectAllItemFlags (via playerRecalculateBonuses),
// inventoryCarryItem (starting provisions), and the small stackable/cursed
// predicates those use; M8 added inventoryCanCarryItemCount/CanCarryItem
// (floor pickup). The rest of inventory.cpp (dropping, destroying,
// elemental item damage) stays with M11.
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

    // src/inventory.cpp:212 inventoryCanCarryItemCount() -- this code must
    // be identical to the inventoryCarryItem() code below (added M8, for
    // picking items up off the floor)
    public static bool CanCarryItemCount(Player py, ObjectIdentification identification, in Inventory item)
    {
        if (py.Pack.UniqueItems < PlayerConfig.Equipment.Wield)
        {
            return true;
        }

        if (!ItemStackable(item))
        {
            return false;
        }

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            var slot = py.Inventory[i];

            var sameCharacter = slot.CategoryId == item.CategoryId;
            var sameCategory = slot.SubCategoryId == item.SubCategoryId;

            // make sure the number field doesn't overflow
            var sameNumber = slot.ItemsCount + item.ItemsCount < 256;

            // they always stack (sub_category_id < 192), or else they have same `misc_use`
            var sameGroup = item.SubCategoryId < InventoryConfig.ItemGroupMin || slot.MiscUse == item.MiscUse;

            // only stack if both or neither are identified
            var slotIsColorless = identification.ItemSetColorlessAsIdentified(slot.CategoryId, slot.SubCategoryId, slot.Identification);
            var itemIsColorless = identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification);
            var sameIdentification = slotIsColorless == itemIsColorless;

            if (sameCharacter && sameCategory && sameNumber && sameGroup && sameIdentification)
            {
                return true;
            }
        }

        return false;
    }

    // src/inventory.cpp:247 inventoryCanCarryItem() -- return false if
    // picking up an object would change the players speed (added M8)
    public static bool CanCarryItem(Player py, in Inventory item)
    {
        var limit = Character.PlayerCore.CarryingLoadLimit(py);
        var newWeight = item.ItemsCount * item.Weight + py.Pack.Weight;

        if (limit < newWeight)
        {
            limit = newWeight / (limit + 1);
        }
        else
        {
            limit = 0;
        }

        return py.Pack.Heaviness == limit;
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
