// Item-magic matrix dump harness: a headless CLI that exercises
// magicTreasureMagicalAbility() (see src/treasure.cpp) directly across a
// fixed seed/level/item_id matrix and dumps the resulting Inventory_t
// fields, for byte-for-byte comparison against the equivalent .NET port
// (Umoria.Core.Dungeon.TreasureMagic.MagicalAbility).
//
// Unlike golden_trace_harness, this does not run a dungeon at all --
// magicTreasureMagicalAbility() only needs a single item_id/level pair, so
// this calls it directly for every row of the static game_objects[]
// catalog (MAX_OBJECTS_IN_GAME = 420), at a handful of fixed levels, once
// per seed. That exercises every TV_* branch the function has, since the
// real catalog already covers every category/sub_category combination the
// game ships with.
//
// Output format: one line per scalar leaf, matching data_table_dump.cpp's
// "table.row.field<TAB>value" convention, with the synthetic row key
// "<seed>_<level>_<catalog_id>" standing in for a single (seed, level,
// item) combination.
//
// Usage: item_magic_dump [output_file]
// With no output_file, writes to stdout.

#include "../../src/headers.h"

#include <cstdio>

namespace {

FILE *out = stdout;

void value(uint32_t v) { std::fprintf(out, "%u", v); }
void value(uint16_t v) { std::fprintf(out, "%u", v); }
void value(uint8_t v) { std::fprintf(out, "%u", v); }
void value(int32_t v) { std::fprintf(out, "%d", v); }
void value(int16_t v) { std::fprintf(out, "%d", v); }

template <typename T>
void field(const char *row, const char *name, T v) {
    std::fprintf(out, "ItemMagic.%s.%s\t", row, name);
    value(v);
    std::fprintf(out, "\n");
}

void dump_item(const char *row, Inventory_t const &item) {
    field(row, "special_name_id", item.special_name_id);
    field(row, "flags", item.flags);
    field(row, "category_id", item.category_id);
    field(row, "misc_use", item.misc_use);
    field(row, "cost", item.cost);
    field(row, "sub_category_id", item.sub_category_id);
    field(row, "items_count", item.items_count);
    field(row, "weight", item.weight);
    field(row, "to_hit", item.to_hit);
    field(row, "to_damage", item.to_damage);
    field(row, "to_ac", item.to_ac);
    field(row, "depth_first_found", item.depth_first_found);
    field(row, "identification", item.identification);
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

    constexpr uint32_t seeds[] = {1, 42, 12345};
    constexpr int levels[] = {1, 5, 10, 20, 40};

    for (uint32_t seed : seeds) {
        for (int level : levels) {
            setRandomSeed(seed);

            for (int catalog_id = 0; catalog_id < MAX_OBJECTS_IN_GAME; catalog_id++) {
                inventoryItemCopyTo(catalog_id, game.treasure.list[1]);
                magicTreasureMagicalAbility(1, level);

                char row[64];
                std::snprintf(row, sizeof(row), "%u_%d_%d", seed, level, catalog_id);
                dump_item(row, game.treasure.list[1]);
            }
        }
    }

    std::fprintf(out, "ItemMagic.missiles_counter.value\t%d\n", missiles_counter);

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
