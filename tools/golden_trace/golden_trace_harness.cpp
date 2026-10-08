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

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
