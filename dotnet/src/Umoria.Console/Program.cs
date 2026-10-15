using Terminal.Gui;
using Umoria.Console.UI;
using Umoria.Core.Dungeon;
using Umoria.Core.Rng;

// CLI parsing mirrors src/main.cpp's subset relevant to this proof-of-concept
// vertical slice: only -s (seed) and -h (help). The original's -n/-r/-d/-v/-w
// (new/roguelike-keys/scores/version/wizard) are deferred along with the
// subsystems they depend on (character creation, scoring, wizard commands).
uint seed = (uint)Environment.TickCount;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-s" when i + 1 < args.Length && uint.TryParse(args[i + 1], out var parsedSeed):
            seed = parsedSeed;
            i++;
            break;
        case "-h":
        case "--help":
            System.Console.WriteLine("Umoria.Console (proof-of-concept .NET port)");
            System.Console.WriteLine("  -s SEED   use a fixed RNG seed (default: current tick count)");
            System.Console.WriteLine("  -h        show this help");
            return;
    }
}

MoriaRng.SetRandomSeed(seed);

var dungeon = new Umoria.Core.Model.Generated.Dungeon();
var generator = new DungeonGenerator(dungeon, new TreasureList(), new MonsterManager(dungeon));
var playerStart = generator.Generate(level: 1);

Application.Init();

try
{
    var top = new Toplevel();
    var dungeonView = new DungeonView(dungeon, playerStart);
    top.Add(dungeonView);

    var statusBar = new Label
    {
        Y = Pos.AnchorEnd(1),
        Width = Dim.Fill(),
        Text = $"seed={seed} level=1 -- move: 1-9/hjklyubn, quit: q/Esc",
    };
    top.Add(statusBar);

    top.KeyDown += (_, key) =>
    {
        if (InputMap.IsQuit(key))
        {
            Application.RequestStop();
            key.Handled = true;
        }
    };

    Application.Run(top);
    top.Dispose();
}
finally
{
    Application.Shutdown();
}
