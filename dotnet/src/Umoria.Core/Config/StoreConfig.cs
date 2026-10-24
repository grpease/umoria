namespace Umoria.Core.Config;

// src/store.h's constants and config::stores (src/config.cpp), plus
// config::treasure::LEVEL_TOWN_OBJECTS, the level store stock is made
// magical at. (M13)
public static class StoreConfig
{
    public const int MaxOwners = 18; // Number of owners to choose from
    public const int MaxStores = 6; // Number of different stores
    public const int StoreMaxDiscreteItems = 24; // Max number of discrete objects in inventory
    public const int StoreMaxItemTypes = 26; // Number of items to choose stock from
    public const int CostAdjustment = 100; // Adjust prices for buying and selling

    public const int StoreMaxAutoBuyItems = 18; // Max diff objects in stock for auto buy
    public const int StoreMinAutoSellItems = 10; // Min diff objects in stock for auto sell
    public const int StoreStockTurnAround = 9; // Amount of buying and selling normally

    public const int LevelTownObjects = 7; // Town object generation level
}
