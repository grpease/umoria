// Golden-trace harness: a headless CLI that exercises
// generateCaveLayoutOnlyForTesting() (see src/dungeon_generate.cpp) and
// dumps its deterministic output in a simple text format, for byte-for-byte
// comparison against the equivalent .NET port (dotnet/src/Umoria.Core).
//
// Deliberately never calls terminalInitialize()/initscr() -- the function
// under test doesn't touch curses, so this runs with stdout redirected to a
// file, no real console needed (unlike umoria.exe itself).
//
// Usage: golden_trace_harness <seed> <level> [output_file]
// With no output_file, writes to stdout.

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>

int main(int argc, char *argv[]) {
    if (argc < 3) {
        std::fprintf(stderr, "usage: %s <seed> <level> [output_file]\n", argv[0]);
        return 1;
    }

    auto seed = (uint32_t) std::strtoul(argv[1], nullptr, 10);
    int level = std::atoi(argv[2]);

    FILE *out = stdout;
    if (argc >= 4) {
        out = std::fopen(argv[3], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[3]);
            return 1;
        }
    }

    Coord_t player_start = generateCaveLayoutOnlyForTesting(level, seed);

    std::fprintf(out, "seed %u\n", seed);
    std::fprintf(out, "level %d\n", level);
    std::fprintf(out, "rnd_seed %u\n", getRandomSeed());
    std::fprintf(out, "player_start %d %d\n", player_start.y, player_start.x);
    std::fprintf(out, "height %d\n", dg.height);
    std::fprintf(out, "width %d\n", dg.width);
    std::fprintf(out, "floor\n");

    for (int y = 0; y < dg.height; y++) {
        for (int x = 0; x < dg.width; x++) {
            // feature_id is 0-15 (TILE_BOUNDARY_WALL is the max), so one
            // hex digit per cell keeps each row a fixed, diffable width.
            std::fprintf(out, "%x", dg.floor[y][x].feature_id);
        }
        std::fprintf(out, "\n");
    }

    // Monster list placed by monsterPlaceNewWithinDistance() (see
    // src/monster_manager.cpp, ported in Umoria.Core.Dungeon.MonsterManager).
    // Monster slots run from MON_MIN_INDEX_ID (index 0 = no monster, 1 =
    // reserved for the player) up to (not including) next_free_monster_id.
    std::fprintf(out, "monsters %d\n", next_free_monster_id - config::monsters::MON_MIN_INDEX_ID);
    for (int i = config::monsters::MON_MIN_INDEX_ID; i < next_free_monster_id; i++) {
        Monster_t const &m = monsters[i];
        std::fprintf(
            out, "monster %d %u %d %d %d %d %d %u %u %u %d\n", i, m.creature_id, m.hp, m.sleep_count, m.pos.y,
            m.pos.x, m.speed, m.distance_from_player, (unsigned) m.lit, m.stunned_amount, m.confused_amount);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
