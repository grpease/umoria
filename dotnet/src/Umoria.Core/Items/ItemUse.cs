using Umoria.Core.Character;

namespace Umoria.Core.Items;

// The tail every use-an-item command shares (eat, quaff, read, use a staff,
// aim a wand -- written out inline five times in the original): a use that
// revealed the item's kind identifies it, for some experience; one that
// didn't marks the kind as tried. (M11)
public static class ItemUse
{
    // `itemId` follows itemIdentify()'s stack merge.
    public static void IdentifyOrTry(PlayerWorld w, bool identified, ref int itemId)
    {
        var py = w.Py;
        var item = py.Inventory[itemId];

        if (w.Identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification))
        {
            return;
        }

        if (identified)
        {
            // use identified it, gain experience
            // round half-way case up
            py.Misc.Exp += (item.DepthFirstFound + (py.Misc.Level >> 1)) / py.Misc.Level;

            PlayerCore.DisplayCharacterExperience(w);

            w.Identification.ItemIdentify(w, ref itemId);
        }
        else
        {
            w.Identification.ItemSetAsTried(item);
        }
    }
}
