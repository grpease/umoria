using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Stores;

// src/store.h InventoryRecord_t -- a store inventory item. A cost <= 0 is
// the negated asking price before haggling; a positive one is the price the
// last one sold for, shown as "[Fixed]".
public struct InventoryRecord
{
    public int Cost;
    public Inventory Item;
}

// src/store.h Store_t -- all the data for any given store in the game.
// (M13) The six live on PlayerWorld.Stores (the original's stores[]).
public sealed class Store
{
    public int TurnsLeftBeforeClosing { get; set; }
    public short InsultsCounter { get; set; }
    public byte OwnerId { get; set; }
    public byte UniqueItemsCounter { get; set; }
    public ushort GoodPurchases { get; set; }
    public ushort BadPurchases { get; set; }
    public InventoryRecord[] Inventory { get; } = CreateInventory();

    // stores[] is a zero-initialized global in the original; an empty
    // record's item is all zeros (TV_NOTHING) until storeInitializeOwners()
    // copies OBJ_NOTHING into every slot.
    private static InventoryRecord[] CreateInventory()
    {
        var inventory = new InventoryRecord[StoreConfig.StoreMaxDiscreteItems];
        for (var i = 0; i < inventory.Length; i++)
        {
            inventory[i].Item = default(Inventory) with { Inscription = "" };
        }
        return inventory;
    }

    public static Store[] CreateTable()
    {
        var stores = new Store[StoreConfig.MaxStores];
        for (var i = 0; i < stores.Length; i++)
        {
            stores[i] = new Store();
        }
        return stores;
    }

    // An empty slot, as storeInitializeOwners() / storeDestroyItem() leave it.
    internal static InventoryRecord EmptyRecord() => new() { Cost = 0, Item = InventoryCatalog.CopyFromCatalog(DungeonGenerationConfig.Objects.ObjNothing) };
}
