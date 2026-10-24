using System.Globalization;
using Umoria.Core.Character;
using Umoria.Core.Data.Generated;
using Umoria.Core.Persistence;
using Umoria.Core.Spells;

namespace Umoria.Core.Game;

// src/game_death.cpp -- code executed when player dies (M15)
public sealed partial class GameRun
{
    // src/helpers.cpp:157 humanDateString() -- "%a %b %d" on Windows,
    // "%a %b %e" (space-padded day) elsewhere, as the original builds.
    private string HumanDateString()
    {
        var now = Clock.LocalNow();
        var day = OperatingSystem.IsWindows()
            ? now.Day.ToString("00", CultureInfo.InvariantCulture)
            : now.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2);

        return now.ToString("ddd MMM ", CultureInfo.InvariantCulture) + day;
    }

    // putString() of `text` centred on column 26 of the tombstone
    private void PutCentred(string text, int row) => _ui.PutString(text, row, 26 - text.Length / 2);

    // src/game_death.cpp:11 printTomb() -- prints the gravestone of the
    // character -RAK-
    private void PrintTomb()
    {
        var py = Py;

        _ui.DisplayDeathFile(Files.DeathTomb);

        PutCentred(py.Misc.Name, 6);

        PutCentred(!GameState.TotalWinner ? PlayerBonuses.RankTitle(py) : "Magnificent", 8);

        string text;
        if (!GameState.TotalWinner)
        {
            text = DataPlayerData.Classes[py.Misc.ClassId].Title;
        }
        else if (PlayerBonuses.IsMale(py))
        {
            text = "*King*";
        }
        else
        {
            text = "*Queen*";
        }
        PutCentred(text, 10);

        _ui.PutString(py.Misc.Level.ToString(CultureInfo.InvariantCulture), 11, 30);

        PutCentred($"{py.Misc.Exp} Exp", 12);

        PutCentred($"{py.Misc.Au} Au", 13);

        _ui.PutString(Dg.CurrentLevel.ToString(CultureInfo.InvariantCulture), 14, 34);

        PutCentred(GameState.CharacterDiedFrom, 16);

        PutCentred(HumanDateString(), 17);

        while (true)
        {
            // retry:
            _ui.FlushInputBuffer();

            _ui.PutString("(ESC to abort, return to print on screen, or file name)", 23, 0);
            _ui.PutString("Character record?", 22, 0);

            var str = _ui.GetStringInput(22, 18, 60);
            if (str is null)
            {
                return;
            }

            for (var i = 0; i < py.Inventory.Length; i++)
            {
                ref var item = ref py.Inventory[i];
                W.Identification.ItemSetAsIdentified(item.CategoryId, item.SubCategoryId);
                W.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref item);
            }

            PlayerBonuses.RecalculateBonuses(py);

            if (str.Length != 0)
            {
                if (!CharacterSheet.OutputPlayerCharacterToFile(W, Path.Combine(Files.BaseDirectory, str)))
                {
                    continue; // goto retry
                }
            }
            else
            {
                _ui.ClearScreen();
                _ui.PrintCharacter();
                _ui.PutString("Type ESC to skip the inventory:", 23, 0);
                if (_ui.GetKeyInput() != Escape)
                {
                    _ui.ClearScreen();
                    PlayerCore.PrintMessage(W, "You are using:");
                    _ui.DisplayEquipment(true, 0);
                    _ui.PrintMessage(null);
                    PlayerCore.PrintMessage(W, "You are carrying:");
                    _ui.ClearToBottom(1);
                    _ui.DisplayInventoryItems(0, py.Pack.UniqueItems - 1, true, 0, null);
                    _ui.PrintMessage(null);
                }
            }

            return;
        }
    }

    // src/game_death.cpp:96 printCrown() -- let the player know they did
    // good.
    private void PrintCrown()
    {
        _ui.DisplayDeathFile(Files.DeathRoyal);
        _ui.PutString(PlayerBonuses.IsMale(Py) ? "King!" : "Queen!", 17, 45);
        _ui.FlushInputBuffer();
        _ui.WaitForContinueKey(23);
    }

    // src/game_death.cpp:108 kingly() -- change the player into a King!
    // -RAK-
    private void Kingly()
    {
        var py = Py;

        // Change the character attributes.
        Dg.CurrentLevel = 0;
        GameState.CharacterDiedFrom = "Ripe Old Age";

        _ = SpellEffects.RestorePlayerLevels(W);

        py.Misc.Level += Config.PlayerConfig.PlayerMaxLevel;
        py.Misc.Au += 250000;
        py.Misc.MaxExp += 5000000;
        py.Misc.Exp = py.Misc.MaxExp;

        PrintCrown();
    }

    // src/game_death.cpp:124 endGame() -- what happens upon dying -RAK-;
    // handles the gravestone and top-twenty routines -RAK-. Never returns:
    // it ends with exitProgram().
    private void EndGame()
    {
        _ui.PrintMessage(null);

        // flush all input
        _ui.FlushInputBuffer();

        // If the game has been saved, then save sets turn back to -1,
        // which inhibits the printing of the tomb.
        if (Dg.GameTurn >= 0)
        {
            if (GameState.TotalWinner)
            {
                Kingly();
            }
            PrintTomb();
        }

        // Save the memory at least.
        if (GameState.CharacterGenerated && !GameState.CharacterSaved)
        {
            _ = Save.SaveGame(W);
        }

        // add score to score file if applicable
        if (GameState.CharacterGenerated)
        {
            // Clear `game.character_saved`, strange thing to do, but it prevents
            // getKeyInput() from recursively calling endGame() when there has
            // been an eof on stdin detected.
            GameState.CharacterSaved = false;

            // (recordNewHighScore() starts with clearScreen())
            _ui.ClearScreen();
            HighScores.RecordNewHighScore(W, Files.Scores);
            _ui.ShowScoresScreen(Files.Scores);
        }
        _ui.EraseLine(23, 0);

        ExitProgram();
    }
}
