using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Model.Generated;
using Umoria.Core.Persistence;

namespace Umoria.Core.Ui;

// The screens behind single commands: the message history (^P), the
// shrunken dungeon map (M), map sector viewing (L), the score table and the
// text-file pagers (help, splash, death). The main loop (M15) dispatches to
// them; they are ported here because they are nothing but screen. (M14)
public sealed partial class GameUi
{
    // src/dungeon.h RATIO -- size ratio of the Map screen
    private const int MapRatio = 3;

    // src/game_run.cpp:1405 calculateMaxMessageCount()
    private int CalculateMaxMessageCount()
    {
        var maxMessages = MessageHistory.Size;

        if (W.Game.CommandCount > 0)
        {
            if (W.Game.CommandCount < MessageHistory.Size)
            {
                maxMessages = (int)W.Game.CommandCount;
            }
            W.Game.CommandCount = 0;
        }
        else if (W.Game.LastCommand != CtrlKey('P'))
        {
            maxMessages = 1;
        }

        return maxMessages;
    }

    // src/game_run.cpp:1420 commandPreviousMessage() -- ^P
    public void CommandPreviousMessage()
    {
        var history = W.MessageHistory;
        var maxMessages = CalculateMaxMessageCount();

        if (maxMessages <= 1)
        {
            // Distinguish real and recovered messages with a '>'. -CJS-
            PutString(">", 0, 0);
            PutStringClearToEol(history.Messages[history.LastMessageId], 0, 1);
            return;
        }

        TerminalSaveScreen();

        var lineNumber = maxMessages;
        int msgId = history.LastMessageId;

        while (maxMessages > 0)
        {
            maxMessages--;

            PutStringClearToEol(history.Messages[msgId], maxMessages, 0);

            msgId = msgId == 0 ? MessageHistory.Size - 1 : msgId - 1;
        }

        EraseLine(lineNumber, 0);
        WaitForContinueKey(lineNumber);
        TerminalRestoreScreen();
    }

    // src/dungeon.cpp:15 dungeonDisplayMap() -- shrinks the dungeon to a
    // single screen
    public void DungeonDisplayMap()
    {
        // Save the game screen
        TerminalSaveScreen();
        ClearScreen();

        var priority = new short[256];
        priority['<'] = 5;
        priority['>'] = 5;
        priority['@'] = 10;
        priority['#'] = -5;
        priority['.'] = -10;
        priority['\\'] = -3;
        priority[' '] = -15;

        // Display highest priority object in the RATIO, by RATIO area
        const int panelWidth = DungeonConstants.MaxWidth / MapRatio;
        const int panelHeight = DungeonConstants.MaxHeight / MapRatio;

        var map = new char[panelWidth];

        // Add screen border
        AddChar('+', 0, 0);
        AddChar('+', 0, panelWidth + 1);
        for (var i = 0; i < panelWidth; i++)
        {
            AddChar('-', 0, i + 1);
            AddChar('-', panelHeight + 1, i + 1);
        }
        for (var i = 0; i < panelHeight; i++)
        {
            AddChar('|', i + 1, 0);
            AddChar('|', i + 1, panelWidth + 1);
        }
        AddChar('+', panelHeight + 1, 0);
        AddChar('+', panelHeight + 1, panelWidth + 1);
        PutString("Hit any key to continue", 23, 23);

        var playerY = 0;
        var playerX = 0;
        var line = -1;

        // Shrink the dungeon!
        for (var y = 0; y < DungeonConstants.MaxHeight; y++)
        {
            var row = y / MapRatio;
            if (row != line)
            {
                if (line >= 0)
                {
                    PutString($"|{new string(map)}|", line + 1, 0);
                }
                Array.Fill(map, ' ');
                line = row;
            }

            for (var x = 0; x < DungeonConstants.MaxWidth; x++)
            {
                var col = x / MapRatio;
                var caveChar = TileGlyph(new Coord(y, x));
                if (priority[(byte)map[col]] < priority[(byte)caveChar])
                {
                    map[col] = caveChar;
                }
                if (map[col] == '@')
                {
                    // +1 to account for border
                    playerX = col + 1;
                    playerY = row + 1;
                }
            }
        }

        if (line >= 0)
        {
            PutString($"|{new string(map)}|", line + 1, 0);
        }

        // Move cursor onto player character
        MoveCursor(playerY, playerX);

        // wait for any keypress
        GetKeyInput();

        // restore the game screen
        TerminalRestoreScreen();
    }

    // src/game_run.cpp:1484 commandLocateOnMap() -- L: look at other map
    // sectors. The panel moves are game state (ScreenPanel), as are the
    // direction memory and free turn of getDirectionWithMemory().
    public void CommandLocateOnMap()
    {
        var py = W.Py;
        var dg = W.Dg;

        if (py.Flags.Blind > 0 || PlayerMovement.NoLight(W))
        {
            PlayerCore.PrintMessage(W, "You can't see your map.");
            return;
        }

        var playerCoord = py.Pos;
        if (OutsidePanel(playerCoord, true))
        {
            DrawDungeonPanel();
        }

        var oldPanelY = dg.Panel.Row;
        var oldPanelX = dg.Panel.Col;

        while (true)
        {
            var panelY = dg.Panel.Row;
            var panelX = dg.Panel.Col;

            string tmpStr;
            if (panelY == oldPanelY && panelX == oldPanelX)
            {
                tmpStr = "";
            }
            else
            {
                var ns = panelY < oldPanelY ? " North" : panelY > oldPanelY ? " South" : "";
                var ew = panelX < oldPanelX ? " West" : panelX > oldPanelX ? " East" : "";
                tmpStr = $"{ns}{ew} of";
            }

            var outVal = Helpers.Truncate($"Map sector [{panelY},{panelX}], which is{tmpStr} your sector. Look which direction?", Helpers.MoriaMessageSize);

            if (!PlayerCore.GetDirectionWithMemory(W, outVal, out var dirVal))
            {
                break;
            }

            // -CJS-
            // Should really use the move function, but what the hell. This
            // is nicer, as it moves exactly to the same place in another
            // section. The direction calculation is not intuitive. Sorry.
            while (true)
            {
                var x = playerCoord.X + ((dirVal - 1) % 3 - 1) * DungeonConstants.ScreenWidth / 2;
                var y = playerCoord.Y - ((dirVal - 1) / 3 - 1) * DungeonConstants.ScreenHeight / 2;
                playerCoord = new Coord(y, x);

                if (playerCoord.X < 0 || playerCoord.Y < 0 || playerCoord.X >= dg.Width || playerCoord.Y >= dg.Width)
                {
                    PlayerCore.PrintMessage(W, "You've gone past the end of your map.");

                    x = playerCoord.X - ((dirVal - 1) % 3 - 1) * DungeonConstants.ScreenWidth / 2;
                    y = playerCoord.Y + ((dirVal - 1) / 3 - 1) * DungeonConstants.ScreenHeight / 2;
                    playerCoord = new Coord(y, x);

                    break;
                }

                if (OutsidePanel(playerCoord, true))
                {
                    DrawDungeonPanel();
                    break;
                }
            }
        }

        // Move to a new panel - but only if really necessary.
        if (OutsidePanel(py.Pos, false))
        {
            DrawDungeonPanel();
        }
    }

    private bool OutsidePanel(Coord coord, bool force) =>
        ScreenPanel.CoordOutsidePanel(W.Py, W.Dg, W.Treasure, W.Game.Options, coord, force);

    // src/scores.cpp:173 showScoresScreen() -- the scoreboard read from
    // `path` (Persistence.HighScores' JSON file), twenty to a page.
    public void ShowScoresScreen(string path)
    {
        HighScoreFile? file;
        try
        {
            file = File.Exists(path) ? HighScores.ReadFile(path) : throw new FileNotFoundException();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            PlayerCore.PrintMessage(W, $"Error opening score file '{path}'.");
            PrintMessage(null);
            return;
        }

        var scores = file?.Entries ?? [];
        var next = 0;
        var rank = 1;

        // (the original's loop runs while the file has data left; an empty
        // file shows nothing)
        while (next < scores.Count)
        {
            var i = 1;
            ClearScreen();

            // Put twenty scores on each page, on lines 2 through 21.
            while (next < scores.Count && i < 21)
            {
                i++;
                PutStringClearToEol(HighScores.FormatScoreLine(rank, scores[next]), i, 0);
                rank++;
                next++;
            }

            PutStringClearToEol("Rank  Points Name              Sex Race       Class  Lvl Killed By", 0, 0);
            EraseLine(1, 0);
            PutStringClearToEol("[ press any key to continue ]", 23, 23);
            if (GetKeyInput() == Escape)
            {
                break;
            }
        }
    }

    // The lines fgets(buf, size, file) returns, newline kept.
    private static IEnumerable<string> FgetsLines(string path, int size)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var pos = 0;

        while (pos < text.Length)
        {
            var end = text.IndexOf('\n', pos);
            var lineEnd = end < 0 ? text.Length : end + 1;
            var take = Math.Min(lineEnd - pos, size - 1);

            yield return text.Substring(pos, take);
            pos += take;
        }
    }

    // src/game_files.cpp:25 displaySplashScreen()
    public void DisplaySplashScreen(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ClearScreen();
        var i = 0;
        foreach (var line in FgetsLines(path, 80))
        {
            PutString(line, i++, 0);
        }
        WaitForContinueKey(23);
    }

    // src/game_files.cpp:42 displayTextHelpFile() -- open and display a text
    // help file. File perusal, primitive, but portable -CJS-
    public void DisplayTextHelpFile(string path)
    {
        if (!File.Exists(path))
        {
            PutStringClearToEol($"Can not find help file '{path}'.", 0, 0);
            return;
        }

        TerminalSaveScreen();

        using var lines = FgetsLines(path, 79).GetEnumerator();
        var more = true;

        while (more)
        {
            ClearScreen();

            for (var i = 0; i < 23; i++)
            {
                if (lines.MoveNext())
                {
                    PutString(lines.Current, i, 0);
                }
                else
                {
                    more = false;
                }
            }

            PutStringClearToEol("[ press any key to continue ]", 23, 23);
            if (GetKeyInput() == Escape)
            {
                break;
            }
        }

        TerminalRestoreScreen();
    }

    // src/game_files.cpp:75 displayDeathFile() -- open and display a "death"
    // text file
    public void DisplayDeathFile(string path)
    {
        if (!File.Exists(path))
        {
            PutStringClearToEol($"Can not find help file '{path}'.", 0, 0);
            return;
        }

        ClearScreen();

        var i = 0;
        foreach (var line in FgetsLines(path, 79))
        {
            if (i >= 23)
            {
                break;
            }
            PutString(line, i++, 0);
        }
    }
}
