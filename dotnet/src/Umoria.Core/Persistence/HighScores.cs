using System.Text.Json;
using Umoria.Core.Character;
using Umoria.Core.Data.Generated;
using Umoria.Core.Items;

namespace Umoria.Core.Persistence;

// src/scores.h HighScore_t -- one scoreboard entry. Name and DiedFrom were
// fixed char[27] / char[25] buffers (strcpy'd into, so a longer cause of
// death overflowed); here they are plain strings, and the score screen's
// "%-19.19s" / "%-22.22s" still cut them down for display.
public sealed class HighScore
{
    public int Points { get; set; }
    public int BirthDate { get; set; }
    public short Uid { get; set; }
    public short Mhp { get; set; }
    public short Chp { get; set; }
    public byte DungeonDepth { get; set; }
    public byte Level { get; set; }
    public byte DeepestDungeonDepth { get; set; }

    // 'M' or 'F'
    public byte Gender { get; set; }
    public byte Race { get; set; }
    public byte CharacterClass { get; set; }
    public string Name { get; set; } = "";
    public string DiedFrom { get; set; } = "";
}

// The JSON scoreboard file (replaces scores.dat's XOR-encoded records, by
// the same compatibility-breaking decision as the save file). Entries are
// kept in the order scores.cpp keeps them in the file: highest first.
public sealed class HighScoreFile
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public List<HighScore> Entries { get; set; } = [];
}

// src/scores.cpp -- the scoreboard: entering a finished character
// (recordNewHighScore()), the score screen's lines (showScoresScreen()) and
// the score itself (playerCalculateTotalPoints()).
//
// The file handling is new (JSON, see HighScoreFile); the insertion rules
// are the original's, split out of its read-seek-write loop into Insert()
// so they can be checked against the real game without the file format.
public static class HighScores
{
    // src/scores.h MAX_HIGH_SCORE_ENTRIES -- number of entries allowed in
    // the score file.
    public const int MaxHighScoreEntries = 1000;

    // showScoresScreen() puts twenty scores on each page, on lines 2
    // through 21.
    public const int ScoresPerPage = 20;

    public const string ScoreScreenHeader = "Rank  Points Name              Sex Race       Class  Lvl Killed By";

    // src/scores.cpp:236 playerCalculateTotalPoints()
    public static int PlayerCalculateTotalPoints(PlayerWorld w)
    {
        var py = w.Py;

        var total = py.Misc.MaxExp + (100 * py.Misc.MaxDungeonDepth);
        total += py.Misc.Au / 100;

        foreach (var item in py.Inventory)
        {
            total += ItemValue.StoreItemValue(w.Identification, item);
        }

        total += w.Dg.CurrentLevel * 50;

        // Don't ever let the score decrease from one save to the next.
        if (py.MaxScore > total)
        {
            return py.MaxScore;
        }

        return total;
    }

    // The entry-building half of src/scores.cpp:22 recordNewHighScore().
    public static HighScore NewEntry(PlayerWorld w)
    {
        var py = w.Py;

        return new HighScore
        {
            Points = PlayerCalculateTotalPoints(w),
            BirthDate = py.Misc.DateOfBirth,
            Uid = 0, // NOTE: do we not want to use `getuid()`? -MRC-
            Mhp = py.Misc.MaxHp,
            Chp = py.Misc.CurrentHp,
            DungeonDepth = (byte)w.Dg.CurrentLevel,
            Level = (byte)py.Misc.Level,
            DeepestDungeonDepth = (byte)py.Misc.MaxDungeonDepth,
            Gender = (byte)(PlayerBonuses.IsMale(py) ? 'M' : 'F'),
            Race = py.Misc.RaceId,
            CharacterClass = py.Misc.ClassId,
            Name = py.Misc.Name,
            DiedFrom = StripArticle(w.Game.CharacterDiedFrom),
        };
    }

    // recordNewHighScore()'s cause-of-death trim: "a Kobold" / "an Orc"
    // lose the article. Quirk kept: only a leading 'a' is tested, so any
    // cause starting with "a" or "an" loses it, with or without a following
    // space ("ant" -> "t").
    private static string StripArticle(string diedFrom)
    {
        var i = 0;
        if (diedFrom.Length > 0 && diedFrom[0] == 'a')
        {
            i++;
            if (i < diedFrom.Length && diedFrom[i] == 'n')
            {
                i++;
            }

            while (i < diedFrom.Length && char.IsWhiteSpace(diedFrom[i]))
            {
                i++;
            }
        }

        return diedFrom[i..];
    }

    // The search-and-insert loops of recordNewHighScore(), on a list
    // instead of the open file. Returns false when the entry is not added.
    //
    // Walking down from the top: an entry scoring strictly more than the
    // new one that is "the same character" stops the insert (it already
    // has a better score), and so does passing `maxEntries` of them. The
    // new entry goes in front of the first entry it equals or beats; the
    // rest shift down one place, except that the first of them that is
    // "the same character" is dropped instead (the loop overwrites it and
    // stops shifting). Nothing trims the list to `maxEntries` afterwards --
    // as in the original, the cap only limits how far down a new entry can
    // land.
    public static bool Insert(List<HighScore> scores, HighScore entry, int maxEntries = MaxHighScoreEntries)
    {
        var position = 0;
        var passed = 0;

        while (position < scores.Count)
        {
            var old = scores[position];
            if (entry.Points >= old.Points)
            {
                break;
            }

            if (IsSameCharacter(entry, old))
            {
                return false;
            }

            // only allow one thousand scores in the score file
            passed++;
            if (passed >= maxEntries)
            {
                return false;
            }

            position++;
        }

        scores.Insert(position, entry);

        for (var i = position + 1; i < scores.Count; i++)
        {
            if (IsSameCharacter(entry, scores[i]))
            {
                scores.RemoveAt(i);
                break;
            }
        }

        return true;
    }

    // under unix, only allow one gender/race/class combo per person,
    // on single user system, allow any number of entries, but try to
    // prevent multiple entries per character by checking for case when
    // birthdate/gender/race/class are the same, and game.character_died_from
    // of score file entry is "(saved)"
    private static bool IsSameCharacter(HighScore entry, HighScore old)
    {
        return ((entry.Uid != 0 && entry.Uid == old.Uid) ||
                (entry.Uid == 0 && old.DiedFrom == "(saved)" && entry.BirthDate == old.BirthDate)) &&
               entry.Gender == old.Gender && entry.Race == old.Race && entry.CharacterClass == old.CharacterClass;
    }

    // src/scores.cpp:22 recordNewHighScore() -- enters the character on the
    // scoreboard at `path`. The original needs the score file to exist
    // already (main.cpp creates it); a missing or empty file here is a new,
    // empty scoreboard, as an empty scores.dat was. A file from another
    // format version is left alone, silently, as the original does (the
    // score screen reports it).
    public static void RecordNewHighScore(PlayerWorld w, string path)
    {
        // (clearScreen() is display, M14.)

        if (w.Game.NoScore != 0)
        {
            return;
        }

        if (w.Game.PanicSave)
        {
            w.Print("Sorry, scores for games restored from panic save files are not saved.");
            return;
        }

        var entry = NewEntry(w);

        HighScoreFile? file;
        try
        {
            file = ReadFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            w.Print($"Error opening score file '{path}'.");
            return;
        }

        file ??= new HighScoreFile();

        if (file.FormatVersion != HighScoreFile.CurrentFormatVersion)
        {
            return;
        }

        if (!Insert(file.Entries, entry))
        {
            return;
        }

        try
        {
            WriteFile(path, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            w.Print($"Error opening score file '{path}'.");
        }
    }

    // One line of src/scores.cpp:173 showScoresScreen()'s table. Paging (20
    // per page, the header on line 0, "[ press any key to continue ]",
    // <ESCAPE> to stop) is the score screen's, M14.
    public static string FormatScoreLine(int rank, HighScore score)
    {
        // "%-4d%8d %-19.19s %c %-10.10s %-7.7s%3d %-22.22s"
        return $"{rank,-4}{score.Points,8} {Fit(score.Name, 19)} {(char)score.Gender} " +
               $"{Fit(DataPlayerData.CharacterRaces[score.Race].Name, 10)} " +
               $"{Fit(DataPlayerData.Classes[score.CharacterClass].Title, 7)}{score.Level,3} {Fit(score.DiedFrom, 22)}";
    }

    // printf's "%-N.Ns"
    private static string Fit(string s, int width)
    {
        return (s.Length > width ? s[..width] : s).PadRight(width);
    }

    // The scoreboard at `path`, or null when there is no file yet (or it is
    // empty). Throws JsonException on a damaged file.
    public static HighScoreFile? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        if (json.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize<HighScoreFile>(json, SaveFileJson.Options);
    }

    public static void WriteFile(string path, HighScoreFile file)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(file, SaveFileJson.Options));
    }
}
