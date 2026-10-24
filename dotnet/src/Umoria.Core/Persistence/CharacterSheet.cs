using System.Text;
using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Persistence;

// src/game_files.cpp outputPlayerCharacterToFile() and its three writers --
// the character sheet, equipment list and inventory written to a text file
// (the 'C' screen's "save to file" and the death screen's "print to file").
// Text, not game state; ported for the file, and so M14's character screen
// can reuse the two ui.cpp formatters below.
//
// The rest of game_files.cpp is not here: initializeScoreFile() is file
// opening (HighScores reads its file on demand); displaySplashScreen(),
// displayTextHelpFile() and displayDeathFile() are text-file pagers (UI,
// M14); outputRandomLevelObjectsToFile() is a wizard command (out of scope,
// like wizard.cpp).
public static class CharacterSheet
{
    // CTRL_KEY('L') -- the original separates pages with a form feed.
    private const char FormFeed = '\f';

    // src/game_files.cpp:360 outputPlayerCharacterToFile() -- never replaces
    // an existing file without asking (w.Confirm). Lines end in "\n" on
    // every platform (the original's text-mode file gets "\r\n" on Windows).
    public static bool OutputPlayerCharacterToFile(PlayerWorld w, string filename)
    {
        FileStream? file = null;
        try
        {
            file = new FileStream(filename, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException) when (File.Exists(filename))
        {
            if (PlayerCore.GetInputConfirmation(w, $"Replace existing file {filename}?"))
            {
                file = TryOpen(filename);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        if (file is null)
        {
            w.Print($"Can't open file {filename}:");
            return false;
        }

        using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
        {
            // ("Writing character sheet..." is display.)
            writer.Write(Write(w));
        }

        // ("Completed." is display.)
        return true;
    }

    private static FileStream? TryOpen(string filename)
    {
        try
        {
            return new FileStream(filename, FileMode.Truncate, FileAccess.Write);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The file's whole text: writeCharacterSheetToFile(),
    // writeEquipmentListToFile(), writeInventoryToFile().
    public static string Write(PlayerWorld w)
    {
        var o = new StringBuilder();
        WriteCharacterSheet(o, w.Py);
        WriteEquipmentList(o, w);
        WriteInventory(o, w);
        return o.ToString();
    }

    // src/game_files.cpp:198 writeCharacterSheetToFile()
    private static void WriteCharacterSheet(StringBuilder o, Player py)
    {
        const string colon = ":";
        const string blank = " ";
        var m = py.Misc;
        var used = py.Stats.Used;

        o.Append($"{FormFeed}\n\n");

        o.Append($" Name{colon,9} {m.Name,-23}");
        o.Append($" Age{colon,11} {m.Age,6}");
        o.Append($"   STR : {StatsAsString(used[PlayerConfig.Attr.Str])}\n");
        o.Append($" Race{colon,9} {DataPlayerData.CharacterRaces[m.RaceId].Name,-23}");
        o.Append($" Height{colon,8} {m.Height,6}");
        o.Append($"   INT : {StatsAsString(used[PlayerConfig.Attr.Int])}\n");
        o.Append($" Sex{colon,10} {PlayerBonuses.GenderLabel(py),-23}");
        o.Append($" Weight{colon,8} {m.Weight,6}");
        o.Append($"   WIS : {StatsAsString(used[PlayerConfig.Attr.Wis])}\n");
        o.Append($" Class{colon,8} {DataPlayerData.Classes[m.ClassId].Title,-23}");
        o.Append($" Social Class : {m.SocialClass,6}");
        o.Append($"   DEX : {StatsAsString(used[PlayerConfig.Attr.Dex])}\n");
        o.Append($" Title{colon,8} {PlayerBonuses.RankTitle(py),-23}");
        o.Append($"{blank,22}");
        o.Append($"   CON : {StatsAsString(used[PlayerConfig.Attr.Con])}\n");
        o.Append($"{blank,34}");
        o.Append($"{blank,26}");
        o.Append($"   CHR : {StatsAsString(used[PlayerConfig.Attr.Chr])}\n\n");

        o.Append($" + To Hit    : {m.DisplayToHit,6}");
        o.Append($"{blank,7}Level      : {m.Level,7}");
        o.Append($"    Max Hit Points : {m.MaxHp,6}\n");
        o.Append($" + To Damage : {m.DisplayToDamage,6}");
        o.Append($"{blank,7}Experience : {m.Exp,7}");
        o.Append($"    Cur Hit Points : {m.CurrentHp,6}\n");
        o.Append($" + To AC     : {m.DisplayToAc,6}");
        o.Append($"{blank,7}Max Exp    : {m.MaxExp,7}");
        o.Append($"    Max Mana{colon,8} {m.Mana,6}\n");
        o.Append($"   Total AC  : {m.DisplayAc,6}");
        if (m.Level >= PlayerConfig.PlayerMaxLevel)
        {
            o.Append($"{blank,7}Exp to Adv : *******");
        }
        else
        {
            o.Append($"{blank,7}Exp to Adv : {(int)(py.BaseExpLevels[m.Level - 1] * m.ExperienceFactor / 100),7}");
        }

        o.Append($"    Cur Mana{colon,8} {m.CurrentMana,6}\n");
        o.Append($"{blank,28}Gold{colon,8} {m.Au,7}\n\n");

        var adj = DataPlayerData.ClassLevelAdj[m.ClassId];

        var xbth = m.Bth + m.PlussesToHit * PlayerConfig.BthPerPlusToHitAdjust +
                   (adj[PlayerConfig.ClassLevelAdj.Bth] * m.Level);
        var xbthb = m.BthWithBows + m.PlussesToHit * PlayerConfig.BthPerPlusToHitAdjust +
                    (adj[PlayerConfig.ClassLevelAdj.Bthb] * m.Level);

        // this results in a range from 0 to 29
        var xfos = 40 - m.Fos;
        if (xfos < 0)
        {
            xfos = 0;
        }

        int xsrh = m.ChanceInSearch;

        // this results in a range from 0 to 9
        var xstl = m.StealthFactor + 1;
        var xdis = m.Disarm + 2 * PlayerStatLogic.DisarmAdjustment(py) +
                   PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int) +
                   (adj[PlayerConfig.ClassLevelAdj.Disarm] * m.Level / 3);
        var xsave = m.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Wis) +
                    (adj[PlayerConfig.ClassLevelAdj.Save] * m.Level / 3);
        var xdev = m.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int) +
                   (adj[PlayerConfig.ClassLevelAdj.Device] * m.Level / 3);

        var xinfra = $"{py.Flags.SeeInfra * 10} feet";

        o.Append("(Miscellaneous Abilities)\n\n");
        o.Append($" Fighting    : {StatRating(12, xbth),-10}");
        o.Append($"   Stealth     : {StatRating(1, xstl),-10}");
        o.Append($"   Perception  : {StatRating(3, xfos)}\n");
        o.Append($" Bows/Throw  : {StatRating(12, xbthb),-10}");
        o.Append($"   Disarming   : {StatRating(8, xdis),-10}");
        o.Append($"   Searching   : {StatRating(6, xsrh)}\n");
        o.Append($" Saving Throw: {StatRating(6, xsave),-10}");
        o.Append($"   Magic Device: {StatRating(6, xdev),-10}");
        o.Append($"   Infra-Vision: {xinfra}\n\n");

        // Write out the character's history
        o.Append("Character Background\n");
        foreach (var entry in m.History)
        {
            o.Append($" {entry}\n");
        }
    }

    // src/game_files.cpp:296 equipmentPlacementDescription()
    private static string EquipmentPlacementDescription(int itemId)
    {
        return itemId switch
        {
            PlayerConfig.Equipment.Wield => "You are wielding",
            PlayerConfig.Equipment.Head => "Worn on head",
            PlayerConfig.Equipment.Neck => "Worn around neck",
            PlayerConfig.Equipment.Body => "Worn on body",
            PlayerConfig.Equipment.Arm => "Worn on shield arm",
            PlayerConfig.Equipment.Hands => "Worn on hands",
            PlayerConfig.Equipment.Right => "Right ring finger",
            PlayerConfig.Equipment.Left => "Left  ring finger",
            PlayerConfig.Equipment.Feet => "Worn on feet",
            PlayerConfig.Equipment.Outer => "Worn about body",
            PlayerConfig.Equipment.Light => "Light source is",
            PlayerConfig.Equipment.Auxiliary => "Secondary weapon",
            _ => "*Unknown value*",
        };
    }

    // src/game_files.cpp:328 writeEquipmentListToFile()
    private static void WriteEquipmentList(StringBuilder o, PlayerWorld w)
    {
        var py = w.Py;

        o.Append("\n  [Character's Equipment List]\n\n");

        if (py.EquipmentCount == 0)
        {
            o.Append("  Character has no equipment in use.\n");
            return;
        }

        var itemSlotId = 0;

        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.PlayerInventorySize; i++)
        {
            if (py.Inventory[i].CategoryId == TreasureCategory.TvNothing)
            {
                continue;
            }

            var description = ItemDescription.Describe(w.Identification, py.Inventory[i], true);
            o.Append($"  {(char)(itemSlotId + 'a')}) {EquipmentPlacementDescription(i),-19}: {description}\n");

            itemSlotId++;
        }

        o.Append($"{FormFeed}\n\n");
    }

    // src/game_files.cpp:351 writeInventoryToFile()
    private static void WriteInventory(StringBuilder o, PlayerWorld w)
    {
        var py = w.Py;

        o.Append("  [General Inventory List]\n\n");

        if (py.Pack.UniqueItems == 0)
        {
            o.Append("  Character has no objects in inventory.\n");
            return;
        }

        for (var i = 0; i < py.Pack.UniqueItems; i++)
        {
            var description = ItemDescription.Describe(w.Identification, py.Inventory[i], true);
            o.Append($"{(char)(i + 'a')}) {description}\n");
        }

        o.Append(FormFeed);
    }

    // src/ui.cpp:144 statsAsString() -- a stat as "    17", " 18/05",
    // "18/100".
    public static string StatsAsString(byte stat)
    {
        var percentile = stat - 18;

        if (stat <= 18)
        {
            return $"{stat,6}";
        }

        if (percentile == 100)
        {
            return "18/100";
        }

        return $" 18/{percentile:00}";
    }

    // src/ui.cpp:504 statRating() -- returns a rating of x depending on y
    // -JWT- (the original takes them as a Coord_t{y, x}).
    public static string StatRating(int y, int x)
    {
        return (x / y) switch
        {
            -3 or -2 or -1 => "Very Bad",
            0 or 1 => "Bad",
            2 => "Poor",
            3 or 4 => "Fair",
            5 => "Good",
            6 => "Very Good",
            7 or 8 => "Excellent",
            _ => "Superb",
        };
    }
}
