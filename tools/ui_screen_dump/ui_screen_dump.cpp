// UI screen dump harness: verifies the .NET migration's M14 port of the
// terminal UI (Umoria.Core.Ui -- src/ui_io.cpp, src/ui.cpp and the display
// half of src/ui_inventory.cpp, plus the screens behind ^P, M and L) against
// the real compiled C++ game, screen by screen.
//
// Unlike every earlier harness this one keeps the REAL ui_io.cpp and swaps
// curses instead: shim/curses.h (first on the include path) is a 24x80
// character buffer with scripted keys. Everything above curses -- message
// line and -more-, prompts, the status block, the dungeon panel, the
// inventory and equipment lists, the item prompt's '*' menu, the character
// screen, the spell lists, the store screen -- is the game's own code.
// game_run.cpp is #included (and dropped from the target) for its static
// commandPreviousMessage() / commandLocateOnMap(). Zero changes to src/.
//
// Each scenario creates a character (char_create_common's scripted
// characterCreate(), whose keys go through the shim with the screen dump
// off), builds item_use_dump's arena (or generates a level with the real
// generateCave(), position GEN), applies the same setup bits, runs
// playDungeon()'s preamble, draws the screen (drawCavePanel()) and runs its
// script. The screen is dumped every time a key is read (what the player
// is looking at while asked), and after every token; a state line follows
// each token. The scenario list is ui_screen_scenarios.inc, shared with the
// C# test.
//
// Hallucination is left out: the port draws its glyphs from a cosmetic RNG
// (the M14 decision), so those screens differ by design.
//
// Script tokens (space separated; <keys>: ~ <ESCAPE>, _ space, $ <RETURN>,
// # backspace, ^x control-x, anything else itself):
//   D                 drawCavePanel()
//   !                 dump the screen
//   m<dir>            playerMove(dir, false)
//   M<text>[:<keys>]  printMessage(text), _ = space in the text
//   N[:<keys>]        printMessage(CNIL)
//   i<cmd>[:<keys>]   inventoryExecuteCommand(cmd)
//   I[:<keys>]        inventoryExecuteCommand(game.doing_inventory_command)
//   C[:<keys>]        changeCharacterName()
//   H<n>[:<keys>]     ^P: game.command_count = n, commandPreviousMessage(),
//                     then game.last_command = ^P
//   L[:<keys>]        commandLocateOnMap()
//   V[:<keys>]        dungeonDisplayMap()
//   G<lo>,<hi>[:<keys>]  inventoryGetInputForItemId("Which one? ", lo, hi)
//   c[:<keys>]        getAndCastMagicSpell()
//   p[:<keys>]        pray()
//   s[:<keys>]        playerGainSpells()
//   S<n>[:<keys>]     storeEnter(n)
//   O                 dg.game_turn = 10, storeInitializeOwners() and ten
//                     storeMaintenance()s
//   Y[:<keys>]        getInputConfirmation("Are you sure?")
//   T[:<keys>]        "Text:" + getStringInput(0, 6, 20)
//   E<n>              py.misc.exp = n; displayCharacterExperience()
//   U<n>[:<keys>]     n status-effect turns: status_effects_dump's tick()
//                     (game_turn++, the playerUpdate*() sequence, the
//                     status-flag upkeep)
//   P<id>,<count>     carry a catalog object
//   W<id>,<slot>      wear a catalog object in a slot
//   X<f><n>           status: h hungry, w weak, b blind n, c confused n,
//                     f afraid n, p poisoned n, s playerSearchOn(), r rest n,
//                     z paralysis n, v speed n, n new spells n,
//                     k command_count n, g gold n, d food n
//
// Usage: ui_screen_dump [output_file]

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "shim/curses.h"
#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

// game_run.cpp's static command screens
#include "../../src/game_run.cpp"

// defined in identification.cpp but not declared in any header
extern char magic_item_titles[MAX_TITLES][10];

static FILE *out = stdout;

// ---- arena and setup (item_use_dump's, copied as the earlier harnesses do) ----

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
    for (auto &item : game.treasure.list) {
        inventoryItemCopyTo(config::dungeon::objects::OBJ_NOTHING, item);
    }
    game.treasure.current_id = config::treasure::MIN_TREASURE_LIST_ID;
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

// ---- setup (mirrored in the C# test) --------------------------------------

enum SetupBits {
    S_TOUGH = 1,
    S_DARK = 2,
    S_BLIND = 4,
    S_HURT = 8,
    S_BOOKS = 16,
    S_LEARN = 32,
    S_MANA = 64,
    S_STAFF = 128,
    S_TRAPS = 256,
    S_CURSED = 512,
    S_AILING = 1024,
    S_DRAINED = 2048,
    S_SUSTAIN = 4096,
    S_RUBBLE = 8192,
    S_WEAPON = 16384,
    S_ITEMS = 32768,
    S_NEAR_ITEMS = 65536,
    S_UNKNOWN = 131072,
    S_RUBBLE_ROW = 262144,
};

static void equipCopyOfFirst(int lo, int hi, int slot) {
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].category_id >= lo && py.inventory[i].category_id <= hi) {
            py.inventory[slot] = py.inventory[i];
            py.inventory[slot].items_count = 1;
            py.equipment_count++;
            return;
        }
    }
    std::fprintf(stderr, "scenario setup: no pack item in category %d..%d\n", lo, hi);
    std::exit(3);
}

static void equipObject(int object_id, int slot) {
    inventoryItemCopyTo(object_id, py.inventory[slot]);
    py.equipment_count++;
}

static void carry(int object_id, int count, int16_t misc_use) {
    Inventory_t item{};
    inventoryItemCopyTo(object_id, item);
    item.items_count = (uint8_t) count;
    if (misc_use >= 0) {
        item.misc_use = misc_use;
    }
    (void) inventoryCarryItem(item);
}

static void setup(int param, int32_t exp) {
    if ((param & S_DARK) == 0) {
        equipCopyOfFirst(TV_LIGHT, TV_LIGHT, PlayerEquipment::Light);
        py.carrying_light = true;
    }
    if ((param & S_WEAPON) != 0) {
        equipObject(37, PlayerEquipment::Wield); // & Broad Sword
        py.inventory[PlayerEquipment::Wield].to_hit = 2;
    }
    if ((param & S_CURSED) != 0) {
        equipObject(103, PlayerEquipment::Body); // Soft Leather Armor
        equipObject(140, PlayerEquipment::Left); // Resist Fire (ring)
        py.inventory[PlayerEquipment::Body].flags |= config::treasure::flags::TR_CURSED;
        py.inventory[PlayerEquipment::Left].flags |= config::treasure::flags::TR_CURSED;
        if ((param & S_WEAPON) != 0) {
            py.inventory[PlayerEquipment::Wield].flags |= config::treasure::flags::TR_CURSED;
        }
    }
    if ((param & (S_WEAPON | S_CURSED)) != 0) {
        playerRecalculateBonuses();
    }
    if ((param & S_TOUGH) != 0) {
        for (int i = 0; i < PLAYER_MAX_LEVEL; i++) {
            py.base_hp_levels[i] = (uint16_t) (600 + 10 * i);
        }
        py.misc.max_hp = 600;
        py.misc.current_hp = 600;
    }
    if ((param & S_ITEMS) != 0) {
        carry(268, 3, -1); // & Flask~ of Oil
        carry(237, 2, -1); // Cure Light Wounds (potion)
        carry(176, 2, -1); // Identify (scroll)
        carry(140, 1, -1); // Resist Fire (ring)
    }
    if ((param & S_STAFF) != 0) {
        carry(293, 1, 5); // Light (staff)
        carry(282, 1, 6); // Magic Missile (wand)
    }
    if ((param & S_BOOKS) != 0) {
        int first = classes[py.misc.class_id].class_to_use_mage_spells == config::spells::SPELL_TYPE_MAGE ? 319 : 323;
        for (int book = first; book < first + 3; book++) {
            carry(book, 1, -1);
        }
    }
    if (exp > 0) {
        // playerGainLevel() halves the excess on each level gained, so
        // repeat until the level settles
        int level;
        do {
            level = py.misc.level;
            py.misc.exp = exp;
            displayCharacterExperience();
        } while (py.misc.level != level);
    }
    if ((param & S_DRAINED) != 0) {
        py.misc.exp = py.misc.exp / 3;
    }
    if ((param & S_LEARN) != 0) {
        Spell_t const *spells = magic_spells[py.misc.class_id - 1];
        int order = 0;
        py.flags.spells_learnt = 0;
        for (int i = 0; i < 31; i++) {
            if (spells[i].level_required <= py.misc.level) {
                py.flags.spells_learnt |= 1L << i;
                py.flags.spells_learned_order[order++] = (uint8_t) i;
            }
        }
        py.flags.new_spells_to_learn = 0;
    }
    if ((param & S_HURT) != 0) {
        py.misc.current_hp = (int16_t) (py.misc.max_hp / 4);
    }
    if ((param & S_AILING) != 0) {
        py.flags.poisoned = 30;
        py.flags.afraid = 20;
        for (int stat = 0; stat < 6; stat++) {
            py.stats.current[stat] = (uint8_t) (py.stats.max[stat] - 2);
            playerSetAndUseStat(stat);
        }
    }
    if ((param & S_SUSTAIN) != 0) {
        py.flags.sustain_str = true;
        py.flags.sustain_int = true;
        py.flags.sustain_wis = true;
        py.flags.sustain_dex = true;
        py.flags.sustain_con = true;
        py.flags.sustain_chr = true;
    }
    if ((param & S_BLIND) != 0) {
        py.flags.blind = 50;
        py.flags.status |= config::player::status::PY_BLIND;
    }
    if ((param & S_TRAPS) != 0) {
        dungeonSetTrap(Coord_t{py.pos.y, py.pos.x + 3}, 0);
        dungeonSetTrap(Coord_t{py.pos.y - 2, py.pos.x + 5}, 4);
        dungeonSetTrap(Coord_t{py.pos.y + 1, py.pos.x - 1}, 7);
        placeDoor(Coord_t{py.pos.y + 1, py.pos.x + 2}, config::dungeon::objects::OBJ_SECRET_DOOR, 0);
        int chest = placeObject(Coord_t{py.pos.y - 2, py.pos.x - 2}, 331); // & Large Steel Chest
        game.treasure.list[chest].flags |= config::treasure::chests::CH_LOCKED | 0x00000010;
        chest = placeObject(Coord_t{py.pos.y - 1, py.pos.x + 1}, 331);
        game.treasure.list[chest].flags |= 0x00000020;
        (void) placeObject(Coord_t{py.pos.y + 3, py.pos.x - 5}, config::dungeon::objects::OBJ_DOWN_STAIR);
        (void) placeObject(Coord_t{py.pos.y - 3, py.pos.x - 5}, config::dungeon::objects::OBJ_UP_STAIR);
        (void) placeObject(Coord_t{py.pos.y - 3, py.pos.x + 6}, config::dungeon::objects::OBJ_GOLD_LIST);
        (void) placeObject(Coord_t{py.pos.y + 4, py.pos.x + 4}, 237);
        (void) placeObject(Coord_t{py.pos.y - 4, py.pos.x + 2}, 21);
    }
    if ((param & S_NEAR_ITEMS) != 0) {
        (void) placeObject(Coord_t{py.pos.y - 1, py.pos.x - 1}, 237);
        (void) placeObject(Coord_t{py.pos.y + 1, py.pos.x + 1}, 268);
        (void) placeObject(Coord_t{py.pos.y, py.pos.x - 1}, 176);
    }
    if ((param & S_UNKNOWN) != 0) {
        // two stacks each of two unknown potion kinds -- dungeon-found and
        // store-bought don't stack -- for identify's merge (one kind with
        // the dungeon stack first, one with it second), and a cursed ring
        // for its "damned" inscription
        carry(243, 2, -1); // Constitution (potion)
        Inventory_t bought{};
        inventoryItemCopyTo(243, bought);
        itemIdentifyAsStoreBought(bought);
        (void) inventoryCarryItem(bought);
        inventoryItemCopyTo(251, bought); // Dexterity (potion)
        itemIdentifyAsStoreBought(bought);
        (void) inventoryCarryItem(bought);
        carry(251, 2, -1);
        Inventory_t ring{};
        inventoryItemCopyTo(140, ring); // Resist Fire (ring)
        ring.flags |= config::treasure::flags::TR_CURSED;
        (void) inventoryCarryItem(ring);
    }
    if ((param & S_RUBBLE_ROW) != 0) {
        for (int x = py.pos.x + 2; x <= py.pos.x + 16; x++) {
            dungeonPlaceRubble(Coord_t{py.pos.y, x});
        }
    }
    if ((param & S_RUBBLE) != 0) {
        dungeonPlaceRubble(Coord_t{py.pos.y, py.pos.x + 2});
        dungeonPlaceRubble(Coord_t{py.pos.y - 2, py.pos.x});
        dungeonPlaceRubble(Coord_t{py.pos.y + 2, py.pos.x});
    }
    if ((param & S_MANA) != 0) {
        // last: level gain recomputes mana
        py.misc.mana = 200;
        py.misc.current_mana = 200;
    }
}

static void placeMonsters(const char *spec) {
    std::string s(spec);
    size_t pos = 0;
    while (pos < s.size()) {
        size_t end = s.find(' ', pos);
        if (end == std::string::npos) {
            end = s.size();
        }
        std::string token = s.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }

        int creature_id = 0;
        int y = 0;
        int x = 0;
        int consumed = 0;
        (void) std::sscanf(token.c_str(), "%d@%d,%d%n", &creature_id, &y, &x, &consumed);
        Coord_t coord = Coord_t{y, x};
        if (!monsterPlaceNew(coord, creature_id, false)) {
            std::fprintf(stderr, "could not place monster %s\n", token.c_str());
            std::exit(6);
        }
        Monster_t &monster = monsters[dg.floor[y][x].creature_id];

        const char *mods = token.c_str() + consumed;
        while (*mods != '\0') {
            char key = *mods++;
            int value = (int) std::strtol(mods, const_cast<char **>(&mods), 10);
            switch (key) {
                case 'z':
                    monster.sleep_count = (int16_t) value;
                    break;
                case 'h':
                    monster.hp = (int16_t) value;
                    break;
                case 'c':
                    monster.confused_amount = (uint8_t) value;
                    break;
                default:
                    std::fprintf(stderr, "bad monster modifier in %s\n", token.c_str());
                    std::exit(7);
            }
        }
    }
}

static void resetOptions() {
    config::options::find_bound = false;
    config::options::run_cut_corners = true;
    config::options::run_examine_corners = true;
    config::options::run_ignore_doors = false;
    config::options::run_print_self = false;
    config::options::highlight_seams = false;
    config::options::prompt_to_pickup = false;
}

// ---- hashes (M10's) ------------------------------------------------------

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

static uint32_t hashTreasure() {
    uint32_t h = 2166136261u;
    for (int i = config::treasure::MIN_TREASURE_LIST_ID; i < game.treasure.current_id; i++) {
        h = hashItem(h, game.treasure.list[i]);
    }
    return h;
}

static uint32_t hashInventory() {
    uint32_t h = 2166136261u;
    for (auto const &item : py.inventory) {
        h = hashItem(h, item);
    }
    return h;
}

// ---- flavour names --------------------------------------------------------

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


// ---- char_create_common's input, through the shim -------------------------

// createScriptedCharacter() hands the stub its creation keys and a name; the
// real characterCreate() reads the name with getStringInput() just before
// the last key ("[ press any key to continue, or Q to exit ]").
void uiStubSetScript(const std::string &keys, const std::string &name) {
    std::string script = keys;
    script.insert(script.size() - 1, name + "\r");
    shimSetKeys(script);
}

bool uiStubScriptConsumed() {
    return shimKeysLeft() == 0 && shimExtraEscapes() == 0;
}

// ---- screen dumps ---------------------------------------------------------

static bool dumping = false;
static int step_number = 0;
static int screen_number = 0;

static void dumpScreen(const char *label) {
    int y, x;
    shimCursor(y, x);
    std::fprintf(out, "screen %d.%d %s cursor=%d,%d\n", step_number, screen_number++, label, y, x);

    for (auto line : shimSnapshot()) {
        while (!line.empty() && line.back() == ' ') {
            line.pop_back();
        }
        std::fprintf(out, "|%s\n", line.c_str());
    }
}

static void beforeKeyRead() {
    if (dumping) {
        dumpScreen("key");
    }
}

static int token_result = 0;

static void dumpStep(const char *token) {
    std::fprintf(out,
                 "step %d %s rng=%u free=%d inv=%d scr=%d left=%d bot=%d msg=%d ready=%d changed=%d status=%08x cc=%d au=%d hp=%d pack=%08x keys=%d esc=%d result=%d\n",
                 step_number, token, (unsigned) getRandomSeed(), (int) game.player_free_turn, (int) game.doing_inventory_command,
                 (int) game.screen.current_screen_id, game.screen.screen_left_pos, game.screen.screen_bottom_pos, (int) last_message_id,
                 (int) message_ready_to_print, (int) screen_has_changed, (unsigned) py.flags.status, (int) game.command_count, (int) py.misc.au,
                 (int) py.misc.current_hp, (unsigned) hashInventory(), (int) shimKeysLeft(), shimExtraEscapes(), token_result);
    dumpScreen("after");
    std::fflush(out);
}

// ---- status-effect turns (status_effects_dump's tick()) --------------------

static void tick() {
    dg.game_turn++;

    playerUpdateLightStatus();

    // Heroism and Super Heroism must precede anything that can damage player
    playerUpdateHeroStatus();

    int regen_amount = playerFoodConsumption();
    playerUpdateRegeneration(regen_amount);

    playerUpdateBlindness();
    playerUpdateConfusion();
    playerUpdateFearState();
    playerUpdatePoisonedState();
    playerUpdateSpeed();
    playerUpdateRestingState();

    playerUpdateHallucination();
    playerUpdateParalysis();
    playerUpdateEvilProtection();
    playerUpdateInvulnerability();
    playerUpdateBlessedness();
    playerUpdateHeatResistance();
    playerUpdateColdResistance();
    playerUpdateDetectInvisible();
    playerUpdateInfraVision();
    playerUpdateWordOfRecall();

    if ((py.flags.status & config::player::status::PY_STR_WGT) != 0u) {
        playerStrength();
    }

    if ((py.flags.status & config::player::status::PY_STUDY) != 0u) {
        printCharacterStudyInstruction();
    }

    playerUpdateStatusFlags();

    int chance = 10 + 750 / (5 + py.misc.level);
    if ((dg.game_turn & 0xF) == 0 && py.flags.confused == 0 && randomNumber(chance) == 1) {
        playerDetectEnchantment();
    }
}

// ---- tokens ---------------------------------------------------------------

static std::string decodeKeys(const std::string &text) {
    std::string keys;
    for (size_t i = 0; i < text.size(); i++) {
        char c = text[i];
        if (c == '~') {
            keys += ESCAPE;
        } else if (c == '_') {
            keys += ' ';
        } else if (c == '$') {
            keys += '\r';
        } else if (c == '#') {
            keys += CTRL_KEY('H');
        } else if (c == '^' && i + 1 < text.size()) {
            keys += CTRL_KEY(text[++i]);
        } else {
            keys += c;
        }
    }
    return keys;
}

static void runToken(const std::string &token) {
    // <op><arg>[:<keys>]
    std::string arg = token.substr(1);
    std::string keys;
    size_t colon = arg.find(':');
    if (colon != std::string::npos) {
        keys = decodeKeys(arg.substr(colon + 1));
        arg = arg.substr(0, colon);
    }
    shimSetKeys(keys);
    token_result = 0;

    int a = 0;
    int b = 0;

    switch (token[0]) {
        case 'D':
            drawCavePanel();
            break;
        case '!':
            break;
        case 'm':
            playerMove(std::atoi(arg.c_str()), false);
            break;
        case 'M': {
            std::string text = arg;
            for (auto &c : text) {
                if (c == '_') {
                    c = ' ';
                }
            }
            printMessage(text.c_str());
            break;
        }
        case 'N':
            printMessage(CNIL);
            break;
        case 'i':
            inventoryExecuteCommand(arg[0]);
            break;
        case 'I':
            if (game.doing_inventory_command != 0) {
                inventoryExecuteCommand(game.doing_inventory_command);
            }
            break;
        case 'C':
            changeCharacterName();
            break;
        case 'H':
            game.command_count = std::atoi(arg.c_str());
            commandPreviousMessage();
            game.last_command = CTRL_KEY('P');
            break;
        case 'L':
            commandLocateOnMap();
            break;
        case 'V':
            dungeonDisplayMap();
            break;
        case 'G': {
            (void) std::sscanf(arg.c_str(), "%d,%d", &a, &b);
            int item_id = -1;
            bool found = inventoryGetInputForItemId(item_id, "Which one? ", a, b, CNIL, CNIL);
            token_result = found ? item_id : -1;
            break;
        }
        case 'c':
            getAndCastMagicSpell();
            break;
        case 'p':
            pray();
            break;
        case 's':
            playerGainSpells();
            break;
        case 'S':
            storeEnter(std::atoi(arg.c_str()));
            break;
        case 'O':
            // (a turn past 0, or every store's doors are "locked")
            dg.game_turn = 10;
            storeInitializeOwners();
            for (int i = 0; i < 10; i++) {
                storeMaintenance();
            }
            break;
        case 'Y':
            token_result = (int) getInputConfirmation("Are you sure?");
            break;
        case 'T': {
            putStringClearToEOL("Text:", Coord_t{0, 0});
            char text[80] = {'\0'};
            if (getStringInput(text, Coord_t{0, 6}, 20)) {
                token_result = (int) std::strlen(text);
                std::fprintf(out, "text [%s]\n", text);
            } else {
                token_result = -1;
            }
            break;
        }
        case 'E':
            py.misc.exp = std::atoi(arg.c_str());
            displayCharacterExperience();
            break;
        case 'U':
            for (int i = std::atoi(arg.c_str()); i > 0; i--) {
                tick();
            }
            break;
        case 'P':
            (void) std::sscanf(arg.c_str(), "%d,%d", &a, &b);
            carry(a, b, -1);
            break;
        case 'W':
            (void) std::sscanf(arg.c_str(), "%d,%d", &a, &b);
            equipObject(a, b);
            playerRecalculateBonuses();
            break;
        case 'X': {
            int n = std::atoi(arg.c_str() + 1);
            switch (arg[0]) {
                case 'h':
                    py.flags.status |= config::player::status::PY_HUNGRY;
                    break;
                case 'w':
                    py.flags.status |= config::player::status::PY_WEAK;
                    break;
                case 'b':
                    py.flags.blind = (int16_t) n;
                    py.flags.status |= config::player::status::PY_BLIND;
                    break;
                case 'c':
                    py.flags.confused = (int16_t) n;
                    py.flags.status |= config::player::status::PY_CONFUSED;
                    break;
                case 'f':
                    py.flags.afraid = (int16_t) n;
                    py.flags.status |= config::player::status::PY_FEAR;
                    break;
                case 'p':
                    py.flags.poisoned = (int16_t) n;
                    py.flags.status |= config::player::status::PY_POISONED;
                    break;
                case 's':
                    playerSearchOn();
                    break;
                case 'r':
                    py.flags.rest = (int16_t) n;
                    py.flags.status |= config::player::status::PY_REST;
                    break;
                case 'z':
                    py.flags.paralysis = (int16_t) n;
                    break;
                case 'v':
                    py.flags.speed = (int16_t) n;
                    break;
                case 'n':
                    py.flags.new_spells_to_learn = (uint8_t) n;
                    break;
                case 'k':
                    game.command_count = n;
                    break;
                case 'g':
                    py.misc.au = n;
                    break;
                case 'd':
                    py.flags.food = (int16_t) n;
                    break;
                default:
                    std::fprintf(stderr, "bad status token %s\n", token.c_str());
                    std::exit(5);
            }
            break;
        }
        default:
            std::fprintf(stderr, "bad token %s\n", token.c_str());
            std::exit(5);
    }
}

static void runScript(const char *script) {
    std::string s(script);
    size_t pos = 0;
    while (pos < s.size()) {
        size_t end = s.find(' ', pos);
        if (end == std::string::npos) {
            end = s.size();
        }
        std::string token = s.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }

        step_number++;
        screen_number = 0;
        runToken(token);
        dumpStep(token.c_str());
    }
}

// ---- scenarios ------------------------------------------------------------

typedef struct {
    const char *name;
    uint32_t seed;
    int race_id;
    bool is_male;
    int class_choice;
    Coord_t pos; // GEN: a generated level instead of the arena
    int16_t depth;
    int setup;
    int32_t exp;
    const char *monsters;
    const char *script;
} Scenario;

static void resetScreen() {
    message_ready_to_print = false;
    screen_has_changed = false;
    last_message_id = 0;
    for (auto &message : messages) {
        message[0] = '\0';
    }

    // a blank screen and a blank spare one
    (void) clear();
    terminalSaveScreen();
}

static void runScenario(Scenario const &s) {
    resetOptions();
    dumping = false;

    // (before creation too: its first clearScreen() would otherwise show a
    // message the last scenario left waiting, and read a -more- key)
    resetScreen();

    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "scenario %s: character creation script not consumed\n", s.name);
        std::exit(2);
    }

    // startMoria(): magicInitializeItemNames() right after creation
    shuffleNames(s.seed ^ 0x5a5a5a5au);

    std::fprintf(stderr, "scenario %s\n", s.name);

    buildArena(s.depth);
    game.character_generated = true;

    if (s.pos.y < 0) {
        // the real generateCave(); it places the player
        generateCave();
    } else {
        py.pos = s.pos;
    }

    setup(s.setup, s.exp);
    placeMonsters(s.monsters);

    resetScreen();

    // generateCave()'s panel setup, then playDungeon()'s preamble (which
    // marks the player's tile), then the whole screen
    dg.panel = Panel_t{};
    dg.panel.max_rows = (int16_t) ((dg.height / SCREEN_HEIGHT) * 2 - 2);
    dg.panel.max_cols = (int16_t) ((dg.width / SCREEN_WIDTH) * 2 - 2);
    playerInitializePlayerLight();
    playerUpdateMaxDungeonDepth();
    resetDungeonFlags();
    dg.panel.row = dg.panel.col = -1;
    dungeonResetView();
    if ((py.flags.status & config::player::status::PY_SEARCH) != 0u) {
        playerSearchOff();
    }
    updateMonsters(false);
    printCharacterCurrentDepth();
    drawCavePanel();

    std::fprintf(out, "scenario %s\n", s.name);
    step_number = 0;
    screen_number = 0;
    token_result = 0;
    shimSetKeys("");
    dumping = true;
    dumpStep("start");

    runScript(s.script);

    dumping = false;
    std::fprintf(out, "messages %d\n", (int) last_message_id);
    for (int i = 0; i < MESSAGE_HISTORY_SIZE; i++) {
        std::fprintf(out, "message %d [%s]\n", i, messages[i]);
    }
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

    if (!terminalInitialize()) {
        return 1;
    }
    config::options::error_beep_sound = false;
    shimSetBeforeKeyRead(beforeKeyRead);

    const Coord_t P = Coord_t{11, 20};
    const Coord_t DARK = Coord_t{11, 120};
    const Coord_t GEN = Coord_t{-1, -1};

    const int T = S_TOUGH;

    const Scenario scenarios[] = {
#include "ui_screen_scenarios.inc"
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
