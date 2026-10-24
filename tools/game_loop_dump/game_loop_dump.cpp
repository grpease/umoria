// Game loop dump harness: verifies the .NET migration's M15 port of the
// main loop (Umoria.Core.Game.GameRun -- src/game_run.cpp's startMoria(),
// playDungeon(), executeInputCommands() / doCommand(), src/game_death.cpp,
// character creation's screens, town generation) against the real compiled
// C++ game, end to end.
//
// It runs the REAL startMoria() -- splash screen, character creation from
// keys, the town, every command, saving, death, the tombstone and the
// score screen -- on tools/ui_screen_dump's headless curses shim. The only
// things swapped are at the edges of the program:
//   - curses (the shim; its keys come from the scenario script, and end of
//     input when they run out -- the game's own EOF handling ends a run:
//     save, score, exit);
//   - the clock: helpers.cpp is #included here with time()/localtime()
//     fixed, so the birth date, the save file's age and the tombstone's date
//     are the same on every run (and in the C# test);
//   - exit(): game.cpp is #included with exit() throwing, so exitProgram()
//     ends the run back in main().
// Zero changes to src/.
//
// The screen is dumped every time a key is read -- what the player is
// looking at while asked -- or "same" when it hasn't changed since the last
// dump, after a state line (RNG, turn, level, position, hit points, gold,
// experience, the floor / monster / inventory hashes). The scenario list is
// game_loop_scenarios.inc, shared with the C# test.
//
// A scenario is one or more runs of the program against the same work
// directory: the first starts with no save file and an empty score file;
// later ones start where the last left off (a saved game is restored). Each
// run is its own process, as the real game's globals only start fresh with
// one: the driver spawns this executable once per run.
//
// Script keys: ~ <ESCAPE>, $ <RETURN>, ^x control-x, `x a literal x,
// anything else itself.
//
// Usage: game_loop_dump <work_dir> <output_file>   (both absolute; the work
//        directory must hold data/ and LICENSE, as the game's own directory)
//        game_loop_dump --run <scenario> <run> <work_dir> <output_file>

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <string>
#include <vector>

#ifdef _WIN32
#include <direct.h>
#include <process.h>
#define chdir _chdir
#else
#include <sys/wait.h>
#include <unistd.h>
#endif

#include "../ui_screen_dump/shim/curses.h"

// ---- the clock --------------------------------------------------------------

// Tue Nov 14 22:13:20 2023 UTC (a day of the month >= 10, so "%d" and "%e"
// agree and the dump is the same on every platform)
static const time_t FIXED_TIME = 1700000000;

static time_t harnessTime(time_t *out) {
    if (out != nullptr) {
        *out = FIXED_TIME;
    }
    return FIXED_TIME;
}

static struct tm *harnessLocaltime(const time_t *when) {
    static struct tm result;
#ifdef _WIN32
    (void) gmtime_s(&result, when);
#else
    (void) gmtime_r(when, &result);
#endif
    return &result;
}

#define time(out) harnessTime(out)
#define localtime(when) harnessLocaltime(when)
#include "../../src/helpers.cpp"
#undef time
#undef localtime

// ---- exit() -----------------------------------------------------------------

struct HarnessExit {
    int code;
};

#define exit(code) throw HarnessExit{code}
#include "../../src/game.cpp"
#undef exit

// ---- scenarios --------------------------------------------------------------

struct Scenario {
    const char *name;
    uint32_t seed;
    std::vector<const char *> runs;
};

#define SCENARIO(name, seed, ...) {name, seed, {__VA_ARGS__}},

static const std::vector<Scenario> scenarios = {
#include "game_loop_scenarios.inc"
};

static std::string decodeKeys(const char *text) {
    std::string keys;
    for (size_t i = 0; text[i] != '\0'; i++) {
        char c = text[i];
        if (c == '~') {
            keys += ESCAPE;
        } else if (c == '$') {
            keys += '\r';
        } else if (c == '^' && text[i + 1] != '\0') {
            keys += CTRL_KEY(text[++i]);
        } else if (c == '`' && text[i + 1] != '\0') {
            keys += text[++i];
        } else {
            keys += c;
        }
    }
    return keys;
}

// ---- hashes (M10's, plus the monster list) -----------------------------------

static uint32_t mix(uint32_t h, int32_t v) {
    h ^= (uint32_t) v;
    h *= 16777619u;
    return h;
}

static uint32_t hashFloor() {
    uint32_t h = 2166136261u;
    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            Tile_t const &t = dg.floor[y][x];
            h = mix(h, t.creature_id | (t.treasure_id << 8) | (t.feature_id << 16) | ((int) t.perma_lit_room << 24) | ((int) t.field_mark << 25) | ((int) t.permanent_light << 26) |
                           ((int) t.temporary_light << 27));
        }
    }
    return h;
}

static uint32_t hashItem(uint32_t h, Inventory_t const &item) {
    h = mix(h, item.id);
    h = mix(h, item.special_name_id);
    h = mix(h, (int32_t) item.flags);
    h = mix(h, item.category_id);
    h = mix(h, item.sprite);
    h = mix(h, item.misc_use);
    h = mix(h, item.cost);
    h = mix(h, item.sub_category_id);
    h = mix(h, item.items_count);
    h = mix(h, item.weight);
    h = mix(h, item.to_hit);
    h = mix(h, item.to_damage);
    h = mix(h, item.ac);
    h = mix(h, item.to_ac);
    h = mix(h, item.damage.dice);
    h = mix(h, item.damage.sides);
    h = mix(h, item.depth_first_found);
    h = mix(h, item.identification);
    return h;
}

static uint32_t hashInventory() {
    uint32_t h = 2166136261u;
    for (auto const &item : py.inventory) {
        h = hashItem(h, item);
    }
    return h;
}

static uint32_t hashTreasure() {
    uint32_t h = 2166136261u;
    for (int i = config::treasure::MIN_TREASURE_LIST_ID; i < game.treasure.current_id; i++) {
        h = hashItem(h, game.treasure.list[i]);
    }
    return h;
}

static uint32_t hashMonsters() {
    uint32_t h = 2166136261u;
    for (int i = config::monsters::MON_MIN_INDEX_ID; i < next_free_monster_id; i++) {
        Monster_t const &m = monsters[i];
        h = mix(h, m.hp);
        h = mix(h, m.sleep_count);
        h = mix(h, m.speed);
        h = mix(h, m.creature_id);
        h = mix(h, m.pos.y);
        h = mix(h, m.pos.x);
        h = mix(h, m.distance_from_player);
        h = mix(h, (int) m.lit);
        h = mix(h, m.stunned_amount);
        h = mix(h, m.confused_amount);
    }
    return h;
}

// ---- the dump ---------------------------------------------------------------

static FILE *out = nullptr;
static int key_number = 0;
static std::vector<std::string> last_screen;

static void dumpState(const char *label) {
    int y, x;
    shimCursor(y, x);

    std::fprintf(out,
                 "%s %d rng=%u turn=%d lvl=%d pos=%d,%d hp=%d/%d au=%d exp=%d cc=%d free=%d dead=%d gen=%d eof=%d floor=%08x tr=%08x mon=%08x/%d inv=%08x cursor=%d,%d\n",
                 label, key_number, (unsigned) getRandomSeed(), dg.game_turn, dg.current_level, py.pos.y, py.pos.x, py.misc.current_hp, py.misc.max_hp, (int) py.misc.au,
                 (int) py.misc.exp, (int) game.command_count, (int) game.player_free_turn, (int) game.character_is_dead, (int) dg.generate_new_level, eof_flag,
                 (unsigned) hashFloor(), (unsigned) hashTreasure(), (unsigned) hashMonsters(), next_free_monster_id, (unsigned) hashInventory(), y, x);

    std::vector<std::string> screen = shimSnapshot();
    if (screen == last_screen) {
        std::fprintf(out, "same\n");
    } else {
        for (auto line : screen) {
            while (!line.empty() && line.back() == ' ') {
                line.pop_back();
            }
            std::fprintf(out, "|%s\n", line.c_str());
        }
        last_screen = screen;
    }
}

static void beforeKeyRead() {
    key_number++;
    dumpState("key");
}

// ---- one run ----------------------------------------------------------------

static int runOnce(int scenario_id, int run_id, const char *work_dir, const char *output) {
    if (chdir(work_dir) != 0) {
        std::fprintf(stderr, "could not change to work directory: %s\n", work_dir);
        return 1;
    }

    Scenario const &s = scenarios[scenario_id];

    if (run_id == 0) {
        // a fresh game directory: no save file, an empty score file
        (void) std::remove(config::files::save_game.c_str());
        FILE *scores = std::fopen(config::files::scores.c_str(), "wb");
        if (scores != nullptr) {
            std::fclose(scores);
        }
    }

    out = std::fopen(output, "ab");
    if (out == nullptr) {
        std::fprintf(stderr, "could not open output file: %s\n", output);
        return 1;
    }

    std::fprintf(out, "scenario %s run %d\n", s.name, run_id);

    // main()'s set-up
    if (!initializeScoreFile()) {
        std::fprintf(stderr, "can't open score file\n");
        return 1;
    }
    if (!terminalInitialize()) {
        return 1;
    }

    shimSetKeys(decodeKeys(s.runs[run_id]));
    shimSetEofWhenDrained(true);
    shimSetBeforeKeyRead(beforeKeyRead);

    int code = -1;
    try {
        startMoria(s.seed, false, false);
    } catch (HarnessExit const &e) {
        code = e.code;
    }

    shimSetBeforeKeyRead(nullptr);
    dumpState("exit");
    std::fprintf(out, "exit %d keys_left=%d eofs=%d died_from=[%s]\n", code, (int) shimKeysLeft(), shimExtraEscapes(), game.character_died_from);
    std::fclose(out);

    return 0;
}

// ---- the driver -------------------------------------------------------------

static int spawnRun(const char *self, int scenario_id, int run_id, const char *work_dir, const char *output) {
    std::string scenario = std::to_string(scenario_id);
    std::string run = std::to_string(run_id);

#ifdef _WIN32
    // _spawnv() joins its arguments with spaces: quote the paths
    std::string q_self = std::string("\"") + self + "\"";
    std::string q_work = std::string("\"") + work_dir + "\"";
    std::string q_out = std::string("\"") + output + "\"";
    const char *args[] = {q_self.c_str(), "--run", scenario.c_str(), run.c_str(), q_work.c_str(), q_out.c_str(), nullptr};
    return (int) _spawnv(_P_WAIT, self, args);
#else
    pid_t pid = fork();
    if (pid == 0) {
        execl(self, self, "--run", scenario.c_str(), run.c_str(), work_dir, output, (char *) nullptr);
        _exit(127);
    }
    int status = 0;
    (void) waitpid(pid, &status, 0);
    return WIFEXITED(status) ? WEXITSTATUS(status) : 1;
#endif
}

int main(int argc, char *argv[]) {
    if (argc == 6 && std::strcmp(argv[1], "--run") == 0) {
        return runOnce(std::atoi(argv[2]), std::atoi(argv[3]), argv[4], argv[5]);
    }

    if (argc != 3) {
        std::fprintf(stderr, "usage: game_loop_dump <work_dir> <output_file>   (absolute paths)\n");
        return 1;
    }

    FILE *truncate = std::fopen(argv[2], "wb");
    if (truncate == nullptr) {
        std::fprintf(stderr, "could not open output file: %s\n", argv[2]);
        return 1;
    }
    std::fclose(truncate);

    for (size_t i = 0; i < scenarios.size(); i++) {
        std::fprintf(stderr, "scenario %s\n", scenarios[i].name);
        for (size_t r = 0; r < scenarios[i].runs.size(); r++) {
            int code = spawnRun(argv[0], (int) i, (int) r, argv[1], argv[2]);
            if (code != 0) {
                std::fprintf(stderr, "scenario %s run %d failed (%d)\n", scenarios[i].name, (int) r, code);
                return 2;
            }
        }
    }

    return 0;
}
