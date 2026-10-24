using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Persistence;
using Umoria.Core.Spells;

namespace Umoria.Core.Ui;

// src/ui.cpp -- the dungeon panel, the status block and the character
// screen. (M14)
//
// Two of these "display" routines change game state, as in the original:
// printCharacterMovementState() maintains PY_REPEAT and
// printCharacterStudyInstruction() clears PY_STUDY. With a terminal attached,
// PlayerCore.UpdateMovementStateFlags and PlayerCore.DrawCavePanel are these
// routines; headless, they do only the state upkeep.
public sealed partial class GameUi
{
    // src/ui.h STAT_COLUMN
    private const int StatColumn = 0;

    private static readonly string[] StatNames = ["STR : ", "INT : ", "WIS : ", "DEX : ", "CON : ", "CHR : "];

    // src/ui.cpp blank_string (BLANK_LENGTH 24); &blank_string[BLANK_LENGTH - n]
    private static string Blanks(int n) => new(' ', n);

    // src/ui.cpp:82 drawDungeonPanel() -- prints the map of the dungeon -RAK-
    public void DrawDungeonPanel()
    {
        var panel = W.Dg.Panel;
        var line = 1;

        // Top to bottom
        for (var y = panel.Top; y <= panel.Bottom; y++)
        {
            EraseLine(line, 13);
            line++;

            // Left to right
            for (var x = panel.Left; x <= panel.Right; x++)
            {
                var ch = TileGlyph(new Coord(y, x));
                if (ch != ' ')
                {
                    PanelPutTile(ch, y, x);
                }
            }
        }
    }

    // src/ui.cpp:103 drawCavePanel() -- draws entire screen -RAK-
    public void DrawCavePanel()
    {
        ClearScreen();
        PrintCharacterStatsBlock();
        DrawDungeonPanel();
        PrintCharacterCurrentDepth();
    }

    // src/ui.cpp:157 displayCharacterStats() -- print character stat in
    // given row, column -RAK-
    public void DisplayCharacterStats(int stat)
    {
        PutString(StatNames[stat], 6 + stat, StatColumn);
        PutString(CharacterSheet.StatsAsString(W.Py.Stats.Used[stat]), 6 + stat, StatColumn + 6);
    }

    // src/ui.cpp:166 printCharacterInfoInField() -- the longest title is 13
    // characters, so only pad to 13
    private void PrintCharacterInfoInField(string info, int y, int x)
    {
        // blank out the current field space
        PutString(Blanks(13), y, x);

        PutString(info, y, x);
    }

    // printHeaderNumber() / printHeaderLongNumber() -- "%s: %6d"
    private void PrintHeaderNumber(string header, int num, int y, int x)
    {
        PutString(Helpers.Truncate($"{header}: {num,6}", Helpers.MoriaMessageSize), y, x);
    }

    // printHeaderLongNumber7Spaces() -- "%s: %7d"
    private void PrintHeaderLongNumber7Spaces(string header, int num, int y, int x)
    {
        PutString(Helpers.Truncate($"{header}: {num,7}", Helpers.MoriaMessageSize), y, x);
    }

    // printNumber() / printLongNumber() -- "%6d"
    private static string Number(int num) => $"{num,6}";

    // src/ui.cpp:209 printCharacterTitle()
    public void PrintCharacterTitle()
    {
        PrintCharacterInfoInField(PlayerBonuses.RankTitle(W.Py), 4, StatColumn);
    }

    // src/ui.cpp:214 printCharacterLevel()
    public void PrintCharacterLevel()
    {
        PutString(Number(W.Py.Misc.Level), 13, StatColumn + 6);
    }

    // src/ui.cpp:219 printCharacterCurrentMana()
    public void PrintCharacterCurrentMana()
    {
        PutString(Number(W.Py.Misc.CurrentMana), 15, StatColumn + 6);
    }

    // src/ui.cpp:224 printCharacterMaxHitPoints()
    public void PrintCharacterMaxHitPoints()
    {
        PutString(Number(W.Py.Misc.MaxHp), 16, StatColumn + 6);
    }

    // src/ui.cpp:229 printCharacterCurrentHitPoints()
    public void PrintCharacterCurrentHitPoints()
    {
        PutString(Number(W.Py.Misc.CurrentHp), 17, StatColumn + 6);
    }

    // src/ui.cpp:234 printCharacterCurrentArmorClass()
    public void PrintCharacterCurrentArmorClass()
    {
        PutString(Number(W.Py.Misc.DisplayAc), 19, StatColumn + 6);
    }

    // src/ui.cpp:239 printCharacterGoldValue()
    public void PrintCharacterGoldValue()
    {
        PutString(Number(W.Py.Misc.Au), 20, StatColumn + 6);
    }

    // src/ui.cpp:760 displayCharacterExperience()'s display half (the level
    // gain is PlayerCore.DisplayCharacterExperience)
    public void PrintCharacterExperience()
    {
        PutString(Number(W.Py.Misc.Exp), 14, StatColumn + 6);
    }

    // src/ui.cpp:244 printCharacterCurrentDepth() -- prints depth in stat
    // area -RAK-
    public void PrintCharacterCurrentDepth()
    {
        PutStringClearToEol(DepthText(), 23, 65);
    }

    private string DepthText()
    {
        var depth = W.Dg.CurrentLevel * 50;
        return depth == 0 ? "Town level" : $"{depth} feet";
    }

    // src/ui.cpp:259 printCharacterHungerStatus() -- prints status of hunger
    // -RAK-
    public void PrintCharacterHungerStatus() => PutString(HungerText(), 23, 0);

    private string HungerText()
    {
        var status = W.Py.Flags.Status;

        if ((status & PlayerConfig.Status.PyWeak) != 0)
        {
            return "Weak  ";
        }

        return (status & PlayerConfig.Status.PyHungry) != 0 ? "Hungry" : Blanks(6);
    }

    // src/ui.cpp:270 printCharacterBlindStatus() -- prints Blind status -RAK-
    public void PrintCharacterBlindStatus() => PutString(BlindText(), 23, 7);

    private string BlindText() => (W.Py.Flags.Status & PlayerConfig.Status.PyBlind) != 0 ? "Blind" : Blanks(5);

    // src/ui.cpp:279 printCharacterConfusedState() -- prints Confusion status
    // -RAK-
    public void PrintCharacterConfusedState() => PutString(ConfusedText(), 23, 13);

    private string ConfusedText() => (W.Py.Flags.Status & PlayerConfig.Status.PyConfused) != 0 ? "Confused" : Blanks(8);

    // src/ui.cpp:288 printCharacterFearState() -- prints Fear status -RAK-
    public void PrintCharacterFearState() => PutString(FearText(), 23, 22);

    private string FearText() => (W.Py.Flags.Status & PlayerConfig.Status.PyFear) != 0 ? "Afraid" : Blanks(6);

    // src/ui.cpp:297 printCharacterPoisonedState() -- prints Poisoned status
    // -RAK-
    public void PrintCharacterPoisonedState() => PutString(PoisonedText(), 23, 29);

    private string PoisonedText() => (W.Py.Flags.Status & PlayerConfig.Status.PyPoisoned) != 0 ? "Poisoned" : Blanks(8);

    // src/ui.cpp:306 printCharacterMovementState() -- prints Searching,
    // Resting, Paralysis, or 'count' status -RAK-. Maintains PY_REPEAT (see
    // the class comment).
    public void PrintCharacterMovementState()
    {
        var f = W.Py.Flags;

        f.Status &= ~PlayerConfig.Status.PyRepeat;

        if (f.Paralysis <= 1 && (f.Status & PlayerConfig.Status.PyRest) == 0 && W.Game.CommandCount > 0)
        {
            f.Status |= PlayerConfig.Status.PyRepeat;
        }

        PutString(MovementText(), 23, 38);
    }

    // The text printCharacterMovementState() leaves on screen.
    private string MovementText()
    {
        var f = W.Py.Flags;

        if (f.Paralysis > 1)
        {
            return "Paralysed";
        }

        if ((f.Status & PlayerConfig.Status.PyRest) != 0)
        {
            if (f.Rest < 0)
            {
                return "Rest *";
            }

            return W.Game.Options.DisplayCounts ? $"Rest {f.Rest,-5}" : "Rest";
        }

        if (W.Game.CommandCount > 0)
        {
            var repeat = W.Game.Options.DisplayCounts ? $"Repeat {W.Game.CommandCount:000}" : "Repeat";

            if ((f.Status & PlayerConfig.Status.PySearch) != 0)
            {
                // "Search" is printed over the start of "Repeat"
                repeat = "Search" + repeat[6..];
            }

            return repeat;
        }

        if ((f.Status & PlayerConfig.Status.PySearch) != 0)
        {
            return "Searching";
        }

        // "repeat 999" is 10 characters
        return Blanks(10);
    }

    // src/ui.cpp:360 printCharacterSpeed() -- prints the speed of a
    // character. -CJS-
    public void PrintCharacterSpeed() => PutString(SpeedText(), 23, 49);

    private string SpeedText()
    {
        var speed = (int)W.Py.Flags.Speed;

        // Search mode.
        if ((W.Py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
        {
            speed--;
        }

        return speed switch
        {
            > 1 => "Very Slow",
            1 => "Slow     ",
            0 => Blanks(9),
            -1 => "Fast     ",
            _ => "Very Fast",
        };
    }

    // src/ui.cpp:381 printCharacterStudyInstruction() -- clears PY_STUDY (see
    // the class comment)
    public void PrintCharacterStudyInstruction()
    {
        W.Py.Flags.Status &= ~PlayerConfig.Status.PyStudy;

        PutString(StudyText(), 23, 59);
    }

    private string StudyText() => W.Py.Flags.NewSpellsToLearn == 0 ? Blanks(5) : "Study";

    // src/ui.cpp:392 printCharacterWinner() -- prints winner status on
    // display -RAK-
    public void PrintCharacterWinner()
    {
        var text = WinnerText();
        if (text is not null)
        {
            PutString(text, 22, 0);
        }
    }

    // null: printCharacterWinner() prints nothing
    private string? WinnerText()
    {
        var game = W.Game;

        if ((game.NoScore & 0x2) != 0)
        {
            return game.WizardMode ? "Is wizard  " : "Was wizard ";
        }

        if ((game.NoScore & 0x1) != 0)
        {
            return "Resurrected";
        }

        if ((game.NoScore & 0x4) != 0)
        {
            return "Duplicate";
        }

        return game.TotalWinner ? "*Winner*   " : null;
    }

    // src/ui.cpp:409 printCharacterStatsBlock() -- prints character-screen
    // info -RAK-
    public void PrintCharacterStatsBlock()
    {
        var py = W.Py;
        var m = py.Misc;

        PrintCharacterInfoInField(DataPlayerData.CharacterRaces[m.RaceId].Name, 2, StatColumn);
        PrintCharacterInfoInField(DataPlayerData.Classes[m.ClassId].Title, 3, StatColumn);
        PrintCharacterInfoInField(PlayerBonuses.RankTitle(py), 4, StatColumn);

        for (var i = 0; i < 6; i++)
        {
            DisplayCharacterStats(i);
        }

        PrintHeaderNumber("LEV ", m.Level, 13, StatColumn);
        PrintHeaderNumber("EXP ", m.Exp, 14, StatColumn);
        PrintHeaderNumber("MANA", m.CurrentMana, 15, StatColumn);
        PrintHeaderNumber("MHP ", m.MaxHp, 16, StatColumn);
        PrintHeaderNumber("CHP ", m.CurrentHp, 17, StatColumn);
        PrintHeaderNumber("AC  ", m.DisplayAc, 19, StatColumn);
        PrintHeaderNumber("GOLD", m.Au, 20, StatColumn);
        PrintCharacterWinner();

        var status = py.Flags.Status;

        if (((PlayerConfig.Status.PyHungry | PlayerConfig.Status.PyWeak) & status) != 0)
        {
            PrintCharacterHungerStatus();
        }

        if ((status & PlayerConfig.Status.PyBlind) != 0)
        {
            PrintCharacterBlindStatus();
        }

        if ((status & PlayerConfig.Status.PyConfused) != 0)
        {
            PrintCharacterConfusedState();
        }

        if ((status & PlayerConfig.Status.PyFear) != 0)
        {
            PrintCharacterFearState();
        }

        if ((status & PlayerConfig.Status.PyPoisoned) != 0)
        {
            PrintCharacterPoisonedState();
        }

        if (((PlayerConfig.Status.PySearch | PlayerConfig.Status.PyRest) & status) != 0)
        {
            PrintCharacterMovementState();
        }

        // if speed non zero, print it, modify speed if Searching
        var speed = (short)(py.Flags.Speed - (short)((status & PlayerConfig.Status.PySearch) >> 8));
        if (speed != 0)
        {
            PrintCharacterSpeed();
        }

        // display the study field
        PrintCharacterStudyInstruction();
    }

    // src/ui.cpp:464 printCharacterInformation() -- prints the following
    // information on the screen. -JWT-
    public void PrintCharacterInformation()
    {
        ClearScreen();

        PutString("Name        :", 2, 1);
        PutString("Race        :", 3, 1);
        PutString("Sex         :", 4, 1);
        PutString("Class       :", 5, 1);

        if (!W.Game.CharacterGenerated)
        {
            return;
        }

        var m = W.Py.Misc;

        PutString(m.Name, 2, 15);
        PutString(DataPlayerData.CharacterRaces[m.RaceId].Name, 3, 15);
        PutString(PlayerBonuses.GenderLabel(W.Py), 4, 15);
        PutString(DataPlayerData.Classes[m.ClassId].Title, 5, 15);
    }

    // src/ui.cpp:483 printCharacterStats() -- prints the following
    // information on the screen. -JWT-
    public void PrintCharacterStats()
    {
        var py = W.Py;

        for (var i = 0; i < 6; i++)
        {
            PutString(StatNames[i], 2 + i, 61);
            PutString(CharacterSheet.StatsAsString(py.Stats.Used[i]), 2 + i, 66);

            if (py.Stats.Max[i] > py.Stats.Current[i])
            {
                PutString(CharacterSheet.StatsAsString(py.Stats.Max[i]), 2 + i, 73);
            }
        }

        PrintHeaderNumber("+ To Hit    ", py.Misc.DisplayToHit, 9, 1);
        PrintHeaderNumber("+ To Damage ", py.Misc.DisplayToDamage, 10, 1);
        PrintHeaderNumber("+ To AC     ", py.Misc.DisplayToAc, 11, 1);
        PrintHeaderNumber("  Total AC  ", py.Misc.DisplayAc, 12, 1);
    }

    // src/ui.cpp:531 printCharacterVitalStatistics() -- prints age, height,
    // weight, and SC -JWT-
    public void PrintCharacterVitalStatistics()
    {
        var m = W.Py.Misc;

        PrintHeaderNumber("Age          ", m.Age, 2, 38);
        PrintHeaderNumber("Height       ", m.Height, 3, 38);
        PrintHeaderNumber("Weight       ", m.Weight, 4, 38);
        PrintHeaderNumber("Social Class ", m.SocialClass, 5, 38);
    }

    // src/ui.cpp:539 printCharacterLevelExperience() -- prints the following
    // information on the screen. -JWT-
    public void PrintCharacterLevelExperience()
    {
        var py = W.Py;
        var m = py.Misc;

        PrintHeaderLongNumber7Spaces("Level      ", m.Level, 9, 28);
        PrintHeaderLongNumber7Spaces("Experience ", m.Exp, 10, 28);
        PrintHeaderLongNumber7Spaces("Max Exp    ", m.MaxExp, 11, 28);

        if (m.Level >= PlayerConfig.PlayerMaxLevel)
        {
            PutStringClearToEol("Exp to Adv.: *******", 12, 28);
        }
        else
        {
            PrintHeaderLongNumber7Spaces("Exp to Adv.", (int)(py.BaseExpLevels[m.Level - 1] * m.ExperienceFactor / 100), 12, 28);
        }

        PrintHeaderLongNumber7Spaces("Gold       ", m.Au, 13, 28);
        PrintHeaderNumber("Max Hit Points ", m.MaxHp, 9, 52);
        PrintHeaderNumber("Cur Hit Points ", m.CurrentHp, 10, 52);
        PrintHeaderNumber("Max Mana       ", m.Mana, 11, 52);
        PrintHeaderNumber("Cur Mana       ", m.CurrentMana, 12, 52);
    }

    // src/ui.cpp:558 printCharacterAbilities() -- prints ratings on certain
    // abilities -RAK-
    public void PrintCharacterAbilities()
    {
        ClearToBottom(14);

        var py = W.Py;
        var m = py.Misc;
        var adj = DataPlayerData.ClassLevelAdj[m.ClassId];

        var xbth = m.Bth + m.PlussesToHit * PlayerConfig.BthPerPlusToHitAdjust + (adj[PlayerConfig.ClassLevelAdj.Bth] * m.Level);
        var xbthb = m.BthWithBows + m.PlussesToHit * PlayerConfig.BthPerPlusToHitAdjust + (adj[PlayerConfig.ClassLevelAdj.Bthb] * m.Level);

        // this results in a range from 0 to 29
        var xfos = 40 - m.Fos;
        if (xfos < 0)
        {
            xfos = 0;
        }

        int xsrh = m.ChanceInSearch;

        // this results in a range from 0 to 9
        var xstl = m.StealthFactor + 1;
        var xdis = m.Disarm + 2 * PlayerStatLogic.DisarmAdjustment(py) + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int) +
                   (adj[PlayerConfig.ClassLevelAdj.Disarm] * m.Level / 3);
        var xsave = m.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Wis) +
                    (adj[PlayerConfig.ClassLevelAdj.Save] * m.Level / 3);
        var xdev = m.SavingThrow + PlayerStatLogic.StatAdjustmentWisdomIntelligence(py, PlayerConfig.Attr.Int) +
                   (adj[PlayerConfig.ClassLevelAdj.Device] * m.Level / 3);

        var xinfra = $"{py.Flags.SeeInfra * 10} feet";

        PutString("(Miscellaneous Abilities)", 15, 25);
        PutString("Fighting    :", 16, 1);
        PutString(CharacterSheet.StatRating(12, xbth), 16, 15);
        PutString("Bows/Throw  :", 17, 1);
        PutString(CharacterSheet.StatRating(12, xbthb), 17, 15);
        PutString("Saving Throw:", 18, 1);
        PutString(CharacterSheet.StatRating(6, xsave), 18, 15);

        PutString("Stealth     :", 16, 28);
        PutString(CharacterSheet.StatRating(1, xstl), 16, 42);
        PutString("Disarming   :", 17, 28);
        PutString(CharacterSheet.StatRating(8, xdis), 17, 42);
        PutString("Magic Device:", 18, 28);
        PutString(CharacterSheet.StatRating(6, xdev), 18, 42);

        PutString("Perception  :", 16, 55);
        PutString(CharacterSheet.StatRating(3, xfos), 16, 69);
        PutString("Searching   :", 17, 55);
        PutString(CharacterSheet.StatRating(6, xsrh), 17, 69);
        PutString("Infra-Vision:", 18, 55);
        PutString(xinfra, 18, 69);
    }

    // src/ui.cpp:608 printCharacter() -- used to display the character on
    // the screen. -RAK-
    public void PrintCharacter()
    {
        PrintCharacterInformation();
        PrintCharacterVitalStatistics();
        PrintCharacterStats();
        PrintCharacterLevelExperience();
        PrintCharacterAbilities();
    }

    // src/ui.cpp:617 getCharacterName() -- gets a name for the character
    // -JWT-
    public void GetCharacterName()
    {
        PutStringClearToEol("Enter your player's name  [press <RETURN> when finished]", 21, 2);

        PutString(Blanks(23), 2, 15);

        var name = GetStringInput(2, 15, 23);
        if (string.IsNullOrEmpty(name))
        {
            name = GetDefaultPlayerName();
            W.Py.Misc.Name = name;
            PutString(name, 2, 15);
        }
        else
        {
            W.Py.Misc.Name = name;
        }

        ClearToBottom(20);
    }

    // src/ui.cpp:631 changeCharacterName() -- changes the name of the
    // character -JWT-
    public void ChangeCharacterName()
    {
        var flag = false;

        PrintCharacter();

        while (!flag)
        {
            PutStringClearToEol("<f>ile character description. <c>hange character name.", 21, 2);

            switch (GetKeyInput())
            {
                case 'c':
                    GetCharacterName();
                    flag = true;
                    break;
                case 'f':
                    PutStringClearToEol("File name:", 0, 0);

                    var temp = GetStringInput(0, 10, 60);
                    if (!string.IsNullOrEmpty(temp))
                    {
                        if (OutputPlayerCharacterToFile(temp))
                        {
                            flag = true;
                        }
                    }
                    break;
                case Escape:
                case ' ':
                case '\n':
                case '\r':
                    flag = true;
                    break;
                default:
                    TerminalBellSound();
                    break;
            }
        }
    }

    // src/game_files.cpp:360 outputPlayerCharacterToFile() (its messages are
    // printed through w.Ui)
    private bool OutputPlayerCharacterToFile(string filename) => CharacterSheet.OutputPlayerCharacterToFile(W, filename);

    // src/ui.cpp:670 displaySpellsList() -- print list of spells -RAK-
    // if nonConsecutive is  -1: spells numbered consecutively from 'a' to 'a'+num
    //                      >=0: spells numbered by offset from nonConsecutive
    public void DisplaySpellsList(IReadOnlyList<int> spellIds, int numberOfChoices, bool comment, int nonConsecutive)
    {
        var py = W.Py;
        var col = comment ? 22 : 31;

        var consecutiveOffset = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells == SpellConfig.SpellTypeMage
            ? SpellConfig.NameOffsetSpells
            : SpellConfig.NameOffsetPrayers;

        EraseLine(1, col);
        PutString("Name", 1, col + 5);
        PutString("Lv Mana Fail", 1, col + 35);

        // only show the first 22 choices
        if (numberOfChoices > 22)
        {
            numberOfChoices = 22;
        }

        for (var i = 0; i < numberOfChoices; i++)
        {
            var spellId = spellIds[i];
            var spell = DataPlayerData.MagicSpells[py.Misc.ClassId - 1][spellId];

            string p;
            if (!comment)
            {
                p = "";
            }
            else if ((py.Flags.SpellsForgotten & (1u << spellId)) != 0)
            {
                p = " forgotten";
            }
            else if ((py.Flags.SpellsLearnt & (1u << spellId)) == 0)
            {
                p = " unknown";
            }
            else if ((py.Flags.SpellsWorked & (1u << spellId)) == 0)
            {
                p = " untried";
            }
            else
            {
                p = "";
            }

            // determine whether or not to leave holes in character choices, nonConsecutive -1
            // when learning spells, consecutive_offset>=0 when asking which spell to cast.
            var spellChar = nonConsecutive == -1 ? (char)('a' + i) : (char)('a' + spellId - nonConsecutive);

            var outVal = $"  {spellChar}) {DataPlayerData.SpellNames[spellId + consecutiveOffset],-30}{spell.LevelRequired,2} {spell.ManaRequired,4} {SpellCasting.SpellChanceOfSuccess(py, spellId),3}%{p}";
            PutStringClearToEol(Helpers.Truncate(outVal, Helpers.MoriaMessageSize), 2 + i, col);
        }
    }
}
