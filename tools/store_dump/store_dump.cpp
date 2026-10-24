// Store dump harness: verifies the .NET migration's port of the stores
// (M13 -- Umoria.Core.Stores.StoreInventory / StoreCommands) against the
// real compiled C++ game. Three sections:
//
//   maint     storeInitializeOwners() and twelve days of storeMaintenance()
//             from four seeds: the owners, then every store's full stock
//             (cost, item fields, description) and the RNG state after
//             each day. Restocking is RNG-sequenced (it draws the stock
//             choice and runs magicTreasureMagicalAbility() at the town
//             level), so this is the golden trace the roadmap asked for.
//   price     storeItemSellPrice() for every catalog object at each of the
//             18 owners (race = owner % 8): the asking price and its
//             min/max inflation.
//   scenario  scripted visits to the real storeEnter(): buying (with and
//             without haggling, short of gold, a full pack, fixed prices),
//             selling (refused kinds, cursed/damaged items, no room, the
//             owner out of money), insults and being thrown out, locked
//             doors, the good-bargainer shortcut, browsing, the inventory
//             keys, charisma's effect on prices, stepping onto a store
//             door. After each token: a state line, every store whose
//             state changed, every message, and every line the store
//             screen wrote. The scenario list is store_scenarios.inc,
//             shared with the C# test.
//
// Same build pattern as item_use_dump (M11): the real sources minus
// main.cpp and ui_io.cpp (the M6 headless stub) -- zero changes to src/.
// The stub gained a list of scripted getStringInput() lines (a haggle reads
// several offers) and an opt-in putString() log.
//
// Script tokens (space separated):
//   S<store>:<keys>[/<line>...]   storeEnter(store); <keys> are getKeyInput()
//                                 keys (~ = <ESCAPE>, %N. = the letter of
//                                 the first pack slot holding catalog
//                                 object N); each /<line> is one
//                                 getStringInput() answer (~ alone =
//                                 <ESCAPE>, _ = a space, empty = <RETURN>)
//   m<dir>:<keys>[/<line>...]     playerMove(dir, true), with the same input
//   M            storeMaintenance()
//   G<n>         py.misc.au = n
//   C<n>         charisma = n (max/current, then playerSetAndUseStat())
//   T<n>         dg.game_turn = n
//   B<s>:<g>:<b> store s's good/bad purchase record
//   N<s>:<n>     store s's insults counter
//   P<id>:<count>[:<misc>]  carry a catalog object
//   Q<id>:<count>           carry one identified as store bought
//   H<id>:<hit>:<dam>:<ac>  set the plusses of the first pack slot holding id
//   D<id>        spell-identify the first pack slot holding catalog object id
//   K<n>         inventoryDestroyItem(n), if n is in the pack
//   A<s>:<id>[:<count>]     storeCarryItem() a store-bought catalog object
//   X<id>:<slot> wear a catalog object in a slot
//   O<dy>,<dx>:<id>         place a catalog object near the player
//   E<s>         empty store s (storeDestroyItem() of its first slot until
//                none is left)
//   V0 / V1      stop / start dumping the screen lines (around the
//                inventory keys, whose screens are M14's and not drawn by
//                the port)
//
// The screen lines of a visit stop where drawCavePanel() starts (its first
// write, the race name, is at row 2, column 0, where the store screen never
// writes): leaving the store redraws the dungeon view and status block,
// which is display only and not the store's.
//
// Usage: store_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

static FILE *out = stdout;

// ---- hashes (M10's) -------------------------------------------------------

static uint32_t mix(uint32_t h, int32_t v) {
    h ^= (uint32_t) v;
    h *= 16777619u;
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

static uint32_t hashIdentified() {
    uint32_t h = 2166136261u;
    for (auto flag : objects_identified) {
        h = mix(h, flag);
    }
    return h;
}

static uint32_t hashStore(Store_t const &store) {
    uint32_t h = 2166136261u;
    h = mix(h, store.turns_left_before_closing);
    h = mix(h, store.insults_counter);
    h = mix(h, store.owner_id);
    h = mix(h, store.unique_items_counter);
    h = mix(h, store.good_purchases);
    h = mix(h, store.bad_purchases);
    for (auto const &record : store.inventory) {
        h = mix(h, record.cost);
        h = hashItem(h, record.item);
    }
    return h;
}

// ---- store dump ---------------------------------------------------------

static void dumpStore(int store_id) {
    Store_t const &store = stores[store_id];
    std::fprintf(out, "store %d own=%d ins=%d close=%d n=%d good=%d bad=%d hash=%08x\n", store_id, store.owner_id, store.insults_counter,
                 store.turns_left_before_closing, store.unique_items_counter, store.good_purchases, store.bad_purchases, hashStore(store));
    for (int i = 0; i < store.unique_items_counter; i++) {
        Inventory_t const &item = store.inventory[i].item;
        obj_desc_t description = {'\0'};
        itemDescription(description, item, true);
        std::fprintf(out, "item %d.%d cost=%d id=%d n=%d ident=%d misc=%d plus=%d,%d,%d flags=%08x sn=%d | %s\n", store_id, i, store.inventory[i].cost, item.id,
                     item.items_count, item.identification, item.misc_use, item.to_hit, item.to_damage, item.to_ac, item.flags, item.special_name_id, description);
    }
}

// ---- flavour names (M11's, copied) ----------------------------------------

template <int N>
struct SavedNames {
    const char *names[N];
};

static SavedNames<MAX_COLORS> saved_colors;
static SavedNames<MAX_MUSHROOMS> saved_mushrooms;
static SavedNames<MAX_WOODS> saved_woods;
static SavedNames<MAX_METALS> saved_metals;
static SavedNames<MAX_ROCKS> saved_rocks;
static SavedNames<MAX_AMULETS> saved_amulets;

template <int N>
static void save(SavedNames<N> &saved, const char *(&table)[N]) {
    std::memcpy(saved.names, table, sizeof(saved.names));
}

template <int N>
static void restore(SavedNames<N> const &saved, const char *(&table)[N]) {
    std::memcpy(table, saved.names, sizeof(saved.names));
}

static void shuffleNames(uint32_t magic_seed) {
    restore(saved_colors, colors);
    restore(saved_mushrooms, mushrooms);
    restore(saved_woods, woods);
    restore(saved_metals, metals);
    restore(saved_rocks, rocks);
    restore(saved_amulets, amulets);

    game.magic_seed = (int32_t) magic_seed;
    magicInitializeItemNames();
}

static void resetTreasure() {
    for (auto &item : game.treasure.list) {
        inventoryItemCopyTo(config::dungeon::objects::OBJ_NOTHING, item);
    }
    game.treasure.current_id = config::treasure::MIN_TREASURE_LIST_ID;
}

// ---- maint ----------------------------------------------------------------

static void dumpMaintenance() {
    const uint32_t seeds[] = {1, 4242, 31337, 987654321};

    for (auto seed : seeds) {
        game = Game_t{};
        resetTreasure();
        std::memset(objects_identified, 0, sizeof(objects_identified));
        missiles_counter = 0;
        py.misc.race_id = (uint8_t) (seed % PLAYER_MAX_RACES);
        shuffleNames(seed);

        setRandomSeed(seed);
        storeInitializeOwners();

        std::fprintf(out, "maint %u owners rnd=%u\n", seed, getRandomSeed());
        for (int s = 0; s < MAX_STORES; s++) {
            dumpStore(s);
        }

        for (int day = 1; day <= 12; day++) {
            storeMaintenance();
            std::fprintf(out, "maint %u day %d rnd=%u tid=%d\n", seed, day, getRandomSeed(), game.treasure.current_id);
            for (int s = 0; s < MAX_STORES; s++) {
                dumpStore(s);
            }
        }
    }
}

// ---- price ----------------------------------------------------------------

static void dumpPrices() {
    std::memset(objects_identified, 0, sizeof(objects_identified));

    for (int owner = 0; owner < MAX_OWNERS; owner++) {
        py.misc.race_id = (uint8_t) (owner % PLAYER_MAX_RACES);
        Store_t store{};
        store.owner_id = (uint8_t) owner;

        std::fprintf(out, "price owner %d race %d\n", owner, py.misc.race_id);
        for (int id = 0; id < MAX_OBJECTS_IN_GAME; id++) {
            Inventory_t item{};
            inventoryItemCopyTo(id, item);
            int32_t min_price = 0;
            int32_t max_price = 0;
            int32_t price = storeItemSellPrice(store, min_price, max_price, item);
            std::fprintf(out, "price %d.%d %d %d %d\n", owner, id, price, min_price, max_price);
        }
    }
}

// ---- scenario: arena (M9's, copied from item_use_dump) -------------------

static void carve(int y1, int x1, int y2, int x2, uint8_t feature) {
    for (int y = y1; y <= y2; y++) {
        for (int x = x1; x <= x2; x++) {
            dg.floor[y][x].feature_id = feature;
        }
    }
}

static void carveRoom(int y1, int x1, int y2, int x2, bool lit) {
    for (int y = y1; y <= y2; y++) {
        for (int x = x1; x <= x2; x++) {
            Tile_t &tile = dg.floor[y][x];
            bool ring = y == y1 || y == y2 || x == x1 || x == x2;
            tile.perma_lit_room = lit;
            if (ring) {
                tile.feature_id = TILE_GRANITE_WALL;
            } else {
                tile.feature_id = lit ? TILE_LIGHT_FLOOR : TILE_DARK_FLOOR;
            }
        }
    }
}

static int placeObject(Coord_t coord, int object_id) {
    int id = popt();
    dg.floor[coord.y][coord.x].treasure_id = (uint8_t) id;
    inventoryItemCopyTo(object_id, game.treasure.list[id]);
    return id;
}

static void placeDoor(Coord_t coord, int object_id, int16_t misc_use) {
    int id = placeObject(coord, object_id);
    game.treasure.list[id].misc_use = misc_use;
    dg.floor[coord.y][coord.x].feature_id = object_id == config::dungeon::objects::OBJ_OPEN_DOOR ? TILE_CORR_FLOOR : TILE_BLOCKED_FLOOR;
}

static void buildArena(int16_t depth) {
    dg.height = MAX_HEIGHT;
    dg.width = MAX_WIDTH;
    dg.current_level = depth;
    dg.game_turn = 0;
    dg.generate_new_level = false;

    for (int y = 0; y < MAX_HEIGHT; y++) {
        for (int x = 0; x < MAX_WIDTH; x++) {
            dg.floor[y][x] = Tile_t{};
            bool edge = y == 0 || y == MAX_HEIGHT - 1 || x == 0 || x == MAX_WIDTH - 1;
            dg.floor[y][x].feature_id = edge ? TILE_BOUNDARY_WALL : TILE_GRANITE_WALL;
        }
    }

    game = Game_t{};
    resetTreasure();
    for (auto &monster : monsters) {
        monster = blank_monster;
    }
    next_free_monster_id = config::monsters::MON_MIN_INDEX_ID;
    monster_multiply_total = 0;
    hack_monptr = -1;
    for (auto &memory : creature_recall) {
        memory = Recall_t{};
    }

    // Room A (lit), a granite pillar and an enclosed 3x3 granite block
    carveRoom(1, 1, 21, 61, true);
    carve(8, 40, 8, 40, TILE_GRANITE_WALL);
    carve(14, 45, 16, 47, TILE_GRANITE_WALL);

    // door corridor east to room B: closed, locked, stuck and secret doors
    carve(11, 61, 11, 100, TILE_CORR_FLOOR);
    placeDoor(Coord_t{11, 65}, config::dungeon::objects::OBJ_CLOSED_DOOR, 0);
    placeDoor(Coord_t{11, 70}, config::dungeon::objects::OBJ_CLOSED_DOOR, 10);
    placeDoor(Coord_t{11, 75}, config::dungeon::objects::OBJ_CLOSED_DOOR, -5);
    placeDoor(Coord_t{11, 80}, config::dungeon::objects::OBJ_SECRET_DOOR, 0);

    // Room B (dark)
    carveRoom(1, 100, 21, 170, false);
    carve(11, 100, 11, 100, TILE_CORR_FLOOR);
}

// ---- scenario: dump ---------------------------------------------------------

static std::vector<std::string> step_messages;
static std::vector<std::string> step_screen;
static std::vector<std::string> step_lines;
static uint32_t store_hashes[MAX_STORES];
static size_t keys_used = 0;
static size_t keys_given = 0;
static size_t strings_used = 0;
static bool screen_on = true;

// Drops drawCavePanel()'s lines (see the header).
static void cutCavePanel() {
    for (size_t i = 0; i < step_screen.size(); i++) {
        if (step_screen[i].rfind("2,0=", 0) == 0) {
            step_screen.resize(i);
            return;
        }
    }
}

static void dumpStep(int step, const char *cmd) {
    std::fprintf(out,
                 "step %d %s rnd=%u turn=%d au=%d free=%d cmd=%d keys=%d/%d strs=%d/%d chr=%d/%d pos=%d,%d pack=%d/%d eq=%d heavy=%d tid=%d inv=%08x "
                 "ident=%08x st=%08x\n",
                 step, cmd, getRandomSeed(), dg.game_turn, py.misc.au, (int) game.player_free_turn, game.command_count, (int) keys_used, (int) keys_given,
                 (int) strings_used, (int) step_lines.size(), py.stats.used[PlayerAttr::A_CHR], playerStatAdjustmentCharisma(), py.pos.y, py.pos.x,
                 py.pack.unique_items, py.pack.weight, py.equipment_count, (int) py.weapon_is_heavy, game.treasure.current_id, hashInventory(), hashIdentified(),
                 py.flags.status);

    for (int s = 0; s < MAX_STORES; s++) {
        uint32_t h = hashStore(stores[s]);
        if (h != store_hashes[s]) {
            store_hashes[s] = h;
            dumpStore(s);
        }
    }

    for (auto const &message : step_messages) {
        std::fprintf(out, "msg %s\n", message.c_str());
    }
    step_messages.clear();

    if (screen_on) {
        for (auto const &line : step_screen) {
            std::fprintf(out, "scr %s\n", line.c_str());
        }
    }
    step_screen.clear();
}

// ---- scenario: script runner ------------------------------------------------

static int firstPackSlotOf(int id, const std::string &token) {
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].id == id) {
            return i;
        }
    }
    std::fprintf(stderr, "token %s: object %d not in the pack\n", token.c_str(), id);
    std::exit(8);
}

// Turns a token's key text into the stub's key script (see the header).
static std::string resolveKeys(const std::string &token, size_t start, size_t end) {
    std::string keys;
    for (size_t k = start; k < end; k++) {
        char c = token[k];
        if (c == '~') {
            keys += (char) ESCAPE;
        } else if (c == '%') {
            size_t dot = token.find('.', k);
            keys += (char) ('a' + firstPackSlotOf(std::atoi(token.substr(k + 1, dot - k - 1).c_str()), token));
            k = dot;
        } else {
            keys += c;
        }
    }
    return keys;
}

// The /<line> answers after the keys.
static void resolveLines(const std::string &token, size_t slash) {
    step_lines.clear();
    while (slash != std::string::npos) {
        size_t next = token.find('/', slash + 1);
        std::string line = token.substr(slash + 1, next == std::string::npos ? std::string::npos : next - slash - 1);
        if (line == "~") {
            line = std::string(1, (char) ESCAPE);
        } else {
            for (auto &c : line) {
                if (c == '_') {
                    c = ' ';
                }
            }
        }
        step_lines.push_back(line);
        slash = next;
    }
}

// Sets up both input streams for a store-visiting token: keys from
// `start` up to the first '/', then the lines.
static void withInput(const std::string &token, size_t start) {
    size_t slash = token.find('/', start);
    std::string keys = resolveKeys(token, start, slash == std::string::npos ? token.size() : slash);
    resolveLines(token, slash);
    uiStubSetScript(keys, "");
    uiStubSetStringScript(&step_lines);
    keys_given = keys.size();
}

static void runToken(const std::string &token) {
    char op = token[0];
    int a = 0;
    int b = 0;
    int c = -1;
    int d = 0;

    game.player_free_turn = false;
    keys_used = 0;
    keys_given = 0;
    strings_used = 0;
    step_lines.clear();
    uiStubSetStringScript(&step_lines);

    switch (op) {
        case 'S': {
            size_t colon = token.find(':');
            a = std::atoi(token.substr(1, colon - 1).c_str());
            withInput(token, colon + 1);
            storeEnter(a);
            keys_used = uiStubKeysConsumed();
            strings_used = uiStubStringsConsumed();
            cutCavePanel();
            break;
        }
        case 'm': {
            size_t colon = token.find(':');
            a = std::atoi(token.substr(1, colon - 1).c_str());
            withInput(token, colon + 1);
            playerMove(a, true);
            keys_used = uiStubKeysConsumed();
            strings_used = uiStubStringsConsumed();
            cutCavePanel();
            break;
        }
        case 'M':
            storeMaintenance();
            break;
        case 'G':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.misc.au = a;
            break;
        case 'C':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            py.stats.max[PlayerAttr::A_CHR] = (uint8_t) a;
            py.stats.current[PlayerAttr::A_CHR] = (uint8_t) a;
            playerSetAndUseStat(PlayerAttr::A_CHR);
            break;
        case 'T':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            dg.game_turn = a;
            break;
        case 'B':
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);
            stores[a].good_purchases = (uint16_t) b;
            stores[a].bad_purchases = (uint16_t) c;
            break;
        case 'N':
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &a, &b);
            stores[a].insults_counter = (int16_t) b;
            break;
        case 'P': {
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);
            Inventory_t item{};
            inventoryItemCopyTo(a, item);
            item.items_count = (uint8_t) b;
            if (c >= 0) {
                item.misc_use = (int16_t) c;
            }
            (void) inventoryCarryItem(item);
            break;
        }
        case 'Q': {
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &a, &b);
            Inventory_t item{};
            inventoryItemCopyTo(a, item);
            item.items_count = (uint8_t) b;
            itemIdentifyAsStoreBought(item);
            (void) inventoryCarryItem(item);
            break;
        }
        case 'H': {
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d:%d", &a, &b, &c, &d);
            Inventory_t &item = py.inventory[firstPackSlotOf(a, token)];
            item.to_hit = (int16_t) b;
            item.to_damage = (int16_t) c;
            item.to_ac = (int16_t) d;
            break;
        }
        case 'D':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            spellItemIdentifyAndRemoveRandomInscription(py.inventory[firstPackSlotOf(a, token)]);
            break;
        case 'K':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            if (a < py.pack.unique_items) {
                inventoryDestroyItem(a);
            }
            break;
        case 'A': {
            b = 0;
            c = 1;
            (void) std::sscanf(token.c_str() + 1, "%d:%d:%d", &a, &b, &c);
            Inventory_t item{};
            inventoryItemCopyTo(b, item);
            item.items_count = (uint8_t) c;
            itemIdentifyAsStoreBought(item);
            int index_id;
            storeCarryItem(a, index_id, item);
            break;
        }
        case 'X':
            (void) std::sscanf(token.c_str() + 1, "%d:%d", &a, &b);
            if (py.inventory[b].category_id == TV_NOTHING) {
                py.equipment_count++;
            }
            inventoryItemCopyTo(a, py.inventory[b]);
            playerRecalculateBonuses();
            break;
        case 'O':
            (void) std::sscanf(token.c_str() + 1, "%d,%d:%d", &a, &b, &c);
            (void) placeObject(Coord_t{py.pos.y + a, py.pos.x + b}, c);
            break;
        case 'E':
            (void) std::sscanf(token.c_str() + 1, "%d", &a);
            while (stores[a].unique_items_counter > 0) {
                storeDestroyItem(a, 0, false);
            }
            break;
        case 'V':
            screen_on = token[1] == '1';
            break;
        default:
            std::fprintf(stderr, "bad script token: %s\n", token.c_str());
            std::exit(5);
    }

    uiStubSetStringScript(nullptr);
}

typedef struct {
    const char *name;
    uint32_t seed;
    int race_id;
    bool is_male;
    int class_choice;
    int32_t gold;
    const char *script;
} Scenario;

static void runScenario(Scenario const &s) {
    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "scenario %s: character creation script not consumed\n", s.name);
        std::exit(2);
    }

    // startMoria(): magicInitializeItemNames() right after creation
    shuffleNames(s.seed ^ 0x5a5a5a5au);
    missiles_counter = 0;

    // the town: owners, then generateCave()'s storeMaintenance()
    buildArena(0);
    py.pos = Coord_t{11, 20};
    dg.floor[11][20].creature_id = 1;
    dg.game_turn = 100;
    storeInitializeOwners();
    storeMaintenance();
    py.misc.au = s.gold;

    step_messages.clear();
    step_screen.clear();
    step_lines.clear();
    for (auto &h : store_hashes) {
        h = 0;
    }
    keys_used = 0;
    keys_given = 0;
    strings_used = 0;
    screen_on = true;

    std::fprintf(out, "scenario %s\n", s.name);
    int step = 0;
    dumpStep(step, "start");

    std::string script(s.script);
    size_t pos = 0;
    while (pos < script.size()) {
        size_t end = script.find(' ', pos);
        if (end == std::string::npos) {
            end = script.size();
        }
        std::string token = script.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }
        runToken(token);
        dumpStep(++step, token.c_str());
    }

    std::fprintf(out, "final %s\n", s.name);
    dumpPlayer();
}

int main(int argc, char *argv[]) {
    if (argc >= 2) {
        out = std::fopen(argv[1], "w");
        if (out == nullptr) {
            std::fprintf(stderr, "could not open output file: %s\n", argv[1]);
            return 1;
        }
    }
    playerDumpSetOutput(out);

    save(saved_colors, colors);
    save(saved_mushrooms, mushrooms);
    save(saved_woods, woods);
    save(saved_metals, metals);
    save(saved_rocks, rocks);
    save(saved_amulets, amulets);

    initializeMonsterLevels();
    initializeTreasureLevels();

    dumpMaintenance();
    dumpPrices();

    uiStubSetMessageLog(&step_messages);
    uiStubSetScreenLog(&step_screen);
    uiStubSetScreenLogPutString(true);

    // Human=0; class_choice is the class id (0 Warrior, 1 Mage, 2 Priest,
    // 3 Rogue, 4 Ranger, 5 Paladin).
    const Scenario scenarios[] = {
#include "store_scenarios.inc"
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
