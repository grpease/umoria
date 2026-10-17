// Player movement/actions dump harness: verifies the .NET migration's port
// of the player's step, running, traps, tunnelling, doors, disarming and
// stat changes (M8 -- Umoria.Core.Character.PlayerMovement, PlayerRunning,
// PlayerTraps, PlayerTunnel, PlayerDoors, PlayerStatLogic) against the real
// compiled C++ game.
//
// Every function exercised here is already a global in the game (playerMove,
// playerFindInitialize, playerTunnel, playerBash, ...), so unlike M7 this
// target needs no #include of a game .cpp: it links the real sources minus
// main.cpp and ui_io.cpp, the latter replaced by the M6 headless stub. The
// direction prompts (getDirectionWithMemory()) read their digit from the
// stub's key script, and getInputConfirmation() answers what the scenario
// sets.
//
// Each scenario: create a real character (M6, char_create_common.cpp),
// build the hand-made 66x198 test level below (mirrored exactly by
// PlayerActionsTraceTests.BuildTestLevel()), apply the scenario's setup,
// run playDungeon()'s level-entry panel/light reset, then execute a
// scripted command string. One state line per step (player, flags, panel,
// RNG, and hashes of the whole floor, treasure list, monster list and
// inventory), and the full player dump at the end. A scenario stops early
// when dg.generate_new_level is set (trap door, death), as the real loop
// would.
//
// Script tokens (space separated, `*N` repeats a token N times):
//   m<d>  playerMove(d, true)          M<d>  playerMove(d, false)
//   r<d>  playerFindInitialize(d), then playerRunAndFind() until it stops
//   t<d>  playerTunnel(d)              b<d>  playerBash()       (key d)
//   o<d>  playerOpenClosedObject()     c<d>  playerCloseDoor()  (key d)
//   x<d>  playerDisarmTrap()  (key d)  s     playerSearch(py.pos, chance)
//   S1/S0 playerSearchOn()/Off()       p<n>  playerTeleport(n)
//   n<k>  game.command_count = k       (to exercise the no-interrupt
//                                       messages)
//   h<k>  py.flags.image = k           (hallucination is set by a token,
//                                       after the level-entry redraw: see
//                                       migration-plan.md's M7 section)
// Like playDungeon()'s command loop, each step starts with
// game.player_free_turn = false, and a step that leaves
// game.teleport_player set is followed by playerTeleport(100).
//
// Then a "stats" section drives player_stats.cpp's stat
// gain/loss/restore/boost, attack-blows and charisma tables directly.
//
// Usage: player_actions_dump [output_file]
// With no output_file, writes to stdout.

#include "../../src/headers.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

#include "../char_create_dump/char_create_common.h"
#include "../char_create_dump/ui_io_stub.h"

static FILE *out = stdout;

// ---- test level (mirrored by PlayerActionsTraceTests.BuildTestLevel) ----

static void carve(int y1, int x1, int y2, int x2, uint8_t feature) {
    for (int y = y1; y <= y2; y++) {
        for (int x = x1; x <= x2; x++) {
            dg.floor[y][x].feature_id = feature;
        }
    }
}

// A room with a granite ring; lit rooms start with TILE_LIGHT_FLOOR but not
// yet permanent_light (it lights up when entered), like generated rooms.
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

static void buildTestLevel(int16_t depth) {
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
    // treasureLinker() / monsterLinker() (dungeon_generate.cpp)
    for (auto &item : game.treasure.list) {
        inventoryItemCopyTo(config::dungeon::objects::OBJ_NOTHING, item);
    }
    game.treasure.current_id = config::treasure::MIN_TREASURE_LIST_ID;
    for (auto &monster : monsters) {
        monster = blank_monster;
    }
    next_free_monster_id = config::monsters::MON_MIN_INDEX_ID;

    // Room A (lit), doorway south, closed door east
    carveRoom(4, 4, 13, 31, true);
    carve(13, 15, 13, 15, TILE_CORR_FLOOR);
    placeDoor(Coord_t{8, 31}, config::dungeon::objects::OBJ_CLOSED_DOOR, 0);

    // corridor east from room A, corner south, branch east to room B
    carve(8, 32, 8, 60, TILE_CORR_FLOOR);
    carve(9, 60, 30, 60, TILE_CORR_FLOOR);
    carve(20, 61, 20, 69, TILE_CORR_FLOOR);

    // Room B (dark), doorway west
    carveRoom(15, 70, 30, 95, false);
    carve(20, 70, 20, 70, TILE_CORR_FLOOR);

    // long corridor south from room A's doorway, then far east
    carve(14, 15, 45, 15, TILE_CORR_FLOOR);
    carve(45, 16, 45, 190, TILE_CORR_FLOOR);

    // door corridor
    carve(40, 30, 40, 60, TILE_CORR_FLOOR);
    placeDoor(Coord_t{40, 35}, config::dungeon::objects::OBJ_CLOSED_DOOR, 0);
    placeDoor(Coord_t{40, 40}, config::dungeon::objects::OBJ_CLOSED_DOOR, 5);
    placeDoor(Coord_t{40, 45}, config::dungeon::objects::OBJ_CLOSED_DOOR, 60);
    placeDoor(Coord_t{40, 50}, config::dungeon::objects::OBJ_CLOSED_DOOR, -8);
    placeDoor(Coord_t{40, 55}, config::dungeon::objects::OBJ_OPEN_DOOR, 0);
    placeDoor(Coord_t{40, 57}, config::dungeon::objects::OBJ_OPEN_DOOR, 1);

    // trap corridor
    carve(50, 10, 50, 100, TILE_CORR_FLOOR);

    // dig corridor: magma, quartz and permanent rock above, rubble and a
    // secret door in it, lit room C below
    carve(55, 10, 55, 40, TILE_CORR_FLOOR);
    carve(54, 12, 54, 12, TILE_MAGMA_WALL);
    carve(54, 14, 54, 14, TILE_QUARTZ_WALL);
    carve(54, 18, 54, 18, TILE_BOUNDARY_WALL);
    dungeonPlaceRubble(Coord_t{55, 25});
    placeDoor(Coord_t{55, 30}, config::dungeon::objects::OBJ_SECRET_DOOR, 0);
    carveRoom(56, 8, 61, 22, true);

    // running maze (top right): a corridor with magma walls, an enclosed
    // corner, a T junction, a crossing with a dead end, an angled
    // (diagonal) corridor, and a corridor opening into a dark room
    carve(3, 111, 3, 130, TILE_CORR_FLOOR);
    carve(2, 115, 2, 125, TILE_MAGMA_WALL);
    carve(4, 115, 4, 125, TILE_MAGMA_WALL);
    carve(4, 130, 12, 130, TILE_CORR_FLOOR);
    carve(8, 131, 8, 150, TILE_CORR_FLOOR);
    carve(5, 140, 11, 140, TILE_CORR_FLOOR);
    const Coord_t diagonal[] = {{12, 131}, {11, 132}, {10, 133}, {10, 134}, {11, 135}, {12, 136}, {12, 137}, {12, 138}};
    for (auto const &c : diagonal) {
        dg.floor[c.y][c.x].feature_id = TILE_CORR_FLOOR;
    }
    carveRoom(4, 151, 12, 161, false);
    carve(8, 151, 8, 151, TILE_CORR_FLOOR);
}

// ---- scenario helpers (mirrored in the C# test) -------------------------

static int equipCopyOfFirst(int lo, int hi, int slot) {
    for (int i = 0; i < py.pack.unique_items; i++) {
        if (py.inventory[i].category_id >= lo && py.inventory[i].category_id <= hi) {
            py.inventory[slot] = py.inventory[i];
            py.inventory[slot].items_count = 1;
            py.equipment_count++;
            return slot;
        }
    }
    std::fprintf(stderr, "scenario setup: no pack item in category %d..%d\n", lo, hi);
    std::exit(3);
}

static void carryTorch() {
    equipCopyOfFirst(TV_LIGHT, TV_LIGHT, PlayerEquipment::Light);
    py.carrying_light = true;
}

static void wieldObject(int object_id) {
    inventoryItemCopyTo(object_id, py.inventory[PlayerEquipment::Wield]);
    py.equipment_count++;
}

static int placeChest(Coord_t coord, uint32_t chest_flags, bool identified) {
    int id = placeObject(coord, 326); // & Small Wooden Chest
    game.treasure.list[id].flags |= chest_flags;
    if (identified) {
        game.treasure.list[id].identification |= config::identification::ID_KNOWN2;
    }
    return id;
}

static void placeMonster(Coord_t coord, bool lit) {
    (void) monsterPlaceNew(coord, 1, false); // a Blubbering Idiot (no monster ever acts here)
    monsters[dg.floor[coord.y][coord.x].creature_id].lit = lit;
}

static void resetOptions() {
    config::options::find_bound = false;
    config::options::run_cut_corners = true;
    config::options::run_examine_corners = true;
    config::options::run_ignore_doors = false;
    config::options::run_print_self = false;
    config::options::highlight_seams = false;
    config::options::prompt_to_pickup = false;
    uiStubSetConfirmAnswer(false);
}

// ---- scenarios ----------------------------------------------------------

typedef struct {
    const char *name;
    uint32_t seed;
    int race_id;
    bool is_male;
    int class_choice;
    Coord_t pos;
    int16_t depth;
    void (*setup)(int param);
    int param;
    const char *script;
} Scenario;

static const Coord_t TRAP_START = Coord_t{50, 20};
static const Coord_t TRAP_SPOT = Coord_t{50, 21};

static void setupLight(int /*param*/) {
    carryTorch();
}

static void setupDark(int /*param*/) {
    py.carrying_light = false;
}

// param bits: 1 = walls already known (permanent_light, as if mapped),
// 2 = don't cut corners, 4 = don't examine corners, 8 = highlight seams,
// 16 = no light
static void setupMaze(int param) {
    if ((param & 16) == 0) {
        carryTorch();
    }
    if ((param & 1) != 0) {
        for (int y = 2; y <= 13; y++) {
            for (int x = 110; x <= 162; x++) {
                if (dg.floor[y][x].feature_id >= MIN_CAVE_WALL) {
                    dg.floor[y][x].permanent_light = true;
                }
            }
        }
    }
    config::options::run_cut_corners = (param & 2) == 0;
    config::options::run_examine_corners = (param & 4) == 0;
    config::options::highlight_seams = (param & 8) != 0;
}

static void setupOpenEastDoor(int param) {
    carryTorch();
    int id = dg.floor[8][31].treasure_id;
    inventoryItemCopyTo(config::dungeon::objects::OBJ_OPEN_DOOR, game.treasure.list[id]);
    dg.floor[8][31].feature_id = TILE_CORR_FLOOR;
    // param: 1 = don't cut corners, 2 = print self while running
    if (param == 1) {
        config::options::run_cut_corners = false;
    }
    if (param == 2) {
        config::options::run_print_self = true;
    }
}

static void setupLongRun(int param) {
    carryTorch();
    config::options::find_bound = param == 1;
}

static void setupConfused(int param) {
    carryTorch();
    py.flags.confused = (int16_t) param;
}

static void setupSearch(int param) {
    carryTorch();
    dungeonSetTrap(Coord_t{55, 28}, 1); // invisible arrow trap
    (void) placeChest(Coord_t{55, 27}, config::treasure::chests::CH_POISON, false);
    // param: 1 = searches on every step, 2 = confused, in the dark
    if (param == 1) {
        py.misc.fos = 1;
    }
    if (param == 2) {
        py.flags.confused = 10;
        py.carrying_light = false;
    }
}

static void setupPickup(int param) {
    carryTorch();
    dungeonPlaceGold(Coord_t{22, 81});
    (void) placeObject(Coord_t{22, 82}, 84); // & Iron Spike~
    int chest = placeObject(Coord_t{22, 83}, 331); // & Large Steel Chest
    game.treasure.list[chest].weight = 3000;      // too heavy to carry unasked
    dungeonPlaceRandomObjectAt(Coord_t{22, 84}, false);
    dungeonPlaceGold(Coord_t{22, 85});
    (void) placeObject(Coord_t{23, 85}, 84);
    (void) placeObject(Coord_t{23, 84}, 84);
    // param: 1 = answer yes to the weight prompt, 2 = pack already full,
    // 3 = prompt_to_pickup on (answered no)
    if (param == 1) {
        uiStubSetConfirmAnswer(true);
    }
    if (param == 2) {
        while (py.pack.unique_items < PlayerEquipment::Wield) {
            py.inventory[py.pack.unique_items] = py.inventory[0];
            py.pack.unique_items++;
        }
    }
    if (param == 3) {
        config::options::prompt_to_pickup = true;
    }
}

// param: trap sub_category_id (1..19), or 99 for a rune of protection;
// plus flag bits in the hundreds/thousands for the variants.
enum TrapVariant {
    TV_PLAIN = 0,
    TV_FREE = 1000,     // free_fall + free_action
    TV_SUSTAIN = 2000,  // sustain_str + sustain_con
    TV_DYING = 3000,    // 1 hp left
    TV_ASLEEP = 4000,   // already paralysed
    TV_ARMORED = 5000,  // armour class 200
    TV_GUARDED = 6000,  // invulnerable, and resting
    TV_BACKTRAP = 7000, // another trap where the player starts
};

static void setupTrap(int param) {
    carryTorch();
    int trap = param % 1000;
    int variant = param - trap;

    if (trap == 99) {
        (void) placeObject(TRAP_SPOT, config::dungeon::objects::OBJ_SCARE_MON);
    } else {
        dungeonSetTrap(TRAP_SPOT, trap - 1);
    }

    if (variant == TV_FREE) {
        py.flags.free_fall = true;
        py.flags.free_action = true;
    } else if (variant == TV_SUSTAIN) {
        py.flags.sustain_str = true;
        py.flags.sustain_con = true;
    } else if (variant == TV_DYING) {
        py.misc.current_hp = 1;
    } else if (variant == TV_ASLEEP) {
        py.flags.paralysis = 2;
    } else if (variant == TV_ARMORED) {
        py.misc.ac = 200;
    } else if (variant == TV_GUARDED) {
        py.flags.invulnerability = 5;
        py.flags.rest = 10;
        py.flags.status |= config::player::status::PY_REST;
    } else if (variant == TV_BACKTRAP) {
        dungeonSetTrap(TRAP_START, 1);
    }
}

static void setupDisarmFloor(int param) {
    carryTorch();
    // arrow trap (level 90, exp 3); for the level-gain cases an open pit
    // (level 50, exp 1), which a mage or priest can actually disarm
    dungeonSetTrap(TRAP_SPOT, param >= 2 && param <= 3 ? 0 : 1);
    trapChangeVisibility(TRAP_SPOT);
    dungeonSetTrap(Coord_t{50, 23}, 6); // dart of strength
    trapChangeVisibility(Coord_t{50, 23});
    // param: 1 = confused, 2 = one exp short of the next level, 3 = at the
    // experience cap (many levels at once), 4 = blind, 5 = middling skill
    if (param == 5) {
        py.misc.disarm = 30;
    }
    if (param == 1) {
        py.flags.confused = 30;
    }
    if (param == 2) {
        py.misc.exp = (int32_t) (py.base_exp_levels[py.misc.level - 1] * py.misc.experience_factor / 100) - 1;
    }
    if (param == 3) {
        py.misc.exp = config::player::PLAYER_MAX_EXP;
    }
    if (param == 4) {
        py.flags.blind = 20;
    }
}

static void setupDisarmChest(int param) {
    carryTorch();
    (void) placeChest(TRAP_SPOT, config::treasure::chests::CH_LOCKED | config::treasure::chests::CH_POISON | config::treasure::chests::CH_PARALYSED | config::treasure::chests::CH_LOSE_STR, false);
    (void) placeChest(Coord_t{50, 19}, config::treasure::chests::CH_EXPLODE | config::treasure::chests::CH_SUMMON, true);
    // param: 1 = clumsy (sets the traps off), 2 = clumsy but protected,
    // 3 = middling skill
    if (param == 1 || param == 2) {
        py.misc.disarm = -40;
    }
    if (param == 3) {
        py.misc.disarm = 5;
    }
    if (param == 2) {
        py.flags.sustain_str = true;
        py.flags.free_action = true;
    }
}

static void setupDoors(int param) {
    carryTorch();
    // param: 1 = confused
    if (param == 1) {
        py.flags.confused = 25;
    }
}

static void setupDoorsMonster(int param) {
    carryTorch();
    // param: 0 = on the open door and a trap, 1 = on the stuck door,
    // 2 = in the rubble
    if (param == 0) {
        placeMonster(Coord_t{40, 55}, true);
        dungeonSetTrap(Coord_t{40, 53}, 0);
        placeMonster(Coord_t{40, 53}, false);
    } else if (param == 1) {
        placeMonster(Coord_t{40, 50}, false);
    } else {
        placeMonster(Coord_t{55, 25}, true);
    }
    py.flags.afraid = 50;
}

static void setupRunMonster(int param) {
    carryTorch();
    // param: 0 = in room A (plus one beside the start and gold, which the
    // running code sees as glyphs), 1 = in the corridor beyond the east door
    if (param == 0) {
        placeMonster(Coord_t{8, 25}, true);
        placeMonster(Coord_t{7, 11}, true);
        (void) placeObject(Coord_t{9, 11}, config::dungeon::objects::OBJ_GOLD_LIST);
    } else {
        setupOpenEastDoor(0);
        placeMonster(Coord_t{8, 45}, true);
    }
}

static void setupBlind(int /*param*/) {
    carryTorch();
    py.flags.blind = 10;
}

static void setupTunnel(int param) {
    carryTorch();
    // gold in the magma vein
    (void) placeObject(Coord_t{54, 12}, config::dungeon::objects::OBJ_GOLD_LIST);
    // param: 0 = bare hands, 1 = wielded starting weapon, 2 = pick,
    // 3 = pick too heavy to wield, 4 = pick while confused
    if (param == 1) {
        equipCopyOfFirst(TV_HAFTED, TV_SWORD, PlayerEquipment::Wield);
    }
    if (param >= 2) {
        wieldObject(349); // & Pick, TR_TUNNEL
    }
    if (param == 3) {
        py.weapon_is_heavy = true;
        py.inventory[PlayerEquipment::Wield].weight = 400;
    }
    if (param == 4) {
        py.flags.confused = 40;
    }
}

// ---- dump ---------------------------------------------------------------

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
        h = mix(h, m.lit);
        h = mix(h, m.stunned_amount);
        h = mix(h, m.confused_amount);
    }
    return h;
}

static void dumpStep(int step, const char *cmd) {
    Panel_t const &p = dg.panel;
    std::fprintf(out,
                 "step %d %s rnd=%u pos=%d,%d free=%d run=%d cmd=%d dir=%d panel=%d,%d/%d-%d,%d-%d hp=%d.%u/%d exp=%d/%d lvl=%d au=%d st=%08x "
                 "str=%d/%d/%d con=%d/%d/%d para=%d blind=%d conf=%d slow=%d pois=%d afraid=%d spd=%d dig=%d tlo=%d gen=%d depth=%d dead=%d died=[%s] "
                 "tele=%d tid=%d mon=%d pack=%d/%d floor=%08x treas=%08x mons=%08x inv=%08x\n",
                 step, cmd, getRandomSeed(), py.pos.y, py.pos.x, (int) game.player_free_turn, py.running_tracker, game.command_count, (int) py.prev_dir, p.row, p.col, p.top, p.bottom,
                 p.left, p.right, py.misc.current_hp, py.misc.current_hp_fraction, py.misc.max_hp, py.misc.exp, py.misc.max_exp, py.misc.level, py.misc.au, py.flags.status,
                 py.stats.current[PlayerAttr::A_STR], py.stats.max[PlayerAttr::A_STR], py.stats.used[PlayerAttr::A_STR], py.stats.current[PlayerAttr::A_CON], py.stats.max[PlayerAttr::A_CON],
                 py.stats.used[PlayerAttr::A_CON], py.flags.paralysis, py.flags.blind, py.flags.confused, py.flags.slow, py.flags.poisoned, py.flags.afraid, py.flags.speed,
                 py.flags.food_digested, (int) py.temporary_light_only, (int) dg.generate_new_level, dg.current_level, (int) game.character_is_dead, game.character_died_from,
                 (int) game.teleport_player, game.treasure.current_id, next_free_monster_id, py.pack.unique_items, py.pack.weight, hashFloor(), hashTreasure(), hashMonsters(),
                 hashInventory());
}

// ---- script runner ------------------------------------------------------

static int step_number = 0;

static void withDirectionKey(int dir, void (*command)()) {
    uiStubSetScript(std::string(1, (char) ('0' + dir)), "");
    command();
    if (!uiStubScriptConsumed()) {
        std::fprintf(stderr, "direction key not consumed\n");
        std::exit(4);
    }
}

static void afterStep(const char *cmd) {
    dumpStep(++step_number, cmd);
    if (game.teleport_player && !dg.generate_new_level) {
        playerTeleport(100);
        dumpStep(++step_number, "teleport");
    }
}

static void runToken(const std::string &token) {
    char op = token[0];
    int arg = token.size() > 1 ? std::atoi(token.c_str() + 1) : 0;

    game.player_free_turn = false;

    switch (op) {
        case 'm':
            playerMove(arg, true);
            break;
        case 'M':
            playerMove(arg, false);
            break;
        case 'r':
            playerFindInitialize(arg);
            afterStep(token.c_str());
            for (int n = 0; py.running_tracker != 0 && !dg.generate_new_level && n < 300; n++) {
                game.player_free_turn = false;
                playerRunAndFind();
                afterStep("run");
            }
            return;
        case 't':
            playerTunnel(arg);
            break;
        case 'b':
            withDirectionKey(arg, playerBash);
            break;
        case 'o':
            withDirectionKey(arg, playerOpenClosedObject);
            break;
        case 'c':
            withDirectionKey(arg, playerCloseDoor);
            break;
        case 'x':
            withDirectionKey(arg, playerDisarmTrap);
            break;
        case 's':
            playerSearch(py.pos, py.misc.chance_in_search);
            break;
        case 'S':
            if (arg == 1) {
                playerSearchOn();
            } else {
                playerSearchOff();
            }
            break;
        case 'p':
            playerTeleport(arg);
            break;
        case 'n':
            game.command_count = arg;
            break;
        case 'h':
            py.flags.image = (int16_t) arg;
            break;
        default:
            std::fprintf(stderr, "bad script token: %s\n", token.c_str());
            std::exit(5);
    }

    afterStep(token.c_str());
}

static void runScript(const char *script) {
    std::string s(script);
    size_t pos = 0;
    while (pos < s.size() && !dg.generate_new_level) {
        size_t end = s.find(' ', pos);
        if (end == std::string::npos) {
            end = s.size();
        }
        std::string token = s.substr(pos, end - pos);
        pos = end + 1;
        if (token.empty()) {
            continue;
        }

        int repeat = 1;
        size_t star = token.find('*');
        if (star != std::string::npos) {
            repeat = std::atoi(token.c_str() + star + 1);
            token = token.substr(0, star);
        }

        for (int i = 0; i < repeat && !dg.generate_new_level; i++) {
            runToken(token);
        }
    }
}

static void runScenario(Scenario const &s) {
    resetOptions();

    if (!createScriptedCharacter(s.seed, s.race_id, s.is_male, s.class_choice, 0)) {
        std::fprintf(stderr, "scenario %s: character creation script not consumed\n", s.name);
        std::exit(2);
    }

    buildTestLevel(s.depth);
    py.pos = s.pos;
    dg.floor[s.pos.y][s.pos.x].creature_id = 1;

    s.setup(s.param);

    // generateCave()'s panel setup, then playDungeon()'s preamble:
    // panel.row/col = -1 and dungeonResetView().
    dg.panel = Panel_t{};
    dg.panel.max_rows = (int16_t) ((dg.height / SCREEN_HEIGHT) * 2 - 2);
    dg.panel.max_cols = (int16_t) ((dg.width / SCREEN_WIDTH) * 2 - 2);
    dg.panel.row = dg.panel.col = -1;
    dungeonResetView();

    std::fprintf(out, "scenario %s\n", s.name);
    step_number = 0;
    dumpStep(0, "start");

    runScript(s.script);

    std::fprintf(out, "final %s\n", s.name);
    dumpPlayer();
}

// ---- stats section ------------------------------------------------------

static void dumpStatsLine(const char *what, int stat, int value, bool result) {
    std::fprintf(out, "%s stat=%d v=%d -> %d cur=%d max=%d mod=%d used=%d rnd=%u st=%08x hp=%d.%u/%d bth=%d dis=%d ac=%d mana=%d\n", what, stat, value, (int) result,
                 py.stats.current[stat], py.stats.max[stat], py.stats.modified[stat], py.stats.used[stat], getRandomSeed(), py.flags.status, py.misc.current_hp,
                 py.misc.current_hp_fraction, py.misc.max_hp, py.misc.bth, py.misc.disarm, py.misc.magical_ac, py.misc.mana);
}

static void runStatsFor(uint32_t seed, int class_choice) {
    std::fprintf(out, "stats class=%d\n", class_choice);

    // Human, so class_choice is the class id: a mage reaches the spell code
    // through INT, a priest through WIS.
    (void) createScriptedCharacter(seed, 0, false, class_choice, 0);

    const int values[] = {3, 4, 10, 17, 18, 19, 28, 50, 88, 100, 108, 115, 116, 117, 118};

    for (int stat = 0; stat < 6; stat++) {
        uint8_t saved_cur = py.stats.current[stat];
        uint8_t saved_max = py.stats.max[stat];
        int16_t saved_mod = py.stats.modified[stat];

        for (int v : values) {
            py.stats.current[stat] = (uint8_t) v;
            py.stats.max[stat] = (uint8_t) v;
            dumpStatsLine("inc", stat, v, playerStatRandomIncrease(stat));

            py.stats.current[stat] = (uint8_t) v;
            py.stats.max[stat] = (uint8_t) v;
            dumpStatsLine("dec", stat, v, playerStatRandomDecrease(stat));

            py.stats.max[stat] = (uint8_t) (v + 20 > 118 ? 118 : v + 20);
            dumpStatsLine("restore", stat, v, playerStatRestore(stat));
            dumpStatsLine("restore2", stat, v, playerStatRestore(stat));

            playerStatBoost(stat, 2);
            dumpStatsLine("boost+", stat, v, true);
            playerStatBoost(stat, -3);
            dumpStatsLine("boost-", stat, v, true);
            playerStatBoost(stat, 1);
        }

        py.stats.current[stat] = saved_cur;
        py.stats.max[stat] = saved_max;
        py.stats.modified[stat] = saved_mod;
        playerSetAndUseStat(stat);
    }

    // hit points while heroic / berserk
    py.flags.status |= config::player::status::PY_HERO | config::player::status::PY_SHERO;
    py.stats.current[PlayerAttr::A_CON] = 50;
    py.stats.max[PlayerAttr::A_CON] = 50;
    dumpStatsLine("hero-inc", PlayerAttr::A_CON, 50, playerStatRandomIncrease(PlayerAttr::A_CON));
    dumpStatsLine("hero-dec", PlayerAttr::A_CON, 50, playerStatRandomDecrease(PlayerAttr::A_CON));
    py.flags.status &= ~(config::player::status::PY_HERO | config::player::status::PY_SHERO);
    dumpStatsLine("hero-off", PlayerAttr::A_CON, 50, playerStatRestore(PlayerAttr::A_CON));

    std::fprintf(out, "final stats class=%d\n", class_choice);
    dumpPlayer();
}

static void runStats() {
    runStatsFor(4242, 1);
    runStatsFor(5151, 2);

    std::fprintf(out, "tables\n");

    const int strs[] = {3, 8, 14, 18, 40, 88, 118};
    const int dexs[] = {3, 9, 10, 18, 19, 67, 68, 107, 108, 117, 118};
    const int weights[] = {10, 30, 60, 120, 200, 400, 2000};
    for (int str : strs) {
        for (int dex : dexs) {
            py.stats.used[PlayerAttr::A_STR] = (uint8_t) str;
            py.stats.used[PlayerAttr::A_DEX] = (uint8_t) dex;
            std::fprintf(out, "blows str=%d dex=%d", str, dex);
            for (int weight : weights) {
                int weight_to_hit = 0;
                int blows = playerAttackBlows(weight, weight_to_hit);
                std::fprintf(out, " %d:%d/%d", weight, blows, weight_to_hit);
            }
            std::fprintf(out, "\n");
        }
    }

    std::fprintf(out, "charisma");
    for (int chr = 0; chr <= 118; chr++) {
        py.stats.used[PlayerAttr::A_CHR] = (uint8_t) chr;
        std::fprintf(out, " %d", playerStatAdjustmentCharisma());
    }
    std::fprintf(out, "\n");

    for (int v = 3; v <= 118; v++) {
        py.stats.used[PlayerAttr::A_STR] = (uint8_t) v;
        py.stats.used[PlayerAttr::A_DEX] = (uint8_t) v;
        std::fprintf(out, "adjust v=%d tohit=%d ac=%d dis=%d dam=%d\n", v, playerToHitAdjustment(), playerArmorClassAdjustment(), playerDisarmAdjustment(),
                     playerDamageAdjustment());
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

    initializeMonsterLevels();
    initializeTreasureLevels();

    const Coord_t A_DOORWAY = Coord_t{13, 15};
    const Coord_t A_INSIDE = Coord_t{8, 20};
    const Coord_t A_EAST = Coord_t{8, 28};
    const Coord_t B_INSIDE = Coord_t{22, 80};
    const Coord_t LONG_TOP = Coord_t{14, 15};
    const Coord_t DOORS_WEST = Coord_t{40, 34};
    const Coord_t DIG_WEST = Coord_t{55, 12};
    const Coord_t MAZE_START = Coord_t{3, 111};
    const char *MAZE_SCRIPT = "r6 r2 r6 r4 r2 r6 r8 r6 r6 r4 r2 r6 r4";

    // Human=0, Half-Elf=1, Elf=2, Halfling=3, Gnome=4, Dwarf=5, Half-Orc=6,
    // Half-Troll=7. For race 0 class_choice is the class id (0 Warrior,
    // 1 Mage, 2 Priest, 3 Rogue, 4 Ranger, 5 Paladin).
    const Scenario scenarios[] = {
        {"walk_lit_room", 12345, 0, true, 0, A_DOORWAY, 1, setupLight, 0, "m8*5 m6*16 m6 m9 m8*4 m2 m3 m1*3 m5"},
        {"walk_dark", 31337, 5, false, 0, B_INSIDE, 1, setupDark, 0, "m6*4 m3*3 m4*2 m8 M7"},
        {"walk_into_room", 12345, 0, true, 0, Coord_t{8, 34}, 1, setupOpenEastDoor, 0, "m4 m4 m4 m4"},
        {"walk_room_c", 31337, 0, false, 0, Coord_t{58, 15}, 3, setupLight, 0, "m2 m2 m3*4 m9 m8 m8"},
        {"run_corridor", 12345, 0, true, 0, A_EAST, 1, setupOpenEastDoor, 0, "r6 m6 r6 r2 r2 r8 r6 r6 r4"},
        {"run_corridor_nocut", 31337, 1, false, 3, A_EAST, 1, setupOpenEastDoor, 1, "r6 m6 r6 r2 r2 r8 r6"},
        {"run_print_self", 987654321, 2, true, 0, A_EAST, 1, setupOpenEastDoor, 2, "r6 m6 r6 r2"},
        {"run_long", 12345, 3, false, 0, LONG_TOP, 2, setupLongRun, 0, "r2 r6 r6 r6 r4"},
        {"run_long_find_bound", 31337, 0, true, 4, LONG_TOP, 2, setupLongRun, 1, "r2 r2 r2 r6 r6"},
        {"run_dark_room", 4242, 0, false, 0, B_INSIDE, 1, setupDark, 0, "r6 r8 r4 r2 m6*3 r9 r1"},
        {"run_lit_open", 4242, 6, true, 0, B_INSIDE, 1, setupLight, 0, "r6 r2 r4 r8 r7"},
        {"run_monster", 777, 0, true, 0, Coord_t{8, 10}, 1, setupRunMonster, 0, "r6 r4 r6"},
        {"run_monster_corridor", 31337, 0, true, 0, Coord_t{8, 33}, 1, setupRunMonster, 1, "r6 r4"},
        {"run_blind", 4242, 0, false, 0, LONG_TOP, 2, setupBlind, 0, "r2 r6"},
        {"run_maze", 12345, 0, true, 0, MAZE_START, 1, setupMaze, 0, MAZE_SCRIPT},
        {"run_maze_known", 31337, 0, false, 4, MAZE_START, 1, setupMaze, 1, MAZE_SCRIPT},
        {"run_maze_nocut", 987654321, 0, true, 0, MAZE_START, 1, setupMaze, 1 | 2, MAZE_SCRIPT},
        {"run_maze_noexamine", 4242, 0, true, 0, MAZE_START, 1, setupMaze, 1 | 4, MAZE_SCRIPT},
        {"run_maze_seams", 777, 0, true, 0, Coord_t{3, 118}, 1, setupMaze, 1 | 8, MAZE_SCRIPT},
        {"run_maze_plain", 12345, 0, false, 0, MAZE_START, 1, setupMaze, 1 | 2 | 4, MAZE_SCRIPT},
        {"run_maze_diagonal", 987654321, 0, true, 0, Coord_t{12, 138}, 1, setupMaze, 1, "r4 r6 r4 r9 r3 r7 r1 r4"},
        {"run_maze_cross", 4242, 0, false, 0, Coord_t{8, 145}, 1, setupMaze, 0, "r4 r8 r2 r6 r4 r2 r8 r4"},
        {"run_maze_dark", 31337, 0, true, 0, MAZE_START, 1, setupMaze, 16, MAZE_SCRIPT},
        {"run_maze_room", 12345, 0, true, 0, Coord_t{8, 156}, 1, setupMaze, 1, "r4 r4 r2 r8 r6 r6"},
        {"confused_walk", 777, 0, false, 0, A_INSIDE, 1, setupConfused, 30, "m6*25 m5 m4*5 r6"},
        {"search", 12345, 0, true, 0, Coord_t{55, 29}, 3, setupSearch, 0, "s*40 S1 m4 S0 m6 s*4 n5 S1 S0 n0"},
        {"search_impaired", 4242, 0, true, 0, Coord_t{55, 29}, 3, setupSearch, 2, "s*6 h10 s*10"},
        {"search_fos", 31337, 3, false, 2, Coord_t{55, 24}, 3, setupSearch, 1, "m6 m6 m6 m6"},
        {"pickup", 12345, 0, true, 0, B_INSIDE, 4, setupPickup, 0, "m6*5 m2 M4 m4 m6"},
        {"pickup_confirm", 31337, 7, false, 0, B_INSIDE, 4, setupPickup, 1, "m6*5 m2 M4 m4"},
        {"pickup_full", 987654321, 0, true, 0, B_INSIDE, 4, setupPickup, 2, "m6*5 m2 M4 m4"},
        {"pickup_prompt", 4242, 0, true, 0, B_INSIDE, 4, setupPickup, 3, "m6*5 m2"},
        {"doors", 12345, 0, true, 0, DOORS_WEST, 2, setupDoors, 0,
         "o6 m6*5 o6*8 m6*5 n9 o6*6 n0 b6*12 m6*4 o6 b6*10 m6*4 c6 o6 m6 m6 c6 c4 c8 o8 b8 b4 o4 m4"},
        {"doors_rogue", 31337, 0, false, 3, DOORS_WEST, 2, setupDoors, 0, "o6 m6*5 o6*8 m6*5 o6*12 b6*8"},
        {"doors_confused", 987654321, 7, true, 0, DOORS_WEST, 2, setupDoors, 1, "o6*3 b6*20 m6*3 c4 b6*4"},
        {"doors_monster", 777, 0, true, 0, Coord_t{40, 54}, 2, setupDoorsMonster, 0, "c6 m6 b6 x4 o6 m4"},
        {"doors_monster_stuck", 31337, 0, true, 0, Coord_t{40, 49}, 2, setupDoorsMonster, 1, "o6 x6 b6 S1 S0"},
        {"doors_misc", 4242, 0, false, 0, Coord_t{40, 54}, 2, setupDoors, 0, "b6 c6 c6 x6 o6 b6 x8"},
        {"doors_confused_lock", 777, 0, true, 0, Coord_t{40, 39}, 2, setupDoors, 1, "o6*4"},
        {"tunnel_hands", 12345, 0, true, 0, DIG_WEST, 3, setupTunnel, 0, "t8 t4 t6 t2"},
        {"tunnel_weapon", 31337, 5, true, 0, DIG_WEST, 3, setupTunnel, 1, "t8*30 m6 m6 t8*30"},
        {"tunnel_pick", 12345, 0, true, 0, DIG_WEST, 3, setupTunnel, 2,
         "t8*40 m6 m6 t8*40 m6*4 t8 t6 m6*6 n7 t6*20 n0 m6*5 t6*3 t2 m4*14 t2*40 m4 t8*60 t4"},
        {"tunnel_heavy", 987654321, 3, false, 0, DIG_WEST, 3, setupTunnel, 3, "t8*30 m6 m6 t8*20"},
        {"tunnel_confused", 4242, 0, true, 0, DIG_WEST, 3, setupTunnel, 4, "t8*30"},
        {"tunnel_secret", 31337, 0, true, 0, Coord_t{55, 29}, 3, setupTunnel, 2, "t6*3"},
        {"tunnel_afraid", 777, 0, true, 0, Coord_t{55, 24}, 3, setupDoorsMonster, 2, "t6"},
        {"teleport", 12345, 0, true, 0, A_INSIDE, 1, setupLight, 0, "p10 p10 p100 p100 p3 m6 p50"},
        {"disarm_floor", 12345, 0, true, 3, TRAP_START, 2, setupDisarmFloor, 0, "x6*10 m6 x6*10 x8"},
        {"disarm_floor_confused", 31337, 0, false, 0, TRAP_START, 2, setupDisarmFloor, 1, "x6*6 m6 x6*6"},
        {"disarm_level_gain", 987654321, 0, true, 1, TRAP_START, 2, setupDisarmFloor, 2, "x6 m4 x6 m4 x6 m4 x6 m4 x6 m4 x6"},
        {"disarm_level_cap", 12345, 0, false, 2, TRAP_START, 2, setupDisarmFloor, 3, "x6 m4 x6 m4 x6 m4 x6 m4 x6 m4 x6"},
        {"disarm_floor_priest", 31337, 0, false, 2, TRAP_START, 2, setupDisarmFloor, 0, "x6*10 m4 x6*10"},
        {"disarm_blind", 4242, 0, true, 3, TRAP_START, 2, setupDisarmFloor, 4, "x6*8"},
        {"disarm_floor_middling", 777, 0, true, 0, TRAP_START, 2, setupDisarmFloor, 5, "x6*12 m4 x6*12"},
        {"disarm_chest", 12345, 0, true, 3, TRAP_START, 5, setupDisarmChest, 0, "x6 s*3 x6*8 x4*8"},
        {"disarm_chest_clumsy", 31337, 7, true, 0, TRAP_START, 5, setupDisarmChest, 1, "x6 s*5 x6*4 x4*3"},
        {"disarm_chest_protected", 987654321, 7, true, 0, TRAP_START, 5, setupDisarmChest, 2, "x6 s*15 x6*6"},
        {"disarm_chest_middling", 4242, 0, false, 0, TRAP_START, 5, setupDisarmChest, 3, "x6 s*15 x6*10"},
        {"disarm_chest_priest", 777, 0, true, 2, TRAP_START, 5, setupDisarmChest, 0, "x6 s*5 x6*8 x4*8"},
        {"bash_chest", 4242, 7, true, 0, TRAP_START, 5, setupDisarmChest, 0, "b6*40 b4*15"},
        {"bash_chest_a", 12345, 0, true, 0, TRAP_START, 5, setupDisarmChest, 0, "b6*12"},
        {"bash_chest_b", 31337, 0, true, 0, TRAP_START, 5, setupDisarmChest, 0, "b6*12"},
        {"bash_chest_c", 987654321, 0, true, 0, TRAP_START, 5, setupDisarmChest, 0, "b6*12"},
    };

    for (auto const &s : scenarios) {
        runScenario(s);
    }

    // One scenario per trap type, then the protected/dying variants.
    // Types 10, 12 and 13 (corroding gas, fire, acid) destroy pack items --
    // deferred, see migration-plan.md's M8 section.
    const int traps[] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 14, 15, 16, 17, 18, 19, 99};
    const uint32_t seeds[] = {12345, 31337, 987654321};
    char name[64];
    int n = 0;
    for (int trap : traps) {
        std::snprintf(name, sizeof(name), "trap_%d", trap);
        // (after the summon rune, stepping again would bump-attack a monster: M9)
        runScenario({name, seeds[n++ % 3], 0, true, 0, TRAP_START, 3, setupTrap, trap, trap == 11 ? "m6 S1 S0" : "m6 m4 m6 m6 S1 S0"});
    }
    const int variants[][2] = {
        {TV_FREE, 1}, {TV_FREE, 3}, {TV_FREE, 4}, {TV_FREE, 5}, {TV_FREE, 17}, {TV_SUSTAIN, 7}, {TV_SUSTAIN, 18},
        {TV_DYING, 1}, {TV_DYING, 4}, {TV_DYING, 9}, {TV_DYING, 14}, {TV_ASLEEP, 5},
        {TV_ARMORED, 2}, {TV_ARMORED, 7}, {TV_ARMORED, 17}, {TV_ARMORED, 18},
        {TV_GUARDED, 2}, {TV_GUARDED, 9}, {TV_BACKTRAP, 9},
    };
    for (auto const &v : variants) {
        std::snprintf(name, sizeof(name), "trap_%d_v%d", v[1], v[0] / 1000);
        runScenario({name, seeds[n++ % 3], 0, true, 0, TRAP_START, 3, setupTrap, v[0] + v[1], "m6 m4 m6 S1 S0"});
    }

    runStats();

    if (out != stdout) {
        std::fclose(out);
    }

    return 0;
}
