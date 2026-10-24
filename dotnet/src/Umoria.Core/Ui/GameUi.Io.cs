using Umoria.Core.Character;

namespace Umoria.Core.Ui;

// src/ui_io.cpp -- terminal I/O code, over the virtual curses screen
// (Terminal). Ported line for line except where the original talks to the
// OS: terminalInitialize()/terminalRestore() are the frontend's (it owns the
// real console) and the bell is an event. (M14; M15 added the non-blocking
// read behind flushInputBuffer() and checkForNonBlockingKeyPress().)
public sealed partial class GameUi
{
    public const char Escape = (char)27;
    private const char Delete = (char)0x7f;

    // src/ui.h MSG_LINE
    public const int MsgLine = 0;

    private static char CtrlKey(char key) => (char)(key & 0x1f);

    // src/ui.cpp message_ready_to_print -- set with first message
    public bool MessageReadyToPrint { get; private set; }

    // `message_ready_to_print = false;` -- the store's command loop drops a
    // waiting message without the -more- prompt
    public void ClearMessageReady() => MessageReadyToPrint = false;

    // endGame() and saveGame(), which getKeyInput() calls itself on end of
    // input. They belong to the main loop (Game.GameRun binds them); endGame()
    // never returns -- it ends the program (GameExitException). Unbound,
    // end of input only sets eof_flag and disturbs, as before M15.
    public Action? EndGame { get; set; }
    public Func<bool>? SaveGame { get; set; }

    // terminalSaveScreen()
    public void TerminalSaveScreen()
    {
        Term.SaveScreen();
    }

    // terminalRestoreScreen()
    public void TerminalRestoreScreen()
    {
        Term.RestoreScreen();
    }

    // terminalBellSound()
    public void TerminalBellSound()
    {
        PutQio();

        // The player can turn off beeps if they find them annoying.
        if (W.Game.Options.ErrorBeepSound)
        {
            Term.RingBell();
        }
    }

    // putQIO() -- dump the IO buffer to terminal -RAK-
    public void PutQio()
    {
        // Let inventoryExecuteCommand() know something has changed.
        W.Game.ScreenHasChanged = true;

        Term.Refresh();
    }

    // flushInputBuffer() -- drains the keys typed ahead (M15; a scripted
    // key source never has any, see IKeySource.TryReadKey).
    public void FlushInputBuffer()
    {
        if (W.Game.EofFlag != 0)
        {
            return;
        }

        while (CheckForNonBlockingKeyPress(0))
        {
        }
    }

    // checkForNonBlockingKeyPress() -- does a non-blocking read, consuming
    // the data if any, and then returns true if data was read. The _WIN32
    // branch of the original (timeout(8); getch(); timeout(-1)); the delay
    // is the key source's business. (M15)
    public bool CheckForNonBlockingKeyPress(int microseconds)
    {
        _ = microseconds;

        return Term.GetChNonBlocking() > 0;
    }

    // clearScreen()
    public void ClearScreen()
    {
        if (MessageReadyToPrint)
        {
            PrintMessage(null);
        }

        Term.Clear();
    }

    // clearToBottom()
    public void ClearToBottom(int row)
    {
        Term.Move(row, 0);
        Term.ClrToBot();
    }

    // moveCursor() -- move cursor to a given y, x position
    public void MoveCursor(int y, int x)
    {
        Term.Move(y, x);
    }

    // addChar()
    public void AddChar(char ch, int y, int x)
    {
        if (!Term.MvAddCh(y, x, ch))
        {
            throw new InvalidOperationException($"addChar() off screen at ({y}, {x})"); // abort()
        }
    }

    // putString() -- dump IO to buffer -RAK-
    public void PutString(string text, int y, int x)
    {
        // truncate the string, to make sure that it won't go past right edge of screen.
        if (x > 79)
        {
            x = 79;
        }

        var str = text.Length > 79 - x ? text[..(79 - x)] : text;

        if (!Term.MvAddStr(y, x, str))
        {
            throw new InvalidOperationException($"putString() off screen at ({y}, {x})"); // abort()
        }
    }

    // putStringClearToEOL() -- outputs a line to a given y, x position -RAK-
    public void PutStringClearToEol(string text, int y, int x)
    {
        if (y == MsgLine && MessageReadyToPrint)
        {
            PrintMessage(null);
        }

        Term.Move(y, x);
        Term.ClrToEol();
        PutString(text, y, x);
    }

    // eraseLine() -- clears given line of text -RAK-
    public void EraseLine(int y, int x)
    {
        if (y == MsgLine && MessageReadyToPrint)
        {
            PrintMessage(null);
        }

        Term.Move(y, x);
        Term.ClrToEol();
    }

    // panelMoveCursor() -- moves the cursor to a given interpolated y, x
    // position -RAK-
    public void PanelMoveCursor(int y, int x)
    {
        // Real coords convert to screen positions
        y -= W.Dg.Panel.RowPrt;
        x -= W.Dg.Panel.ColPrt;

        if (!Term.Move(y, x))
        {
            throw new InvalidOperationException($"panelMoveCursor() off screen at ({y}, {x})"); // abort()
        }
    }

    // panelPutTile() -- outputs a char to a given interpolated y, x position
    // -RAK-
    public void PanelPutTile(char ch, int y, int x)
    {
        // Real coords convert to screen positions
        y -= W.Dg.Panel.RowPrt;
        x -= W.Dg.Panel.ColPrt;

        if (!Term.MvAddCh(y, x, ch))
        {
            throw new InvalidOperationException($"panelPutTile() off screen at ({y}, {x})"); // abort()
        }
    }

    // messageLinePrintMessage() -- print a line of text to the message line
    // (0,0), first clearing the line of any text!
    private void MessageLinePrintMessage(string message)
    {
        // save current cursor position
        var y = Term.CursorY;
        var x = Term.CursorX;

        // move to beginning of message line, and clear it
        Term.Move(0, 0);
        Term.ClrToEol();

        // truncate message if it's too long!
        Term.AddStr(message.Length > 79 ? message[..79] : message);

        // restore cursor to old position
        Term.Move(y, x);
    }

    // messageLineClear() -- delete all text from the message line (0,0).
    // The current cursor position will be maintained.
    public void MessageLineClear()
    {
        var y = Term.CursorY;
        var x = Term.CursorX;

        Term.Move(0, 0);
        Term.ClrToEol();

        Term.Move(y, x);
    }

    // printMessage() -- outputs message to top line of screen. These
    // messages are kept for later reference.
    public void PrintMessage(string? msg)
    {
        var oldLen = 0;
        var combineMessages = false;
        var history = W.MessageHistory;

        if (MessageReadyToPrint)
        {
            oldLen = history.Messages[history.LastMessageId].Length + 1;

            // If the new message and the old message are short enough,
            // we want display them together on the same line.  So we
            // don't flush the old message in this case.

            var newLen = msg?.Length ?? 0;

            if (msg is null || newLen + oldLen + 2 >= 73)
            {
                // ensure that the complete -more- message is visible.
                if (oldLen > 73)
                {
                    oldLen = 73;
                }

                PutString(" -more-", MsgLine, oldLen);

                char key;
                do
                {
                    key = GetKeyInput();
                } while (key != ' ' && key != Escape && key != '\n' && key != '\r');
            }
            else
            {
                combineMessages = true;
            }
        }

        if (!combineMessages)
        {
            Term.Move(MsgLine, 0);
            Term.ClrToEol();
        }

        // Make the null string a special case. -CJS-

        if (msg is null)
        {
            MessageReadyToPrint = false;
            return;
        }

        W.Game.CommandCount = 0;
        MessageReadyToPrint = true;

        // If the new message and the old message are short enough,
        // display them on the same line.

        if (combineMessages)
        {
            PutString(msg, MsgLine, oldLen + 2);
            history.Messages[history.LastMessageId] += "  " + msg;
        }
        else
        {
            MessageLinePrintMessage(msg);
            history.LastMessageId++;

            if (history.LastMessageId >= MessageHistory.Size)
            {
                history.LastMessageId = 0;
            }

            history.Messages[history.LastMessageId] = Helpers.Truncate(msg, Helpers.MoriaMessageSize);
        }
    }

    // printMessageNoCommandInterrupt() -- print a message so as not to
    // interrupt a counted command. -CJS-
    public void PrintMessageNoCommandInterrupt(string msg)
    {
        var count = W.Game.CommandCount;

        PrintMessage(msg);

        W.Game.CommandCount = count;
    }

    // getKeyInput() -- returns a single character input from the terminal.
    // -CJS-
    //
    // This silently consumes ^R to redraw the screen and reset the
    // terminal, so that this operation can always be performed at
    // any input prompt. getKeyInput() never returns ^R.
    public char GetKeyInput()
    {
        PutQio(); // Dump IO buffer
        W.Game.CommandCount = 0; // Just to be safe -CJS-

        while (true)
        {
            var ch = Term.GetCh();

            if (ch < 0)
            {
                // avoid infinite loops while trying to call getKeyInput() for a -more- prompt.
                MessageReadyToPrint = false;

                W.Game.EofFlag++;

                Term.Refresh();

                if (!W.Game.CharacterGenerated || W.Game.CharacterSaved)
                {
                    EndGame?.Invoke();
                }

                PlayerCore.Disturb(W, 1, 0);

                if (W.Game.EofFlag > 100 && EndGame is not null)
                {
                    // just in case, to make sure that the process eventually dies
                    W.Game.PanicSave = true;

                    W.Game.CharacterDiedFrom = "(end of input: panic saved)";
                    if (!(SaveGame?.Invoke() ?? false))
                    {
                        W.Game.CharacterDiedFrom = "panic: unexpected eof";
                        W.Game.CharacterIsDead = true;
                    }
                    EndGame();
                }

                return Escape;
            }

            if (ch != CtrlKey('R'))
            {
                return (char)ch;
            }

            // (wrefresh(curscr) -- the frontend redraws everything)
            Term.Refresh();
        }
    }

    // getCommand() -- prompts (optional) and returns ord value of input
    // char. Function returns false if <ESCAPE> is input
    public bool GetCommand(string prompt, out char command)
    {
        if (prompt.Length != 0)
        {
            PutStringClearToEol(prompt, 0, 0);
        }

        command = GetKeyInput();

        MessageLineClear();

        return command != Escape;
    }

    // getStringInput() -- gets a string terminated by <RETURN>. Function
    // returns false (null) if <ESCAPE> is input
    public string? GetStringInput(int y, int x, int slen)
    {
        Term.Move(y, x);

        for (var i = slen; i > 0; i--)
        {
            Term.AddCh(' ');
        }

        Term.Move(y, x);

        var startCol = x;
        var endCol = x + slen - 1;

        if (endCol > 79)
        {
            endCol = 79;
        }

        var text = new System.Text.StringBuilder();

        var flag = false;
        var aborted = false;

        while (!flag && !aborted)
        {
            var key = GetKeyInput();
            switch (key)
            {
                case Escape:
                    aborted = true;
                    break;
                case '\n':
                case '\r':
                    flag = true;
                    break;
                case Delete:
                case '\b':
                    if (x > startCol)
                    {
                        x--;
                        PutString(" ", y, x);
                        MoveCursor(y, x);
                        text.Length--;
                    }
                    break;
                default:
                    if (key < ' ' || key > '~' || x > endCol)
                    {
                        TerminalBellSound();
                    }
                    else
                    {
                        Term.MvAddCh(y, x, key);
                        text.Append(key);
                        x++;
                    }
                    break;
            }
        }

        if (aborted)
        {
            return null;
        }

        // Remove trailing blanks
        return text.ToString().TrimEnd(' ');
    }

    // getInputConfirmation() -- used to verify a choice - user gets the
    // chance to abort choice. -CJS-
    public bool GetInputConfirmation(string prompt)
    {
        return GetInputConfirmationWithAbort(0, prompt) == 1;
    }

    // getInputConfirmationWithAbort() -- used to verify a choice, with the
    // option of aborting (useful for "drop all items") and with the option
    // of setting the column for displaying the prompt.
    public int GetInputConfirmationWithAbort(int column, string prompt)
    {
        PutStringClearToEol(prompt, 0, column);

        if (Term.CursorX > 73)
        {
            Term.Move(0, 73);
        }

        Term.AddStr(" [y/n]");

        var key = ' ';
        while (key == ' ')
        {
            key = GetKeyInput();
        }

        MessageLineClear();

        return key switch
        {
            'N' or 'n' => 0,
            'Y' or 'y' => 1,
            _ => -1,
        };
    }

    // waitForContinueKey() -- pauses for user response before returning
    // -RAK-
    public void WaitForContinueKey(int lineNumber)
    {
        PutStringClearToEol("[ press any key to continue ]", lineNumber, 23);
        GetKeyInput();
        EraseLine(lineNumber, 0);
    }

    // getDefaultPlayerName() -- find a default user name from the system.
    public static string GetDefaultPlayerName()
    {
        // Gotta have some name. PLAYER_NAME_SIZE is 27 including the
        // terminator; GetUserName fails (-> "X") if the login doesn't fit
        // (the same rule as CharacterCreation's).
        var name = Environment.UserName;
        return string.IsNullOrEmpty(name) || name.Length >= 27 ? "X" : name;
    }
}
