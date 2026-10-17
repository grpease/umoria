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
// descriptions, flavor-name shuffling in magicInitializeItemNames, the
// identify command itself) stays with M11.
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
}
