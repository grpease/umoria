using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;

namespace Umoria.Core.Ui;

// src/ui_inventory.cpp's display half: the inventory and equipment lists,
// the help screen and header lines of the inventory command screen (whose
// game logic is Items.InventoryCommands, M11), and the item prompt
// inventoryGetInputForItemId() with its '*' list and '/' pack/equipment
// switch. (M14)
public sealed partial class GameUi
{
    // src/ui_inventory.cpp:8 inventoryItemWeightText()
    private string InventoryItemWeightText(int itemId)
    {
        var item = W.Py.Inventory[itemId];
        var totalWeight = item.Weight * item.ItemsCount;
        var quotient = totalWeight / 10;
        var remainder = totalWeight % 10;

        return $"{quotient,3}.{remainder} lb";
    }

    // src/ui_inventory.cpp:22 displayInventoryItems() -- displays inventory
    // items from `itemIdStart` to `itemIdEnd` -RAK- Designed to keep the
    // display as far to the right as possible. -CJS- The parameter column
    // gives a column at which to start, but if the display does not fit, it
    // may be moved left. The return value is the left edge used. If mask is
    // non-null, then only display those items which have a true entry in the
    // mask array.
    public int DisplayInventoryItems(int itemIdStart, int itemIdEnd, bool weighted, int column, bool[]? mask)
    {
        var descriptions = new string[PlayerConfig.Equipment.Wield + 1];

        var len = 79 - column;
        var lim = weighted ? 68 : 76;

        // Generate the descriptions text
        for (var i = itemIdStart; i <= itemIdEnd; i++)
        {
            if (mask is not null && !mask[i])
            {
                continue;
            }

            var description = ItemDescription.Describe(W.Identification, W.Py.Inventory[i], true);

            // Truncate if too long.
            if (description.Length > lim)
            {
                description = description[..lim];
            }

            descriptions[i] = Helpers.Truncate($"{(char)('a' + i)}) {description}", Helpers.MoriaMessageSize);

            var l = descriptions[i].Length + 2;

            if (weighted)
            {
                l += 9;
            }

            if (l > len)
            {
                len = l;
            }
        }

        column = 79 - len;
        if (column < 0)
        {
            column = 0;
        }

        var currentLine = 1;

        // Print the descriptions
        for (var i = itemIdStart; i <= itemIdEnd; i++)
        {
            if (mask is not null && !mask[i])
            {
                continue;
            }

            // don't need first two spaces if in first column
            if (column == 0)
            {
                PutStringClearToEol(descriptions[i], currentLine, column);
            }
            else
            {
                PutString("  ", currentLine, column);
                PutStringClearToEol(descriptions[i], currentLine, column + 2);
            }

            if (weighted)
            {
                PutStringClearToEol(InventoryItemWeightText(i), currentLine, 71);
            }

            currentLine++;
        }

        return column;
    }

    // src/ui_inventory.cpp:123 equipmentPositionDescription()
    private string EquipmentPositionDescription(int id, int weight)
    {
        return id switch
        {
            PlayerConfig.Equipment.Wield => W.Py.Stats.Used[PlayerConfig.Attr.Str] * 15 < weight ? "Just lifting" : "Wielding",
            PlayerConfig.Equipment.Head => "On head",
            PlayerConfig.Equipment.Neck => "Around neck",
            PlayerConfig.Equipment.Body => "On body",
            PlayerConfig.Equipment.Arm => "On arm",
            PlayerConfig.Equipment.Hands => "On hands",
            PlayerConfig.Equipment.Right => "On right hand",
            PlayerConfig.Equipment.Left => "On left hand",
            PlayerConfig.Equipment.Feet => "On feet",
            PlayerConfig.Equipment.Outer => "About body",
            PlayerConfig.Equipment.Light => "Light source",
            PlayerConfig.Equipment.Auxiliary => "Spare weapon",
            _ => "Unknown equipment position ID",
        };
    }

    // src/ui_inventory.cpp:157 displayEquipment() -- displays equipment
    // items from r1 to end -RAK- Keep display as far right as possible. -CJS-
    public int DisplayEquipment(bool showWeights, int column)
    {
        var py = W.Py;
        var descriptions = new string[PlayerConfig.PlayerInventorySize - PlayerConfig.Equipment.Wield];

        var len = 79 - column;
        var lim = showWeights ? 52 : 60;

        // Range of equipment
        var line = 0;
        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.PlayerInventorySize; i++)
        {
            if (py.Inventory[i].CategoryId == TreasureCategory.TvNothing)
            {
                continue;
            }

            // Get position
            var equippedDescription = EquipmentPositionDescription(i, py.Inventory[i].Weight);

            var description = ItemDescription.Describe(W.Identification, py.Inventory[i], true);

            // Truncate if necessary
            if (description.Length > lim)
            {
                description = description[..lim];
            }

            descriptions[line] = Helpers.Truncate($"{(char)(line + 'a')}) {equippedDescription,-14}: {description}", Helpers.MoriaMessageSize);

            var l = descriptions[line].Length + 2;

            if (showWeights)
            {
                l += 9;
            }

            if (l > len)
            {
                len = l;
            }

            line++;
        }

        column = 79 - len;
        if (column < 0)
        {
            column = 0;
        }

        // Range of equipment
        line = 0;
        for (var i = PlayerConfig.Equipment.Wield; i < PlayerConfig.PlayerInventorySize; i++)
        {
            if (py.Inventory[i].CategoryId == TreasureCategory.TvNothing)
            {
                continue;
            }

            // don't need first two spaces when using whole screen
            if (column == 0)
            {
                PutStringClearToEol(descriptions[line], line + 1, column);
            }
            else
            {
                PutString("  ", line + 1, column);
                PutStringClearToEol(descriptions[line], line + 1, column + 2);
            }

            if (showWeights)
            {
                PutStringClearToEol(InventoryItemWeightText(i), line + 1, 71);
            }

            line++;
        }
        EraseLine(line + 1, column);

        return column;
    }

    // src/ui_inventory.cpp:244 showEquipmentHelpMenu()
    private int ShowEquipmentHelpMenu(int leftColumn)
    {
        if (leftColumn > 52)
        {
            leftColumn = 52;
        }

        PutStringClearToEol("  ESC: exit", 1, leftColumn);
        PutStringClearToEol("  w  : wear or wield object", 2, leftColumn);
        PutStringClearToEol("  t  : take off item", 3, leftColumn);
        PutStringClearToEol("  d  : drop object", 4, leftColumn);
        PutStringClearToEol("  x  : exchange weapons", 5, leftColumn);
        PutStringClearToEol("  i  : inventory of pack", 6, leftColumn);
        PutStringClearToEol("  e  : list used equipment", 7, leftColumn);

        return 7; // current line position
    }

    // src/ui_inventory.cpp:281 uiCommandSwitchScreen() -- the drawing, once
    // the screen id has been switched (InventoryCommands.SwitchScreen)
    public void DrawInventoryScreen(Screen nextScreen)
    {
        var py = W.Py;
        var screen = W.Game.Screen;
        var weights = W.Game.Options.ShowInventoryWeights;

        var currentLinePos = 0;
        switch (nextScreen)
        {
            case Screen.Blank:
                break;
            case Screen.Help:
                currentLinePos = ShowEquipmentHelpMenu(screen.ScreenLeftPos);
                break;
            case Screen.Inventory:
                screen.ScreenLeftPos = DisplayInventoryItems(0, py.Pack.UniqueItems - 1, weights, screen.ScreenLeftPos, null);
                currentLinePos = py.Pack.UniqueItems;
                break;
            case Screen.Wear:
                screen.ScreenLeftPos = DisplayInventoryItems(screen.WearLowId, screen.WearHighId, weights, screen.ScreenLeftPos, null);
                currentLinePos = screen.WearHighId - screen.WearLowId + 1;
                break;
            case Screen.Equipment:
                screen.ScreenLeftPos = DisplayEquipment(weights, screen.ScreenLeftPos);
                currentLinePos = py.EquipmentCount;
                break;
        }

        if (currentLinePos >= screen.ScreenBottomPos)
        {
            screen.ScreenBottomPos = currentLinePos + 1;
            EraseLine(screen.ScreenBottomPos, screen.ScreenLeftPos);
            return;
        }
        currentLinePos++;

        while (currentLinePos <= screen.ScreenBottomPos)
        {
            EraseLine(currentLinePos, screen.ScreenLeftPos);
            currentLinePos++;
        }
    }

    // src/ui_inventory.cpp:339 requestAndShowInventoryScreen()'s
    // `screen_left_pos = 50; screen_bottom_pos = 0;`
    public void ResetInventoryScreenPositions()
    {
        W.Game.Screen.ScreenLeftPos = 50;
        W.Game.Screen.ScreenBottomPos = 0;
    }

    // src/ui_inventory.cpp:950 inventoryDisplayAppropriateHeader() -- put an
    // appropriate header message (on message line).
    private void InventoryDisplayAppropriateHeader()
    {
        var py = W.Py;
        var screen = W.Game.Screen;

        if (screen.CurrentScreenId == Screen.Inventory)
        {
            var weightQuotient = py.Pack.Weight / 10;
            var weightRemainder = py.Pack.Weight % 10;

            string msg;
            if (!W.Game.Options.ShowInventoryWeights || py.Pack.UniqueItems == 0)
            {
                msg = $"You are carrying {weightQuotient}.{weightRemainder} pounds. In your pack there is {(py.Pack.UniqueItems == 0 ? "nothing." : "-")}";
            }
            else
            {
                var limit = Character.PlayerCore.CarryingLoadLimit(W.Py);
                var capacityQuotient = limit / 10;
                var capacityRemainder = limit % 10;

                msg = $"You are carrying {weightQuotient}.{weightRemainder} pounds. Your capacity is {capacityQuotient}.{capacityRemainder} pounds. In your pack is -";
            }

            PutStringClearToEol(Helpers.Truncate(msg, Helpers.MoriaObjDescSize), 0, 0);
        }
        else if (screen.CurrentScreenId == Screen.Wear)
        {
            PutStringClearToEol(screen.WearHighId < screen.WearLowId ? "You have nothing you could wield." : "You could wield -", 0, 0);
        }
        else if (screen.CurrentScreenId == Screen.Equipment)
        {
            PutStringClearToEol(py.EquipmentCount == 0 ? "You are not using anything." : "You are using -", 0, 0);
        }
        else
        {
            PutStringClearToEol("Allowed commands:", 0, 0);
        }

        EraseLine(screen.ScreenBottomPos, screen.ScreenLeftPos);
    }

    // inventoryExecuteCommand()'s command prompt: the header line and the
    // "e/i/t/w/x/d/?/ESC:" line, then the key, then the line erased.
    public char InventoryCommandPrompt()
    {
        var screen = W.Game.Screen;

        InventoryDisplayAppropriateHeader();

        PutString("e/i/t/w/x/d/?/ESC:", screen.ScreenBottomPos, 60);
        var command = GetKeyInput();

        EraseLine(screen.ScreenBottomPos, screen.ScreenLeftPos);

        return command;
    }

    private enum PackMenu
    {
        CloseMenu,
        Equipment,
        Inventory,
    }

    // src/ui_inventory.cpp:1106 inventorySwitchPackMenu() -- switch between
    // Equipment/Inventory menu. Returns true when menu has changed
    private bool InventorySwitchPackMenu(string prompt, ref PackMenu menu, bool menuActive, ref int itemIdEnd)
    {
        var py = W.Py;

        if (menu == PackMenu.Inventory)
        {
            var changed = false;

            if (py.EquipmentCount == 0)
            {
                PutStringClearToEol("But you're not using anything -more-", 0, 0);
                GetKeyInput();
            }
            else
            {
                menu = PackMenu.Equipment;
                changed = true;

                if (menuActive)
                {
                    itemIdEnd = py.EquipmentCount;

                    while (itemIdEnd < py.Pack.UniqueItems)
                    {
                        itemIdEnd++;
                        EraseLine(itemIdEnd, 0);
                    }
                }
                itemIdEnd = py.EquipmentCount - 1;
            }

            PutStringClearToEol(prompt, 0, 0);
            return changed;
        }

        if (py.Pack.UniqueItems == 0)
        {
            PutStringClearToEol("But you're not carrying anything -more-", 0, 0);
            GetKeyInput();
            return false;
        }

        menu = PackMenu.Inventory;
        if (menuActive)
        {
            itemIdEnd = py.Pack.UniqueItems;

            while (itemIdEnd < py.EquipmentCount)
            {
                itemIdEnd++;
                EraseLine(itemIdEnd, 0);
            }
        }
        itemIdEnd = py.Pack.UniqueItems - 1;
        return true;
    }

    // src/ui_inventory.cpp:1152 inventoryGetInputForItemId() -- get the ID
    // of an item and return the CTR value of it -RAK- (Character.PlayerCore
    // .GetItemId calls this when there is a UI.)
    public bool InventoryGetInputForItemId(out int commandKeyId, string prompt, int itemIdStart, int itemIdEnd, bool[]? mask, string? message)
    {
        var py = W.Py;
        var menu = PackMenu.Inventory;
        var packFull = false;

        commandKeyId = 0;

        if (itemIdEnd > PlayerConfig.Equipment.Wield)
        {
            packFull = true;

            if (py.Pack.UniqueItems == 0)
            {
                menu = PackMenu.Equipment;
                itemIdEnd = py.EquipmentCount - 1;
            }
            else
            {
                itemIdEnd = py.Pack.UniqueItems - 1;
            }
        }

        if (py.Pack.UniqueItems < 1 && (!packFull || py.EquipmentCount < 1))
        {
            PutStringClearToEol("You are not carrying anything.", 0, 0);
            return false;
        }

        var itemFound = false;
        var menuActive = false;

        do
        {
            if (menuActive)
            {
                if (menu == PackMenu.Inventory)
                {
                    DisplayInventoryItems(itemIdStart, itemIdEnd, false, 80, mask);
                }
                else
                {
                    DisplayEquipment(false, 80);
                }
            }

            var inven = menu == PackMenu.Inventory;
            var description = packFull
                ? $"({(inven ? "Inven" : "Equip")}: {(char)(itemIdStart + 'a')}-{(char)(itemIdEnd + 'a')},{(inven ? " 0-9," : "")}{(menuActive ? "" : " * to see,")} / for {(inven ? "Equip" : "Inven")}, or ESC) {prompt}"
                : $"(Items {(char)(itemIdStart + 'a')}-{(char)(itemIdEnd + 'a')},{(inven ? " 0-9," : "")}{(menuActive ? "" : " * for inventory list,")} ESC to exit) {prompt}";
            description = Helpers.Truncate(description, Helpers.MoriaMessageSize);

            PutStringClearToEol(description, 0, 0);

            var done = false;
            while (!done)
            {
                var which = GetKeyInput();

                switch (which)
                {
                    case Escape:
                        menu = PackMenu.CloseMenu;
                        done = true;
                        W.Game.PlayerFreeTurn = true;
                        break;
                    case '/':
                        done = InventorySwitchPackMenu(description, ref menu, menuActive, ref itemIdEnd);
                        break;
                    case '*': // activate menu if required
                        if (!menuActive)
                        {
                            done = true;
                            TerminalSaveScreen();
                            menuActive = true;
                        }
                        break;
                    default:
                        // look for item whose inscription matches "which"
                        if (which >= '0' && which <= '9' && menu != PackMenu.Equipment)
                        {
                            int m;

                            // Note: loop to find the inventory item
                            for (m = itemIdStart; m < PlayerConfig.Equipment.Wield && py.Inventory[m].Inscription != which.ToString(); m++)
                            {
                            }

                            commandKeyId = m < PlayerConfig.Equipment.Wield ? m : -1;
                        }
                        else if (char.IsAsciiLetterUpper(which))
                        {
                            commandKeyId = which - 'A';
                        }
                        else
                        {
                            commandKeyId = which - 'a';
                        }

                        if (commandKeyId >= itemIdStart && commandKeyId <= itemIdEnd && (mask is null || mask[commandKeyId]))
                        {
                            if (menu == PackMenu.Equipment)
                            {
                                itemIdStart = 21;
                                itemIdEnd = commandKeyId;

                                do
                                {
                                    // Note: a simple loop to find first inventory item
                                    itemIdStart++;
                                    while (py.Inventory[itemIdStart].CategoryId == TreasureCategory.TvNothing)
                                    {
                                        itemIdStart++;
                                    }

                                    itemIdEnd--;
                                } while (itemIdEnd >= 0);

                                commandKeyId = itemIdStart;
                            }

                            if (char.IsAsciiLetterUpper(which) && !VerifyAction("Try", commandKeyId))
                            {
                                menu = PackMenu.CloseMenu;
                                done = true;

                                W.Game.PlayerFreeTurn = true;

                                break;
                            }

                            menu = PackMenu.CloseMenu;
                            done = true;

                            itemFound = true;
                        }
                        else if (message is not null)
                        {
                            Character.PlayerCore.PrintMessage(W, message);

                            // Set `done` to force redraw of the question.
                            done = true;
                        }
                        else
                        {
                            TerminalBellSound();
                        }
                        break;
                }
            }
        } while (menu != PackMenu.CloseMenu);

        if (menuActive)
        {
            TerminalRestoreScreen();
        }

        MessageLineClear();

        return itemFound;
    }

    // src/ui_inventory.cpp:326 verifyAction() -- used to verify if this
    // really is the item we wish to wear or read. -CJS-
    private bool VerifyAction(string prompt, int item)
    {
        var description = ItemDescription.Describe(W.Identification, W.Py.Inventory[item], true);

        // change the period to a question mark
        description = description[..^1] + "?";

        return GetInputConfirmation(Helpers.Truncate($"{prompt} {description}", Helpers.MoriaObjDescSize));
    }
}
