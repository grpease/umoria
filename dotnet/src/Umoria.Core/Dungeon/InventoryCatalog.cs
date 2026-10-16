using Umoria.Core.Data.Generated;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Dungeon;

// src/inventory.cpp:336 inventoryItemCopyTo() -- builds a fresh item
// instance from a read-only catalog row (DataTreasureData.GameObjects),
// the same way the real game does before placing an item or handing it to
// magicTreasureMagicalAbility() (TreasureMagic.MagicalAbility).
public static class InventoryCatalog
{
    public static Inventory CopyFromCatalog(int itemId)
    {
        var from = DataTreasureData.GameObjects[itemId];

        return new Inventory(
            Id: (ushort)itemId,
            SpecialNameId: 0,
            Inscription: "",
            Flags: from.Flags,
            CategoryId: from.CategoryId,
            Sprite: from.Sprite,
            MiscUse: from.MiscUse,
            Cost: from.Cost,
            SubCategoryId: from.SubCategoryId,
            ItemsCount: from.ItemsCount,
            Weight: from.Weight,
            ToHit: from.ToHit,
            ToDamage: from.ToDamage,
            Ac: from.Ac,
            ToAc: from.ToAc,
            Damage: from.Damage,
            DepthFirstFound: from.DepthFirstFound,
            Identification: 0);
    }
}
