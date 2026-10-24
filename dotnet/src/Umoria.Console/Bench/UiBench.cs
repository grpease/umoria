using Umoria.Core.Character;
using Umoria.Core.Creatures;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;
using Umoria.Core.Stores;
using Umoria.Core.Ui;

namespace Umoria.Console.Bench;

// A TEMPORARY command loop for seeing the M14 UI in a real terminal -- not
// the game's main loop. game_run.cpp's playDungeon()/doCommand() (repeat
// counts, running, resting, the town, saving, death and scores, every
// remaining command) is M15's; until then this bench creates a character
// from fixed answers, generates a dungeon level, and dispatches the
// commands whose screens M14 ported:
//
//   1-9 / arrows    move (5 stays put)       i e t w x d  inventory screens
//   E q r a u v {   eat quaff read aim use throw inscribe
//   m p G           cast, pray, study        C   character screen
//   ^P              previous messages        M   dungeon map
//   L               locate on the map        ?   help file
//   _               (bench only) enter a store: 1-6
//   Q               (bench only) quit
//
// Each turn runs the status effects first and the monsters after the
// player's command, in playDungeon()'s order; stairs ('<' / '>' underfoot)
// are not wired (no commands for them yet), so the level never changes.
public sealed class UiBench
{
    private readonly PlayerWorld _w;
    private readonly GameUi _ui;
    private readonly string _dataDir;
    private bool _quit;

    public UiBench(Umoria.Core.Ui.Terminal terminal, uint seed, CharacterChoices choices, string dataDir)
    {
        _dataDir = dataDir;

        MoriaRng.SetRandomSeed(seed);

        var py = new Player();
        var identification = new ObjectIdentification();
        PlayerStatLogic.InitializeBaseExperienceLevels(py);
        CharacterCreation.Create(py, choices);
        CharacterCreation.InitializeNewCharacter(py, identification);

        var game = new GameState { CharacterGenerated = true, MagicSeed = seed ^ 0x5a5a5a5au };
        identification.MagicInitializeItemNames(game.MagicSeed);

        var dg = new Umoria.Core.Model.Generated.Dungeon { CurrentLevel = 1 };
        var monsters = new MonsterManager(dg);
        var treasure = new TreasureList();
        _w = new PlayerWorld(py, dg, game, monsters, treasure, identification);

        // (startMoria() sets the stores up before creation; the bench has no
        // town, so they're only reachable through '_')
        StoreCommands.InitializeOwners(_w);
        for (var i = 0; i < 10; i++)
        {
            StoreInventory.Maintenance(_w);
        }

        py.Pos = new DungeonGenerator(dg, treasure, monsters).Generate(dg.CurrentLevel);
        dg.GameTurn = 1;

        // (attached once the level and its panel exist)
        _ui = new GameUi(_w, terminal);
        _ui.Attach();
        _ui.EndOfInput = () => _quit = true;
    }

    public void Run()
    {
        var w = _w;
        var dg = w.Dg;

        // playDungeon()'s preamble
        dg.Panel.Row = -1;
        dg.Panel.Col = -1;
        PlayerStatusEffects.EnterLevel(w);
        _ui.PrintCharacterCurrentDepth();
        _ui.DrawCavePanel();

        PlayerCore.PrintMessage(w, "M14 UI bench -- '?' for help, 'Q' to quit.");

        while (!_quit && !w.Game.CharacterIsDead)
        {
            dg.GameTurn++;

            PlayerStatusEffects.UpdateTimedEffects(w);
            PlayerStatusEffects.UpdateStatusUpkeep(w);

            if (w.Py.Flags.Paralysis < 1 && w.Py.Flags.Rest == 0 && !w.Game.CharacterIsDead)
            {
                do
                {
                    ExecuteInputCommand();
                } while (w.Game.PlayerFreeTurn && !_quit && !w.Game.CharacterIsDead);
            }

            if (!_quit && !w.Game.CharacterIsDead)
            {
                MonsterAi.UpdateMonsters(w, true);
            }
        }

        if (w.Game.CharacterIsDead)
        {
            PlayerCore.PrintMessage(w, $"You die, killed by {w.Game.CharacterDiedFrom}.");
            _ui.PrintMessage(null);
        }
    }

    // executeInputCommands()'s key read and doCommand()'s dispatch, for the
    // bench's commands
    private void ExecuteInputCommand()
    {
        var w = _w;

        w.Game.UseLastDirection = false;
        w.Game.PlayerFreeTurn = false;

        if (w.Game.DoingInventoryCommand != 0)
        {
            InventoryCommands.ExecuteCommand(w, w.Game.DoingInventoryCommand);
            return;
        }

        // move the cursor to the players character
        _ui.PanelMoveCursor(w.Py.Pos.Y, w.Py.Pos.X);
        _ui.ClearMessageReady();

        var command = _ui.GetKeyInput();
        w.Game.LastCommand = command;

        switch (command)
        {
            case >= '1' and <= '9' and not '5':
                PlayerMovement.Move(w, command - '0', true);
                break;
            case '5':
                break;
            case 'i' or 'e' or 't' or 'w' or 'x' or 'd':
                InventoryCommands.ExecuteCommand(w, command);
                break;
            case 'E':
                PlayerEat.Eat(w);
                break;
            case 'q':
                PlayerQuaff.Quaff(w);
                break;
            case 'r':
                Scrolls.Read(w);
                break;
            case 'a':
                Staves.WandAim(w);
                break;
            case 'u':
                Staves.StaffUse(w);
                break;
            case 'v':
                PlayerThrow.ThrowItem(w);
                break;
            case '{':
                InventoryCommands.ItemInscribe(w);
                w.Game.PlayerFreeTurn = true;
                break;
            case 'm':
                SpellCasting.GetAndCastMagicSpell(w);
                break;
            case 'p':
                SpellCasting.Pray(w);
                break;
            case 'G':
                PlayerSpells.GainSpells(w);
                break;
            case 'C':
                _ui.TerminalSaveScreen();
                _ui.ChangeCharacterName();
                _ui.TerminalRestoreScreen();
                w.Game.PlayerFreeTurn = true;
                break;
            case (char)('P' & 0x1f):
                _ui.CommandPreviousMessage();
                w.Game.PlayerFreeTurn = true;
                break;
            case 'M':
                _ui.DungeonDisplayMap();
                w.Game.PlayerFreeTurn = true;
                break;
            case 'L':
                _ui.CommandLocateOnMap();
                w.Game.PlayerFreeTurn = true;
                break;
            case '?':
                _ui.DisplayTextHelpFile(Path.Combine(_dataDir, "help.txt"));
                w.Game.PlayerFreeTurn = true;
                break;
            case '_':
                if (PlayerCore.GetCommand(w, "Which store (1-6)?", out var store) && store is >= '1' and <= '6')
                {
                    StoreCommands.Enter(w, store - '1');
                }
                else
                {
                    w.Game.PlayerFreeTurn = true;
                }
                break;
            case 'Q':
                _quit = PlayerCore.GetInputConfirmation(w, "Quit the bench?");
                w.Game.PlayerFreeTurn = true;
                break;
            case (char)27:
                w.Game.PlayerFreeTurn = true;
                break;
            default:
                _ui.PutStringClearToEol("Type '?' for help.", 0, 0);
                w.Game.PlayerFreeTurn = true;
                break;
        }
    }
}
