using Terminal.Gui;
using Umoria.Console.Bench;
using Umoria.Console.UI;
using Umoria.Core.Character;

// Umoria.Console: the terminal frontend. The game runs on its own thread,
// exactly as the original's code runs -- it blocks for keys deep inside its
// commands (Umoria.Core.Ui) -- while Terminal.Gui owns the real console:
// TerminalView copies the game's virtual 80x24 screen and queues the keys.
//
// Until the main loop is ported (M15), the game thread runs the M14 UI
// bench (Bench/UiBench.cs). CLI parsing mirrors the relevant subset of
// src/main.cpp: -s (seed) and -h (help); the bench adds --race / --class /
// --female / --name for its fixed character answers.
uint seed = (uint)Environment.TickCount;
var race = 0;
var classChoice = 0;
var isMale = true;
var name = "Bench";
string? script = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-s" when i + 1 < args.Length && uint.TryParse(args[i + 1], out var parsedSeed):
            seed = parsedSeed;
            i++;
            break;
        case "--race" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedRace) && parsedRace is >= 0 and <= 7:
            race = parsedRace;
            i++;
            break;
        case "--class" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedClass):
            classChoice = parsedClass;
            i++;
            break;
        case "--female":
            isMale = false;
            break;
        case "--name" when i + 1 < args.Length:
            name = args[i + 1];
            i++;
            break;
        case "--script" when i + 1 < args.Length:
            script = args[i + 1];
            i++;
            break;
        case "-h":
        case "--help":
            System.Console.WriteLine("Umoria.Console (.NET port) -- M14 UI bench");
            System.Console.WriteLine("  -s SEED       use a fixed RNG seed (default: current tick count)");
            System.Console.WriteLine("  --race N      race 0-7 (0 Human, 1 Half-Elf, 2 Elf, 3 Halfling, 4 Gnome, 5 Dwarf, 6 Half-Orc, 7 Half-Troll)");
            System.Console.WriteLine("  --class N     class menu position for that race (0 = first offered)");
            System.Console.WriteLine("  --female      female character");
            System.Console.WriteLine("  --name NAME   character name");
            System.Console.WriteLine("  --script KEYS run headless: type KEYS (~ = ESC, ^x = Ctrl-x, $ = RETURN),");
            System.Console.WriteLine("                print the screen shown once they run out, and exit");
            System.Console.WriteLine("  -h            show this help");
            return;
    }
}

if (classChoice < 0 || classChoice >= CharacterCreation.AvailableClasses(race).Count)
{
    System.Console.WriteLine($"--class must be 0..{CharacterCreation.AvailableClasses(race).Count - 1} for race {race}");
    return;
}

var dataDir = FindDataDirectory();
var choices = new CharacterChoices(race, isMale, classChoice, name);

if (script is not null)
{
    // headless: the keys, then end of input (which ends the bench)
    var scripted = new KeyQueue();
    for (var k = 0; k < script.Length; k++)
    {
        var c = script[k];
        scripted.Add(c switch
        {
            '~' => (char)27,
            '$' => '\r',
            '^' when k + 1 < script.Length => (char)(script[++k] & 0x1f),
            _ => c,
        });
    }
    scripted.Close();

    // the screen shown when the script runs out (what a player typing those
    // keys would be looking at)
    var headless = new Umoria.Core.Ui.Terminal(scripted);
    string[]? shown = null;
    headless.BeforeKeyRead = t => shown ??= scripted.IsDrained ? t.Snapshot() : null;

    new UiBench(headless, seed, choices, dataDir).Run();

    foreach (var line in shown ?? headless.Snapshot())
    {
        System.Console.WriteLine(line.TrimEnd());
    }
    return;
}

Application.Init();

Exception? gameFailure = null;

try
{
    var keys = new KeyQueue();
    var terminal = new Umoria.Core.Ui.Terminal(keys);
    var bench = new UiBench(terminal, seed, choices, dataDir);

    var top = new Toplevel();
    var view = new TerminalView(terminal, keys);
    top.Add(view);

    var gameThread = new Thread(() =>
    {
        try
        {
            bench.Run();
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

    // the window closed first: end the game's input, as an EOF would
    keys.Close();
    gameThread.Join(TimeSpan.FromSeconds(2));
    top.Dispose();
}
finally
{
    Application.Shutdown();
}

if (gameFailure is not null)
{
    System.Console.Error.WriteLine(gameFailure);
    return;
}

// The repository's data/ directory (help.txt etc.), looked for upwards from
// the executable.
static string FindDataDirectory()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "data");
        if (File.Exists(Path.Combine(candidate, "help.txt")))
        {
            return candidate;
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "data");
}
