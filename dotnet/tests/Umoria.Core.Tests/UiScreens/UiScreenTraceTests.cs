using System.Text;
using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Spells;
using Umoria.Core.Stores;
using Umoria.Core.Tests.CharCreate;
using Umoria.Core.Tests.ItemUse;
using Umoria.Core.Tests.Casting;
using Arena = Umoria.Core.Tests.Casting.SpellCastingTraceTests;
using Umoria.Core.Ui;
using Xunit;

namespace Umoria.Core.Tests.UiScreens;

// Real cross-check of the M14 terminal UI (Umoria.Core.Ui): every screen the
// player sees while a key is read, against tools/ui_screen_dump -- the real
// C++ ui_io.cpp / ui.cpp / ui_inventory.cpp drawing into a headless curses
// shim with the same semantics as Ui.Terminal. The scenarios are the
// harness's ui_screen_scenarios.inc (parsed with ItemUseTraceTests' parser,
// whose fields they share); setup is the M10/M11 arena (SpellCastingTraceTests'
// Arena), or a level from DungeonGenerator (position GEN).
//
// Baseline: dotnet/tests/UiScreenTraces/ui_screen_dump.txt, produced by
// `build-msvc/umoria/ui_screen_dump.exe <file>`.
public class UiScreenTraceTests
{
    [Fact]
    public void UiScreens_MatchCppBaseline()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "UiScreenTraces", "ui_screen_dump.txt"));
        var scenarios = ItemUseTraceTests.ParseScenarios(Path.Combine(AppContext.BaseDirectory, "UiScreenTraces", "ui_screen_scenarios.inc"));

        // A true process-lifetime static in the original (0 at start-up);
        // other tests in this process move it.
        TreasureMagic.MissilesCounter = 0;

        var actual = new List<string>();
        foreach (var scenario in scenarios)
        {
            new Runner(scenario, actual).Run();
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    // The shim's getch(): the scripted keys, then <ESCAPE> (counted) once
    // they run out.
    private sealed class ScriptedKeys : IKeySource
    {
        private string _keys = "";
        private int _pos;

        public int ExtraEscapes { get; private set; }

        public int KeysLeft => _keys.Length - _pos;

        public void Set(string keys)
        {
            _keys = keys;
            _pos = 0;
            ExtraEscapes = 0;
        }

        public int ReadKey()
        {
            if (_pos < _keys.Length)
            {
                return _keys[_pos++];
            }

            ExtraEscapes++;
            if (ExtraEscapes > 50)
            {
                throw new InvalidOperationException("a prompt keeps reading past the end of its script");
            }
            return 27;
        }
    }

    private sealed class Runner
    {
        private readonly ItemUseTraceTests.Scenario _s;
        private readonly List<string> _o;
        private readonly ScriptedKeys _keys = new();
        private PlayerWorld _w = null!;
        private GameUi _ui = null!;
        private bool _dumping;
        private int _step;
        private int _screen;
        private int _result;

        public Runner(ItemUseTraceTests.Scenario s, List<string> o)
        {
            _s = s;
            _o = o;
        }

        public void Run()
        {
            var s = _s;
            var (py, identification) = PlayerTrace.CreateCharacter(s.Seed, s.RaceId, s.IsMale, s.ClassChoice, 0);

            // startMoria(): magicInitializeItemNames() right after creation
            identification.MagicInitializeItemNames(s.Seed ^ 0x5a5a5a5au);

            _w = Arena.BuildArena(py, identification, s.Depth);
            var w = _w;
            w.Game.CharacterGenerated = true;

            if (s.Pos.Y < 0)
            {
                // generateCave(); it places the player
                var start = new DungeonGenerator(w.Dg, w.Treasure, w.Monsters).Generate(s.Depth);
                py.Pos = start;
            }
            else
            {
                py.Pos = s.Pos;
            }

            Arena.Setup(w, s.Setup, s.Exp);
            Arena.PlaceMonsters(w, s.Monsters);

            // a blank screen and a blank spare one
            var terminal = new Terminal(_keys);
            _ui = new GameUi(w, terminal, cosmeticSeed: 1);
            _ui.Attach();
            terminal.BeforeKeyRead = _ =>
            {
                if (_dumping)
                {
                    DumpScreen("key");
                }
            };

            // generateCave()'s panel setup, then playDungeon()'s preamble
            // (which marks the player's tile), then the whole screen
            var dg = w.Dg;
            dg.Panel = new Panel();
            ScreenPanel.InitializeForLevel(dg);
            dg.Panel.Row = -1;
            dg.Panel.Col = -1;
            PlayerStatusEffects.EnterLevel(w);
            _ui.PrintCharacterCurrentDepth();
            _ui.DrawCavePanel();

            _o.Add($"scenario {s.Name}");
            _step = 0;
            _screen = 0;
            _result = 0;
            _keys.Set("");
            _dumping = true;
            DumpStep("start");

            foreach (var token in s.Script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                _step++;
                _screen = 0;
                RunToken(token);
                DumpStep(token);
            }

            _dumping = false;
            var history = w.MessageHistory;
            _o.Add($"messages {history.LastMessageId}");
            for (var i = 0; i < MessageHistory.Size; i++)
            {
                _o.Add($"message {i} [{history.Messages[i]}]");
            }

            ScreenRendering.Detach(dg);
        }

        private void DumpScreen(string label)
        {
            var t = _ui.Term;
            _o.Add($"screen {_step}.{_screen++} {label} cursor={t.CursorY},{t.CursorX}");

            foreach (var line in t.Snapshot())
            {
                _o.Add("|" + line.TrimEnd(' '));
            }
        }

        private void DumpStep(string token)
        {
            var w = _w;
            var g = w.Game;
            var py = w.Py;

            _o.Add($"step {_step} {token} rng={MoriaRng.GetRandomSeed()} free={(g.PlayerFreeTurn ? 1 : 0)} inv={(int)g.DoingInventoryCommand} " +
                   $"scr={(int)g.Screen.CurrentScreenId} left={g.Screen.ScreenLeftPos} bot={g.Screen.ScreenBottomPos} msg={w.MessageHistory.LastMessageId} " +
                   $"ready={(_ui.MessageReadyToPrint ? 1 : 0)} changed={(g.ScreenHasChanged ? 1 : 0)} status={py.Flags.Status:x8} cc={g.CommandCount} " +
                   $"au={py.Misc.Au} hp={py.Misc.CurrentHp} pack={Arena.HashInventory(py):x8} keys={_keys.KeysLeft} esc={_keys.ExtraEscapes} result={_result}");
            DumpScreen("after");
        }

        // Mirrors decodeKeys().
        private static string DecodeKeys(string text)
        {
            var keys = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '~':
                        keys.Append(GameUi.Escape);
                        break;
                    case '_':
                        keys.Append(' ');
                        break;
                    case '$':
                        keys.Append('\r');
                        break;
                    case '#':
                        keys.Append('\b');
                        break;
                    case '^' when i + 1 < text.Length:
                        keys.Append((char)(text[++i] & 0x1f));
                        break;
                    default:
                        keys.Append(c);
                        break;
                }
            }
            return keys.ToString();
        }

        // Mirrors runToken().
        private void RunToken(string token)
        {
            var w = _w;
            var py = w.Py;
            var f = py.Flags;

            var arg = token[1..];
            var keys = "";
            var colon = arg.IndexOf(':');
            if (colon >= 0)
            {
                keys = DecodeKeys(arg[(colon + 1)..]);
                arg = arg[..colon];
            }
            _keys.Set(keys);
            _result = 0;

            (int A, int B) Pair()
            {
                var parts = arg.Split(',');
                return (int.Parse(parts[0]), int.Parse(parts[1]));
            }

            switch (token[0])
            {
                case 'D':
                    _ui.DrawCavePanel();
                    break;
                case '!':
                    break;
                case 'm':
                    PlayerMovement.Move(w, int.Parse(arg), false);
                    break;
                case 'M':
                    PlayerCore.PrintMessage(w, arg.Replace('_', ' '));
                    break;
                case 'N':
                    _ui.PrintMessage(null);
                    break;
                case 'i':
                    InventoryCommands.ExecuteCommand(w, arg[0]);
                    break;
                case 'I':
                    if (w.Game.DoingInventoryCommand != 0)
                    {
                        InventoryCommands.ExecuteCommand(w, w.Game.DoingInventoryCommand);
                    }
                    break;
                case 'C':
                    _ui.ChangeCharacterName();
                    break;
                case 'H':
                    w.Game.CommandCount = uint.Parse(arg);
                    _ui.CommandPreviousMessage();
                    w.Game.LastCommand = (char)('P' & 0x1f);
                    break;
                case 'L':
                    _ui.CommandLocateOnMap();
                    break;
                case 'V':
                    _ui.DungeonDisplayMap();
                    break;
                case 'G':
                {
                    var (a, b) = Pair();
                    var found = PlayerCore.GetItemId(w, "Which one? ", a, b, out var itemId);
                    _result = found ? itemId : -1;
                    break;
                }
                case 'c':
                    SpellCasting.GetAndCastMagicSpell(w);
                    break;
                case 'p':
                    SpellCasting.Pray(w);
                    break;
                case 's':
                    PlayerSpells.GainSpells(w);
                    break;
                case 'S':
                    StoreCommands.Enter(w, int.Parse(arg));
                    break;
                case 'O':
                    // (a turn past 0, or every store's doors are "locked")
                    w.Dg.GameTurn = 10;
                    StoreCommands.InitializeOwners(w);
                    for (var i = 0; i < 10; i++)
                    {
                        StoreInventory.Maintenance(w);
                    }
                    break;
                case 'Y':
                    _result = _ui.GetInputConfirmation("Are you sure?") ? 1 : 0;
                    break;
                case 'T':
                {
                    _ui.PutStringClearToEol("Text:", 0, 0);
                    var text = _ui.GetStringInput(0, 6, 20);
                    if (text is not null)
                    {
                        _result = text.Length;
                        _o.Add($"text [{text}]");
                    }
                    else
                    {
                        _result = -1;
                    }
                    break;
                }
                case 'E':
                    py.Misc.Exp = int.Parse(arg);
                    PlayerCore.DisplayCharacterExperience(w);
                    break;
                case 'U':
                    // status_effects_dump's tick()
                    for (var i = int.Parse(arg); i > 0; i--)
                    {
                        w.Dg.GameTurn++;
                        PlayerStatusEffects.UpdateTimedEffects(w);
                        PlayerStatusEffects.UpdateStatusUpkeep(w);
                    }
                    break;
                case 'P':
                {
                    var (a, b) = Pair();
                    Arena.CarryObject(w, a, b, -1);
                    break;
                }
                case 'W':
                {
                    var (a, b) = Pair();
                    Arena.EquipObject(py, a, b);
                    PlayerBonuses.RecalculateBonuses(py);
                    break;
                }
                case 'X':
                {
                    var n = arg.Length > 1 ? int.Parse(arg[1..]) : 0;
                    switch (arg[0])
                    {
                        case 'h':
                            f.Status |= PlayerConfig.Status.PyHungry;
                            break;
                        case 'w':
                            f.Status |= PlayerConfig.Status.PyWeak;
                            break;
                        case 'b':
                            f.Blind = (short)n;
                            f.Status |= PlayerConfig.Status.PyBlind;
                            break;
                        case 'c':
                            f.Confused = (short)n;
                            f.Status |= PlayerConfig.Status.PyConfused;
                            break;
                        case 'f':
                            f.Afraid = (short)n;
                            f.Status |= PlayerConfig.Status.PyFear;
                            break;
                        case 'p':
                            f.Poisoned = (short)n;
                            f.Status |= PlayerConfig.Status.PyPoisoned;
                            break;
                        case 's':
                            PlayerCore.SearchOn(w);
                            break;
                        case 'r':
                            f.Rest = (short)n;
                            f.Status |= PlayerConfig.Status.PyRest;
                            break;
                        case 'z':
                            f.Paralysis = (short)n;
                            break;
                        case 'v':
                            f.Speed = (short)n;
                            break;
                        case 'n':
                            f.NewSpellsToLearn = (byte)n;
                            break;
                        case 'k':
                            w.Game.CommandCount = (uint)n;
                            break;
                        case 'g':
                            py.Misc.Au = n;
                            break;
                        case 'd':
                            f.Food = (short)n;
                            break;
                        default:
                            throw new InvalidOperationException($"bad status token {token}");
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException($"bad token {token}");
            }
        }
    }
}
