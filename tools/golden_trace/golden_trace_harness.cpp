// Golden-trace harness: a headless CLI that exercises generateCave()'s
// dungeon-level path (see src/dungeon_generate.cpp) and dumps its
// deterministic output in a simple text format, for byte-for-byte
// comparison against the equivalent .NET port (dotnet/src/Umoria.Core).
//
// Through M3, this called a test-only entry point
// (generateCaveLayoutOnlyForTesting) that stopped short of monster/object
// placement, since the .NET port didn't implement those yet. M4 ports
// object placement, so this now calls the real generateCave() directly --
// the only remaining gap is the vault-containing unusual room types, still
// unreachable at level 1 (current_level > randomNumber(DUN_UNUSUAL_ROOMS=300)
// is never true there), so this stays a genuine apples-to-apples comparison.
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

    // Normally called once from startMoria(); this harness bypasses that
    // entirely, so call them here instead. Both are deterministic (built
    // from static tables, no RNG draws), so calling them on every
    // invocation doesn't affect golden-trace reproducibility.
    initializeMonsterLevels();
    initializeTreasureLevels();

    setRandomSeed(seed);

    dg.current_level = level;
    generateCave();

    Coord_t player_start = py.pos;

    std::fprintf(out, "seed %u\n", seed);
    std::fprintf(out, "level %d\n", level);
    std::fprintf(out, "rnd_seed %u\n", getRandomSeed());
    std::fprintf(out, "player_start %d %d\n", player_start.y, player_start.x);
    std::fprintf(out, "height %d\n", dg.height);
    std::fprintf(out, "width %d\n", dg.width);
    std::fprintf(out, "floor\n");

    // treasure_id -> position, built while dumping the floor grid (which
    // itself only encodes feature_id) so the treasure dump below can report
    // where each item actually is.
    int treasure_y[LEVEL_MAX_OBJECTS] = {0};
    int treasure_x[LEVEL_MAX_OBJECTS] = {0};

    for (int y = 0; y < dg.height; y++) {
        for (int x = 0; x < dg.width; x++) {
            // feature_id is 0-15 (TILE_BOUNDARY_WALL is the max), so one
            // hex digit per cell keeps each row a fixed, diffable width.
            std::fprintf(out, "%x", dg.floor[y][x].feature_id);

            uint8_t treasure_id = dg.floor[y][x].treasure_id;
            if (treasure_id != 0) {
                treasure_y[treasure_id] = y;
                treasure_x[treasure_id] = x;
            }
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

    // Treasure list placed by doors/stairs/streamer-gold (dungeon_generate.cpp)
    // and dungeonAllocateAndPlaceObject() (src/dungeon.cpp, ported in
    // Umoria.Core.Dungeon.ObjectPlacement/TreasureList for M4). Slots run
    // from MIN_TREASURE_LIST_ID (0 means "no treasure") up to (not
    // including) game.treasure.current_id.
    std::fprintf(out, "treasures %d\n", game.treasure.current_id - config::treasure::MIN_TREASURE_LIST_ID);
    for (int i = config::treasure::MIN_TREASURE_LIST_ID; i < game.treasure.current_id; i++) {
        Inventory_t const &item = game.treasure.list[i];
        std::fprintf(
            out, "treasure %d %d %d %u %u %u %u %u %d %d %u %u %u %d %d %d %d %u %u %u %u\n", i, treasure_y[i],
            treasure_x[i], (unsigned) item.id, (unsigned) item.special_name_id, (unsigned) item.flags,
            (unsigned) item.category_id, (unsigned) item.sprite, (int) item.misc_use, (int) item.cost,
            (unsigned) item.sub_category_id, (unsigned) item.items_count, (unsigned) item.weight,
            (int) item.to_hit, (int) item.to_damage, (int) item.ac, (int) item.to_ac, (unsigned) item.damage.dice,
            (unsigned) item.damage.sides, (unsigned) item.depth_first_found, (unsigned) item.identification);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
