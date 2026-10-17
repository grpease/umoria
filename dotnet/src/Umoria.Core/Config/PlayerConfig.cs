// Hand-translated constants for the player subsystem (M6), from
// src/player.h, src/inventory.h and config::player/config::spells in
// src/config.cpp. Only the subset ported code needs: character creation
// (M6), plus the food/regeneration constants for status effects (M7).
namespace Umoria.Core.Config;

public static class PlayerConfig
{
    // src/player.h
    public const int PlayerMaxLevel = 40;
    public const int PlayerMaxClasses = 6;
    public const int PlayerMaxRaces = 8;

    // src/inventory.h
    public const int PlayerInventorySize = 34;

    // config::player (src/config.cpp)
    public const ushort PlayerFoodFull = 10000; // Getting full
    public const ushort PlayerFoodMax = 15000; // Maximum food value, beyond is wasted
    public const ushort PlayerFoodFaint = 300; // Character begins fainting
    public const ushort PlayerFoodWeak = 1000; // Warn player that they're getting weak
    public const ushort PlayerFoodAlert = 2000; // Alert player that they're getting low on food
    public const byte PlayerRegenFaint = 33; // Regen factor*2^16 when fainting
    public const byte PlayerRegenWeak = 98; // Regen factor*2^16 when weak
    public const byte PlayerRegenNormal = 197; // Regen factor*2^16 when full
    public const ushort PlayerRegenHpBase = 1442; // Min amount hp regen*2^16
    public const ushort PlayerRegenMnBase = 524; // Min amount mana regen*2^16
    public const byte PlayerWeightCap = 130; // "#"*(1/10 pounds) per strength point

    // src/player.h enum PlayerAttr -- indexes PlayerStats' 6-entry arrays.
    public static class Attr
    {
        public const int Str = 0;
        public const int Int = 1;
        public const int Wis = 2;
        public const int Dex = 3;
        public const int Con = 4;
        public const int Chr = 5;
    }

    // src/inventory.h enum PlayerEquipment -- Player.Inventory slot indices.
    public static class Equipment
    {
        public const int Wield = 22; // must be first item in equipment list
        public const int Head = 23;
        public const int Neck = 24;
        public const int Body = 25;
        public const int Arm = 26;
        public const int Hands = 27;
        public const int Right = 28;
        public const int Left = 29;
        public const int Feet = 30;
        public const int Outer = 31;
        public const int Light = 32;
        public const int Auxiliary = 33;
    }

    // config::player::status (src/config.cpp)
    public static class Status
    {
        public const uint PyHungry = 0x00000001;
        public const uint PyWeak = 0x00000002;
        public const uint PyBlind = 0x00000004;
        public const uint PyConfused = 0x00000008;
        public const uint PyFear = 0x00000010;
        public const uint PyPoisoned = 0x00000020;
        public const uint PyFast = 0x00000040;
        public const uint PySlow = 0x00000080;
        public const uint PySearch = 0x00000100;
        public const uint PyRest = 0x00000200;
        public const uint PyStudy = 0x00000400;
        public const uint PyInvuln = 0x00001000;
        public const uint PyHero = 0x00002000;
        public const uint PyShero = 0x00004000;
        public const uint PyBlessed = 0x00008000;
        public const uint PyDetInv = 0x00010000;
        public const uint PyTimInfra = 0x00020000;
        public const uint PySpeed = 0x00040000;
        public const uint PyStrWgt = 0x00080000;
        public const uint PyParalysed = 0x00100000;
        public const uint PyRepeat = 0x00200000;
        public const uint PyArmor = 0x00400000;
        public const uint PyStats = 0x3F000000;
        public const uint PyStr = 0x01000000; // these 6 stat flags must be adjacent
        public const uint PyInt = 0x02000000;
        public const uint PyWis = 0x04000000;
        public const uint PyDex = 0x08000000;
        public const uint PyCon = 0x10000000;
        public const uint PyChr = 0x20000000;
        public const uint PyHp = 0x40000000;
        public const uint PyMana = 0x80000000;
    }
}

// config::spells (src/config.cpp)
public static class SpellConfig
{
    public const byte SpellTypeNone = 0;
    public const byte SpellTypeMage = 1;
    public const byte SpellTypePriest = 2;

    public const int NameOffsetSpells = 0;
    public const int NameOffsetPrayers = 31;
}

// src/inventory.h item stacking ranges (by sub_category_id).
public static class InventoryConfig
{
    public const byte ItemNeverStackMin = 0;
    public const byte ItemNeverStackMax = 63;
    public const byte ItemSingleStackMin = 64;
    public const byte ItemSingleStackMax = 192;
    public const byte ItemGroupMin = 192;
}
