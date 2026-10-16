// LOS matrix dump harness: exercises los() (see src/dungeon_los.cpp)
// directly across a battery of hand-built grids and dumps every
// in-bounds (from, to) pair's result, for byte-for-byte comparison against
// the equivalent .NET port (Umoria.Core.Dungeon.Los.Check).
//
// los()/look() were listed together in migration-plan.md's M5 scope on the
// assumption both were "pure deterministic functions of grid state." That
// held for los() but not look() (see migration-plan.md's M5 section for
// why), so only los() gets a harness here.
//
// Unlike golden_trace_harness, no dungeon is generated at all: each "grid"
// below is a small (GRID_SIZE x GRID_SIZE) region of dg.floor with walls
// placed by a fixed, deterministic rule (no RNG), then los() is called for
// every pair of coordinates in that region.
//
// Output format: one line per pair, "Los.<grid>.<fromY>_<fromX>_<toY>_<toX><TAB>value",
// where value is 1 if los() returned true, 0 otherwise.
//
// Usage: los_dump [output_file]
// With no output_file, writes to stdout.

#include "../../src/headers.h"

#include <cstdio>

namespace {

FILE *out = stdout;

constexpr int GRID_SIZE = 10;

// Clears the test region to open floor, then applies `wall_at` to decide
// which cells become granite walls (feature_id >= MIN_CLOSED_SPACE).
void buildGrid(bool (*wall_at)(int y, int x)) {
    for (int y = 0; y < GRID_SIZE; y++) {
        for (int x = 0; x < GRID_SIZE; x++) {
            dg.floor[y][x] = Tile_t{};
            dg.floor[y][x].feature_id = wall_at(y, x) ? TILE_GRANITE_WALL : TILE_DARK_FLOOR;
        }
    }
}

void dumpGrid(const char *name) {
    for (int from_y = 0; from_y < GRID_SIZE; from_y++) {
        for (int from_x = 0; from_x < GRID_SIZE; from_x++) {
            for (int to_y = 0; to_y < GRID_SIZE; to_y++) {
                for (int to_x = 0; to_x < GRID_SIZE; to_x++) {
                    bool result = los(Coord_t{from_y, from_x}, Coord_t{to_y, to_x});
                    std::fprintf(out, "Los.%s.%d_%d_%d_%d\t%d\n", name, from_y, from_x, to_y, to_x, result ? 1 : 0);
                }
            }
        }
    }
}

} // namespace

int main(int argc, char *argv[]) {
    if (argc >= 2) {
        out = std::fopen(argv[1], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[1]);
            return 1;
        }
    }

    // No walls at all -- every pair should be visible.
    buildGrid([](int, int) { return false; });
    dumpGrid("open");

    // Sparse walls (~9% of cells).
    buildGrid([](int y, int x) { return (y * 31 + x * 17) % 11 == 0; });
    dumpGrid("sparse");

    // Medium density (~20% of cells).
    buildGrid([](int y, int x) { return (y * 31 + x * 17) % 5 == 0; });
    dumpGrid("medium");

    // Dense walls (~33% of cells) -- stresses the stepping logic with
    // frequent early-outs.
    buildGrid([](int y, int x) { return (y * 31 + x * 17) % 3 == 0; });
    dumpGrid("dense");

    // A single vertical wall column and a single horizontal wall row,
    // isolated -- targets the delta_x == 0 / delta_y == 0 special cases.
    buildGrid([](int, int x) { return x == GRID_SIZE / 2; });
    dumpGrid("vertical_wall");

    buildGrid([](int y, int) { return y == GRID_SIZE / 2; });
    dumpGrid("horizontal_wall");

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
