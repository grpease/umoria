using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Data.Generated;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Items;

// The object-knowledge subset of src/identification.cpp that character
// creation (M6) reaches: objects_identified[] (as instance state, same DI
// style as TreasureList rather than a static global), objectPositionOffset,
// itemSetAsIdentified, unsample, spellItemIdentifyAndRemoveRandomInscription,
// spellItemIdentified, itemIdentifyAsStoreBought, itemSetColorlessAsIdentified.
//
// Starting inventory goes through itemIdentifyAsStoreBought() and
// inventoryCarryItem()'s stacking check reads itemSetColorlessAsIdentified(),
// so these had to land now. M11 added the rest of the object-knowledge
// state: the shuffled flavour names (magicInitializeItemNames), tried
// flags and inscriptions; the description text itself is ItemDescription.
//
// M10 added what the identify and recharge spells reach: itemIdentify()
// (with itemAppendToInscription() and its shop/dungeon stack merging),
// spellItemRemoveIdentification() and itemIdentificationClearEmpty().
public sealed class ObjectIdentification
{
    // OBJECT_IDENT_SIZE (src/game.h): 7 categories x 64 sub-categories.
    public const int ObjectIdentSize = 448;

    // MAX_MUSHROOMS (src/identification.h)
    public const int MaxMushrooms = 22;

    public byte[] ObjectsIdentified { get; } = new byte[ObjectIdentSize];

    // MAX_TITLES (src/identification.h)
    public const int MaxTitles = 45;

    // The flavour names unidentified items are described with (M11). The
    // original shuffles the data_tables.cpp arrays in place at game start
    // (magicInitializeItemNames()), so they are per-game state here: copies
    // of the generated tables, shuffled by MagicInitializeItemNames.
    public string[] Colors { get; } = (string[])DataTablesData.Colors.Clone();
    public string[] Mushrooms { get; } = (string[])DataTablesData.Mushrooms.Clone();
    public string[] Woods { get; } = (string[])DataTablesData.Woods.Clone();
    public string[] Metals { get; } = (string[])DataTablesData.Metals.Clone();
    public string[] Rocks { get; } = (string[])DataTablesData.Rocks.Clone();
    public string[] Amulets { get; } = (string[])DataTablesData.Amulets.Clone();

    // src/identification.cpp:11 magic_item_titles[] -- the scroll titles,
    // empty until MagicInitializeItemNames makes them up.
    public string[] MagicItemTitles { get; } = Enumerable.Repeat("", MaxTitles).ToArray();

    // src/identification.cpp:230 magicInitializeItemNames() -- initialize
    // all Potions, wands, staves, scrolls, etc. Draws from the game RNG
    // re-seeded with game.magic_seed (seedSet()), then restores the normal
    // stream (seedResetToOldSeed()), so the names stay the same for a game
    // across save/load. (M11)
    public void MagicInitializeItemNames(uint magicSeed)
    {
        var oldSeed = MoriaRng.GetRandomSeed();
        MoriaRng.SetRandomSeed(magicSeed);

        // The first 3 entries for colors are fixed, (slime & apple juice, water)
        for (var i = 3; i < Colors.Length; i++)
        {
            var id = RandomNumber.Next(Colors.Length - 3) + 2;
            (Colors[i], Colors[id]) = (Colors[id], Colors[i]);
        }

        Shuffle(Woods);
        Shuffle(Metals);
        Shuffle(Rocks);
        Shuffle(Amulets);
        Shuffle(Mushrooms);

        var syllables = DataTablesData.Syllables;

        for (var t = 0; t < MagicItemTitles.Length; t++)
        {
            var title = "";
            var k = RandomNumber.Next(2) + 1;

            for (var i = 0; i < k; i++)
            {
                for (var s = RandomNumber.Next(2); s > 0; s--)
                {
                    title += syllables[RandomNumber.Next(syllables.Length) - 1];
                }
                if (i < k - 1)
                {
                    title += " ";
                }
            }

            // (the original writes a terminator at [8] or [9]; past the end
            // of a shorter title that changes nothing)
            if (title.Length > 8 && title[8] == ' ')
            {
                title = title[..8];
            }
            else if (title.Length > 9)
            {
                title = title[..9];
            }

            MagicItemTitles[t] = title;
        }

        MoriaRng.SetRandomSeed(oldSeed);
    }

    // magicInitializeItemNames()' swap loop, shared by every table but the
    // colors.
    private static void Shuffle(string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var id = RandomNumber.Next(names.Length) - 1;
            (names[i], names[id]) = (names[id], names[i]);
        }
    }

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

    // src/identification.cpp:425 itemSetAsTried() -- somethings been
    // sampled -CJS- (M11)
    public void ItemSetAsTried(in Inventory item)
    {
        var offset = ObjectPositionOffset(item.CategoryId, item.SubCategoryId);
        if (offset < 0)
        {
            return;
        }

        ObjectsIdentified[IdentIndex(offset, item.SubCategoryId)] |= IdentificationConfig.OdTried;
    }

    // src/identification.cpp:340 isObjectKnown()'s tried counterpart, read
    // by itemDescription() (M11)
    public bool IsObjectTried(int categoryId, int subCategoryId)
    {
        var offset = ObjectPositionOffset(categoryId, subCategoryId);
        return offset >= 0 && (ObjectsIdentified[IdentIndex(offset, subCategoryId)] & IdentificationConfig.OdTried) != 0;
    }

    // src/identification.cpp:489 itemRemoveMagicNaming() -- if an object
    // has lost magical properties, remove the appropriate portion of the
    // name. -CJS- (M11)
    public static void ItemRemoveMagicNaming(ref Inventory item)
    {
        item.SpecialNameId = (byte)SpecialNameIds.SnNull;
    }

    // src/identification.cpp:937 itemReplaceInscription() (M11)
    public static void ItemReplaceInscription(ref Inventory item, string inscription)
    {
        item.Inscription = inscription;
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
