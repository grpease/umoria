using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// src/identification.cpp:520 itemDescription() -- the text an item is
// shown and talked about with. M8 ported only the early-return categories
// (gold, traps, stairs, store entrances, whose text is just the catalog
// name); M11 made it whole: the flavour names (ObjectIdentification's
// shuffled tables), plural and article rules, and the
// identification-dependent suffixes.
//
// Mostly display, but not only: a trap's description is the death label
// passed to playerTakesHit() (game.character_died_from, e.g. "a trap
// door."), and the messages built from it all go through printMessage().
//
// The original assembles the text in fixed obj_desc_t (160) / vtype_t (80)
// buffers; the snprintf() cut-offs are kept (Helpers.Truncate). Its
// strcpy()/strcat() steps could overrun those buffers for an absurdly long
// name, which no catalog item reaches; those aren't emulated.
public static class ItemDescription
{
    // src/identification.cpp:507 ItemMiscUse -- how `item.misc_use` is
    // printed
    private enum ItemMiscUse
    {
        Ignored,
        Charges,
        Plusses,
        Light,
        Flags,
        ZPlusses,
    }

    // src/identification.cpp:493 bowDamageValue()
    public static int BowDamageValue(short miscUse)
    {
        if (miscUse == 1 || miscUse == 2)
        {
            return 2;
        }
        if (miscUse == 3 || miscUse == 5)
        {
            return 3;
        }
        if (miscUse == 4 || miscUse == 6)
        {
            return 4;
        }
        return -1;
    }

    private static char Sign(int value) => value < 0 ? '-' : '+';

    // src/identification.cpp:520 itemDescription() -- `addPrefix` indicates
    // that an article must be added.
    public static string Describe(ObjectIdentification identification, in Inventory item, bool addPrefix)
    {
        var indexx = item.SubCategoryId & (InventoryConfig.ItemSingleStackMin - 1);

        // base name, modifier string
        var basenm = DataTreasureData.GameObjects[item.Id].Name;
        string? modstr = null;

        var damstr = "";

        var appendName = false;
        var modify = !identification.ItemSetColorlessAsIdentified(item.CategoryId, item.SubCategoryId, item.Identification);
        var miscType = ItemMiscUse.Ignored;

        switch (item.CategoryId)
        {
            case TreasureCategory.TvMisc:
            case TreasureCategory.TvChest:
                break;
            case TreasureCategory.TvSlingAmmo:
            case TreasureCategory.TvBolt:
            case TreasureCategory.TvArrow:
                damstr = $" ({item.Damage.Count}d{item.Damage.Sides})";
                break;
            case TreasureCategory.TvLight:
                miscType = ItemMiscUse.Light;
                break;
            case TreasureCategory.TvSpike:
                break;
            case TreasureCategory.TvBow:
                damstr = $" (x{BowDamageValue(item.MiscUse)})";
                break;
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvSword:
                damstr = $" ({item.Damage.Count}d{item.Damage.Sides})";
                miscType = ItemMiscUse.Flags;
                break;
            case TreasureCategory.TvDigging:
                miscType = ItemMiscUse.ZPlusses;
                // (sic: sides twice)
                damstr = $" ({item.Damage.Sides}d{item.Damage.Sides})";
                break;
            case TreasureCategory.TvBoots:
            case TreasureCategory.TvGloves:
            case TreasureCategory.TvCloak:
            case TreasureCategory.TvHelm:
            case TreasureCategory.TvShield:
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvSoftArmor:
                break;
            case TreasureCategory.TvAmulet:
                if (modify)
                {
                    basenm = "& %s Amulet";
                    modstr = identification.Amulets[indexx];
                }
                else
                {
                    basenm = "& Amulet";
                    appendName = true;
                }
                miscType = ItemMiscUse.Plusses;
                break;
            case TreasureCategory.TvRing:
                if (modify)
                {
                    basenm = "& %s Ring";
                    modstr = identification.Rocks[indexx];
                }
                else
                {
                    basenm = "& Ring";
                    appendName = true;
                }
                miscType = ItemMiscUse.Plusses;
                break;
            case TreasureCategory.TvStaff:
                if (modify)
                {
                    basenm = "& %s Staff";
                    modstr = identification.Woods[indexx];
                }
                else
                {
                    basenm = "& Staff";
                    appendName = true;
                }
                miscType = ItemMiscUse.Charges;
                break;
            case TreasureCategory.TvWand:
                if (modify)
                {
                    basenm = "& %s Wand";
                    modstr = identification.Metals[indexx];
                }
                else
                {
                    basenm = "& Wand";
                    appendName = true;
                }
                miscType = ItemMiscUse.Charges;
                break;
            case TreasureCategory.TvScroll1:
            case TreasureCategory.TvScroll2:
                if (modify)
                {
                    basenm = "& Scroll~ titled \"%s\"";
                    modstr = identification.MagicItemTitles[indexx];
                }
                else
                {
                    basenm = "& Scroll~";
                    appendName = true;
                }
                break;
            case TreasureCategory.TvPotion1:
            case TreasureCategory.TvPotion2:
                if (modify)
                {
                    basenm = "& %s Potion~";
                    modstr = identification.Colors[indexx];
                }
                else
                {
                    basenm = "& Potion~";
                    appendName = true;
                }
                break;
            case TreasureCategory.TvFlask:
                break;
            case TreasureCategory.TvFood:
                if (modify)
                {
                    if (indexx <= 15)
                    {
                        basenm = "& %s Mushroom~";
                    }
                    else if (indexx <= 20)
                    {
                        basenm = "& Hairy %s Mold~";
                    }
                    if (indexx <= 20)
                    {
                        modstr = identification.Mushrooms[indexx];
                    }
                }
                else
                {
                    appendName = true;
                    if (indexx <= 15)
                    {
                        basenm = "& Mushroom~";
                    }
                    else if (indexx <= 20)
                    {
                        basenm = "& Hairy Mold~";
                    }
                    else
                    {
                        // Ordinary food does not have a name appended.
                        appendName = false;
                    }
                }
                break;
            case TreasureCategory.TvMagicBook:
                modstr = basenm;
                basenm = "& Book~ of Magic Spells %s";
                break;
            case TreasureCategory.TvPrayerBook:
                modstr = basenm;
                basenm = "& Holy Book~ of Prayers %s";
                break;
            case TreasureCategory.TvOpenDoor:
            case TreasureCategory.TvClosedDoor:
            case TreasureCategory.TvSecretDoor:
            case TreasureCategory.TvRubble:
                break;
            case TreasureCategory.TvGold:
            case TreasureCategory.TvInvisTrap:
            case TreasureCategory.TvVisTrap:
            case TreasureCategory.TvUpStair:
            case TreasureCategory.TvDownStair:
                return DataTreasureData.GameObjects[item.Id].Name + ".";
            case TreasureCategory.TvStoreDoor:
                return Helpers.Truncate($"the entrance to the {DataTreasureData.GameObjects[item.Id].Name}.", Helpers.MoriaObjDescSize);
            default:
                return "Error in objdes()";
        }

        var tmpVal = modstr != null
            ? Helpers.Truncate(ReplaceFormat(basenm, modstr), Helpers.MoriaObjDescSize)
            : basenm;

        if (appendName)
        {
            tmpVal += " of " + DataTreasureData.GameObjects[item.Id].Name;
        }

        if (item.ItemsCount != 1)
        {
            tmpVal = Helpers.InsertStringIntoString(tmpVal, "ch~", "ches");
            tmpVal = Helpers.InsertStringIntoString(tmpVal, "~", "s");
        }
        else
        {
            tmpVal = Helpers.InsertStringIntoString(tmpVal, "~", null);
        }

        if (!addPrefix)
        {
            if (tmpVal.StartsWith("some", StringComparison.Ordinal))
            {
                return tmpVal[5..];
            }
            if (tmpVal.Length > 0 && tmpVal[0] == '&')
            {
                // eliminate the '& ' at the beginning
                return tmpVal[2..];
            }
            return tmpVal;
        }

        var identified = ObjectIdentification.SpellItemIdentified(item);

        if (item.SpecialNameId != (byte)SpecialNameIds.SnNull && identified)
        {
            tmpVal += " " + DataTreasureData.SpecialItemNames[item.SpecialNameId];
        }

        tmpVal += damstr;

        string tmpStr;

        if (identified)
        {
            var absToHit = Math.Abs((int)item.ToHit);
            var absToDamage = Math.Abs((int)item.ToDamage);

            if ((item.Identification & IdentificationConfig.IdShowHitDam) != 0)
            {
                tmpStr = $" ({Sign(item.ToHit)}{absToHit},{Sign(item.ToDamage)}{absToDamage})";
            }
            else if (item.ToHit != 0)
            {
                tmpStr = $" ({Sign(item.ToHit)}{absToHit})";
            }
            else if (item.ToDamage != 0)
            {
                tmpStr = $" ({Sign(item.ToDamage)}{absToDamage})";
            }
            else
            {
                tmpStr = "";
            }
            tmpVal += tmpStr;
        }

        // Crowns have a zero base AC, so make a special test for them.
        var absToAc = Math.Abs((int)item.ToAc);
        if (item.Ac != 0 || item.CategoryId == TreasureCategory.TvHelm)
        {
            tmpVal += $" [{item.Ac}";
            if (identified)
            {
                // originally used %+d, but several machines don't support it
                tmpVal += $",{Sign(item.ToAc)}{absToAc}";
            }
            tmpVal += "]";
        }
        else if (item.ToAc != 0 && identified)
        {
            // originally used %+d, but several machines don't support it
            tmpVal += $" [{Sign(item.ToAc)}{absToAc}]";
        }

        // override defaults, check for `misc_type` flags in the `item.identification` field
        if ((item.Identification & IdentificationConfig.IdNoShowP1) != 0)
        {
            miscType = ItemMiscUse.Ignored;
        }
        else if ((item.Identification & IdentificationConfig.IdShowP1) != 0)
        {
            miscType = ItemMiscUse.ZPlusses;
        }

        tmpStr = "";

        if (miscType == ItemMiscUse.Light)
        {
            tmpStr = $" with {item.MiscUse} turns of light";
        }
        else if (miscType == ItemMiscUse.Ignored)
        {
            // NOOP
        }
        else if (identified)
        {
            var absMiscUse = Math.Abs((int)item.MiscUse);

            if (miscType == ItemMiscUse.ZPlusses)
            {
                // originally used %+d, but several machines don't support it
                tmpStr = $" ({Sign(item.MiscUse)}{absMiscUse})";
            }
            else if (miscType == ItemMiscUse.Charges)
            {
                tmpStr = $" ({item.MiscUse} charges)";
            }
            else if (item.MiscUse != 0)
            {
                if (miscType == ItemMiscUse.Plusses)
                {
                    tmpStr = $" ({Sign(item.MiscUse)}{absMiscUse})";
                }
                else if (miscType == ItemMiscUse.Flags)
                {
                    if ((item.Flags & TreasureConfig.Flags.TrStr) != 0)
                    {
                        tmpStr = $" ({Sign(item.MiscUse)}{absMiscUse} to STR)";
                    }
                    else if ((item.Flags & TreasureConfig.Flags.TrStealth) != 0)
                    {
                        tmpStr = $" ({Sign(item.MiscUse)}{absMiscUse} to stealth)";
                    }
                }
            }
        }
        tmpVal += tmpStr;

        string description;

        // ampersand is always the first character
        if (tmpVal.Length > 0 && tmpVal[0] == '&')
        {
            // use &tmp_val[1], so that & does not appear in output
            var rest = tmpVal[1..];
            if (item.ItemsCount > 1)
            {
                description = item.ItemsCount + Precision(rest, Helpers.MoriaObjDescSize - 4);
            }
            else if (item.ItemsCount < 1)
            {
                description = "no more" + Precision(rest, Helpers.MoriaObjDescSize - 8);
            }
            else if (tmpVal.Length > 2 && Helpers.IsVowel(tmpVal[2]))
            {
                description = "an" + Precision(rest, Helpers.MoriaObjDescSize - 3);
            }
            else
            {
                description = "a" + Precision(rest, Helpers.MoriaObjDescSize - 2);
            }
            description = Helpers.Truncate(description, Helpers.MoriaObjDescSize);
        }
        else if (item.ItemsCount < 1)
        {
            // handle 'no more' case specially

            // (sizeof("no more ") counts the terminator)
            var maxWidth = Helpers.MoriaObjDescSize - 9;

            // check for "some" at start
            if (tmpVal.StartsWith("some", StringComparison.Ordinal))
            {
                description = "no more " + Precision(tmpVal[5..], maxWidth);
            }
            else
            {
                // here if no article
                description = "no more " + Precision(tmpVal, maxWidth);
            }
            description = Helpers.Truncate(description, Helpers.MoriaObjDescSize);
        }
        else
        {
            description = tmpVal;
        }

        tmpStr = "";

        // don't print tried string for store bought items
        if (identification.IsObjectTried(item.CategoryId, item.SubCategoryId) && (item.Identification & IdentificationConfig.IdStoreBought) == 0)
        {
            tmpStr += "tried ";
        }

        if ((item.Identification & (IdentificationConfig.IdMagik | IdentificationConfig.IdEmpty | IdentificationConfig.IdDamd)) != 0)
        {
            if ((item.Identification & IdentificationConfig.IdMagik) != 0)
            {
                tmpStr += "magik ";
            }
            if ((item.Identification & IdentificationConfig.IdEmpty) != 0)
            {
                tmpStr += "empty ";
            }
            if ((item.Identification & IdentificationConfig.IdDamd) != 0)
            {
                tmpStr += "damned ";
            }
        }

        if (!string.IsNullOrEmpty(item.Inscription))
        {
            tmpStr += item.Inscription;
        }
        else if (tmpStr.Length > 0)
        {
            // remove the extra blank at the end
            tmpStr = tmpStr[..^1];
        }

        if (tmpStr.Length != 0)
        {
            description += Helpers.Truncate($" {{{tmpStr}}}", Helpers.MoriaObjDescSize);
        }

        return description + ".";
    }

    // snprintf(basenm, modstr) for the one-%s formats above
    private static string ReplaceFormat(string format, string value)
    {
        var at = format.IndexOf("%s", StringComparison.Ordinal);
        return format[..at] + value + format[(at + 2)..];
    }

    // printf's "%.*s"
    private static string Precision(string text, int max) => text.Length > max ? text[..max] : text;

    // src/ui_inventory.cpp:93 playerItemWearingDescription() -- part of the
    // identify spell's message for a worn item
    public static string PlayerItemWearingDescription(int bodyLocation)
    {
        switch (bodyLocation)
        {
            case PlayerConfig.Equipment.Wield:
                return "wielding";
            case PlayerConfig.Equipment.Head:
                return "wearing on your head";
            case PlayerConfig.Equipment.Neck:
                return "wearing around your neck";
            case PlayerConfig.Equipment.Body:
                return "wearing on your body";
            case PlayerConfig.Equipment.Arm:
                return "wearing on your arm";
            case PlayerConfig.Equipment.Hands:
                return "wearing on your hands";
            case PlayerConfig.Equipment.Right:
                return "wearing on your right hand";
            case PlayerConfig.Equipment.Left:
                return "wearing on your left hand";
            case PlayerConfig.Equipment.Feet:
                return "wearing on your feet";
            case PlayerConfig.Equipment.Outer:
                return "wearing about your body";
            case PlayerConfig.Equipment.Light:
                return "using to light the way";
            case PlayerConfig.Equipment.Auxiliary:
                return "holding ready by your side";
            default:
                return "carrying in your pack";
        }
    }

    // src/identification.cpp:864 itemChargesRemainingDescription() --
    // describe number of remaining charges. -RAK-
    public static void ChargesRemainingDescription(Character.PlayerWorld w, int itemId)
    {
        if (!ObjectIdentification.SpellItemIdentified(w.Py.Inventory[itemId]))
        {
            return;
        }

        var remNum = w.Py.Inventory[itemId].MiscUse;

        Character.PlayerCore.PrintMessage(w, Helpers.Truncate($"You have {remNum} charges remaining.", Helpers.MoriaMessageSize));
    }

    // src/identification.cpp:877 itemTypeRemainingCountDescription() --
    // describe amount of item remaining. -RAK-
    public static void TypeRemainingCountDescription(Character.PlayerWorld w, int itemId)
    {
        var item = w.Py.Inventory[itemId];

        item.ItemsCount--;

        var tmpStr = Describe(w.Identification, item, true);

        // the string already has a dot at the end.
        Character.PlayerCore.PrintMessage(w, Helpers.Truncate($"You have {tmpStr}", Helpers.MoriaObjDescSize));
    }
}
