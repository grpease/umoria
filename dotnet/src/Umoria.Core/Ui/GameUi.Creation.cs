using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;

namespace Umoria.Core.Ui;

// src/character.cpp characterCreate() with its screens (M15). The
// character-building steps are M6's (Character.CharacterCreation, which
// takes the answers as a CharacterChoices record); this is the original's
// interactive flow around them: the race and sex menus, the re-roll loop,
// the class menu, the name prompt and the final "press any key". Each
// step runs exactly where the original runs it, so the RNG stream is the
// same as the scripted version's for the same answers.
public sealed partial class GameUi
{
    // src/character.cpp:474 characterCreate() -- main character creation
    // routine -JWT-. `welcomeScreen` is the '?' help file; `exitProgram` is
    // what 'Q' at the last prompt calls (it doesn't return).
    public void CharacterCreate(string welcomeScreen, Action exitProgram)
    {
        var py = W.Py;

        PrintCharacterInformation();
        CharacterChooseRace(welcomeScreen);
        CharacterSetGender(welcomeScreen);

        // here we start a loop giving a player a choice of characters -RGM-
        var done = false;
        while (!done)
        {
            CharacterCreation.GenerateStatsAndRace(py, W.Print);
            CharacterCreation.GetHistory(py);
            CharacterCreation.SetAgeHeightWeight(py);
            DisplayCharacterHistory();
            PrintCharacterVitalStatistics();
            PrintCharacterStats();

            ClearToBottom(20);
            PutString("Hit space to re-roll or ESC to accept characteristics: ", 20, 2);

            while (true)
            {
                var key = GetKeyInput();
                if (key == Escape)
                {
                    done = true;
                    break;
                }

                if (key == ' ')
                {
                    break;
                }

                TerminalBellSound();
            }
        }

        CharacterGetClass(welcomeScreen);
        CharacterCreation.CalculateStartGold(py);
        PrintCharacterStats();
        PrintCharacterLevelExperience();
        PrintCharacterAbilities();
        GetCharacterName();

        PutStringClearToEol("[ press any key to continue, or Q to exit ]", 23, 17);
        if (GetKeyInput() == 'Q')
        {
            exitProgram();
        }
        EraseLine(23, 0);
    }

    // src/character.cpp:111 displayCharacterRaces() -- prints a list of the
    // available races: Human, Elf, etc., shown during the character
    // creation screens.
    private void DisplayCharacterRaces()
    {
        ClearToBottom(20);
        PutString("Choose a race (? for Help):", 20, 2);

        var y = 21;
        var x = 2;

        for (var i = 0; i < PlayerConfig.PlayerMaxRaces; i++)
        {
            PutString($"{(char)(i + 'a')}) {DataPlayerData.CharacterRaces[i].Name}", y, x);

            x += 15;
            if (x > 70)
            {
                x = 2;
                y++;
            }
        }
    }

    // src/character.cpp:132 characterChooseRace() -- allows player to
    // select a race -JWT-
    private void CharacterChooseRace(string welcomeScreen)
    {
        DisplayCharacterRaces();

        int id;
        while (true)
        {
            MoveCursor(20, 30);
            var key = GetKeyInput();

            id = key - 97; // ASCII `a`, setting id between 0 and 7
            if (id >= 0 && id < PlayerConfig.PlayerMaxRaces)
            {
                break;
            }

            if (key == '?')
            {
                DisplayTextHelpFile(welcomeScreen);
            }
            else
            {
                TerminalBellSound();
            }
        }
        W.Py.Misc.RaceId = (byte)id;

        PutString(DataPlayerData.CharacterRaces[id].Name, 3, 15);
    }

    // src/character.cpp:155 displayCharacterHistory() -- will print the
    // history of a character -JWT-
    private void DisplayCharacterHistory()
    {
        PutString("Character Background", 14, 27);

        for (var i = 0; i < 4; i++)
        {
            PutStringClearToEol(W.Py.Misc.History[i], i + 15, 10);
        }
    }

    // src/character.cpp:267 characterSetGender() -- gets the character's
    // gender -JWT-
    private void CharacterSetGender(string welcomeScreen)
    {
        ClearToBottom(20);
        PutString("Choose a sex (? for Help):", 20, 2);
        PutString("m) Male       f) Female", 21, 2);

        while (true)
        {
            MoveCursor(20, 29);
            var key = GetKeyInput();

            if (key == 'f' || key == 'F')
            {
                PlayerBonuses.SetGender(W.Py, false);
                PutString("Female", 4, 15);
                break;
            }

            if (key == 'm' || key == 'M')
            {
                PlayerBonuses.SetGender(W.Py, true);
                PutString("Male", 4, 15);
                break;
            }

            if (key == '?')
            {
                DisplayTextHelpFile(welcomeScreen);
            }
            else
            {
                TerminalBellSound();
            }
        }
    }

    // src/character.cpp:318 displayRaceClasses() -- prints the classes for
    // a given race: Rogue, Mage, Priest, etc., shown during the character
    // creation screens.
    private IReadOnlyList<int> DisplayRaceClasses(int raceId)
    {
        var classList = CharacterCreation.AvailableClasses(raceId);

        ClearToBottom(20);
        PutString("Choose a class (? for Help):", 20, 2);

        var y = 21;
        var x = 2;

        for (var i = 0; i < classList.Count; i++)
        {
            PutString($"{(char)(i + 'a')}) {DataPlayerData.Classes[classList[i]].Title}", y, x);

            x += 15;
            if (x > 70)
            {
                x = 2;
                y++;
            }
        }

        return classList;
    }

    // src/character.cpp:412 characterGetClass() -- gets a character class
    // -JWT-
    private void CharacterGetClass(string welcomeScreen)
    {
        var classList = DisplayRaceClasses(W.Py.Misc.RaceId);

        // Reset the class ID
        W.Py.Misc.ClassId = 0;

        while (true)
        {
            MoveCursor(20, 31);
            var key = GetKeyInput();

            var id = key - 97; // ASCII `a`, setting id to 0-5
            if (id >= 0 && id < classList.Count)
            {
                // generateCharacterClass() starts by putting the class's
                // title up, below the menu it clears
                ClearToBottom(20);
                PutString(DataPlayerData.Classes[classList[id]].Title, 5, 15);
                CharacterCreation.GenerateCharacterClass(W.Py, (byte)classList[id], W.Print);
                break;
            }

            if (key == '?')
            {
                DisplayTextHelpFile(welcomeScreen);
            }
            else
            {
                TerminalBellSound();
            }
        }
    }
}
