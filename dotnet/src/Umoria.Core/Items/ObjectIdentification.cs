using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// The object-knowledge subset of src/identification.cpp that character
// creation (M6) reaches: objects_identified[] (as instance state, same DI
// style as TreasureList rather than a static global), objectPositionOffset,
// itemSetAsIdentified, unsample, spellItemIdentifyAndRemoveRandomInscription,
// spellItemIdentified, itemIdentifyAsStoreBought, itemSetColorlessAsIdentified.
//
// Starting inventory goes through itemIdentifyAsStoreBought() and
// inventoryCarryItem()'s stacking check reads itemSetColorlessAsIdentified(),
// so these had to land now. The rest of identification.cpp (item
// descriptions, flavor-name shuffling in magicInitializeItemNames) stays
// with M11.
//
// M10 added what the identify and recharge spells reach: itemIdentify()
// (with itemAppendToInscription() and its shop/dungeon stack merging),
// spellItemRemoveIdentification() and itemIdentificationClearEmpty().
public sealed class ObjectIdentification
{
    // OBJECT_IDENT_SIZE (src/game.h): 7 categories x 64 sub-categories.
    public const int ObjectIdentSize = 448;

    // MAX_MUSHROOMS (src/identification.h)
    private const int MaxMushrooms = 22;

    public byte[] ObjectsIdentified { get; } = new byte[ObjectIdentSize];

    // src/identification.cpp:306 objectPositionOffset()
    public static short ObjectPositionOffset(int categoryId, int subCategoryId)
    {
        switch (categoryId)
        {
            case TreasureCategory.TvAmulet:
                return 0;
            case TreasureCategory.TvRing:
                return 1;
            case TreasureCategory.TvStaff:
                return 2;
            case TreasureCategory.TvWand:
                return 3;
            case TreasureCategory.TvScroll1:
            case TreasureCategory.TvScroll2:
                return 4;
            case TreasureCategory.TvPotion1:
            case TreasureCategory.TvPotion2:
                return 5;
            case TreasureCategory.TvFood:
                if ((subCategoryId & (InventoryConfig.ItemSingleStackMin - 1)) < MaxMushrooms)
                {
                    return 6;
                }
                return -1;
            default:
                return -1;
        }
    }

    private static int IdentIndex(short offset, int subCategoryId)
    {
        return (offset << 6) + (byte)(subCategoryId & (InventoryConfig.ItemSingleStackMin - 1));
    }

    private void ClearObjectTriedFlag(int id)
    {
        ObjectsIdentified[id] &= unchecked((byte)~IdentificationConfig.OdTried);
    }

    private bool IsObjectKnown(int id)
    {
        return (ObjectsIdentified[id] & IdentificationConfig.OdKnown1) != 0;
    }

    // src/identification.cpp:345 itemSetAsIdentified()
    public void ItemSetAsIdentified(int categoryId, int subCategoryId)
    {
        var offset = ObjectPositionOffset(categoryId, subCategoryId);
        if (offset < 0)
        {
            return;
        }

        var id = IdentIndex(offset, subCategoryId);
        ObjectsIdentified[id] |= IdentificationConfig.OdKnown1;

        // clear the tried flag, since it is now known
        ClearObjectTriedFlag(id);
    }

    // src/identification.cpp:362 unsample()
    private void Unsample(ref Inventory item)
    {
        // this also used to clear ID_DAMD flag, but I think it should remain set
        item.Identification &= unchecked((byte)~(IdentificationConfig.IdMagik | IdentificationConfig.IdEmpty));

        var offset = ObjectPositionOffset(item.CategoryId, item.SubCategoryId);
        if (offset < 0)
        {
            return;
        }

        // clear the tried flag, since it is now known
        ClearObjectTriedFlag(IdentIndex(offset, item.SubCategoryId));
    }

    // src/identification.cpp:380 spellItemIdentifyAndRemoveRandomInscription()
    public void SpellItemIdentifyAndRemoveRandomInscription(ref Inventory item)
    {
        Unsample(ref item);
        item.Identification |= IdentificationConfig.IdKnown2;
    }

    // src/identification.cpp:385 spellItemIdentified()
    public static bool SpellItemIdentified(in Inventory item)
    {
        return (item.Identification & IdentificationConfig.IdKnown2) != 0;
    }

    // src/identification.cpp:397 itemIdentifyAsStoreBought()
    public void ItemIdentifyAsStoreBought(ref Inventory item)
    {
        item.Identification |= IdentificationConfig.IdStoreBought;
        SpellItemIdentifyAndRemoveRandomInscription(ref item);
    }

    // src/identification.cpp:408 itemSetColorlessAsIdentified() -- items
    // which don't have a 'color' are always known, so that they can be
    // carried in order in the inventory.
    public bool ItemSetColorlessAsIdentified(int categoryId, int subCategoryId, int identification)
    {
        var offset = ObjectPositionOffset(categoryId, subCategoryId);
        if (offset < 0)
        {
            return true;
        }
        if ((identification & IdentificationConfig.IdStoreBought) != 0)
        {
            return true;
        }

        return IsObjectKnown(IdentIndex(offset, subCategoryId));
    }

    // src/identification.cpp:389 spellItemRemoveIdentification() (M10)
    public static void SpellItemRemoveIdentification(ref Inventory item)
    {
        item.Identification &= unchecked((byte)~IdentificationConfig.IdKnown2);
    }

    // src/identification.cpp:393 itemIdentificationClearEmpty() (M10)
    public static void ItemIdentificationClearEmpty(ref Inventory item)
    {
        item.Identification &= unchecked((byte)~IdentificationConfig.IdEmpty);
    }

    // src/identification.cpp:932 itemAppendToInscription() (M10)
    public static void ItemAppendToInscription(ref Inventory item, byte itemIdentType)
    {
        item.Identification |= itemIdentType;
    }

    // src/identification.cpp:440 itemIdentify() -- somethings been
    // identified. Extra complexity by CJS so that it can merge store/dungeon
    // objects when appropriate. `itemId` follows the merged stack. (M10)
    public void ItemIdentify(PlayerWorld w, ref int itemId)
    {
        var py = w.Py;
        ref var item = ref py.Inventory[itemId];

        if (PlayerInventory.ItemIsCursed(item))
        {
            ItemAppendToInscription(ref item, IdentificationConfig.IdDamd);
        }

        if (ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            return;
        }

        ItemSetAsIdentified(item.CategoryId, item.SubCategoryId);

        // no merging possible
        if (!(item.SubCategoryId >= InventoryConfig.ItemSingleStackMin && item.SubCategoryId <= InventoryConfig.ItemSingleStackMax))
        {
            return;
        }

        // (`item` stays a reference to the original slot, as in the
        // original, even after a merge shifts the pack under it)

        int j;

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            var other = py.Inventory[i];

            var matchingCat = other.CategoryId == item.CategoryId;
            var matchingSubCat = other.SubCategoryId == item.SubCategoryId;
            var totalItemsCount = other.ItemsCount + item.ItemsCount;

            if (matchingCat && matchingSubCat && i != itemId && totalItemsCount < 256)
            {
                // make *item_id the smaller number
                if (itemId > i)
                {
                    j = itemId;
                    itemId = i;
                    i = j;
                }

                PlayerCore.PrintMessage(w, "You combine similar objects from the shop and dungeon.");

                py.Inventory[itemId].ItemsCount += py.Inventory[i].ItemsCount;
                py.Pack.UniqueItems--;

                for (j = i; j < py.Pack.UniqueItems; j++)
                {
                    py.Inventory[j] = py.Inventory[j + 1];
                }

                py.Inventory[j] = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing);
            }
        }
    }
}
