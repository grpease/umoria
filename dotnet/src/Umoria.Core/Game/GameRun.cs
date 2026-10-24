using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Creatures;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Persistence;
using Umoria.Core.Rng;
using Umoria.Core.Spells;
using Umoria.Core.Stores;
using Umoria.Core.Ui;

namespace Umoria.Core.Game;

// exitProgram() -- the original calls exit(0) from deep inside the game
// (endGame(), a failed restore, 'Q' at the end of character creation).
// Here that unwinds to GameRun.Run. (M15)
public sealed class GameExitException : Exception
{
}

// The game's wall clock: getCurrentUnixTime() (src/helpers.cpp) for the
// birth date, the seed and the save file's age, and the local time
// humanDateString() prints on the tombstone. Injectable so a trace can fix
// them. (M15)
public sealed class GameClock
{
    public Func<uint> UnixTime { get; init; } = () => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public Func<DateTime> LocalNow { get; init; } = () => DateTime.Now;
}

// src/game_run.cpp -- run the game: the main loop (M15). startMoria()
// (startup, restore or character creation, the level loop),
// generateCave()'s shared half, playDungeon() (the per-turn loop),
// executeInputCommands() / doCommand() (reading and dispatching commands)
// and the small commands that live in game_run.cpp itself; endGame() and
// the tombstone (src/game_death.cpp) are in GameRun.Death.cs.
//
// One GameRun is one run of the program: it owns the world (the
// original's py / dg / game / monsters globals, fresh as at process start)
// and the UI over the given terminal. Everything below it is the port of
// earlier milestones; this is mostly glue, called in the original's order.
//
// Out of scope, as the roadmap says: wizard mode (wizard.cpp -- ^W says so
// and does nothing, and there is no -w), and monster recall (recall.cpp --
// the look command's 'r' and '/'s recall prompt show a placeholder; see
// GameUi.Look.cs).
public sealed partial class GameRun
{
    private readonly GameUi _ui;

    public GameRun(Terminal terminal, GameFiles files, GameClock? clock = null)
    {
        Files = files;
        Clock = clock ?? new GameClock();

        // the process-lifetime statics the original starts at zero
        TreasureMagic.MissilesCounter = 0;
        MoriaRng.ResetToProgramStart();

        var game = new GameState();
        // src/dungeon.cpp's initializer: game_turn -1 is "no game in
        // progress" (loadGame() refuses to restore over a live game)
        var dg = new Model.Generated.Dungeon
        {
            GameTurn = -1,
            GenerateNewLevel = true,
        };

        W = new PlayerWorld(new Player(), dg, game, new MonsterManager(dg), new TreasureList(), new ObjectIdentification());

        // next_free_monster_id's zero initializer, until the first level's
        // monsterLinker() (or a restore) sets it
        W.Monsters.NextFreeMonsterId = 0;

        Save = new GameSave(GameSave.DefaultSaveFile, Clock.UnixTime) { Directory = files.BaseDirectory };

        _ui = new GameUi(W, terminal);
        _ui.Attach();
        _ui.EndGame = EndGame;
        _ui.SaveGame = () => Save.SaveGame(W);
    }

    public PlayerWorld W { get; }
    public GameUi Ui => _ui;
    public GameFiles Files { get; }
    public GameClock Clock { get; }

    // config::files::save_game and game_save.cpp's statics
    public GameSave Save { get; }

    private Player Py => W.Py;
    private Model.Generated.Dungeon Dg => W.Dg;
    private GameState GameState => W.Game;

    private static char CtrlKey(char key) => (char)(key & 0x1f);

    private const char Escape = (char)27;
    private const char Delete = (char)0x7f;

    // Runs startMoria() until the program would exit -- true when it did
    // (exitProgram()), as it always should. (main() -- its option parsing
    // is the frontend's.)
    public bool Run(uint seed, bool startNewGame, bool roguelikeKeys)
    {
        Files.InitializeScoreFile();

        try
        {
            StartMoria(seed, startNewGame, roguelikeKeys);
        }
        catch (GameExitException)
        {
            return true;
        }

        return false;
    }

    // main()'s -d: display high scores and exit
    public void ShowScores()
    {
        Files.InitializeScoreFile();

        try
        {
            _ui.ShowScoresScreen(Files.Scores);
            ExitProgram();
        }
        catch (GameExitException)
        {
        }
    }

    // src/game.cpp:323 exitProgram() -- restore the terminal and exit
    private void ExitProgram()
    {
        _ui.FlushInputBuffer();
        throw new GameExitException();
    }

    // src/game_run.cpp:25 startMoria()
    private void StartMoria(uint seed, bool startNewGame, bool roguelikeKeys)
    {
        var py = Py;

        // Start the game with Roguelike keys (disabled by default)
        // NOTE: this will be overridden by the game save file.
        GameState.Options.UseRoguelikeKeys = roguelikeKeys;

        // (priceAdjust(): COST_ADJUSTMENT is 100, so it does nothing)

        // Show the game splash screen
        _ui.DisplaySplashScreen(Files.SplashScreen);

        // Grab a random seed from the clock
        SeedsInitialize(seed);

        // (initializeMonsterLevels() / initializeTreasureLevels(): the
        // port's MonsterManager and ObjectPlacement build their tables)

        // Init the store inventories
        StoreCommands.InitializeOwners(W);

        // NOTE: base exp levels need initializing before loading a game
        PlayerStatLogic.InitializeBaseExperienceLevels(py);

        // initialize some player fields - may or may not be needed -MRC-
        py.Flags.SpellsLearnt = 0;
        py.Flags.SpellsWorked = 0;
        py.Flags.SpellsForgotten = 0;

        // This restoration of a saved character may get ONLY the monster
        // memory. In this case, `loadGame()` returns false. It may also
        // resurrect a dead character (if you are the wizard). In this case,
        // it returns true, but also sets the parameter "generate" to true, as
        // it does not recover any cave details.

        var result = false;
        var generate = false;

        if (!startNewGame && File.Exists(Path.Combine(Files.BaseDirectory, Save.SaveFilePath)))
        {
            var load = Save.LoadGame(W);
            generate = load.Generate;

            switch (load.Outcome)
            {
                case LoadOutcome.Restored:
                    result = true;
                    break;
                case LoadOutcome.Failed:
                    // loadGame()'s error exit
                    _ui.PutStringClearToEol("Please try again without that save file.", 1, 0);

                    // We have messages for the player to read, this will ask for a keypress
                    _ui.PrintMessage(null);

                    ExitProgram();
                    break;
            }
        }

        // (game.to_be_wizard -- wizard mode is out of scope)

        if (result)
        {
            _ui.ChangeCharacterName();

            // could be restoring a dead character after a signal or HANGUP
            if (py.Misc.CurrentHp < 0)
            {
                GameState.CharacterIsDead = true;
            }
        }
        else
        {
            // Create character
            CharacterCreate();

            py.Misc.DateOfBirth = (int)Clock.UnixTime();

            CharacterCreation.InitializeCharacterInventory(py, W.Identification);
            py.Flags.Food = 7500;
            py.Flags.FoodDigested = 2;

            // Spell and Mana based on class: Mage or Clerical realm.
            var spellType = Data.Generated.DataPlayerData.Classes[py.Misc.ClassId].ClassToUseMageSpells;
            if (spellType == SpellConfig.SpellTypeMage)
            {
                _ui.ClearScreen(); // makes spell list easier to read
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Int, W.Print);
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Int);
            }
            else if (spellType == SpellConfig.SpellTypePriest)
            {
                PlayerSpells.CalculateAllowedSpellsCount(py, PlayerConfig.Attr.Wis, W.Print);
                _ui.ClearScreen(); // force out the 'learn prayer' message
                PlayerSpells.GainMana(py, PlayerConfig.Attr.Wis);
            }

            // Set some default values -MRC-
            py.TemporaryLightOnly = false;
            py.WeaponIsHeavy = false;
            py.Pack.Heaviness = 0;

            // prevent ^c quit from entering score into scoreboard,
            // and prevent signal from creating panic save until this
            // point, all info needed for save file is now valid.
            GameState.CharacterGenerated = true;
            generate = true;
        }

        W.Identification.MagicInitializeItemNames(GameState.MagicSeed);

        //
        // Begin the game
        //
        _ui.ClearScreen();
        _ui.PutString("Press ? for help", 0, 63);
        _ui.PrintCharacterStatsBlock();

        if (generate)
        {
            GenerateCave();
        }

        // Loop till dead, or exit
        while (!GameState.CharacterIsDead)
        {
            // Dungeon logic
            PlayDungeon();

            // check for eof here, see getKeyInput() in io.c
            // eof can occur if the process gets a HANGUP signal
            if (GameState.EofFlag != 0)
            {
                GameState.CharacterDiedFrom = "(end of input: saved)";
                if (!Save.SaveGame(W))
                {
                    GameState.CharacterDiedFrom = "unexpected eof";
                }

                // should not reach here, but if we do, this guarantees exit
                GameState.CharacterIsDead = true;
            }

            // New level if not dead
            if (!GameState.CharacterIsDead)
            {
                GenerateCave();
            }
        }

        // Character gets buried.
        EndGame();
    }

    // src/game.cpp:17 seedsInitialize() -- gets a new random seed for the
    // random number generator
    private void SeedsInitialize(uint seed)
    {
        var clockVar = seed == 0 ? Clock.UnixTime() : seed;

        GameState.MagicSeed = clockVar;

        clockVar += 8762;
        GameState.TownSeed = clockVar;

        clockVar += 113452;
        MoriaRng.SetRandomSeed(clockVar);

        // make it a little more random
        for (clockVar = (uint)RandomNumber.Next(100); clockVar != 0; clockVar--)
        {
            MoriaRng.Rnd();
        }
    }

    // src/dungeon_generate.cpp:1245 generateCave() -- generates a random
    // dungeon level -RAK-. The level itself is DungeonGenerator's; this is
    // the reset around it, and the town's store maintenance.
    private void GenerateCave()
    {
        var dg = Dg;
        var py = Py;

        dg.Panel.Top = 0;
        dg.Panel.Bottom = 0;
        dg.Panel.Left = 0;
        dg.Panel.Right = 0;

        py.Pos = new Coord(-1, -1);

        W.Treasure.Link();
        W.Monsters.Link();

        var generator = new DungeonGenerator(dg, W.Treasure, W.Monsters);

        if (dg.CurrentLevel == 0)
        {
            py.Pos = generator.GenerateTown(GameState.TownSeed, dg.GameTurn, py.Flags.Speed);

            // townGeneration()'s last step
            StoreInventory.Maintenance(W);
        }
        else
        {
            py.Pos = generator.Generate(dg.CurrentLevel, py.Flags.Speed);
        }
    }

    // src/game_run.cpp:2293 playDungeon() -- main procedure for dungeon.
    // -RAK-
    private void PlayDungeon()
    {
        var py = Py;
        var dg = Dg;
        var game = GameState;

        // Note: There is a lot of preliminary magic going on here at first

        // Ensure we display the panel. Used to do this with a global var. -CJS-
        // (set before EnterLevel, whose dungeonResetView() needs it; the
        // original's three calls before it don't touch the panel)
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;

        // playerInitializePlayerLight(), playerUpdateMaxDungeonDepth(),
        // resetDungeonFlags(), dungeonResetView(), playerSearchOff(),
        // updateMonsters(false)
        PlayerStatusEffects.EnterLevel(W);

        // Print the depth
        _ui.PrintCharacterCurrentDepth();

        // Initialize find counter to `0`
        var findCount = 0;

        // Note: yes, this last input command needs to be persisted
        // over different iterations of the main loop below -MRC-
        var lastInputCommand = '\0';

        // Loop until dead,  or new level
        // Exit when `dg.generate_new_level` and `eof_flag` are both set
        do
        {
            // Increment turn counter
            dg.GameTurn++;

            // turn over the store contents every, say, 1000 turns
            if (dg.CurrentLevel != 0 && dg.GameTurn % 1000 == 0)
            {
                StoreInventory.Maintenance(W);
            }

            // Check for creature generation
            if (RandomNumber.Next(MonsterConfig.MonChanceOfNew) == 1)
            {
                W.Monsters.PlaceNewWithinDistance(1, MonsterConfig.MonMaxSight, false, py.Pos, py.Flags.Speed);
            }

            // playerUpdateLightStatus() through playerUpdateWordOfRecall()
            PlayerStatusEffects.UpdateTimedEffects(W, () =>
            {
                // Check for interrupts to find or rest.
                var microseconds = py.RunningTracker != 0 ? 0 : 10000;
                if ((game.CommandCount > 0 || py.RunningTracker != 0 || py.Flags.Rest != 0) && _ui.CheckForNonBlockingKeyPress(microseconds))
                {
                    PlayerCore.Disturb(W, 0, 0);
                }
            });

            // Random teleportation
            if (py.Flags.Teleport && RandomNumber.Next(100) == 1)
            {
                PlayerCore.Disturb(W, 0, 0);
                PlayerMovement.Teleport(W, 40);
            }

            // playerStrength(), printCharacterStudyInstruction(),
            // playerUpdateStatusFlags() and the detect-enchantment roll
            PlayerStatusEffects.UpdateStatusUpkeep(W);

            // Check the state of the monster list, and delete some monsters if
            // the monster list is nearly full.  This helps to avoid problems in
            // creature.c when monsters try to multiply.  Compact_monsters() is
            // much more likely to succeed if called from here, than if called
            // from within updateMonsters().
            if (MonsterConstants.MonTotalAllocations - W.Monsters.NextFreeMonsterId < 10)
            {
                W.Monsters.CompactMonsters();
            }

            // Accept a command?
            if (py.Flags.Paralysis < 1 && py.Flags.Rest == 0 && !game.CharacterIsDead)
            {
                ExecuteInputCommands(ref lastInputCommand, ref findCount);
            }
            else
            {
                // if paralyzed, resting, or dead, flush output
                // but first move the cursor onto the player, for aesthetics
                _ui.PanelMoveCursor(py.Pos.Y, py.Pos.X);
                _ui.PutQio();
            }

            // Teleport?
            if (game.TeleportPlayer)
            {
                PlayerMovement.Teleport(W, 100);
            }

            // Move the creatures
            if (!dg.GenerateNewLevel)
            {
                MonsterAi.UpdateMonsters(W, true);
            }
        } while (!dg.GenerateNewLevel && game.EofFlag == 0);
    }

    // src/game_run.cpp:957 getCommandRepeatCount()
    private int GetCommandRepeatCount(ref char lastInputCommand)
    {
        _ui.PutStringClearToEol("Repeat count:", 0, 0);

        if (lastInputCommand == '#')
        {
            lastInputCommand = '0';
        }

        uint repeatCount = 0;

        while (true)
        {
            if (lastInputCommand == Delete || lastInputCommand == CtrlKey('H'))
            {
                repeatCount /= 10;
                _ui.PutStringClearToEol(((short)repeatCount).ToString("D7"), 0, 14);
            }
            else if (lastInputCommand >= '0' && lastInputCommand <= '9')
            {
                if (repeatCount > 99)
                {
                    _ui.TerminalBellSound();
                }
                else
                {
                    repeatCount = repeatCount * 10 + lastInputCommand - '0';
                    _ui.PutStringClearToEol(repeatCount.ToString("D7"), 0, 14);
                }
            }
            else
            {
                break;
            }
            lastInputCommand = _ui.GetKeyInput();
        }

        if (repeatCount == 0)
        {
            repeatCount = 99;
            _ui.PutStringClearToEol(repeatCount.ToString(), 0, 14);
        }

        // a special hack to allow numbers as commands
        if (lastInputCommand == ' ')
        {
            _ui.PutStringClearToEol("Command:", 0, 20);
            lastInputCommand = _ui.GetKeyInput();
        }

        return (int)repeatCount;
    }

    // src/game_run.cpp:1001 parseAlternateCtrlInput()
    private char ParseAlternateCtrlInput(char lastInputCommand)
    {
        if (GameState.CommandCount > 0)
        {
            _ui.PrintCharacterMovementState();
        }

        if (PlayerCore.GetCommand(W, "Control-", out lastInputCommand))
        {
            if (lastInputCommand >= 'A' && lastInputCommand <= 'Z')
            {
                lastInputCommand -= (char)('A' - 1);
            }
            else if (lastInputCommand >= 'a' && lastInputCommand <= 'z')
            {
                lastInputCommand -= (char)('a' - 1);
            }
            else
            {
                lastInputCommand = ' ';
                PlayerCore.PrintMessage(W, "Type ^ <letter> for a control char");
            }
        }
        else
        {
            lastInputCommand = ' ';
        }

        return lastInputCommand;
    }

    // src/game_run.cpp:1023 executeInputCommands() -- accept a command and
    // execute it
    private void ExecuteInputCommands(ref char command, ref int findCount)
    {
        var py = Py;
        var game = GameState;
        var lastInputCommand = command;

        // Accept a command and execute it
        do
        {
            if ((py.Flags.Status & PlayerConfig.Status.PyRepeat) != 0)
            {
                _ui.PrintCharacterMovementState();
            }

            game.UseLastDirection = false;
            game.PlayerFreeTurn = false;

            if (py.RunningTracker != 0)
            {
                PlayerRunning.RunAndFind(W);
                findCount -= 1;

                if (findCount == 0)
                {
                    PlayerCore.EndRunning(W);
                }

                _ui.PutQio();
                continue;
            }

            if (game.DoingInventoryCommand != 0)
            {
                InventoryCommands.ExecuteCommand(W, game.DoingInventoryCommand);
                continue;
            }

            // move the cursor to the players character
            _ui.PanelMoveCursor(py.Pos.Y, py.Pos.X);

            _ui.ClearMessageReady();

            if (game.CommandCount > 0)
            {
                game.UseLastDirection = true;
            }
            else
            {
                lastInputCommand = _ui.GetKeyInput();

                // Get a count for a command.
                var repeatCount = 0;
                if ((game.Options.UseRoguelikeKeys && lastInputCommand >= '0' && lastInputCommand <= '9') ||
                    (!game.Options.UseRoguelikeKeys && lastInputCommand == '#'))
                {
                    repeatCount = GetCommandRepeatCount(ref lastInputCommand);
                }

                // Another way of typing control codes -CJS-
                if (lastInputCommand == '^')
                {
                    lastInputCommand = ParseAlternateCtrlInput(lastInputCommand);
                }

                // move cursor to player char again, in case it moved
                _ui.PanelMoveCursor(py.Pos.Y, py.Pos.X);

                // Commands are always converted to rogue form. -CJS-
                if (!game.Options.UseRoguelikeKeys)
                {
                    lastInputCommand = OriginalCommands(lastInputCommand);
                }

                if (repeatCount > 0)
                {
                    if (!ValidCountCommand(lastInputCommand))
                    {
                        game.PlayerFreeTurn = true;
                        lastInputCommand = ' ';
                        PlayerCore.PrintMessage(W, "Invalid command with a count.");
                    }
                    else
                    {
                        game.CommandCount = (uint)repeatCount;
                        _ui.PrintCharacterMovementState();
                    }
                }
            }

            // Flash the message line.
            _ui.MessageLineClear();
            _ui.PanelMoveCursor(py.Pos.Y, py.Pos.X);
            _ui.PutQio();

            DoCommand(lastInputCommand);

            // Find is counted differently, as the command changes.
            if (py.RunningTracker != 0)
            {
                findCount = (int)game.CommandCount - 1;
                game.CommandCount = 0;
            }
            else if (game.PlayerFreeTurn)
            {
                game.CommandCount = 0;
            }
            else if (game.CommandCount != 0)
            {
                game.CommandCount--;
            }
        } while (game.PlayerFreeTurn && !Dg.GenerateNewLevel && game.EofFlag == 0);

        command = lastInputCommand;
    }

    // A getDirectionWithMemory() direction as the rogue-form command letter
    // for it ('.' and 'T' map to the run and tunnel letters, '-' to a move).
    private static char DirectionCommand(int direction, string letters) =>
        direction switch
        {
            1 => letters[0],
            2 => letters[1],
            3 => letters[2],
            4 => letters[3],
            6 => letters[4],
            7 => letters[5],
            8 => letters[6],
            9 => letters[7],
            _ => letters[8],
        };

    // src/game_run.cpp:1115 originalCommands() -- the original keyset in
    // rogue form
    private char OriginalCommands(char command)
    {
        switch (command)
        {
            case (char)('K' & 0x1f): // ^K = exit
                command = 'Q';
                break;
            case (char)('J' & 0x1f):
            case (char)('M' & 0x1f):
                command = '+';
                break;
            case (char)('P' & 0x1f): // ^P = repeat
            case (char)('W' & 0x1f): // ^W = password
            case (char)('X' & 0x1f): // ^X = save
            case (char)('V' & 0x1f): // ^V = view license
            case ' ':
            case '!':
            case '$':
                break;
            case '.':
                command = PlayerCore.GetDirectionWithMemory(W, null, out var runDirection)
                    ? DirectionCommand(runDirection, "BJNHLYKU ")
                    : ' ';
                break;
            case '/':
            case '<':
            case '>':
            case '-':
            case '=':
            case '{':
            case '?':
            case 'A':
                break;
            case '1':
                command = 'b';
                break;
            case '2':
                command = 'j';
                break;
            case '3':
                command = 'n';
                break;
            case '4':
                command = 'h';
                break;
            case '5': // Rest one turn
                command = '.';
                break;
            case '6':
                command = 'l';
                break;
            case '7':
                command = 'y';
                break;
            case '8':
                command = 'k';
                break;
            case '9':
                command = 'u';
                break;
            case 'B':
                command = 'f';
                break;
            case 'C':
            case 'D':
            case 'E':
            case 'F':
            case 'G':
                break;
            case 'L':
                command = 'W';
                break;
            case 'M':
            case 'R':
                break;
            case 'S':
                command = '#';
                break;
            case 'T':
                if (PlayerCore.GetDirectionWithMemory(W, null, out var tunnelDirection))
                {
                    command = tunnelDirection switch
                    {
                        1 => CtrlKey('B'),
                        2 => CtrlKey('J'),
                        3 => CtrlKey('N'),
                        4 => CtrlKey('H'),
                        6 => CtrlKey('L'),
                        7 => CtrlKey('Y'),
                        8 => CtrlKey('K'),
                        9 => CtrlKey('U'),
                        _ => ' ',
                    };
                }
                else
                {
                    command = ' ';
                }
                break;
            case 'V':
                break;
            case 'a':
                command = 'z';
                break;
            case 'b':
                command = 'P';
                break;
            case 'c':
            case 'd':
            case 'e':
                break;
            case 'f':
                command = 't';
                break;
            case 'h':
                command = '?';
                break;
            case 'i':
                break;
            case 'j':
                command = 'S';
                break;
            case 'l':
                command = 'x';
                break;
            case 'm':
            case 'o':
            case 'p':
            case 'q':
            case 'r':
            case 's':
                break;
            case 't':
                command = 'T';
                break;
            case 'u':
                command = 'Z';
                break;
            case 'v':
            case 'w':
                break;
            case 'x':
                command = 'X';
                break;

            // wizard mode commands follow
            case (char)('A' & 0x1f): // ^A = cure all
                break;
            case (char)('B' & 0x1f): // ^B = objects
                command = CtrlKey('O');
                break;
            case (char)('D' & 0x1f): // ^D = up/down
                break;
            case (char)('H' & 0x1f): // ^H = wizhelp
                command = '\\';
                break;
            case (char)('I' & 0x1f): // ^I = identify
                break;
            case (char)('L' & 0x1f): // ^L = wizlight
                command = '*';
                break;
            case ':':
            case (char)('T' & 0x1f): // ^T = teleport
            case (char)('E' & 0x1f): // ^E = wizchar
            case (char)('F' & 0x1f): // ^F = genocide
            case (char)('G' & 0x1f): // ^G = treasure
            case '@':
            case '+':
                break;
            case (char)('U' & 0x1f): // ^U = summon
                command = '&';
                break;
            default:
                command = '~'; // Anything illegal.
                break;
        }

        return command;
    }

    // src/game_run.cpp:1339 moveWithoutPickup() -- hack for move without
    // pickup. Map '-' to a movement command.
    private bool MoveWithoutPickup(ref char command)
    {
        if (command != '-')
        {
            return true;
        }

        // Save current game.command_count as getDirectionWithMemory() may change it
        var countSave = GameState.CommandCount;

        if (PlayerCore.GetDirectionWithMemory(W, null, out var direction))
        {
            // Restore game.command_count
            GameState.CommandCount = countSave;

            command = DirectionCommand(direction, "bjnhlyku~");
        }
        else
        {
            command = ' ';
        }

        return false;
    }

    // Commands whose original reads its own direction (playerBash() etc.):
    // the port's take it as a parameter (M8).
    private void WithDirection(Action<PlayerWorld, int> command)
    {
        if (PlayerCore.GetDirectionWithMemory(W, null, out var direction))
        {
            command(W, direction);
        }
    }

    // src/game_run.cpp:1646 doCommand()
    private void DoCommand(char command)
    {
        var py = Py;
        var game = GameState;

        var doPickup = MoveWithoutPickup(ref command);

        switch (command)
        {
            case 'Q': // (Q)uit    (^K)ill
                CommandQuit();
                game.PlayerFreeTurn = true;
                break;
            case (char)('P' & 0x1f): // (^P)revious message.
                _ui.CommandPreviousMessage();
                game.PlayerFreeTurn = true;
                break;
            case (char)('V' & 0x1f): // (^V)iew license
                _ui.DisplayTextHelpFile(Files.License);
                game.PlayerFreeTurn = true;
                break;
            case (char)('W' & 0x1f): // (^W)izard mode
                // (commandFlipWizardMode() -- wizard mode is out of scope)
                PlayerCore.PrintMessage(W, "Wizard mode is not part of this port.");
                game.PlayerFreeTurn = true;
                break;
            case (char)('X' & 0x1f): // e(^X)it and save
                CommandSaveAndExit();
                game.PlayerFreeTurn = true;
                break;
            case '=': // (=) set options
                _ui.TerminalSaveScreen();
                _ui.SetGameOptions();
                _ui.TerminalRestoreScreen();
                game.PlayerFreeTurn = true;
                break;
            case '{': // ({) inscribe an object
                InventoryCommands.ItemInscribe(W);
                game.PlayerFreeTurn = true;
                break;
            case '!': // (!) escape to the shell
            case '$': // escaping to shell disabled -MRC-
            case Escape: // (ESC)   do nothing.
            case ' ': // (space) do nothing.
                game.PlayerFreeTurn = true;
                break;
            case 'b': // (b) down, left  (1)
                PlayerMovement.Move(W, 1, doPickup);
                break;
            case 'j': // (j) down    (2)
                PlayerMovement.Move(W, 2, doPickup);
                break;
            case 'n': // (n) down, right  (3)
                PlayerMovement.Move(W, 3, doPickup);
                break;
            case 'h': // (h) left    (4)
                PlayerMovement.Move(W, 4, doPickup);
                break;
            case 'l': // (l) right    (6)
                PlayerMovement.Move(W, 6, doPickup);
                break;
            case 'y': // (y) up, left    (7)
                PlayerMovement.Move(W, 7, doPickup);
                break;
            case 'k': // (k) up    (8)
                PlayerMovement.Move(W, 8, doPickup);
                break;
            case 'u': // (u) up, right  (9)
                PlayerMovement.Move(W, 9, doPickup);
                break;
            case 'B': // (B) run down, left  (. 1)
                PlayerRunning.FindInitialize(W, 1);
                break;
            case 'J': // (J) run down    (. 2)
                PlayerRunning.FindInitialize(W, 2);
                break;
            case 'N': // (N) run down, right  (. 3)
                PlayerRunning.FindInitialize(W, 3);
                break;
            case 'H': // (H) run left    (. 4)
                PlayerRunning.FindInitialize(W, 4);
                break;
            case 'L': // (L) run right  (. 6)
                PlayerRunning.FindInitialize(W, 6);
                break;
            case 'Y': // (Y) run up, left  (. 7)
                PlayerRunning.FindInitialize(W, 7);
                break;
            case 'K': // (K) run up    (. 8)
                PlayerRunning.FindInitialize(W, 8);
                break;
            case 'U': // (U) run up, right  (. 9)
                PlayerRunning.FindInitialize(W, 9);
                break;
            case '/': // (/) identify a symbol
                _ui.IdentifyGameObject();
                game.PlayerFreeTurn = true;
                break;
            case '.': // (.) stay in one place (5)
                PlayerMovement.Move(W, 5, doPickup);

                if (game.CommandCount > 1)
                {
                    game.CommandCount--;
                    PlayerRestOn();
                }
                break;
            case '<': // (<) go down a staircase
                DungeonGoUpLevel();
                break;
            case '>': // (>) go up a staircase
                DungeonGoDownLevel();
                break;
            case '?': // (?) help with commands
                _ui.DisplayTextHelpFile(game.Options.UseRoguelikeKeys ? Files.HelpRoguelike : Files.Help);
                game.PlayerFreeTurn = true;
                break;
            case 'f': // (f)orce    (B)ash
                WithDirection(PlayerDoors.Bash);
                break;
            case 'C': // (C)haracter description
                _ui.TerminalSaveScreen();
                _ui.ChangeCharacterName();
                _ui.TerminalRestoreScreen();
                game.PlayerFreeTurn = true;
                break;
            case 'D': // (D)isarm trap
                WithDirection(PlayerTraps.DisarmTrap);
                break;
            case 'E': // (E)at food
                PlayerEat.Eat(W);
                break;
            case 'F': // (F)ill lamp
                InventoryRefillLamp();
                break;
            case 'G': // (G)ain magic spells
                PlayerSpells.GainSpells(W);
                break;
            case 'V': // (V)iew scores
                _ui.TerminalSaveScreen();
                _ui.ShowScoresScreen(Files.Scores);
                _ui.TerminalRestoreScreen();
                game.PlayerFreeTurn = true;
                break;
            case 'W': // (W)here are we on the map  (L)ocate on map
                _ui.CommandLocateOnMap();
                game.PlayerFreeTurn = true;
                break;
            case 'R': // (R)est a while
                PlayerRestOn();
                break;
            case '#': // (#) search toggle  (S)earch toggle
                CommandToggleSearch();
                game.PlayerFreeTurn = true;
                break;
            case (char)('B' & 0x1f): // (^B) tunnel down left  (T 1)
                PlayerTunnel.Tunnel(W, 1);
                break;
            case (char)('M' & 0x1f): // cr must be treated same as lf.
            case (char)('J' & 0x1f): // (^J) tunnel down    (T 2)
                PlayerTunnel.Tunnel(W, 2);
                break;
            case (char)('N' & 0x1f): // (^N) tunnel down right  (T 3)
                PlayerTunnel.Tunnel(W, 3);
                break;
            case (char)('H' & 0x1f): // (^H) tunnel left    (T 4)
                PlayerTunnel.Tunnel(W, 4);
                break;
            case (char)('L' & 0x1f): // (^L) tunnel right    (T 6)
                PlayerTunnel.Tunnel(W, 6);
                break;
            case (char)('Y' & 0x1f): // (^Y) tunnel up left    (T 7)
                PlayerTunnel.Tunnel(W, 7);
                break;
            case (char)('K' & 0x1f): // (^K) tunnel up    (T 8)
                PlayerTunnel.Tunnel(W, 8);
                break;
            case (char)('U' & 0x1f): // (^U) tunnel up right    (T 9)
                PlayerTunnel.Tunnel(W, 9);
                break;
            case 'z': // (z)ap a wand    (a)im a wand
                Staves.WandAim(W);
                break;
            case 'M':
                _ui.DungeonDisplayMap();
                game.PlayerFreeTurn = true;
                break;
            case 'P': // (P)eruse a book  (B)rowse in a book
                ExamineBook();
                game.PlayerFreeTurn = true;
                break;
            case 'c': // (c)lose an object
                WithDirection(PlayerDoors.CloseDoor);
                break;
            case 'd': // (d)rop something
                InventoryCommands.ExecuteCommand(W, 'd');
                break;
            case 'e': // (e)quipment list
                InventoryCommands.ExecuteCommand(W, 'e');
                break;
            case 't': // (t)hrow something  (f)ire something
                PlayerThrow.ThrowItem(W);
                break;
            case 'i': // (i)nventory list
                InventoryCommands.ExecuteCommand(W, 'i');
                break;
            case 'S': // (S)pike a door  (j)am a door
                DungeonJamDoor();
                break;
            case 'x': // e(x)amine surrounds  (l)ook about
                _ui.Look();
                game.PlayerFreeTurn = true;
                break;
            case 'm': // (m)agic spells
                SpellCasting.GetAndCastMagicSpell(W);
                break;
            case 'o': // (o)pen something
                WithDirection(PlayerDoors.OpenClosedObject);
                break;
            case 'p': // (p)ray
                SpellCasting.Pray(W);
                break;
            case 'q': // (q)uaff
                PlayerQuaff.Quaff(W);
                break;
            case 'r': // (r)ead
                Scrolls.Read(W);
                break;
            case 's': // (s)earch for a turn
                PlayerMovement.Search(W, py.Pos, py.Misc.ChanceInSearch);
                break;
            case 'T': // (T)ake off something  (t)ake off
                InventoryCommands.ExecuteCommand(W, 't');
                break;
            case 'Z': // (Z)ap a staff  (u)se a staff
                Staves.StaffUse(W);
                break;
            case 'v': // (v)ersion of game
                _ui.DisplayTextHelpFile(Files.VersionsHistory);
                game.PlayerFreeTurn = true;
                break;
            case 'w': // (w)ear or wield
                InventoryCommands.ExecuteCommand(W, 'w');
                break;
            case 'X': // e(X)change weapons  e(x)change
                InventoryCommands.ExecuteCommand(W, 'x');
                break;
            default:
                // Wizard commands are free moves
                game.PlayerFreeTurn = true;

                // (doWizardCommands() -- wizard mode is out of scope, so
                // game.wizard_mode is never set)
                _ui.PutStringClearToEol("Type '?' for help.", 0, 0);
                break;
        }

        game.LastCommand = command;
    }

    // src/game_run.cpp:1904 validCountCommand() -- check whether this
    // command will accept a count. -CJS-
    private static bool ValidCountCommand(char command)
    {
        switch (command)
        {
            case (char)('P' & 0x1f):
            case Escape:
            case ' ':
            case '-':
            case 'b':
            case 'f':
            case 'j':
            case 'n':
            case 'h':
            case 'l':
            case 'y':
            case 'k':
            case 'u':
            case '.':
            case 'B':
            case 'J':
            case 'N':
            case 'H':
            case 'L':
            case 'Y':
            case 'K':
            case 'U':
            case 'D':
            case 'R':
            case (char)('Y' & 0x1f):
            case (char)('K' & 0x1f):
            case (char)('U' & 0x1f):
            case (char)('L' & 0x1f):
            case (char)('N' & 0x1f):
            case (char)('J' & 0x1f):
            case (char)('B' & 0x1f):
            case (char)('H' & 0x1f):
            case 'S':
            case 'o':
            case 's':
            case (char)('D' & 0x1f):
            case (char)('G' & 0x1f):
            case '+':
                return true;
            default:
                // (the original lists the commands that refuse a count
                // explicitly, then refuses everything else too)
                return false;
        }
    }
}
