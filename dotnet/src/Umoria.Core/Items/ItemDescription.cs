using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// src/identification.cpp:520 itemDescription() -- only the early-return
// categories whose text is just the catalog name: gold, traps, stairs and
// store entrances (M8).
//
// That subset is game state, not just display: a trap's description is the
// death label passed to playerTakesHit() (game.character_died_from, e.g. "a
// trap door."). Every other category needs the flavour tables (shuffled at
// game start), plural/article rules and the identification-dependent
// suffixes; that is M11 and throws here rather than return wrong text.
public static class ItemDescription
{
    public static string Describe(in Inventory item)
    {
        var name = DataTreasureData.GameObjects[item.Id].Name;

        switch (item.CategoryId)
        {
            case TreasureCategory.TvGold:
            case TreasureCategory.TvInvisTrap:
            case TreasureCategory.TvVisTrap:
            case TreasureCategory.TvUpStair:
            case TreasureCategory.TvDownStair:
                return name + ".";
            case TreasureCategory.TvStoreDoor:
                return $"the entrance to the {name}.";
            default:
                throw new NotSupportedException(
                    $"itemDescription() for category {item.CategoryId} needs the full identification port (M11).");
        }
    }
}
