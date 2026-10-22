// Item description dump harness: verifies the .NET migration's port of
// magicInitializeItemNames() and itemDescription() (M11 --
// Umoria.Core.Items.ObjectIdentification.MagicInitializeItemNames and
// ItemDescription.Describe) against the real compiled C++ game.
//
// Same shape as item_magic_dump (M3): no dungeon, no UI -- both functions
// only read an item and the identification tables, so this calls them
// directly. For each magic seed it shuffles the flavour names (cumulatively,
// as the shuffle works on the global tables in place; the C# side keeps one
// ObjectIdentification across seeds to match), dumps the shuffled tables and
// the made-up scroll titles, then describes every row of the 420-object
// catalog in a fixed series of identification states (see describeAll()).
// magicTreasureMagicalAbility() supplies real special names, plusses,
// charges and curses for the later variants.
//
// Output: one "<seed>.<key>\t<text>" line per value.
//
// Usage: item_description_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstring>

// defined in identification.cpp but not declared in any header
extern char magic_item_titles[MAX_TITLES][10];

namespace {

FILE *out = stdout;

void line(uint32_t seed, const char *key, const char *text) {
    std::fprintf(out, "%u.%s\t%s\n", seed, key, text);
}

template <int N>
void dumpTable(uint32_t seed, const char *name, const char *(&table)[N]) {
    for (int i = 0; i < N; i++) {
        char key[64];
        std::snprintf(key, sizeof(key), "%s.%d", name, i);
        line(seed, key, table[i]);
    }
}

void describe(uint32_t seed, int id, const char *variant, Inventory_t const &item, bool add_prefix) {
    obj_desc_t description = {'\0'};
    itemDescription(description, item, add_prefix);

    char key[64];
    std::snprintf(key, sizeof(key), "%d.%s", id, variant);
    line(seed, key, description);
}

// Mirrored step by step in ItemDescriptionTraceTests.DescribeAll.
void describeAll(uint32_t seed) {
    for (int id = 0; id < MAX_OBJECTS_IN_GAME; id++) {
        for (auto &flag : objects_identified) {
            flag = 0;
        }

        Inventory_t &item = game.treasure.list[1];
        inventoryItemCopyTo(id, item);

        describe(seed, id, "plain", item, true);
        describe(seed, id, "plain_np", item, false);

        uint8_t count = item.items_count;
        item.items_count = 0;
        describe(seed, id, "none", item, true);
        describe(seed, id, "none_np", item, false);
        item.items_count = 5;
        describe(seed, id, "five", item, true);
        describe(seed, id, "five_np", item, false);
        item.items_count = count;

        itemSetAsTried(item);
        describe(seed, id, "tried", item, true);

        Inventory_t bought = item;
        itemIdentifyAsStoreBought(bought);
        describe(seed, id, "bought", bought, true);

        magicTreasureMagicalAbility(1, 40);
        describe(seed, id, "magic", item, true);

        spellItemIdentifyAndRemoveRandomInscription(item);
        describe(seed, id, "known2", item, true);

        itemSetAsIdentified(item.category_id, item.sub_category_id);
        describe(seed, id, "known", item, true);
        describe(seed, id, "known_np", item, false);

        item.items_count = 0;
        describe(seed, id, "known_none", item, true);
        item.items_count = count;

        (void) std::strcpy(item.inscription, "hello");
        item.identification |= config::identification::ID_MAGIK | config::identification::ID_EMPTY | config::identification::ID_DAMD;
        describe(seed, id, "inscribed", item, true);

        item.inscription[0] = '\0';
        describe(seed, id, "flagged", item, true);

        item.identification = config::identification::ID_KNOWN2 | config::identification::ID_SHOW_HIT_DAM;
        describe(seed, id, "showhitdam", item, true);
        item.identification = config::identification::ID_KNOWN2 | config::identification::ID_NO_SHOW_P1;
        describe(seed, id, "noshowp1", item, true);
        item.identification = config::identification::ID_KNOWN2 | config::identification::ID_SHOW_P1;
        describe(seed, id, "showp1", item, true);
        item.identification = config::identification::ID_SHOW_P1;
        describe(seed, id, "showp1_unknown", item, true);
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

    constexpr uint32_t seeds[] = {12345, 31337, 777};

    for (uint32_t seed : seeds) {
        game.magic_seed = seed;
        setRandomSeed(seed + 1);
        magicInitializeItemNames();

        char key[64];
        std::snprintf(key, sizeof(key), "rnd");
        char value[32];
        std::snprintf(value, sizeof(value), "%u", getRandomSeed());
        line(seed, key, value);

        dumpTable(seed, "colors", colors);
        dumpTable(seed, "mushrooms", mushrooms);
        dumpTable(seed, "woods", woods);
        dumpTable(seed, "metals", metals);
        dumpTable(seed, "rocks", rocks);
        dumpTable(seed, "amulets", amulets);
        for (int i = 0; i < MAX_TITLES; i++) {
            std::snprintf(key, sizeof(key), "titles.%d", i);
            line(seed, key, magic_item_titles[i]);
        }

        missiles_counter = 0;
        describeAll(seed);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
