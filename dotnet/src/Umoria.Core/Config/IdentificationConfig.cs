// Hand-translated from config::identification in src/config.cpp.
// M3 added the ID_SHOW_* subset the item-magic engine needs; M6 added the
// rest (OD_* object-knowledge bits and the remaining ID_* item bits), which
// starting-inventory identification and stacking read.
namespace Umoria.Core.Config;

public static class IdentificationConfig
{
    // objects_identified[] bits
    public const byte OdTried = 0x1;
    public const byte OdKnown1 = 0x2;

    // Inventory.Identification bits
    public const byte IdMagik = 0x1;
    public const byte IdDamd = 0x2;
    public const byte IdEmpty = 0x4;
    public const byte IdKnown2 = 0x8;
    public const byte IdStoreBought = 0x10;
    public const byte IdShowHitDam = 0x20;
    public const byte IdNoShowP1 = 0x40;
    public const byte IdShowP1 = 0x80;
}
