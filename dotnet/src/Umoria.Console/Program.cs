using Terminal.Gui;
using Umoria.Console.UI;
using Umoria.Core.Game;

// Umoria.Console: the terminal frontend. The game runs on its own thread,
// exactly as the original's code runs -- it blocks for keys deep inside its
// commands (Umoria.Core.Ui) -- while Terminal.Gui owns the real console:
// TerminalView copies the game's virtual 80x24 screen and queues the keys.
//
// The game is Umoria.Core.Game.GameRun (src/game_run.cpp's startMoria(),
// M15). Option parsing mirrors src/main.cpp, minus -w (wizard mode is out
// of the port's scope); --dir and --script are the port's own.
uint seed = 0;
var newGame = false;
var roguelikeKeys = false;
var showScores = false;
string? saveGame = null;
string? gameDirectory = null;
string? script = null;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    switch (arg)
    {
        case "-v":
            System.Console.WriteLine(GameFiles.Version);
            return 0;
        case "-n":
            newGame = true;
            break;
        case "-r":
            roguelikeKeys = true;
            break;
        case "-d":
            showScores = true;
            break;
        case "-s":
            // No NUMBER provided?
            if (i + 1 >= args.Length)
            {
                break;
            }

            if (!int.TryParse(args[++i], out var value) || value <= 0)
            {
                System.Console.WriteLine("Game seed must be a decimal number between 1 and 2147483647");
                return -1;
            }

            seed = (uint)value;
            break;
        case "--dir" when i + 1 < args.Length:
            gameDirectory = args[++i];
            break;
        case "--script" when i + 1 < args.Length:
            script = args[++i];
            break;
        default:
            if (!arg.StartsWith('-') && saveGame is null)
            {
                // SAVEGAME
                saveGame = arg;
                break;
            }

            System.Console.WriteLine("Robert A. Koeneke's classic dungeon crawler.");
            System.Console.WriteLine($"Umoria {GameFiles.Version} is released under a GPL-3.0-or-later license.");
            System.Console.WriteLine("""

                Usage:
                    Umoria.Console [OPTIONS] SAVEGAME

                SAVEGAME is an optional save game filename (default: game.sav)

                Options:
                    -n           Force start of new game
                    -r           Enable classic roguelike keys on startup (default: disabled, or save game settings)
                    -d           Display high scores and exit
                    -s NUMBER    Game Seed, as a decimal number (max: 2147483647)

                    -v           Print version info and exit
                    -h           Display this message

                Port options:
                    --dir DIR    Game directory: data/, LICENSE, the save and score files
                                 (default: the executable's directory)
                    --script KEYS  Run headless: type KEYS (~ = ESC, ^x = Ctrl-x, $ = RETURN),
                                 print the screen shown once they run out, and exit
                """);
            return 0;
    }
}

var files = new GameFiles(gameDirectory ?? AppContext.BaseDirectory);
files.RenderDataTemplates();

void Play(GameRun run)
{
    if (saveGame is not null)
    {
        run.Save.SaveFilePath = saveGame;
    }

    if (showScores)
    {
        run.ShowScores();
    }
    else
    {
        run.Run(seed, newGame, roguelikeKeys);
    }
}

if (script is not null)
{
    // headless: the keys, then end of input
    var scripted = new ScriptedKeys(script);

    // the screen shown when the script runs out (what a player typing those
    // keys would be looking at)
    var headless = new Umoria.Core.Ui.Terminal(scripted);
    string[]? shown = null;
    headless.BeforeKeyRead = t => shown ??= scripted.IsDrained ? t.Snapshot() : null;

    Play(new GameRun(headless, files));

    foreach (var line in shown ?? headless.Snapshot())
    {
        System.Console.WriteLine(line.TrimEnd());
    }
    return 0;
}

Application.Init();

Exception? gameFailure = null;

try
{
    var keys = new KeyQueue();
    var terminal = new Umoria.Core.Ui.Terminal(keys);
    var run = new GameRun(terminal, files);

    var top = new Toplevel();
    var view = new TerminalView(terminal, keys);
    top.Add(view);

    var gameThread = new Thread(() =>
    {
        try
        {
            Play(run);
        }
        catch (Exception e)
        {
            gameFailure = e;
        }

        Application.Invoke(() => Application.RequestStop());
    })
    {
        IsBackground = true,
        Name = "Umoria game",
    };

    top.Loaded += (_, _) =>
    {
        view.SetFocus();
        gameThread.Start();
    };

    Application.Run(top);

    // the window closed first: end the game's input, as an EOF (a hangup)
    // would -- the game saves and exits
    keys.Close();
    gameThread.Join(TimeSpan.FromSeconds(5));
    top.Dispose();
}
finally
{
    Application.Shutdown();
}

if (gameFailure is not null)
{
    System.Console.Error.WriteLine(gameFailure);
    return 1;
}

return 0;
