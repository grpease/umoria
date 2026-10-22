using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Items;

// src/ui_inventory.cpp's inventory commands (M11): inventoryExecuteCommand()
// -- show inventory/equipment, wear/wield, take off, drop, exchange weapons
// -- and src/identification.cpp's itemInscribe().
//
// These live in a UI file but carry real game logic: equipping and
// unequipping, curse checks, the free-turn rule, the ring-hand prompt, and
// the screen state the command loop branches on (game.screen's current
// screen and wear range, game.doing_inventory_command). All of that is
// ported; what isn't is the drawing: the item lists, help screen and header
// lines (displayInventoryItems() etc.), and the screen's left/bottom
// positions, which only place them. Keys come through PlayerCore.GetCommand
// (getCommand()/getKeyInput()), yes/no through GetInputConfirmation, the
// drop-all prompt through PlayerWorld.ConfirmWithAbort.
public static class InventoryCommands
{
    private const char Escape = (char)27;

    // getKeyInput() -- a raw key; <ESCAPE> comes back as ESCAPE
    private static char GetKeyInput(PlayerWorld w)
    {
        PlayerCore.GetCommand(w, "", out var key);
        return key;
    }

    // src/ui_inventory.cpp:281 uiCommandSwitchScreen() -- the screen-id
    // half; the redraw is display
    private static void SwitchScreen(PlayerWorld w, Screen nextScreen)
    {
        if (nextScreen == w.Game.Screen.CurrentScreenId)
        {
            return;
        }
        w.Game.Screen.CurrentScreenId = nextScreen;
    }

    // src/ui_inventory.cpp:326 verifyAction() -- used to verify if this
    // really is the item we wish to wear or read. -CJS-
    private static bool VerifyAction(PlayerWorld w, string prompt, int item)
    {
        var description = ItemDescription.Describe(w.Identification, w.Py.Inventory[item], true);

        // change the period to a question mark
        description = description[..^1] + "?";

        return PlayerCore.GetInputConfirmation(w, Helpers.Truncate($"{prompt} {description}", Helpers.MoriaObjDescSize));
    }

    // src/ui_inventory.cpp:339 requestAndShowInventoryScreen()
    private static void RequestAndShowInventoryScreen(PlayerWorld w, bool recoverScreen)
    {
        var game = w.Game;

        if (game.DoingInventoryCommand == 0)
        {
            // this forces exit of inventoryExecuteCommand() if selecting is not set true
            game.Screen.CurrentScreenId = Screen.Blank;
            return;
        }

        // Take up where we left off after a previous inventory command. -CJS-

        // If the screen has been flushed, we need to redraw. If the command
        // is a simple ' ' to recover the screen, just quit. Otherwise, check
        // and see what the user wants.
        if (game.ScreenHasChanged)
        {
            if (recoverScreen || !PlayerCore.GetInputConfirmation(w, "Continuing with inventory command?"))
            {
                game.DoingInventoryCommand = (char)0;
                return;
            }
        }

        // (a forced redraw of the current screen -- display)
    }

    // src/ui_inventory.cpp:366 uiCommandInventoryTakeOffItem()
    private static bool TakeOffItem(PlayerWorld w, bool selecting)
    {
        var py = w.Py;

        if (py.EquipmentCount == 0)
        {
            PlayerCore.PrintMessage(w, "You are not using any equipment.");
            // don't print message restarting inven command after taking off something, it is confusing
            return selecting;
        }

        if (py.Pack.UniqueItems >= PlayerConfig.Equipment.Wield && w.Game.DoingInventoryCommand == 0)
        {
            PlayerCore.PrintMessage(w, "You will have to drop something first.");
            return selecting;
        }

        if (w.Game.Screen.CurrentScreenId != Screen.Blank)
        {
            SwitchScreen(w, Screen.Equipment);
        }

        return true;
    }

    // src/ui_inventory.cpp:385 uiCommandInventoryDropItem()
    private static bool DropItem(PlayerWorld w, ref char command, bool selecting)
    {
        var py = w.Py;

        if (py.Pack.UniqueItems == 0 && py.EquipmentCount == 0)
        {
            PlayerCore.PrintMessage(w, "But you're not carrying anything.");
            return selecting;
        }

        if (w.Dg.Floor[py.Pos.Y, py.Pos.X].TreasureId != 0)
        {
            PlayerCore.PrintMessage(w, "There's no room to drop anything here.");
            return selecting;
        }

        var screen = w.Game.Screen.CurrentScreenId;

        if ((screen == Screen.Equipment && py.EquipmentCount > 0) || py.Pack.UniqueItems == 0)
        {
            if (screen != Screen.Blank)
            {
                SwitchScreen(w, Screen.Equipment);
            }
            command = 'r'; // Remove - or take off and drop.
        }
        else if (screen != Screen.Blank)
        {
            SwitchScreen(w, Screen.Inventory);
        }

        return true;
    }

    // src/ui_inventory.cpp:408 uiCommandInventoryWearWieldItem()
    private static bool WearWieldItem(PlayerWorld w, bool selecting)
    {
        var py = w.Py;
        var screen = w.Game.Screen;

        // Note: simple loop to get the global game.screen.wear_low_id value
        screen.WearLowId = 0;
        while (screen.WearLowId < py.Pack.UniqueItems && py.Inventory[screen.WearLowId].CategoryId > TreasureCategory.TvMaxWear)
        {
            screen.WearLowId++;
        }

        // Note: simple loop to get the global wear_high value
        screen.WearHighId = screen.WearLowId;
        while (screen.WearHighId < py.Pack.UniqueItems && py.Inventory[screen.WearHighId].CategoryId >= TreasureCategory.TvMinWear)
        {
            screen.WearHighId++;
        }
        screen.WearHighId--;

        if (screen.WearLowId > screen.WearHighId)
        {
            PlayerCore.PrintMessage(w, "You have nothing to wear or wield.");
            return selecting;
        }

        if (screen.CurrentScreenId != Screen.Blank && screen.CurrentScreenId != Screen.Inventory)
        {
            SwitchScreen(w, Screen.Wear);
        }

        return true;
    }

    // src/ui_inventory.cpp:434 uiCommandInventoryUnwieldItem() -- exchange
    // the primary and secondary weapons
    private static void UnwieldItem(PlayerWorld w)
    {
        var py = w.Py;

        if (!InventoryActions.IsWieldingItem(py))
        {
            PlayerCore.PrintMessage(w, "But you are wielding no weapons.");
            return;
        }

        if (InventoryActions.WornItemIsCursed(py, PlayerConfig.Equipment.Wield))
        {
            var description = ItemDescription.Describe(w.Identification, py.Inventory[PlayerConfig.Equipment.Wield], false);
            PlayerCore.PrintMessage(w, Helpers.Truncate($"The {description} you are wielding appears to be cursed.", Helpers.MoriaObjDescSize));
            return;
        }

        w.Game.PlayerFreeTurn = false;

        // swap auxiliary and wield items
        (py.Inventory[PlayerConfig.Equipment.Auxiliary], py.Inventory[PlayerConfig.Equipment.Wield]) =
            (py.Inventory[PlayerConfig.Equipment.Wield], py.Inventory[PlayerConfig.Equipment.Auxiliary]);

        PlayerCombat.AdjustBonusesForItem(w, py.Inventory[PlayerConfig.Equipment.Auxiliary], -1); // Subtract bonuses
        PlayerCombat.AdjustBonusesForItem(w, py.Inventory[PlayerConfig.Equipment.Wield], 1); // Add bonuses

        if (py.Inventory[PlayerConfig.Equipment.Wield].CategoryId != TreasureCategory.TvNothing)
        {
            var description = ItemDescription.Describe(w.Identification, py.Inventory[PlayerConfig.Equipment.Wield], true);
            // (strcat() into an obj_desc_t, unbounded)
            PlayerCore.PrintMessage(w, "Primary weapon   : " + description);
        }
        else
        {
            PlayerCore.PrintMessage(w, "No primary weapon.");
        }

        // this is a new weapon, so clear the heavy flag
        py.WeaponIsHeavy = false;
        PlayerCore.Strength(w);
    }

    // src/ui_inventory.cpp:484 inventoryGetItemMatchingInscription() -- look
    // for item whose inscription matches `which`
    private static int ItemMatchingInscription(Player py, char which, char command, int from, int to)
    {
        if (which >= '0' && which <= '9' && command != 'r' && command != 't')
        {
            // Note: simple loop to get id
            var m = from;
            while (m <= to && m < PlayerConfig.PlayerInventorySize && py.Inventory[m].Inscription != which.ToString())
            {
                m++;
            }

            return m <= to ? m : -1;
        }

        if (which >= 'A' && which <= 'Z')
        {
            return which - 'A';
        }

        return which - 'a';
    }

    // src/ui_inventory.cpp:525 changeScreenForCommand()
    private static void ChangeScreenForCommand(PlayerWorld w, char command)
    {
        if (command == 't' || command == 'r')
        {
            SwitchScreen(w, Screen.Equipment);
        }
        else if (command == 'w' && w.Game.Screen.CurrentScreenId != Screen.Inventory)
        {
            SwitchScreen(w, Screen.Wear);
        }
        else
        {
            SwitchScreen(w, Screen.Inventory);
        }
    }

    // src/ui_inventory.cpp:535 flipInventoryEquipmentScreens()
    private static void FlipInventoryEquipmentScreens(PlayerWorld w)
    {
        if (w.Game.Screen.CurrentScreenId == Screen.Equipment)
        {
            SwitchScreen(w, Screen.Inventory);
        }
        else if (w.Game.Screen.CurrentScreenId == Screen.Inventory)
        {
            SwitchScreen(w, Screen.Equipment);
        }
    }

    // src/ui_inventory.cpp:543 requestPutRingOnWhichHand() -- rings. Give
    // choice over where they go.
    private static int RequestPutRingOnWhichHand(PlayerWorld w)
    {
        var hand = 0;

        do
        {
            if (!PlayerCore.GetCommand(w, "Put ring on which hand (l/r/L/R)?", out var query))
            {
                hand = -1;
            }
            else if (query == 'l')
            {
                hand = PlayerConfig.Equipment.Left;
            }
            else if (query == 'r')
            {
                hand = PlayerConfig.Equipment.Right;
            }
            else
            {
                if (query == 'L')
                {
                    hand = PlayerConfig.Equipment.Left;
                }
                else if (query == 'R')
                {
                    hand = PlayerConfig.Equipment.Right;
                }

                // (else: terminalBellSound())

                if (hand != 0 && !VerifyAction(w, "Replace", hand))
                {
                    hand = 0;
                }
            }
        } while (hand == 0);

        return hand;
    }

    // src/ui_inventory.cpp:572 inventoryGetSlotToWearEquipment()
    private static int SlotToWearEquipment(PlayerWorld w, int categoryId)
    {
        switch (categoryId)
        {
            case TreasureCategory.TvSlingAmmo:
            case TreasureCategory.TvBolt:
            case TreasureCategory.TvArrow:
            case TreasureCategory.TvBow:
            case TreasureCategory.TvHafted:
            case TreasureCategory.TvPolearm:
            case TreasureCategory.TvSword:
            case TreasureCategory.TvDigging:
            case TreasureCategory.TvSpike:
                return PlayerConfig.Equipment.Wield;
            case TreasureCategory.TvLight:
                return PlayerConfig.Equipment.Light;
            case TreasureCategory.TvBoots:
                return PlayerConfig.Equipment.Feet;
            case TreasureCategory.TvGloves:
                return PlayerConfig.Equipment.Hands;
            case TreasureCategory.TvCloak:
                return PlayerConfig.Equipment.Outer;
            case TreasureCategory.TvHelm:
                return PlayerConfig.Equipment.Head;
            case TreasureCategory.TvShield:
                return PlayerConfig.Equipment.Arm;
            case TreasureCategory.TvHardArmor:
            case TreasureCategory.TvSoftArmor:
                return PlayerConfig.Equipment.Body;
            case TreasureCategory.TvAmulet:
                return PlayerConfig.Equipment.Neck;
            case TreasureCategory.TvRing:
                if (InventoryActions.RightHandRingEmpty(w.Py))
                {
                    return PlayerConfig.Equipment.Right;
                }
                if (InventoryActions.LeftHandRingEmpty(w.Py))
                {
                    return PlayerConfig.Equipment.Left;
                }
                return RequestPutRingOnWhichHand(w);
            default:
                PlayerCore.PrintMessage(w, "IMPOSSIBLE: I don't see how you can use that.");
                return -1;
        }
    }

    // src/ui_inventory.cpp:630 inventoryItemIsCursedMessage()
    private static void ItemIsCursedMessage(PlayerWorld w, int itemId)
    {
        var description = ItemDescription.Describe(w.Identification, w.Py.Inventory[itemId], false);

        var msg = Helpers.Truncate($"The {description} you are ", Helpers.MoriaObjDescSize);

        // (sic: Head, not Wield)
        msg += itemId == PlayerConfig.Equipment.Head ? "wielding " : "wearing ";

        PlayerCore.PrintMessage(w, msg + "appears to be cursed.");
    }

    // src/ui_inventory.cpp:647 executeRemoveItemCommand() -- take off ('t')
    // or throw off ('r', take off and drop) the n-th worn item
    private static bool ExecuteRemoveItemCommand(PlayerWorld w, bool selecting, int itemId, ref char command, char which, string prompt)
    {
        var py = w.Py;

        // Get its place in the equipment list.
        var itemIdToTakeOff = itemId;
        itemId = 21;

        do
        {
            itemId++;
            if (py.Inventory[itemId].CategoryId != TreasureCategory.TvNothing)
            {
                itemIdToTakeOff--;
            }
        } while (itemIdToTakeOff >= 0);

        if (char.IsAsciiLetterUpper(which) && !VerifyAction(w, prompt, itemId))
        {
            itemId = -1;
        }
        else if (PlayerInventory.ItemIsCursed(py.Inventory[itemId]))
        {
            itemId = -1;
            PlayerCore.PrintMessage(w, "Hmmm, it seems to be cursed.");
        }
        else if (command == 't' && !PlayerInventory.CanCarryItemCount(py, w.Identification, py.Inventory[itemId]))
        {
            if (w.Dg.Floor[py.Pos.Y, py.Pos.X].TreasureId != 0)
            {
                itemId = -1;
                PlayerCore.PrintMessage(w, "You can't carry it.");
            }
            else if (PlayerCore.GetInputConfirmation(w, "You can't carry it.  Drop it?"))
            {
                command = 'r';
            }
            else
            {
                itemId = -1;
            }
        }

        if (itemId >= 0)
        {
            if (command == 'r')
            {
                InventoryActions.DropItem(w, itemId, true);
                // As a safety measure, set the player's inven
                // weight to 0, when the last object is dropped.
                if (py.Pack.UniqueItems == 0 && py.EquipmentCount == 0)
                {
                    py.Pack.Weight = 0;
                }
            }
            else
            {
                var slot = PlayerInventory.CarryItem(py, w.Identification, py.Inventory[itemId]);
                InventoryActions.TakeOff(w, itemId, slot);
            }

            PlayerCore.Strength(w);
            w.Game.PlayerFreeTurn = false;

            if (command == 'r')
            {
                selecting = false;
            }
        }

        return selecting;
    }

    // src/ui_inventory.cpp:698 executeWearItemCommand() -- wearing. Go to a
    // bit of trouble over replacing existing equipment.
    private static void ExecuteWearItemCommand(PlayerWorld w, int itemId, char which, string prompt)
    {
        var py = w.Py;
        var screen = w.Game.Screen;
        var slot = 0;

        if (char.IsAsciiLetterUpper(which) && !VerifyAction(w, prompt, itemId))
        {
            itemId = -1;
        }
        else
        {
            slot = SlotToWearEquipment(w, py.Inventory[itemId].CategoryId);
            if (slot == -1)
            {
                itemId = -1;
            }
        }

        if (itemId >= 0 && py.Inventory[slot].CategoryId != TreasureCategory.TvNothing)
        {
            if (PlayerInventory.ItemIsCursed(py.Inventory[slot]))
            {
                ItemIsCursedMessage(w, slot);
                itemId = -1;
            }
            else if (py.Inventory[itemId].SubCategoryId == InventoryConfig.ItemGroupMin && py.Inventory[itemId].ItemsCount > 1 &&
                     !PlayerInventory.CanCarryItemCount(py, w.Identification, py.Inventory[slot]))
            {
                // this can happen if try to wield a torch,
                // and have more than one in inventory
                PlayerCore.PrintMessage(w, "You will have to drop something first.");
                itemId = -1;
            }
        }

        if (itemId == -1)
        {
            return;
        }

        // OK. Wear it.
        w.Game.PlayerFreeTurn = false;

        //
        // 1. remove new item from inventory
        //
        var savedItem = py.Inventory[itemId];

        screen.WearHighId--;

        // Fix for torches
        if (savedItem.ItemsCount > 1 && savedItem.SubCategoryId <= InventoryConfig.ItemSingleStackMax)
        {
            savedItem.ItemsCount = 1;
            screen.WearHighId++;
        }

        py.Pack.Weight = (short)(py.Pack.Weight + savedItem.Weight * savedItem.ItemsCount);
        InventoryDamage.DestroyItem(py, itemId); // Subtracts weight

        //
        // 2. add old item to inv and remove from equipment list, if necessary.
        //
        if (py.Inventory[slot].CategoryId != TreasureCategory.TvNothing)
        {
            int uniqItems = py.Pack.UniqueItems;

            var id = PlayerInventory.CarryItem(py, w.Identification, py.Inventory[slot]);

            // If item removed did not stack with anything
            // in inventory, then increment wear_high.
            if (py.Pack.UniqueItems != uniqItems)
            {
                screen.WearHighId++;
            }

            InventoryActions.TakeOff(w, slot, id);
        }

        //
        // 3. wear new item
        //
        py.Inventory[slot] = savedItem;
        py.EquipmentCount++;

        PlayerCombat.AdjustBonusesForItem(w, py.Inventory[slot], 1);

        string text;
        if (slot == PlayerConfig.Equipment.Wield)
        {
            text = "You are wielding";
        }
        else if (slot == PlayerConfig.Equipment.Light)
        {
            text = "Your light source is";
        }
        else
        {
            text = "You are wearing";
        }

        var description = ItemDescription.Describe(w.Identification, py.Inventory[slot], true);

        // Get the right equipment letter.
        var itemIdToTakeOff = PlayerConfig.Equipment.Wield;
        itemId = 0;

        while (itemIdToTakeOff != slot)
        {
            if (py.Inventory[itemIdToTakeOff++].CategoryId != TreasureCategory.TvNothing)
            {
                itemId++;
            }
        }

        PlayerCore.PrintMessage(w, Helpers.Truncate($"{text} {description} ({(char)('a' + itemId)})", Helpers.MoriaObjDescSize));

        // this is a new weapon, so clear heavy flag
        if (slot == PlayerConfig.Equipment.Wield)
        {
            py.WeaponIsHeavy = false;
        }
        PlayerCore.Strength(w);

        if (PlayerInventory.ItemIsCursed(py.Inventory[slot]))
        {
            PlayerCore.PrintMessage(w, "Oops! It feels deathly cold!");
            ObjectIdentification.ItemAppendToInscription(ref py.Inventory[slot], IdentificationConfig.IdDamd);

            // To force a cost of 0, even if unidentified.
            py.Inventory[slot].Cost = -1;
        }
    }

    // src/ui_inventory.cpp:813 executeDropItemCommand()
    private static void ExecuteDropItemCommand(PlayerWorld w, int itemId, char which, string prompt)
    {
        var py = w.Py;
        var confirmed = -1;

        if (py.Inventory[itemId].ItemsCount > 1)
        {
            var description = ItemDescription.Describe(w.Identification, py.Inventory[itemId], true);
            description = description[..^1] + "?"; // replace period with question

            // request command from player (reading the key zeroes
            // game.command_count, as getKeyInput() does)
            w.Game.CommandCount = 0;
            confirmed = w.ConfirmWithAbort(Helpers.Truncate($"Drop all {description}", Helpers.MoriaObjDescSize));
            // if aborted
            if (confirmed == -1)
            {
                itemId = -1;
            }
        }
        else if (char.IsAsciiLetterUpper(which) && !VerifyAction(w, prompt, itemId))
        {
            itemId = -1;
        }

        if (itemId >= 0)
        {
            w.Game.PlayerFreeTurn = false;

            InventoryActions.DropItem(w, itemId, confirmed == 1);
            PlayerCore.Strength(w);
        }

        // As a safety measure, set the player's inven weight
        // to 0, when the last object is dropped.
        if (py.Pack.UniqueItems == 0 && py.EquipmentCount == 0)
        {
            py.Pack.Weight = 0;
        }
    }

    // src/ui_inventory.cpp:848 selectItemCommands() -- keep looking for
    // objects to drop/wear/take off/throw off
    private static bool SelectItemCommands(PlayerWorld w, ref char command, ref char which, bool selecting)
    {
        var py = w.Py;
        var game = w.Game;

        while (selecting && game.PlayerFreeTurn)
        {
            var swap = "";
            int fromLine;
            int toLine;
            string prompt;

            if (command == 'w')
            {
                fromLine = game.Screen.WearLowId;
                toLine = game.Screen.WearHighId;
                prompt = "Wear/Wield";
            }
            else
            {
                fromLine = 0;
                if (command == 'd')
                {
                    toLine = py.Pack.UniqueItems - 1;
                    prompt = "Drop";

                    if (py.EquipmentCount > 0)
                    {
                        swap = ", / for Equip";
                    }
                }
                else
                {
                    toLine = py.EquipmentCount - 1;

                    if (command == 't')
                    {
                        prompt = "Take off";
                    }
                    else
                    {
                        // command == 'r'

                        prompt = "Throw off";
                        if (py.Pack.UniqueItems > 0)
                        {
                            swap = ", / for Inven";
                        }
                    }
                }
            }

            if (fromLine > toLine)
            {
                selecting = false;
                continue;
            }

            var headingMsg = BuildCommandHeading(game, fromLine, toLine, swap, command, prompt);

            // Abort everything.
            if (!PlayerCore.GetCommand(w, headingMsg, out which))
            {
                which = Escape;
                selecting = false;
                continue;
            }

            // Draw the screen and maybe exit toLine main prompt.
            if (which == ' ' || which == '*')
            {
                ChangeScreenForCommand(w, command);
                if (which == ' ')
                {
                    selecting = false;
                }
                continue;
            }

            // Swap screens (for drop)
            if (which == '/' && swap.Length != 0)
            {
                command = command == 'd' ? 'r' : 'd';
                FlipInventoryEquipmentScreens(w);
                continue;
            }

            // look for item whose inscription matches "which"
            var itemId = ItemMatchingInscription(py, which, command, fromLine, toLine);
            if (itemId < fromLine || itemId > toLine)
            {
                // (terminalBellSound())
                continue;
            }

            //
            // Found an item - do something with it!
            //
            if (command == 'r' || command == 't')
            {
                // Get its place in the equipment list.
                selecting = ExecuteRemoveItemCommand(w, selecting, itemId, ref command, which, prompt);
            }
            else if (command == 'w')
            {
                ExecuteWearItemCommand(w, itemId, which, prompt);
            }
            else
            {
                // command == 'd'
                ExecuteDropItemCommand(w, itemId, which, prompt);
                selecting = false;
            }

            if (!game.PlayerFreeTurn && game.Screen.CurrentScreenId == Screen.Blank)
            {
                selecting = false;
            }
        }

        return selecting;
    }

    // src/ui_inventory.cpp:508 buildCommandHeading() -- the prompt text
    private static string BuildCommandHeading(GameState game, int from, int to, string swap, char command, string prompt)
    {
        var list = game.Screen.CurrentScreenId == Screen.Blank ? ", * to list" : "";
        var digits = command == 'w' || command == 'd' ? ", 0-9" : "";

        return Helpers.Truncate(
            $"({(char)(from + 'a')}-{(char)(to + 'a')}{list}{swap}{digits}, space to break, ESC to exit) {prompt} which one?",
            Helpers.MoriaObjDescSize);
    }

    // src/ui_inventory.cpp:988 uiCommandDisplayInventory()
    private static void DisplayInventory(PlayerWorld w)
    {
        if (w.Py.Pack.UniqueItems == 0)
        {
            PlayerCore.PrintMessage(w, "You are not carrying anything.");
        }
        else
        {
            SwitchScreen(w, Screen.Inventory);
        }
    }

    // src/ui_inventory.cpp:996 uiCommandDisplayEquipment()
    private static void DisplayEquipment(PlayerWorld w)
    {
        if (w.Py.EquipmentCount == 0)
        {
            PlayerCore.PrintMessage(w, "You are not using any equipment.");
        }
        else
        {
            SwitchScreen(w, Screen.Equipment);
        }
    }

    // src/ui_inventory.cpp:1005 inventoryExecuteCommand() -- this does all
    // the work.
    public static void ExecuteCommand(PlayerWorld w, char command)
    {
        var game = w.Game;

        game.PlayerFreeTurn = true;

        // (terminalSaveScreen() -- display)

        RequestAndShowInventoryScreen(w, command == ' ');

        do
        {
            if (char.IsAsciiLetterUpper(command))
            {
                command = char.ToLowerInvariant(command);
            }

            // Simple command getting and screen selection.
            var selecting = false;
            switch (command)
            {
                case 'i':
                    DisplayInventory(w);
                    break;
                case 'e':
                    DisplayEquipment(w);
                    break;
                case 't':
                    selecting = TakeOffItem(w, selecting);
                    break;
                case 'd':
                    selecting = DropItem(w, ref command, selecting);
                    break;
                case 'w':
                    selecting = WearWieldItem(w, selecting);
                    break;
                case 'x':
                    UnwieldItem(w);
                    break;
                case '?':
                    SwitchScreen(w, Screen.Help);
                    break;
                case ' ':
                    // Dummy command to return again to main prompt.
                    break;
                default:
                    // Nonsense command
                    // (terminalBellSound())
                    break;
            }

            // Clear the game.doing_inventory_command flag here, instead of at beginning, so that
            // can use it to control when messages above appear.
            game.DoingInventoryCommand = (char)0;

            // Keep looking for objects to drop/wear/take off/throw off
            var which = 'z';

            selecting = SelectItemCommands(w, ref command, ref which, selecting);

            if (which == Escape || game.Screen.CurrentScreenId == Screen.Blank)
            {
                command = Escape;
            }
            else if (!game.PlayerFreeTurn)
            {
                // Save state for recovery if they want to call us again next turn.
                // Otherwise, set a dummy command to recover screen.
                game.DoingInventoryCommand = selecting ? command : ' ';

                // (printMessage(CNIL) flushes the last message -- display;
                // a null message leaves command_count alone)

                // This lets us know if the world changes
                game.ScreenHasChanged = false;

                command = Escape;
            }
            else
            {
                // (inventoryDisplayAppropriateHeader() and the command
                // line -- display)
                command = GetKeyInput(w);
            }
        } while (command != Escape);

        // (terminalRestoreScreen() -- display)

        PlayerBonuses.RecalculateBonuses(w.Py);
    }

    // src/identification.cpp:894 itemInscribe() -- add a comment to an
    // object description. -CJS-
    public static void ItemInscribe(PlayerWorld w)
    {
        var py = w.Py;

        if (py.Pack.UniqueItems == 0 && py.EquipmentCount == 0)
        {
            PlayerCore.PrintMessage(w, "You are not carrying anything to inscribe.");
            return;
        }

        if (!PlayerCore.GetItemId(w, "Which one? ", 0, PlayerConfig.PlayerInventorySize, out var itemId))
        {
            return;
        }

        var msg = ItemDescription.Describe(w.Identification, py.Inventory[itemId], true);

        PlayerCore.PrintMessage(w, Helpers.Truncate($"Inscribing {msg}", Helpers.MoriaObjDescSize));

        var inscription = !string.IsNullOrEmpty(py.Inventory[itemId].Inscription)
            ? Helpers.Truncate($"Replace {py.Inventory[itemId].Inscription} New inscription:", Helpers.MoriaObjDescSize)
            : "Inscription: ";

        var msgLen = 78 - msg.Length;
        if (msgLen > 12)
        {
            msgLen = 12;
        }

        // (getStringInput() reads through getKeyInput(), which zeroes
        // game.command_count)
        w.Game.CommandCount = 0;
        var text = w.ReadString(inscription, msgLen);
        if (text is not null)
        {
            ObjectIdentification.ItemReplaceInscription(ref py.Inventory[itemId], text);
        }
    }
}
