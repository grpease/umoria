using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;

namespace Umoria.Core.Game;

// The commands that live in src/game_run.cpp itself (and playerRestOn(),
// src/player.cpp, whose prompt is all there is to it). (M15)
public sealed partial class GameRun
{
    // src/character.cpp:474 characterCreate() -- the screens are the UI's
    private void CharacterCreate() => _ui.CharacterCreate(Files.WelcomeScreen, ExitProgram);

    // src/game_run.cpp:1394 commandQuit()
    private void CommandQuit()
    {
        _ui.FlushInputBuffer();

        if (PlayerCore.GetInputConfirmation(W, "Do you really want to quit?"))
        {
            GameState.CharacterIsDead = true;
            Dg.GenerateNewLevel = true;

            GameState.CharacterDiedFrom = "Quitting";
        }
    }

    // src/game_run.cpp:1463 commandSaveAndExit()
    private void CommandSaveAndExit()
    {
        if (GameState.TotalWinner)
        {
            PlayerCore.PrintMessage(W, "You are a Total Winner,  your character must be retired.");

            PlayerCore.PrintMessage(W, GameState.Options.UseRoguelikeKeys
                ? "Use 'Q' to when you are ready to quit."
                : "Use <Control>-K when you are ready to quit.");
        }
        else
        {
            GameState.CharacterDiedFrom = "(saved)";
            PlayerCore.PrintMessage(W, "Saving game...");

            if (Save.SaveGame(W))
            {
                EndGame();
            }

            GameState.CharacterDiedFrom = "(alive and well)";
        }
    }

    // src/game_run.cpp:1556 commandToggleSearch()
    private void CommandToggleSearch()
    {
        if ((Py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
        {
            PlayerCore.SearchOff(W);
        }
        else
        {
            PlayerCore.SearchOn(W);
        }
    }

    // src/player.cpp:191 playerRestOn() -- resting allows a player to
    // safely restore his hp -RAK-
    private void PlayerRestOn()
    {
        var py = Py;
        var game = GameState;

        int restNum;

        if (game.CommandCount > 0)
        {
            restNum = (int)game.CommandCount;
            game.CommandCount = 0;
        }
        else
        {
            restNum = 0;

            _ui.PutStringClearToEol("Rest for how long? ", 0, 0);

            var restStr = _ui.GetStringInput(0, 19, 5);
            if (restStr is not null)
            {
                if (restStr.Length > 0 && restStr[0] == '*')
                {
                    restNum = -short.MaxValue;
                }
                else
                {
                    _ = Helpers.StringToNumber(restStr, ref restNum);
                }
            }
        }

        // check for reasonable value, must be positive number
        // in range of a short, or must be -MAX_SHORT
        if (restNum == -short.MaxValue || (restNum > 0 && restNum <= short.MaxValue))
        {
            if ((py.Flags.Status & PlayerConfig.Status.PySearch) != 0)
            {
                PlayerCore.SearchOff(W);
            }

            py.Flags.Rest = (short)restNum;
            py.Flags.Status |= PlayerConfig.Status.PyRest;
            _ui.PrintCharacterMovementState();
            py.Flags.FoodDigested--;

            _ui.PutStringClearToEol("Press any key to stop resting...", 0, 0);
            _ui.PutQio();

            return;
        }

        // Something went wrong
        if (restNum != 0)
        {
            PlayerCore.PrintMessage(W, "Invalid rest count.");
        }
        _ui.MessageLineClear();

        game.PlayerFreeTurn = true;
    }

    // src/game_run.cpp:2089 examineBook() -- examine a Book -RAK-
    private void ExamineBook()
    {
        var py = Py;

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvMagicBook, TreasureCategory.TvPrayerBook, out var itemPosStart, out var itemPosEnd))
        {
            PlayerCore.PrintMessage(W, "You are not carrying any books.");
            return;
        }

        if (py.Flags.Blind > 0)
        {
            PlayerCore.PrintMessage(W, "You can't see to read your spell book!");
            return;
        }

        if (PlayerMovement.NoLight(W))
        {
            PlayerCore.PrintMessage(W, "You have no light to read by.");
            return;
        }

        if (py.Flags.Confused > 0)
        {
            PlayerCore.PrintMessage(W, "You are too confused.");
            return;
        }

        if (!PlayerCore.GetItemId(W, "Which Book?", itemPosStart, itemPosEnd, out var itemId))
        {
            return;
        }

        var canRead = true;

        var treasureType = py.Inventory[itemId].CategoryId;

        var spellType = DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;
        if (spellType == SpellConfig.SpellTypeMage)
        {
            if (treasureType != TreasureCategory.TvMagicBook)
            {
                canRead = false;
            }
        }
        else if (spellType == SpellConfig.SpellTypePriest)
        {
            if (treasureType != TreasureCategory.TvPrayerBook)
            {
                canRead = false;
            }
        }
        else
        {
            canRead = false;
        }

        if (!canRead)
        {
            PlayerCore.PrintMessage(W, "You do not understand the language.");
            return;
        }

        var itemFlags = py.Inventory[itemId].Flags;
        var spellIndex = new List<int>();

        while (itemFlags != 0)
        {
            var spell = Helpers.GetAndClearFirstBit(ref itemFlags);

            if (DataPlayerData.MagicSpells[py.Misc.ClassId - 1][spell].LevelRequired < 99)
            {
                spellIndex.Add(spell);
            }
        }

        _ui.TerminalSaveScreen();
        _ui.DisplaySpellsList(spellIndex, spellIndex.Count, true, -1);
        _ui.WaitForContinueKey(0);
        _ui.TerminalRestoreScreen();
    }

    // src/game_run.cpp:2155 dungeonGoUpLevel() -- go up one level -RAK-
    private void DungeonGoUpLevel()
    {
        var tileId = Dg.Floor[Py.Pos.Y, Py.Pos.X].TreasureId;

        if (tileId != 0 && W.Treasure[tileId].CategoryId == TreasureCategory.TvUpStair)
        {
            Dg.CurrentLevel--;

            PlayerCore.PrintMessage(W, "You enter a maze of up staircases.");
            PlayerCore.PrintMessage(W, "You pass through a one-way door.");

            Dg.GenerateNewLevel = true;
        }
        else
        {
            PlayerCore.PrintMessage(W, "I see no up staircase here.");
            GameState.PlayerFreeTurn = true;
        }
    }

    // src/game_run.cpp:2172 dungeonGoDownLevel() -- go down one level -RAK-
    private void DungeonGoDownLevel()
    {
        var tileId = Dg.Floor[Py.Pos.Y, Py.Pos.X].TreasureId;

        if (tileId != 0 && W.Treasure[tileId].CategoryId == TreasureCategory.TvDownStair)
        {
            Dg.CurrentLevel++;

            PlayerCore.PrintMessage(W, "You enter a maze of down staircases.");
            PlayerCore.PrintMessage(W, "You pass through a one-way door.");

            Dg.GenerateNewLevel = true;
        }
        else
        {
            PlayerCore.PrintMessage(W, "I see no down staircase here.");
            GameState.PlayerFreeTurn = true;
        }
    }

    // src/game_run.cpp:2189 dungeonJamDoor() -- jam a closed door -RAK-
    private void DungeonJamDoor()
    {
        var py = Py;

        GameState.PlayerFreeTurn = true;

        var coord = py.Pos;

        if (!PlayerCore.GetDirectionWithMemory(W, null, out var direction))
        {
            return;
        }
        PlayerMovement.MovePosition(Dg, direction, ref coord);

        var tile = Dg.Floor[coord.Y, coord.X];

        if (tile.TreasureId == 0)
        {
            PlayerCore.PrintMessage(W, "That isn't a door!");
            return;
        }

        ref var item = ref W.Treasure[tile.TreasureId];

        var itemId = item.CategoryId;
        if (itemId != TreasureCategory.TvClosedDoor && itemId != TreasureCategory.TvOpenDoor)
        {
            PlayerCore.PrintMessage(W, "That isn't a door!");
            return;
        }

        if (itemId == TreasureCategory.TvOpenDoor)
        {
            PlayerCore.PrintMessage(W, "The door must be closed first.");
            return;
        }

        // If we reach here, the door is closed and we can try to jam it -MRC-

        if (tile.CreatureId == 0)
        {
            if (InventoryDamage.FindRange(py, TreasureCategory.TvSpike, TreasureCategory.TvNever, out var itemPosStart, out _))
            {
                GameState.PlayerFreeTurn = false;

                PlayerCore.PrintMessageNoCommandInterrupt(W, "You jam the door with a spike.");

                if (item.MiscUse > 0)
                {
                    // Make locked to stuck.
                    item.MiscUse = (short)-item.MiscUse;
                }

                // Successive spikes have a progressively smaller effect.
                // Series is: 0 20 30 37 43 48 52 56 60 64 67 70 ...
                item.MiscUse -= (short)(1 + 190 / (10 - item.MiscUse));

                if (py.Inventory[itemPosStart].ItemsCount > 1)
                {
                    py.Inventory[itemPosStart].ItemsCount--;
                    py.Pack.Weight -= (short)py.Inventory[itemPosStart].Weight;
                }
                else
                {
                    InventoryDamage.DestroyItem(py, itemPosStart);
                }
            }
            else
            {
                PlayerCore.PrintMessage(W, "But you have no spikes.");
            }
        }
        else
        {
            GameState.PlayerFreeTurn = false;

            var name = DataCreaturesData.CreaturesList[W.Monsters.Monsters[tile.CreatureId].CreatureId].Name;
            PlayerCore.PrintMessage(W, $"The {name} is in your way!");
        }
    }

    // src/game_run.cpp:2257 inventoryRefillLamp() -- refill the players
    // lamp -RAK-
    private void InventoryRefillLamp()
    {
        var py = Py;

        GameState.PlayerFreeTurn = true;

        if (py.Inventory[PlayerConfig.Equipment.Light].SubCategoryId != 0)
        {
            PlayerCore.PrintMessage(W, "But you are not using a lamp.");
            return;
        }

        if (!InventoryDamage.FindRange(py, TreasureCategory.TvFlask, TreasureCategory.TvNever, out var itemPosStart, out _))
        {
            PlayerCore.PrintMessage(W, "You have no oil.");
            return;
        }

        GameState.PlayerFreeTurn = false;

        ref var item = ref py.Inventory[PlayerConfig.Equipment.Light];
        item.MiscUse += py.Inventory[itemPosStart].MiscUse;

        if (item.MiscUse > TreasureConfig.ObjectLampMaxCapacity)
        {
            item.MiscUse = TreasureConfig.ObjectLampMaxCapacity;
            PlayerCore.PrintMessage(W, "Your lamp overflows, spilling oil on the ground.");
            PlayerCore.PrintMessage(W, "Your lamp is full.");
        }
        else if (item.MiscUse > TreasureConfig.ObjectLampMaxCapacity / 2)
        {
            PlayerCore.PrintMessage(W, "Your lamp is more than half full.");
        }
        else if (item.MiscUse == TreasureConfig.ObjectLampMaxCapacity / 2)
        {
            PlayerCore.PrintMessage(W, "Your lamp is half full.");
        }
        else
        {
            PlayerCore.PrintMessage(W, "Your lamp is less than half full.");
        }

        ItemDescription.TypeRemainingCountDescription(W, itemPosStart);
        InventoryDamage.DestroyItem(py, itemPosStart);
    }
}
