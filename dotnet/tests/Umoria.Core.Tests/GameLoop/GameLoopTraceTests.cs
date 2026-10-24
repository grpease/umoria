using System.Text;
using System.Text.RegularExpressions;
using Umoria.Core.Config;
using Umoria.Core.Game;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;
using Umoria.Core.Tests.CharCreate;
using Umoria.Core.Ui;
using Arena = Umoria.Core.Tests.Casting.SpellCastingTraceTests;

namespace Umoria.Core.Tests.GameLoop;

// Real end-to-end cross-check of the M15 main loop (Umoria.Core.Game.GameRun)
// against tools/game_loop_dump -- the real C++ startMoria(), character
// creation from keys, the town, the dungeon, every command, saving, death,
// the tombstone and the score screen, on the same headless curses
// semantics as Ui.Terminal. At every key read: a state line (RNG, turn,
// level, position, hit points, gold, experience, the floor / treasure /
// monster / inventory hashes) and the screen (or "same").
//
// Each scenario is one or more runs of the program in one game directory
// (data/, LICENSE, the save and score files): the first with neither file,
// later ones restoring what the last saved. Like the harness, the clock is
// fixed (Tue Nov 14 22:13:20 2023 UTC) and the keys end in end of input.
// The two sides' save and score files are each their own format (the
// original's binary files, the port's JSON), so they are compared through
// what restoring them shows.
//
// Baseline: dotnet/tests/GameLoopTraces/game_loop_dump.txt, produced by
// `build-msvc/umoria/game_loop_dump.exe <work_dir> <file>` with a work
// directory holding build-msvc/umoria's data/ and LICENSE.
public class GameLoopTraceTests
{
    private const uint FixedTime = 1700000000;

    [Fact]
    public void GameLoop_MatchesCppBaseline()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "GameLoopTraces", "game_loop_dump.txt"));
        var scenarios = ParseScenarios(Path.Combine(AppContext.BaseDirectory, "GameLoopTraces", "game_loop_scenarios.inc"));

        var actual = new List<string>();
        foreach (var scenario in scenarios)
        {
            var work = Path.Combine(Path.GetTempPath(), "umoria-game-loop-" + Guid.NewGuid().ToString("N"));
            try
            {
                PrepareGameDirectory(work);

                for (var run = 0; run < scenario.Runs.Count; run++)
                {
                    new Runner(scenario, run, work, actual).Run();
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        PlayerTrace.AssertMatches(expected, actual, "scenario ");
    }

    internal sealed record Scenario(string Name, uint Seed, List<string> Runs);

    // game_loop_scenarios.inc: SCENARIO("name", seed, "keys", ...) lines,
    // the keys C string literals
    internal static List<Scenario> ParseScenarios(string path)
    {
        var scenarios = new List<Scenario>();
        var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("SCENARIO(", StringComparison.Ordinal))
            {
                continue;
            }

            var strings = literal.Matches(trimmed).Select(m => Unescape(m.Groups[1].Value)).ToList();
            var seed = uint.Parse(Regex.Match(trimmed, "\",\\s*(\\d+)\\s*,").Groups[1].Value);

            scenarios.Add(new Scenario(strings[0], seed, strings.Skip(1).ToList()));
        }

        return scenarios;
    }

    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                result.Append(text[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => text[i],
                });
            }
            else
            {
                result.Append(text[i]);
            }
        }
        return result.ToString();
    }

    // The harness's decodeKeys(): ~ <ESCAPE>, $ <RETURN>, ^x control-x, `x
    // a literal x
    private static string DecodeKeys(string text)
    {
        var keys = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '~')
            {
                keys.Append((char)27);
            }
            else if (c == '$')
            {
                keys.Append('\r');
            }
            else if (c == '^' && i + 1 < text.Length)
            {
                keys.Append((char)(text[++i] & 0x1f));
            }
            else if (c == '`' && i + 1 < text.Length)
            {
                keys.Append(text[++i]);
            }
            else
            {
                keys.Append(c);
            }
        }
        return keys.ToString();
    }

    // The game directory: the repository's data files (the templates
    // rendered, as CMake's configure_file() does for the C++ build) and
    // LICENSE, copied next to the tests.
    private static void PrepareGameDirectory(string work)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "GameLoopTraces", "game");
        Directory.CreateDirectory(Path.Combine(work, "data"));

        foreach (var file in Directory.GetFiles(Path.Combine(source, "data")))
        {
            File.Copy(file, Path.Combine(work, "data", Path.GetFileName(file)));
        }
        File.Copy(Path.Combine(source, "LICENSE"), Path.Combine(work, "LICENSE"));

        new GameFiles(work).RenderDataTemplates();
    }

    // The shim's getch() in the harness's mode: the scripted keys, then end
    // of input (counted).
    private sealed class ScriptedKeys(string keys) : IKeySource
    {
        private int _pos;

        public int KeysLeft => keys.Length - _pos;
        public int Eofs { get; private set; }

        public int ReadKey()
        {
            if (_pos < keys.Length)
            {
                return keys[_pos++];
            }

            Eofs++;
            if (Eofs > 200)
            {
                throw new InvalidOperationException("end of input keeps being read");
            }
            return -1;
        }
    }

    // One run of the program: the harness's runOnce().
    private sealed class Runner(Scenario scenario, int runId, string work, List<string> output)
    {
        private int _keyNumber;
        private string[]? _lastScreen;
        private GameRun? _run;

        public void Run()
        {
            var files = new GameFiles(work);

            if (runId == 0)
            {
                // a fresh game directory: no save file, an empty score file
                File.Delete(Path.Combine(work, "game.sav"));
                File.Delete(files.Scores);
            }

            output.Add($"scenario {scenario.Name} run {runId}");

            var keys = new ScriptedKeys(DecodeKeys(scenario.Runs[runId]));
            var terminal = new Terminal(keys);

            _run = new GameRun(terminal, files, new GameClock
            {
                UnixTime = () => FixedTime,
                LocalNow = () => new DateTime(2023, 11, 14, 22, 13, 20),
            });

            terminal.BeforeKeyRead = t =>
            {
                _keyNumber++;
                DumpState("key", t);
            };

            var exited = _run.Run(scenario.Seed, false, false);

            terminal.BeforeKeyRead = null;
            DumpState("exit", terminal);
            output.Add($"exit {(exited ? 0 : -1)} keys_left={keys.KeysLeft} eofs={keys.Eofs} died_from=[{_run.W.Game.CharacterDiedFrom}]");
        }

        private void DumpState(string label, Terminal t)
        {
            var w = _run!.W;
            var py = w.Py;
            var dg = w.Dg;
            var game = w.Game;
            int B(bool b) => b ? 1 : 0;

            output.Add(
                $"{label} {_keyNumber} rng={MoriaRng.GetRandomSeed()} turn={dg.GameTurn} lvl={dg.CurrentLevel} pos={py.Pos.Y},{py.Pos.X} " +
                $"hp={py.Misc.CurrentHp}/{py.Misc.MaxHp} au={py.Misc.Au} exp={py.Misc.Exp} cc={game.CommandCount} free={B(game.PlayerFreeTurn)} " +
                $"dead={B(game.CharacterIsDead)} gen={B(dg.GenerateNewLevel)} eof={game.EofFlag} floor={Arena.HashFloor(dg):x8} " +
                $"tr={Arena.HashTreasure(w.Treasure):x8} mon={HashMonsters(w.Monsters):x8}/{w.Monsters.NextFreeMonsterId} " +
                $"inv={Arena.HashInventory(py):x8} cursor={t.CursorY},{t.CursorX}");

            var screen = t.Snapshot();
            if (_lastScreen is not null && screen.SequenceEqual(_lastScreen))
            {
                output.Add("same");
            }
            else
            {
                foreach (var line in screen)
                {
                    output.Add("|" + line.TrimEnd(' '));
                }
                _lastScreen = screen;
            }
        }

        private static uint HashMonsters(Umoria.Core.Dungeon.MonsterManager monsters)
        {
            var h = Arena.FnvOffset;
            for (var i = MonsterConfig.MonMinIndexId; i < monsters.NextFreeMonsterId; i++)
            {
                var m = monsters.Monsters[i];
                h = Arena.Mix(h, m.Hp);
                h = Arena.Mix(h, m.SleepCount);
                h = Arena.Mix(h, m.Speed);
                h = Arena.Mix(h, m.CreatureId);
                h = Arena.Mix(h, m.Pos.Y);
                h = Arena.Mix(h, m.Pos.X);
                h = Arena.Mix(h, m.DistanceFromPlayer);
                h = Arena.Mix(h, m.Lit ? 1 : 0);
                h = Arena.Mix(h, m.StunnedAmount);
                h = Arena.Mix(h, m.ConfusedAmount);
            }
            return h;
        }
    }
}
