using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// Moving items between the pack, the equipment slots and the floor (M11):
// src/inventory.cpp's inventoryDropItem() and src/player.cpp's equipment
// helpers (playerTakeOff() and the worn-item predicates). The commands built
// on them -- wear, take off, drop, exchange -- are InventoryCommands.
//
// M13 added inventoryTakeOneItem(), for the store's buy/sell.
public static class InventoryActions
{
    // src/inventory.cpp:361 inventoryItemSingleStackable() -- checks if an
    // item is stackable, only as a singles object.
    public static bool ItemSingleStackable(in Inventory item)
    {
        return item.SubCategoryId >= InventoryConfig.ItemSingleStackMin && item.SubCategoryId <= InventoryConfig.ItemSingleStackMax;
    }

    // src/inventory.cpp:43 inventoryTakeOneItem() -- a copy of the item, one
    // of a single-stackable stack. (M13)
    public static Inventory TakeOneItem(in Inventory fromItem)
    {
        var toItem = fromItem;

        if (toItem.ItemsCount > 1 && ItemSingleStackable(toItem))
        {
            toItem.ItemsCount = 1;
        }

        return toItem;
    }

    // src/inventory.cpp:374 inventoryItemRemoveCurse()
    public static void ItemRemoveCurse(ref Inventory item)
    {
        item.Flags &= ~TreasureConfig.Flags.TrCursed;
    }

    // src/player.cpp:780 playerLeftHandRingEmpty()
    public static bool LeftHandRingEmpty(Player py) => py.Inventory[PlayerConfig.Equipment.Left].CategoryId == TreasureCategory.TvNothing;

    // src/player.cpp:784 playerRightHandRingEmpty()
    public static bool RightHandRingEmpty(Player py) => py.Inventory[PlayerConfig.Equipment.Right].CategoryId == TreasureCategory.TvNothing;

    // src/player.cpp:788 playerIsWieldingItem()
    public static bool IsWieldingItem(Player py)
    {
        return py.Inventory[PlayerConfig.Equipment.Wield].CategoryId != TreasureCategory.TvNothing ||
               py.Inventory[PlayerConfig.Equipment.Auxiliary].CategoryId != TreasureCategory.TvNothing;
    }

    // src/player.cpp:795 playerWornItemIsCursed()
    public static bool WornItemIsCursed(Player py, int id) => PlayerInventory.ItemIsCursed(py.Inventory[id]);

    // src/player.cpp:591 playerTakeOff() -- remove item from equipment list
    // -RAK-
    public static void TakeOff(PlayerWorld w, int itemId, int packPositionId)
    {
        var py = w.Py;

        py.Flags.Status |= PlayerConfig.Status.PyStrWgt;

        ref var item = ref py.Inventory[itemId];

        py.Pack.Weight = (short)(py.Pack.Weight - item.Weight * item.ItemsCount);
        py.EquipmentCount--;

        string p;
        if (itemId == PlayerConfig.Equipment.Wield || itemId == PlayerConfig.Equipment.Auxiliary)
        {
            p = "Was wielding ";
        }
        else if (itemId == PlayerConfig.Equipment.Light)
        {
            p = "Light source was ";
        }
        else
        {
            p = "Was wearing ";
        }

        var description = ItemDescription.Describe(w.Identification, item, true);

        var msg = packPositionId >= 0
            ? $"{p}{description} ({(char)('a' + packPositionId)})"
            : $"{p}{description}";
        PlayerCore.PrintMessage(w, Helpers.Truncate(msg, Helpers.MoriaObjDescSize));

        // For secondary weapon
        if (itemId != PlayerConfig.Equipment.Auxiliary)
        {
            PlayerCombat.AdjustBonusesForItem(w, item, -1);
        }

        item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
    }

    // src/inventory.cpp:52 inventoryDropItem() -- drops an item from
    // inventory to given location -RAK-
    public static void DropItem(PlayerWorld w, int itemId, bool dropAll)
    {
        var py = w.Py;

        if (w.Dg.Floor[py.Pos.Y, py.Pos.X].TreasureId != 0)
        {
            w.Objects.DeleteObject(py.Pos);
        }

        var treasureId = w.Treasure.Popt();

        w.Treasure[treasureId] = py.Inventory[itemId];

        w.Dg.Floor[py.Pos.Y, py.Pos.X].TreasureId = (byte)treasureId;

        if (itemId >= PlayerConfig.Equipment.Wield)
        {
            TakeOff(w, itemId, -1);
        }
        else
        {
            ref var item = ref py.Inventory[itemId];

            if (dropAll || item.ItemsCount == 1)
            {
                py.Pack.Weight = (short)(py.Pack.Weight - item.Weight * item.ItemsCount);
                py.Pack.UniqueItems--;

                while (itemId < py.Pack.UniqueItems)
                {
                    py.Inventory[itemId] = py.Inventory[itemId + 1];
                    itemId++;
                }

                py.Inventory[py.Pack.UniqueItems] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
            }
            else
            {
                w.Treasure[treasureId].ItemsCount = 1;
                py.Pack.Weight = (short)(py.Pack.Weight - item.Weight);
                item.ItemsCount--;
            }

            var prt1 = ItemDescription.Describe(w.Identification, w.Treasure[treasureId], true);
            PlayerCore.PrintMessage(w, Helpers.Truncate($"Dropped {prt1}", Helpers.MoriaObjDescSize));
        }

        py.Flags.Status |= PlayerConfig.Status.PyStrWgt;
    }
}
